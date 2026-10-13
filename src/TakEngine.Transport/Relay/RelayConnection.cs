using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Relay;

/// <summary>
/// The shell around one relay: opens the socket, sends the subscription, reads frames, and on a drop or a failed connect waits
/// out the <see cref="RelayLink"/> backoff on the <see cref="TimeProvider"/> and tries again. All sends go through one gate so
/// frames never interleave. Deciding logic lives in <see cref="RelayLink"/>; this class only awaits.
/// </summary>
public sealed class RelayConnection : IAsyncDisposable
{
    private readonly IRelaySocketFactory _factory;
    private readonly TimeProvider _time;
    private readonly Func<RelayMessage.Req?> _subscription;
    private readonly Action<RelayConnection, RelayMessage> _onMessage;
    private readonly Action<RelayConnection> _onSubscribed;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private IRelaySocket? _socket;
    private Task? _run;
    private TaskCompletionSource _poke = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _checkRequested;

    /// <summary>A probe timeout seen this late means the process was frozen or asleep (M1 code review 5).</summary>
    private static readonly TimeSpan LateWake = TimeSpan.FromSeconds(1);

    /// <summary>How long a receive already under way then gets before the unanswered probe drops the socket.</summary>
    private static readonly TimeSpan DropSettle = TimeSpan.FromMilliseconds(250);

    /// <param name="subscription">Evaluated at every (re)connect, so <c>since</c> follows the newest event seen.</param>
    /// <param name="onMessage">Every parsed frame from the relay, on the reader's thread.</param>
    /// <param name="onSubscribed">After each successful connect + REQ (the pool resends what this relay hasn't acknowledged).</param>
    public RelayConnection(
        Uri url,
        IRelaySocketFactory factory,
        TimeProvider time,
        Func<RelayMessage.Req?> subscription,
        Action<RelayConnection, RelayMessage> onMessage,
        Action<RelayConnection> onSubscribed)
    {
        Link = new RelayLink(url);
        _factory = factory;
        _time = time;
        _subscription = subscription;
        _onMessage = onMessage;
        _onSubscribed = onSubscribed;
    }

    public RelayLink Link { get; }

    public Uri Url => Link.Url;

    /// <summary>Increments on every successful subscribe; lets the pool tell a fresh socket from the one a send went out on.</summary>
    public int Generation { get; private set; }

    public void Start() => _run ??= Task.Run(() => RunAsync(_stop.Token));

    /// <summary>
    /// Sends one frame on the current socket. False when not connected, stopping, or the socket failed (the caller resends after the
    /// next subscribe). Never throws: a send racing a drop must not stop the pool's pump.
    /// </summary>
    public async Task<bool> SendAsync(string frame)
    {
        IRelaySocket? socket = _socket;
        if (socket is null)
            return false;
        try
        {
            await _sendGate.WaitAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
        try
        {
            return socket == _socket && await socket.SendAsync(frame, _stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>Sends the current subscription again on a live socket (a catch-up, or a changed filter). NIP-01: a REQ with the same
    /// id replaces the old one, and the relay answers with stored events again.</summary>
    public async Task<bool> ResubscribeAsync()
    {
        if (!Link.CanWrite || _subscription() is not { } req)
            return false;
        return await SendAsync(req.Format()).ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            DateTimeOffset now = _time.GetUtcNow();
            if (!Link.IsDueToConnect(now))
            {
                try
                {
                    await Task.Delay(Link.RetryAt!.Value - now, _time, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }

            Link.OnConnectStarted();
            IRelaySocket socket = _factory.Create(Url);
            try
            {
                await socket.ConnectAsync(ct).ConfigureAwait(false);
            }
            catch (RelayConnectException ex)
            {
                await socket.DisposeAsync().ConfigureAwait(false);
                Link.OnDisconnected(_time.GetUtcNow(), ex.Message);
                continue;
            }
            catch (OperationCanceledException)
            {
                await socket.DisposeAsync().ConfigureAwait(false);
                return;
            }

            _socket = socket;
            bool silent = false;
            RelayMessage.Req? req = _subscription();
            if (req is null || await SendAsync(req.Format()).ConfigureAwait(false))
            {
                Generation++;
                Link.OnSubscribed(_time.GetUtcNow());
                _onSubscribed(this);
                // A silent socket is closed below like any other; the next REQ's since catches up what it missed.
                silent = await ReadUntilClosedAsync(socket, ct).ConfigureAwait(false);
            }

            // Under the send gate, so no send is half way through on the socket being disposed.
            await _sendGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                _socket = null;
                await socket.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
            if (!ct.IsCancellationRequested)
                Link.OnDisconnected(_time.GetUtcNow(), silent ? null : "connection dropped"); // silent: the link already named the reason
        }
    }

    /// <summary>Probe now rather than after the idle window (a tab became visible again, a device woke). Thread-safe.</summary>
    public void CheckNow()
    {
        Interlocked.Exchange(ref _checkRequested, 1);
        Volatile.Read(ref _poke).TrySetResult();
    }

    /// <summary>
    /// Reads frames until the socket closes (false) or the link's liveness check gives up on a silent socket (true). Every frame
    /// resets the idle clock; when the relay has been quiet for the idle window a probe goes out on this socket, and its EOSE is
    /// answered with a CLOSE by the pool.
    /// </summary>
    private async Task<bool> ReadUntilClosedAsync(IRelaySocket socket, CancellationToken ct)
    {
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<string?> receive = socket.ReceiveAsync(reading.Token);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TaskCompletionSource poke = Volatile.Read(ref _poke);
                if (Interlocked.Exchange(ref _checkRequested, 0) == 1)
                    Link.RequestProbe();

                // A frame that has already arrived is read before the liveness check. A thawed tab (or a woken device) runs the
                // overdue probe timeout and the queued frame's receive together, in either order. When the timeout is found late,
                // the receive gets a moment first (real time: it settles work already queued, it is not protocol time), so a
                // healthy socket is not dropped. A timeout found on time drops at once.
                if (!receive.IsCompleted && Link.ProbeSentAt is not null
                    && Link.NextLivenessCheckAt is DateTimeOffset dropAt && _time.GetUtcNow() - dropAt >= LateWake)
                {
                    await Task.WhenAny(receive, Task.Delay(DropSettle, ct)).ConfigureAwait(false);
                }
                if (!receive.IsCompleted)
                {
                    switch (Link.CheckLiveness(_time.GetUtcNow()))
                    {
                        case LivenessAction.SendProbe:
                            await SendAsync(SubscriptionPlan.Probe().Format()).ConfigureAwait(false);
                            break;
                        case LivenessAction.Drop:
                            return true;
                    }
                }

                if (!receive.IsCompleted)
                {
                    using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var waits = new List<Task> { receive, poke.Task };
                    if (Link.NextLivenessCheckAt is DateTimeOffset due)
                    {
                        // Read the clock again: sending the probe awaited, and time may have moved on since `now`.
                        TimeSpan wait = due - _time.GetUtcNow();
                        waits.Add(Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, _time, waiting.Token));
                    }
                    await Task.WhenAny(waits).ConfigureAwait(false);
                    await waiting.CancelAsync().ConfigureAwait(false);
                    if (poke.Task.IsCompleted)
                        Interlocked.CompareExchange(ref _poke, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), poke);
                    if (!receive.IsCompleted)
                        continue;
                }

                string? frame;
                try
                {
                    frame = await receive.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                if (frame is null)
                    return false;
                Link.OnFrame(_time.GetUtcNow());
                _onMessage(this, RelayMessage.Parse(frame));
                receive = socket.ReceiveAsync(reading.Token);
            }
            return false;
        }
        finally
        {
            // A receive still pending on a silent socket ends here (the real socket aborts on cancel).
            await reading.CancelAsync().ConfigureAwait(false);
            try
            {
                await receive.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException
                or System.Net.WebSockets.WebSocketException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_run is not null)
        {
            try
            {
                await _run.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (_socket is { } socket)
            await socket.DisposeAsync().ConfigureAwait(false);
        Link.Stop();
        _stop.Dispose();
        _sendGate.Dispose();
    }
}

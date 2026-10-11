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

    /// <summary>Sends one frame on the current socket. False when not connected (the caller resends after the next subscribe).</summary>
    public async Task<bool> SendAsync(string frame)
    {
        IRelaySocket? socket = _socket;
        if (socket is null)
            return false;
        await _sendGate.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            return socket == _socket && await socket.SendAsync(frame, _stop.Token).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
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
            RelayMessage.Req? req = _subscription();
            if (req is null || await SendAsync(req.Format()).ConfigureAwait(false))
            {
                Generation++;
                Link.OnSubscribed();
                _onSubscribed(this);
                while (!ct.IsCancellationRequested)
                {
                    string? frame;
                    try
                    {
                        frame = await socket.ReceiveAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    if (frame is null)
                        break;
                    _onMessage(this, RelayMessage.Parse(frame));
                }
            }

            _socket = null;
            await socket.DisposeAsync().ConfigureAwait(false);
            if (!ct.IsCancellationRequested)
                Link.OnDisconnected(_time.GetUtcNow(), "connection dropped");
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

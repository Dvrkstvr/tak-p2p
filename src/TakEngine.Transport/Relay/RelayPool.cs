using System.Threading.Channels;
using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Relay;

/// <summary>An event a relay delivered, once per event id across all relays and reconnects.</summary>
/// <param name="IsOwn">Authored by this client (our own echo); the turn exchange decides what to do with it.</param>
public sealed record ReceivedEvent(Uri Relay, NostrEvent Event, bool IsOwn);

/// <summary>
/// Fans out to several relays (F-034): publishes with per-relay status under <see cref="PublishBudget"/>, (re)subscribes on every
/// connect, and delivers each valid event once (dedupe by event id in a bounded LRU). Events whose id or signature don't verify
/// are dropped here and counted per relay (tamper row 1: "drop, count only"), so a forged copy can never shadow the real event.
/// Deciding logic is in <see cref="PublishTracker"/>, <see cref="PublishBudget"/> and <see cref="RelayLink"/>; this class awaits.
/// </summary>
public sealed class RelayPool : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly string? _ownPubkey;
    private readonly List<RelayConnection> _connections;
    private readonly PublishTracker _tracker = new();
    private readonly PublishBudget _budget;
    private readonly List<NostrEvent> _outgoing = [];
    private readonly Dictionary<(string Id, Uri Relay), (int Generation, DateTimeOffset SentAt)> _inFlight = [];
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private readonly int _seenCapacity;
    private readonly Dictionary<Uri, int> _invalidDropped = [];
    private readonly Channel<ReceivedEvent> _events = Channel.CreateUnbounded<ReceivedEvent>();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private Func<RelayMessage.Req?> _subscription = () => null;
    private Task? _pump;
    private Task? _pendingWake;
    private long _maxCreatedAt;

    /// <param name="budget">Shared by every pool of one client (the 4-per-10-s limit is per client); a fresh one when null.</param>
    public RelayPool(
        IEnumerable<Uri> relays,
        IRelaySocketFactory factory,
        TimeProvider time,
        string? ownPubkeyHex = null,
        int seenCapacity = 4096,
        PublishBudget? budget = null)
    {
        _time = time;
        _budget = budget ?? new PublishBudget();
        _ownPubkey = ownPubkeyHex;
        _seenCapacity = seenCapacity;
        _connections = relays.Distinct()
            .Select(url => new RelayConnection(url, factory, time, () => _subscription(), OnMessage, OnSubscribed))
            .ToList();
    }

    public IReadOnlyList<RelayLink> Links => _connections.Select(c => c.Link).ToList();

    /// <summary>Delivered events, once each, in arrival order.</summary>
    public ChannelReader<ReceivedEvent> Events => _events.Reader;

    /// <summary>The newest <c>created_at</c> delivered so far (0 before any), from any author. Raw: anyone can raise it, so a
    /// caller builds <c>since</c> only from events it accepted.</summary>
    public long MaxCreatedAt => Interlocked.Read(ref _maxCreatedAt);

    /// <summary>Raised (on a pool thread) whenever a relay acknowledges or refuses one of our events.</summary>
    public event Action<string, PublishOutcome>? PublishStatusChanged;

    public int InvalidEventsDropped(Uri relay)
    {
        lock (_gate) return _invalidDropped.GetValueOrDefault(relay);
    }

    /// <summary>Starts every connection. <paramref name="subscription"/> is evaluated at each (re)connect.</summary>
    public void Start(Func<RelayMessage.Req?> subscription)
    {
        _subscription = subscription;
        foreach (RelayConnection c in _connections)
            c.Start();
        _pump ??= Task.Run(() => PumpAsync(_stop.Token));
    }

    /// <summary>Queues an event for every relay. Sending happens under the budget; follow it with <see cref="Outcome"/> or the event.</summary>
    public void Publish(NostrEvent evt)
    {
        lock (_gate)
        {
            if (_tracker.IsTracked(evt.Id))
                return;
            _tracker.Track(evt.Id, _connections.Select(c => c.Url));
            _outgoing.Add(evt);
            MarkSeenLocked(evt.Id); // our own echo is not delivered back as new
        }
        _wake.Release();
    }

    /// <summary>
    /// Sends a stored event again, verbatim (same id), to every relay, even ones that accepted it before: a JOIN on its republish
    /// schedule, an ACCEPT for a retried JOIN, our last move after a restart. Receivers treat it as a duplicate.
    /// </summary>
    public void Republish(NostrEvent evt)
    {
        lock (_gate)
        {
            _tracker.Retrack(evt.Id, _connections.Select(c => c.Url));
            foreach (RelayConnection c in _connections)
                _inFlight.Remove((evt.Id, c.Url));
            if (!_outgoing.Any(e => e.Id == evt.Id))
                _outgoing.Add(evt);
            MarkSeenLocked(evt.Id);
        }
        _wake.Release();
    }

    /// <summary>Stops sending an event that no relay should get any more (a guest cancelled before its JOIN went out).</summary>
    public void Withdraw(string eventId)
    {
        lock (_gate)
        {
            _outgoing.RemoveAll(e => e.Id == eventId);
            foreach (RelayConnection c in _connections)
                _inFlight.Remove((eventId, c.Url));
        }
    }

    /// <summary>Sends the subscription again on every connected relay (catch-up after a gap, or the filter changed).</summary>
    public async Task ResubscribeAsync()
    {
        foreach (RelayConnection c in _connections)
            await c.ResubscribeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Probes every connected relay now instead of after the idle window (a browser tab became visible again, a device woke up):
    /// a socket that died silently meanwhile is then dropped and reconnected 10 s later rather than up to 70 s later (D-051).
    /// </summary>
    public void CheckNow()
    {
        foreach (RelayConnection c in _connections)
            c.CheckNow();
    }

    public PublishOutcome Outcome(string eventId)
    {
        lock (_gate) return _tracker.Outcome(eventId);
    }

    public IReadOnlyDictionary<Uri, RelayPublishState> PerRelay(string eventId)
    {
        lock (_gate) return new Dictionary<Uri, RelayPublishState>(_tracker.PerRelay(eventId));
    }

    private void OnSubscribed(RelayConnection connection)
    {
        lock (_gate) _tracker.OnReconnected(connection.Url);
        _wake.Release();
    }

    private void OnMessage(RelayConnection connection, RelayMessage message)
    {
        switch (message)
        {
            case RelayMessage.Event e:
                Receive(connection, e.NostrEvent);
                break;
            case RelayMessage.Ok ok:
                PublishOutcome? changed = null;
                lock (_gate)
                {
                    if (!_tracker.IsTracked(ok.EventId))
                        break;
                    _inFlight.Remove((ok.EventId, connection.Url));
                    _tracker.OnOk(ok.EventId, connection.Url, ok.Accepted, ok.Message, _time.GetUtcNow());
                    changed = _tracker.Outcome(ok.EventId);
                }
                PublishStatusChanged?.Invoke(ok.EventId, changed.Value);
                _wake.Release();
                break;
            case RelayMessage.Eose eose when eose.SubscriptionId == SubscriptionPlan.ProbeSubscriptionId:
                // The probe was answered (the connection already reset its idle clock); end its subscription. SendAsync never throws.
                _ = connection.SendAsync(new RelayMessage.Close(SubscriptionPlan.ProbeSubscriptionId).Format());
                break;
            case RelayMessage.Eose:
                connection.Link.OnEose();
                break;
            case RelayMessage.Closed closed when closed.SubscriptionId == SubscriptionPlan.ProbeSubscriptionId:
                break; // an answer all the same; says nothing about the game's subscription
            case RelayMessage.Closed closed:
                connection.Link.OnClosed(closed.Message);
                break;
            case RelayMessage.Notice notice:
                connection.Link.OnNotice(notice.Message);
                break;
            case RelayMessage.AuthChallenge challenge:
                connection.Link.OnNotice("AUTH challenge (not answered)");
                break;
        }
    }

    private void Receive(RelayConnection connection, NostrEvent evt)
    {
        if (!NostrEvents.Verify(evt))
        {
            lock (_gate) _invalidDropped[connection.Url] = _invalidDropped.GetValueOrDefault(connection.Url) + 1;
            return;
        }
        bool isOwn = _ownPubkey is not null && evt.Pubkey == _ownPubkey;
        lock (_gate)
        {
            if (!MarkSeenLocked(evt.Id))
                return;
            if (evt.CreatedAt > _maxCreatedAt)
                Interlocked.Exchange(ref _maxCreatedAt, evt.CreatedAt);
            _events.Writer.TryWrite(new ReceivedEvent(connection.Url, evt, isOwn));
        }
    }

    private bool MarkSeenLocked(string id)
    {
        if (!_seen.Add(id))
            return false;
        _seenOrder.Enqueue(id);
        while (_seenOrder.Count > _seenCapacity)
            _seen.Remove(_seenOrder.Dequeue());
        return true;
    }

    /// <summary>One loop decides what to send, under the budget, and sleeps until the next thing is due.</summary>
    private async Task PumpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            List<(RelayConnection Connection, string Frame)> sends = [];
            DateTimeOffset? nextDue;
            lock (_gate)
            {
                DateTimeOffset now = _time.GetUtcNow();
                ExpireUnansweredLocked(now);
                foreach (NostrEvent evt in _outgoing)
                {
                    var targets = _connections.Where(c => NeedsSendLocked(evt.Id, c, now)).ToList();
                    if (targets.Count == 0)
                        continue;
                    if (!_budget.TryTake(now))
                        break;
                    string frame = new RelayMessage.Publish(evt).Format();
                    foreach (RelayConnection c in targets)
                    {
                        _tracker.OnResent(evt.Id, c.Url);
                        _inFlight[(evt.Id, c.Url)] = (c.Generation, now);
                        sends.Add((c, frame));
                    }
                }
                nextDue = NextDueLocked(now);
            }

            foreach (var (connection, frame) in sends)
            {
                // SendAsync never throws; a failed send is resent after the next subscribe.
                if (!await connection.SendAsync(frame).ConfigureAwait(false))
                {
                    lock (_gate) _inFlight.Remove((ParseId(frame), connection.Url));
                }
            }

            try
            {
                // One outstanding wait at a time: an abandoned WaitAsync would swallow a later Release.
                if (_pendingWake is null || _pendingWake.IsCompleted)
                    _pendingWake = _wake.WaitAsync(ct);
                Task wake = _pendingWake;
                if (nextDue is { } due)
                {
                    TimeSpan delay = due - _time.GetUtcNow();
                    if (delay > TimeSpan.Zero)
                        await Task.WhenAny(wake, Task.Delay(delay, _time, ct)).ConfigureAwait(false);
                }
                else
                {
                    await wake.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private bool NeedsSendLocked(string id, RelayConnection c, DateTimeOffset now)
    {
        if (!c.Link.CanWrite)
            return false;
        RelayPublishState state = _tracker.PerRelay(id)[c.Url];
        if (state.Status != RelayPublishStatus.Pending)
            return false;
        if (state.RetryAt is { } at && at > now)
            return false;
        return !(_inFlight.TryGetValue((id, c.Url), out var sent) && sent.Generation == c.Generation);
    }

    /// <summary>
    /// A send on the current socket with no OK after <see cref="PublishTracker.NoAnswerAfter"/> becomes NoAnswer and is retried after a
    /// backoff; one from an older socket is simply forgotten (the reconnect resends it).
    /// </summary>
    private void ExpireUnansweredLocked(DateTimeOffset now)
    {
        foreach (var ((id, url), sent) in _inFlight.ToList())
        {
            RelayConnection c = _connections.First(x => x.Url == url);
            if (sent.Generation != c.Generation)
            {
                _inFlight.Remove((id, url));
            }
            else if (now - sent.SentAt >= PublishTracker.NoAnswerAfter)
            {
                _inFlight.Remove((id, url));
                _tracker.OnNoAnswer(id, url, now);
            }
        }
    }

    /// <summary>
    /// When the pump must look again: the next future rate-limit retry, or, if something is ready but the budget is spent, when
    /// the budget frees up. Always in the future (a past time would spin the loop).
    /// </summary>
    private DateTimeOffset? NextDueLocked(DateTimeOffset now)
    {
        DateTimeOffset? due = _tracker.NextRetryAt(after: now);
        foreach (var (_, sentAt) in _inFlight.Values)
        {
            DateTimeOffset expires = sentAt + PublishTracker.NoAnswerAfter;
            if (expires > now && (due is null || expires < due))
                due = expires;
        }
        if (_outgoing.Any(e => _connections.Any(c => NeedsSendLocked(e.Id, c, now))))
        {
            DateTimeOffset budgetAt = _budget.NextAvailable(now);
            if (budgetAt > now && (due is null || budgetAt < due))
                due = budgetAt;
        }
        return due;
    }

    private static string ParseId(string frame) => RelayMessage.Parse(frame) is RelayMessage.Publish p ? p.NostrEvent.Id : "";

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_pump is not null)
        {
            try
            {
                await _pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        foreach (RelayConnection c in _connections)
            await c.DisposeAsync().ConfigureAwait(false);
        _events.Writer.TryComplete();
        _stop.Dispose();
        _wake.Dispose();
    }
}

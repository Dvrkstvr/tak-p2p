using System.Threading.Channels;
using TakEngine.Transport.Nostr;
using TakEngine.Transport.Relay;

namespace TakEngine.Testing;

/// <summary>
/// A NIP-01 relay in memory (pipeline/architecture.md "Seams"): verifies id and sig (else <c>OK false invalid:</c>), stores events,
/// answers REQ with history newest-first, then EOSE, then live events; CLOSE stops delivery. Fault knobs reproduce what the
/// spikes saw on nos.lol and damus; every knob is deterministic (a seed drives any shuffling). Thread-safe.
/// </summary>
public sealed class InMemoryRelay
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly Random _random;
    private readonly List<NostrEvent> _stored = [];
    private readonly HashSet<string> _storedIds = new(StringComparer.Ordinal);
    private readonly List<Link> _links = [];
    private readonly List<(Func<NostrEvent, bool> Predicate, string Reason)> _refusals = [];
    private readonly HashSet<string> _withheld = new(StringComparer.Ordinal);
    private Func<NostrEvent, bool>? _withholdWhere;
    private readonly List<string> _receivedFrames = [];
    private readonly List<(DateTimeOffset At, string Frame)> _frameLog = [];
    private int _upgradeRejectStatus;
    private int _upgradeRejectsLeft;
    private bool _partitioned;
    private bool _authForReads;
    private int _rateLimitBurst;
    private TimeSpan _rateLimitWindow;
    private int _duplicateDeliveries = 1;
    private int _reorderWindow;

    public InMemoryRelay(string url, TimeProvider? time = null, int seed = 0)
    {
        Url = new Uri(url);
        _time = time ?? TimeProvider.System;
        _random = new Random(seed);
    }

    public Uri Url { get; }

    /// <summary>Connection attempts, including refused ones.</summary>
    public int ConnectAttempts { get; private set; }

    /// <summary>Every frame any client sent, in arrival order (F-017's "no network traffic" check counts these).</summary>
    public IReadOnlyList<string> ReceivedFrames
    {
        get { lock (_gate) return _receivedFrames.ToList(); }
    }

    /// <summary>Client frames with the relay clock's time of arrival (rate tests count EVENTs per window).</summary>
    public IReadOnlyList<(DateTimeOffset At, string Frame)> FrameLog
    {
        get { lock (_gate) return _frameLog.ToList(); }
    }

    public int FramesSent { get; private set; }

    public IReadOnlyList<NostrEvent> StoredEvents
    {
        get { lock (_gate) return _stored.ToList(); }
    }

    public int OpenConnections
    {
        get { lock (_gate) return _links.Count; }
    }

    // ---- knobs ----

    /// <summary>Answer <c>OK false</c> with <paramref name="reason"/> (e.g. "blocked: ...") for matching events; they are not stored.</summary>
    public void Refuse(Func<NostrEvent, bool> predicate, string reason)
    {
        lock (_gate) _refusals.Add((predicate, reason));
    }

    public void ClearRefusals()
    {
        lock (_gate) _refusals.Clear();
    }

    /// <summary>Like damus: within any <paramref name="window"/> (default 10 s) a connection may publish <paramref name="burst"/> events (default 5); the next is refused with <c>rate-limited:</c>.</summary>
    public void RateLimitLikeDamus(int burst = 5, TimeSpan? window = null)
    {
        lock (_gate)
        {
            _rateLimitBurst = burst;
            _rateLimitWindow = window ?? TimeSpan.FromSeconds(10);
        }
    }

    /// <summary>The next <paramref name="times"/> connection attempts fail with HTTP <paramref name="status"/>.</summary>
    public void RejectUpgrade(int status = 503, int times = int.MaxValue)
    {
        lock (_gate)
        {
            _upgradeRejectStatus = status;
            _upgradeRejectsLeft = times;
        }
    }

    /// <summary>Closes every open socket now (clients see a drop). New connections are still accepted.</summary>
    public void DropConnections()
    {
        lock (_gate)
        {
            foreach (Link link in _links.ToList())
                link.CloseLocked();
        }
    }

    /// <summary>Unreachable until <see cref="Heal"/>: open sockets drop and connects fail.</summary>
    public void Partition()
    {
        lock (_gate)
        {
            _partitioned = true;
            foreach (Link link in _links.ToList())
                link.CloseLocked();
        }
    }

    public void Heal()
    {
        lock (_gate) _partitioned = false;
    }

    /// <summary>Live events are held per socket and released in a seeded shuffle once <paramref name="window"/> are pending (or on <see cref="FlushReordered"/>).</summary>
    public void ReorderLive(int window = 2)
    {
        lock (_gate) _reorderWindow = window;
    }

    public void FlushReordered()
    {
        lock (_gate)
        {
            foreach (Link link in _links)
                link.FlushLocked();
        }
    }

    /// <summary>Every live delivery is sent <paramref name="copies"/> times.</summary>
    public void DuplicateDeliveries(int copies)
    {
        lock (_gate) _duplicateDeliveries = Math.Max(1, copies);
    }

    /// <summary>The event is stored and acknowledged but never delivered (live or history) until <see cref="Release"/>.</summary>
    public void Withhold(string eventId)
    {
        lock (_gate) _withheld.Add(eventId);
    }

    /// <summary>Withholds every event published from now on that matches <paramref name="predicate"/> (null stops it).</summary>
    public void WithholdWhere(Func<NostrEvent, bool>? predicate)
    {
        lock (_gate) _withholdWhere = predicate;
    }

    /// <summary>Stops withholding and delivers the event to every matching subscription.</summary>
    public void Release(string eventId)
    {
        lock (_gate)
        {
            if (!_withheld.Remove(eventId))
                return;
            NostrEvent? evt = _stored.FirstOrDefault(e => e.Id == eventId);
            if (evt is not null)
                DeliverLiveLocked(evt);
        }
    }

    /// <summary>REQ gets an AUTH challenge and <c>CLOSED auth-required:</c>; EVENTs are still accepted (damus's broken AUTH, seen in the spike).</summary>
    public void AuthRequiredForReads(bool required = true)
    {
        lock (_gate) _authForReads = required;
    }

    /// <summary>Sends a raw frame to every open socket (junk, forged events, NOTICEs).</summary>
    public void Inject(string frame)
    {
        lock (_gate)
        {
            foreach (Link link in _links)
                link.SendLocked(frame);
        }
    }

    /// <summary>Stores an event without any client (history for catch-up tests). Invalid events are refused.</summary>
    public bool Seed(NostrEvent evt)
    {
        lock (_gate)
        {
            if (!NostrEvents.Verify(evt))
                return false;
            if (_storedIds.Add(evt.Id))
                _stored.Add(evt);
            return true;
        }
    }

    // ---- connection side ----

    internal Link Accept()
    {
        lock (_gate)
        {
            ConnectAttempts++;
            if (_partitioned)
                throw new RelayConnectException($"{Url} is unreachable (partition).");
            if (_upgradeRejectsLeft > 0)
            {
                _upgradeRejectsLeft--;
                throw new RelayConnectException($"{Url} answered the upgrade with HTTP {_upgradeRejectStatus}.", _upgradeRejectStatus);
            }
            var link = new Link(this);
            _links.Add(link);
            return link;
        }
    }

    private void HandleLocked(Link link, string frame)
    {
        _receivedFrames.Add(frame);
        _frameLog.Add((_time.GetUtcNow(), frame));
        switch (RelayMessage.Parse(frame))
        {
            case RelayMessage.Publish p:
                HandlePublishLocked(link, p.NostrEvent);
                break;
            case RelayMessage.Req r:
                HandleReqLocked(link, r);
                break;
            case RelayMessage.Close c:
                link.Subscriptions.Remove(c.SubscriptionId);
                break;
            default:
                link.SendLocked(new RelayMessage.Notice("error: could not parse frame").Format());
                break;
        }
    }

    private void HandlePublishLocked(Link link, NostrEvent evt)
    {
        string id = evt.Id ?? "";
        if (!NostrEvents.Verify(evt))
        {
            link.SendLocked(new RelayMessage.Ok(id, false, "invalid: bad event id or signature").Format());
            return;
        }

        if (_rateLimitBurst > 0)
        {
            DateTimeOffset now = _time.GetUtcNow();
            link.PublishTimes.RemoveAll(t => now - t >= _rateLimitWindow);
            if (link.PublishTimes.Count >= _rateLimitBurst)
            {
                link.SendLocked(new RelayMessage.Ok(id, false, "rate-limited: slow down").Format());
                return;
            }
            link.PublishTimes.Add(now);
        }

        foreach (var (predicate, reason) in _refusals)
        {
            if (predicate(evt))
            {
                link.SendLocked(new RelayMessage.Ok(id, false, reason).Format());
                return;
            }
        }

        if (!_storedIds.Add(id))
        {
            link.SendLocked(new RelayMessage.Ok(id, true, "duplicate: already have this event").Format());
            return;
        }

        _stored.Add(evt);
        if (_withholdWhere?.Invoke(evt) == true)
            _withheld.Add(id);
        link.SendLocked(new RelayMessage.Ok(id, true, "").Format());
        if (!_withheld.Contains(id))
            DeliverLiveLocked(evt);
    }

    private void HandleReqLocked(Link link, RelayMessage.Req req)
    {
        if (_authForReads)
        {
            link.SendLocked(new RelayMessage.AuthChallenge("challenge-" + _random.Next()).Format());
            link.SendLocked(new RelayMessage.Closed(req.SubscriptionId, "auth-required: this relay requires authentication for reads").Format());
            return;
        }

        link.Subscriptions[req.SubscriptionId] = req.Filters;
        var history = new List<NostrEvent>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (NostrFilter filter in req.Filters)
        {
            IEnumerable<NostrEvent> matches = Newest(_stored.Where(e => !_withheld.Contains(e.Id) && FilterMatcher.Matches(filter, e)));
            if (filter.Limit is int limit)
                matches = matches.Take(limit);
            foreach (NostrEvent e in matches)
            {
                if (seen.Add(e.Id))
                    history.Add(e);
            }
        }
        foreach (NostrEvent e in Newest(history))
            link.SendLocked(new RelayMessage.Event(req.SubscriptionId, e).Format());
        link.SendLocked(new RelayMessage.Eose(req.SubscriptionId).Format());
    }

    private static IEnumerable<NostrEvent> Newest(IEnumerable<NostrEvent> events) =>
        events.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id, StringComparer.Ordinal);

    private void DeliverLiveLocked(NostrEvent evt)
    {
        foreach (Link link in _links)
        {
            foreach (var (subId, filters) in link.Subscriptions)
            {
                if (!filters.Any(f => FilterMatcher.Matches(f, evt)))
                    continue;
                string frame = new RelayMessage.Event(subId, evt).Format();
                for (int i = 0; i < _duplicateDeliveries; i++)
                {
                    if (_reorderWindow > 0)
                    {
                        link.Pending.Add(frame);
                        if (link.Pending.Count >= _reorderWindow)
                            link.FlushLocked();
                    }
                    else
                    {
                        link.SendLocked(frame);
                    }
                }
            }
        }
    }

    /// <summary>One client socket on the relay side.</summary>
    internal sealed class Link(InMemoryRelay relay)
    {
        private readonly Channel<string> _outbound = Channel.CreateUnbounded<string>();

        public Dictionary<string, IReadOnlyList<NostrFilter>> Subscriptions { get; } = new(StringComparer.Ordinal);
        public List<DateTimeOffset> PublishTimes { get; } = [];
        public List<string> Pending { get; } = [];
        public bool IsClosed { get; private set; }

        public ChannelReader<string> Outbound => _outbound.Reader;

        public bool Receive(string frame)
        {
            lock (relay._gate)
            {
                if (IsClosed)
                    return false;
                relay.HandleLocked(this, frame);
                return true;
            }
        }

        public void Close()
        {
            lock (relay._gate) CloseLocked();
        }

        public void SendLocked(string frame)
        {
            if (IsClosed)
                return;
            relay.FramesSent++;
            _outbound.Writer.TryWrite(frame);
        }

        public void FlushLocked()
        {
            List<string> batch = Pending.OrderBy(_ => relay._random.Next()).ToList();
            Pending.Clear();
            foreach (string frame in batch)
                SendLocked(frame);
        }

        public void CloseLocked()
        {
            if (IsClosed)
                return;
            IsClosed = true;
            Subscriptions.Clear();
            relay._links.Remove(this);
            _outbound.Writer.TryComplete();
        }
    }
}

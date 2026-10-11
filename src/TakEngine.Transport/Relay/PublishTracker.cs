namespace TakEngine.Transport.Relay;

public enum RelayPublishStatus
{
    Pending,
    Sent,
    Refused,
}

public enum PublishOutcome
{
    /// <summary>No relay has said OK true yet, and at least one may still.</summary>
    Pending,

    /// <summary>At least one relay answered OK true (F-034's definition of "sent").</summary>
    Sent,

    /// <summary>Every relay refused for good.</summary>
    Failed,
}

/// <param name="RetryAt">For a rate-limited refusal: when to try this relay again.</param>
public sealed record RelayPublishState(RelayPublishStatus Status, string? Reason = null, DateTimeOffset? RetryAt = null, int RateLimits = 0);

/// <summary>
/// Per event id, per relay: Pending, Sent or Refused(reason). <c>rate-limited:</c> keeps the relay Pending with a retry due after
/// a backoff (2, 4, 8, 16, 30 s); any other <c>OK false</c> is final for that relay. Pure: time is passed in.
/// </summary>
public sealed class PublishTracker
{
    private readonly Dictionary<string, Dictionary<Uri, RelayPublishState>> _events = new(StringComparer.Ordinal);

    public void Track(string eventId, IEnumerable<Uri> relays)
    {
        if (_events.ContainsKey(eventId))
            return;
        _events[eventId] = relays.Distinct().ToDictionary(r => r, _ => new RelayPublishState(RelayPublishStatus.Pending));
    }

    public bool IsTracked(string eventId) => _events.ContainsKey(eventId);

    /// <summary>Starts an event over as Pending on every relay (a deliberate republish of the stored event, same id).</summary>
    public void Retrack(string eventId, IEnumerable<Uri> relays)
    {
        _events.Remove(eventId);
        Track(eventId, relays);
    }

    public void OnOk(string eventId, Uri relay, bool accepted, string message, DateTimeOffset now)
    {
        if (!_events.TryGetValue(eventId, out var relays) || !relays.TryGetValue(relay, out RelayPublishState? current))
            return;
        if (current.Status == RelayPublishStatus.Sent)
            return; // a late refusal never un-sends

        if (accepted)
        {
            relays[relay] = new RelayPublishState(RelayPublishStatus.Sent, message.Length == 0 ? null : message);
        }
        else if (message.StartsWith("rate-limited:", StringComparison.Ordinal))
        {
            int limits = current.RateLimits + 1;
            relays[relay] = new RelayPublishState(RelayPublishStatus.Pending, message, now + RelayLink.BackoffDelay(limits + 1), limits);
        }
        else
        {
            relays[relay] = new RelayPublishState(RelayPublishStatus.Refused, message);
        }
    }

    public IReadOnlyDictionary<Uri, RelayPublishState> PerRelay(string eventId) =>
        _events.TryGetValue(eventId, out var relays) ? relays : new Dictionary<Uri, RelayPublishState>();

    public PublishOutcome Outcome(string eventId)
    {
        if (!_events.TryGetValue(eventId, out var relays) || relays.Count == 0)
            return PublishOutcome.Pending;
        if (relays.Values.Any(s => s.Status == RelayPublishStatus.Sent))
            return PublishOutcome.Sent;
        if (relays.Values.All(s => s.Status == RelayPublishStatus.Refused))
            return PublishOutcome.Failed;
        return PublishOutcome.Pending;
    }

    /// <summary>Rate-limited (event, relay) pairs whose retry time has come. The caller resends, under the budget, and calls <see cref="OnResent"/>.</summary>
    public IReadOnlyList<(string EventId, Uri Relay)> DueRetries(DateTimeOffset now) =>
        _events.SelectMany(e => e.Value
                .Where(r => r.Value.Status == RelayPublishStatus.Pending && r.Value.RetryAt is { } at && at <= now)
                .Select(r => (e.Key, r.Key)))
            .ToList();

    public void OnResent(string eventId, Uri relay)
    {
        if (_events.TryGetValue(eventId, out var relays) && relays.TryGetValue(relay, out RelayPublishState? s))
            relays[relay] = s with { RetryAt = null };
    }

    /// <summary>The earliest pending retry (strictly later than <paramref name="after"/> when given), so the shell knows when to wake up.</summary>
    public DateTimeOffset? NextRetryAt(DateTimeOffset? after = null) =>
        _events.Values.SelectMany(r => r.Values).Where(s => s.RetryAt is { } at && (after is null || at > after)).Min(s => s.RetryAt);
}

namespace TakEngine.Transport.Relay;

public enum RelayLinkState
{
    /// <summary>Not started, or stopped on purpose.</summary>
    Disconnected,
    Connecting,

    /// <summary>Connected and the REQ is out; history is still arriving.</summary>
    Subscribed,

    /// <summary>EOSE seen: history is complete, live events follow.</summary>
    CaughtUp,

    /// <summary>Waiting until <see cref="RelayLink.RetryAt"/> before the next connect.</summary>
    Backoff,
}

/// <summary>
/// One relay's connection state (scope.md "Reconnect and catch-up"): Connecting -> Subscribed -> CaughtUp, and on a drop or a
/// failed connect (HTTP 503 included) Backoff 1, 2, 4, 8, 16, 30, 30 ... s -> Connecting. The backoff resets once a subscribe
/// succeeds. <c>CLOSED auth-required</c> makes the relay read-impossible; it stays connected for writes. Pure: time is passed in.
/// </summary>
public sealed class RelayLink(Uri url)
{
    private static readonly int[] BackoffSeconds = [1, 2, 4, 8, 16, 30];

    public Uri Url { get; } = url;

    public RelayLinkState State { get; private set; } = RelayLinkState.Disconnected;

    /// <summary>Consecutive failed connects or drops since the last successful subscribe.</summary>
    public int Failures { get; private set; }

    public DateTimeOffset? RetryAt { get; private set; }

    /// <summary>The relay refused reads (<c>CLOSED auth-required</c>); writes still go to it.</summary>
    public bool ReadImpossible { get; private set; }

    /// <summary>The last reason the relay gave (NOTICE, CLOSED, connect failure), for the per-relay status line.</summary>
    public string? LastMessage { get; private set; }

    public bool CanWrite => State is RelayLinkState.Subscribed or RelayLinkState.CaughtUp;

    /// <summary>Delay before retry number <paramref name="failures"/> (1-based): 1, 2, 4, 8, 16, then 30 s for ever.</summary>
    public static TimeSpan BackoffDelay(int failures) =>
        TimeSpan.FromSeconds(BackoffSeconds[Math.Clamp(failures, 1, BackoffSeconds.Length) - 1]);

    public bool IsDueToConnect(DateTimeOffset now) =>
        State == RelayLinkState.Disconnected || (State == RelayLinkState.Backoff && RetryAt <= now);

    public void OnConnectStarted()
    {
        State = RelayLinkState.Connecting;
        RetryAt = null;
    }

    /// <summary>The socket is open and the REQ has been sent.</summary>
    public void OnSubscribed()
    {
        State = RelayLinkState.Subscribed;
        Failures = 0;
        ReadImpossible = false;
    }

    public void OnEose()
    {
        if (State == RelayLinkState.Subscribed)
            State = RelayLinkState.CaughtUp;
    }

    public void OnClosed(string message)
    {
        LastMessage = message;
        if (message.StartsWith("auth-required:", StringComparison.Ordinal))
            ReadImpossible = true;
    }

    public void OnNotice(string message) => LastMessage = message;

    /// <summary>A connect failed (unreachable, HTTP 503, ...) or an open socket dropped: back off.</summary>
    public void OnDisconnected(DateTimeOffset now, string? reason = null)
    {
        Failures++;
        State = RelayLinkState.Backoff;
        RetryAt = now + BackoffDelay(Failures);
        if (reason is not null)
            LastMessage = reason;
    }

    public void Stop()
    {
        State = RelayLinkState.Disconnected;
        RetryAt = null;
    }
}

namespace TakEngine.Transport.Relay;

/// <summary>What <see cref="RelayLink.CheckLiveness"/> asks the shell to do.</summary>
public enum LivenessAction
{
    None,

    /// <summary>Send <see cref="SubscriptionPlan.Probe"/> now: the relay has been quiet for the idle window (or a check was asked for).</summary>
    SendProbe,

    /// <summary>The probe went unanswered: treat the socket as dropped (close it; the reader reports the drop and backs off).</summary>
    Drop,
}

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
/// Liveness (a socket that died without a close: a slept device, a NAT that dropped the flow): when nothing has arrived for
/// <see cref="IdleWindow"/> the link asks for one probe; if nothing arrives within <see cref="ProbeTimeout"/> after it, the link
/// asks for a drop, and the reconnect's REQ catches up (D-051).
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

    /// <summary>Quiet time after which a connected link probes the relay.</summary>
    public static readonly TimeSpan IdleWindow = TimeSpan.FromSeconds(60);

    /// <summary>How long a probe may go unanswered before the socket counts as dropped.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>When anything last arrived from the relay (or the subscribe, for a fresh socket).</summary>
    public DateTimeOffset? LastHeardAt { get; private set; }

    /// <summary>When the outstanding probe went out; null when none is waiting for an answer.</summary>
    public DateTimeOffset? ProbeSentAt { get; private set; }

    private bool _probeRequested;

    /// <summary>When <see cref="CheckLiveness"/> next has something to do; null while not connected.</summary>
    public DateTimeOffset? NextLivenessCheckAt =>
        !CanWrite || LastHeardAt is not DateTimeOffset heard ? null
        : ProbeSentAt is DateTimeOffset sent ? sent + ProbeTimeout
        : _probeRequested ? heard
        : heard + IdleWindow;

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
    public void OnSubscribed(DateTimeOffset now)
    {
        State = RelayLinkState.Subscribed;
        Failures = 0;
        ReadImpossible = false;
        LastHeardAt = now;
        ProbeSentAt = null;
        _probeRequested = false;
    }

    /// <summary>Any frame arrived from the relay (an event, EOSE, OK, NOTICE, CLOSED, ...): the socket is alive.</summary>
    public void OnFrame(DateTimeOffset now)
    {
        LastHeardAt = now;
        ProbeSentAt = null;
    }

    /// <summary>Probe at the next check instead of after the idle window (a tab or device just woke up). No-op while not connected
    /// or while a probe is already out.</summary>
    public void RequestProbe()
    {
        if (CanWrite && ProbeSentAt is null)
            _probeRequested = true;
    }

    /// <summary>
    /// Idle for <see cref="IdleWindow"/> (or a probe was requested): <see cref="LivenessAction.SendProbe"/>, once, and the probe is
    /// recorded as sent at <paramref name="now"/>. A probe unanswered for <see cref="ProbeTimeout"/>: <see cref="LivenessAction.Drop"/>.
    /// </summary>
    public LivenessAction CheckLiveness(DateTimeOffset now)
    {
        if (!CanWrite || LastHeardAt is not DateTimeOffset heard)
            return LivenessAction.None;
        if (ProbeSentAt is DateTimeOffset sent)
        {
            if (now - sent < ProbeTimeout)
                return LivenessAction.None;
            LastMessage = $"no answer from the relay within {ProbeTimeout.TotalSeconds:0} s of a probe (silent drop)";
            ProbeSentAt = null;
            return LivenessAction.Drop;
        }
        if (!_probeRequested && now - heard < IdleWindow)
            return LivenessAction.None;
        _probeRequested = false;
        ProbeSentAt = now;
        return LivenessAction.SendProbe;
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
        ProbeSentAt = null;
        _probeRequested = false;
        RetryAt = now + BackoffDelay(Failures);
        if (reason is not null)
            LastMessage = reason;
    }

    public void Stop()
    {
        State = RelayLinkState.Disconnected;
        RetryAt = null;
        ProbeSentAt = null;
        _probeRequested = false;
    }
}

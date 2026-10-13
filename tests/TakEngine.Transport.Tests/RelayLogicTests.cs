using System.Security.Cryptography;
using System.Text;
using TakEngine.Crypto;
using TakEngine.Transport.Nostr;
using TakEngine.Transport.Relay;

namespace TakEngine.Transport.Tests;

public class RelayLinkTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri Url = new("wss://r1");

    [Fact]
    public void HappyPath_ConnectingSubscribedCaughtUp()
    {
        var link = new RelayLink(Url);
        Assert.Equal(RelayLinkState.Disconnected, link.State);
        Assert.True(link.IsDueToConnect(T0));
        Assert.False(link.CanWrite);

        link.OnConnectStarted();
        Assert.Equal(RelayLinkState.Connecting, link.State);
        Assert.False(link.IsDueToConnect(T0));
        link.OnSubscribed(T0);
        Assert.Equal(RelayLinkState.Subscribed, link.State);
        Assert.True(link.CanWrite);
        link.OnEose();
        Assert.Equal(RelayLinkState.CaughtUp, link.State);
        link.OnEose();
        Assert.Equal(RelayLinkState.CaughtUp, link.State);
    }

    [Fact]
    public void Backoff_Is_1_2_4_8_16_30_30_Seconds()
    {
        var link = new RelayLink(Url);
        var delays = new List<double>();
        DateTimeOffset now = T0;
        for (int i = 0; i < 7; i++)
        {
            link.OnConnectStarted();
            link.OnDisconnected(now, "HTTP 503");
            Assert.Equal(RelayLinkState.Backoff, link.State);
            delays.Add((link.RetryAt!.Value - now).TotalSeconds);
            Assert.False(link.IsDueToConnect(link.RetryAt.Value.AddMilliseconds(-1)));
            Assert.True(link.IsDueToConnect(link.RetryAt.Value));
            now = link.RetryAt.Value;
        }

        Assert.Equal([1, 2, 4, 8, 16, 30, 30], delays);
        Assert.Equal("HTTP 503", link.LastMessage);
    }

    [Fact]
    public void Backoff_ResetsAfterASuccessfulSubscribe()
    {
        var link = new RelayLink(Url);
        link.OnDisconnected(T0);
        link.OnDisconnected(T0);
        link.OnDisconnected(T0);
        Assert.Equal(3, link.Failures);

        link.OnConnectStarted();
        link.OnSubscribed(T0);
        link.OnDisconnected(T0);

        Assert.Equal(T0.AddSeconds(1), link.RetryAt);
    }

    [Fact]
    public void ClosedAuthRequired_MakesReadImpossible_ButWritable()
    {
        var link = new RelayLink(Url);
        link.OnConnectStarted();
        link.OnSubscribed(T0);

        link.OnClosed("auth-required: sign in");

        Assert.True(link.ReadImpossible);
        Assert.True(link.CanWrite);
        Assert.Equal(RelayLinkState.Subscribed, link.State);

        link.OnClosed("error: other");
        Assert.True(link.ReadImpossible);
        link.OnNotice("hello");
        Assert.Equal("hello", link.LastMessage);
    }

    [Fact]
    public void OtherClosedReasons_DontBlockReads_AndAResubscribeClearsReadImpossible()
    {
        var link = new RelayLink(Url);
        link.OnClosed("error: shutting down");
        Assert.False(link.ReadImpossible);

        link.OnClosed("auth-required: x");
        link.OnDisconnected(T0);
        link.OnConnectStarted();
        link.OnSubscribed(T0);
        Assert.False(link.ReadImpossible);
    }

    [Fact]
    public void Stop_IsDisconnected()
    {
        var link = new RelayLink(Url);
        link.OnDisconnected(T0);
        link.Stop();
        Assert.Equal(RelayLinkState.Disconnected, link.State);
        Assert.Null(link.RetryAt);
    }

    private static RelayLink Subscribed()
    {
        var link = new RelayLink(Url);
        link.OnConnectStarted();
        link.OnSubscribed(T0);
        return link;
    }

    [Fact]
    public void SilentRelay_IsProbedAfterTheIdleWindow_AndDroppedWhenTheProbeIsNotAnswered()
    {
        RelayLink link = Subscribed();
        Assert.Equal(T0 + RelayLink.IdleWindow, link.NextLivenessCheckAt);
        Assert.Equal(TimeSpan.FromSeconds(60), RelayLink.IdleWindow);
        Assert.Equal(TimeSpan.FromSeconds(10), RelayLink.ProbeTimeout);

        Assert.Equal(LivenessAction.None, link.CheckLiveness(T0.AddSeconds(59)));
        Assert.Equal(LivenessAction.SendProbe, link.CheckLiveness(T0.AddSeconds(60)));
        Assert.Equal(T0.AddSeconds(60), link.ProbeSentAt);
        Assert.Equal(T0.AddSeconds(70), link.NextLivenessCheckAt);
        Assert.Equal(LivenessAction.None, link.CheckLiveness(T0.AddSeconds(61))); // one probe per idle window
        Assert.Equal(LivenessAction.None, link.CheckLiveness(T0.AddSeconds(70).AddMilliseconds(-1)));
        Assert.Equal(RelayLinkState.Subscribed, link.State);

        Assert.Equal(LivenessAction.Drop, link.CheckLiveness(T0.AddSeconds(70)));
        Assert.Contains("no answer", link.LastMessage);
        // The shell closes the socket; its reader ends and reports the drop like any other.
        link.OnDisconnected(T0.AddSeconds(70));
        Assert.Equal(RelayLinkState.Backoff, link.State);
        Assert.Equal(T0.AddSeconds(71), link.RetryAt);
        Assert.Null(link.NextLivenessCheckAt);
        Assert.Equal(LivenessAction.None, link.CheckLiveness(T0.AddSeconds(500)));

        link.OnConnectStarted();
        link.OnSubscribed(T0.AddSeconds(71));
        Assert.Null(link.ProbeSentAt);
        Assert.Equal(T0.AddSeconds(131), link.NextLivenessCheckAt);
    }

    [Fact]
    public void AnyFrame_ResetsTheIdleClock_AndAnswersAPendingProbe()
    {
        RelayLink link = Subscribed();
        link.OnEose();
        link.OnFrame(T0.AddSeconds(50));
        Assert.Equal(LivenessAction.None, link.CheckLiveness(T0.AddSeconds(60)));
        Assert.Equal(T0.AddSeconds(110), link.NextLivenessCheckAt);
        Assert.Equal(LivenessAction.SendProbe, link.CheckLiveness(T0.AddSeconds(110)));

        link.OnFrame(T0.AddSeconds(111)); // the probe's EOSE, or anything else
        Assert.Null(link.ProbeSentAt);
        Assert.Equal(LivenessAction.None, link.CheckLiveness(T0.AddSeconds(125)));
        Assert.Equal(RelayLinkState.CaughtUp, link.State);
        Assert.Equal(T0.AddSeconds(171), link.NextLivenessCheckAt);
    }

    [Fact]
    public void RequestProbe_ProbesAtOnce_ButNeverTwiceAndNeverWhileDown()
    {
        RelayLink link = Subscribed();
        link.RequestProbe();
        Assert.Equal(T0, link.NextLivenessCheckAt);
        Assert.Equal(LivenessAction.SendProbe, link.CheckLiveness(T0.AddSeconds(5)));
        link.RequestProbe(); // the tab became visible again while the probe is out
        Assert.Equal(LivenessAction.None, link.CheckLiveness(T0.AddSeconds(6)));
        Assert.Equal(LivenessAction.Drop, link.CheckLiveness(T0.AddSeconds(15)));

        var down = new RelayLink(Url);
        down.OnDisconnected(T0);
        down.RequestProbe();
        Assert.Null(down.NextLivenessCheckAt);
        Assert.Equal(LivenessAction.None, down.CheckLiveness(T0.AddSeconds(100)));
    }
}

public class PublishTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri R1 = new("wss://r1"), R2 = new("wss://r2");

    [Fact]
    public void Sent_WhenAnyRelaySaysOkTrue()
    {
        var tracker = new PublishTracker();
        tracker.Track("e", [R1, R2]);
        Assert.Equal(PublishOutcome.Pending, tracker.Outcome("e"));

        tracker.OnOk("e", R1, false, "blocked: no", T0);
        Assert.Equal(PublishOutcome.Pending, tracker.Outcome("e"));
        tracker.OnOk("e", R2, true, "", T0);

        Assert.Equal(PublishOutcome.Sent, tracker.Outcome("e"));
        Assert.Equal(RelayPublishStatus.Refused, tracker.PerRelay("e")[R1].Status);
        Assert.Equal("blocked: no", tracker.PerRelay("e")[R1].Reason);
        Assert.Equal(RelayPublishStatus.Sent, tracker.PerRelay("e")[R2].Status);
    }

    [Fact]
    public void Retrack_StartsAnEventOverAsPending()
    {
        var tracker = new PublishTracker();
        tracker.Track("e", [R1, R2]);
        tracker.OnOk("e", R1, true, "", T0);
        tracker.OnOk("e", R2, false, "blocked: y", T0);
        tracker.Track("e", [R1, R2]); // a plain Track keeps the state

        Assert.Equal(PublishOutcome.Sent, tracker.Outcome("e"));

        tracker.Retrack("e", [R1, R2]);

        Assert.Equal(PublishOutcome.Pending, tracker.Outcome("e"));
        Assert.All(tracker.PerRelay("e").Values, s => Assert.Equal(RelayPublishStatus.Pending, s.Status));
    }

    [Fact]
    public void Failed_WhenEveryRelayRefuses()
    {
        var tracker = new PublishTracker();
        tracker.Track("e", [R1, R2]);
        tracker.OnOk("e", R1, false, "invalid: x", T0);
        tracker.OnOk("e", R2, false, "blocked: y", T0);

        Assert.Equal(PublishOutcome.Failed, tracker.Outcome("e"));
    }

    [Fact]
    public void RateLimited_StaysPending_WithABackedOffRetry()
    {
        var tracker = new PublishTracker();
        tracker.Track("e", [R1]);

        tracker.OnOk("e", R1, false, "rate-limited: slow down", T0);

        Assert.Equal(PublishOutcome.Pending, tracker.Outcome("e"));
        RelayPublishState state = tracker.PerRelay("e")[R1];
        Assert.Equal(RelayPublishStatus.Pending, state.Status);
        Assert.Equal("rate-limited: slow down", state.Reason);
        Assert.Equal(T0.AddSeconds(2), state.RetryAt);
        Assert.Empty(tracker.DueRetries(T0.AddSeconds(1)));
        Assert.Equal([("e", R1)], tracker.DueRetries(T0.AddSeconds(2)));
        Assert.Equal(T0.AddSeconds(2), tracker.NextRetryAt());

        tracker.OnResent("e", R1);
        Assert.Empty(tracker.DueRetries(T0.AddSeconds(60)));
        tracker.OnOk("e", R1, false, "rate-limited: again", T0.AddSeconds(2));
        Assert.Equal(T0.AddSeconds(6), tracker.PerRelay("e")[R1].RetryAt);
    }

    [Fact]
    public void NoAnswer_StaysPending_WithABackedOffRetry_SharedWithRateLimits()
    {
        var tracker = new PublishTracker();
        tracker.Track("e", [R1, R2]);

        tracker.OnNoAnswer("e", R1, T0);

        Assert.Equal(PublishOutcome.Pending, tracker.Outcome("e"));
        Assert.Equal(new RelayPublishState(RelayPublishStatus.Pending, PublishTracker.NoAnswerReason, T0.AddSeconds(2), 1), tracker.PerRelay("e")[R1]);
        tracker.OnResent("e", R1);
        tracker.OnOk("e", R1, false, "rate-limited: x", T0.AddSeconds(10));
        Assert.Equal(T0.AddSeconds(14), tracker.PerRelay("e")[R1].RetryAt); // the second retry of either kind waits 4 s

        tracker.OnOk("e", R2, false, "blocked: y", T0);
        tracker.OnNoAnswer("e", R2, T0); // final states don't come back
        tracker.OnNoAnswer("unknown", R1, T0);
        Assert.Equal(RelayPublishStatus.Refused, tracker.PerRelay("e")[R2].Status);
        tracker.OnOk("e", R1, true, "", T0);
        tracker.OnNoAnswer("e", R1, T0);
        Assert.Equal(RelayPublishStatus.Sent, tracker.PerRelay("e")[R1].Status);
    }

    [Fact]
    public void Reconnect_MakesANoAnswerRetryDueAtOnce_ButKeepsRateLimitBackoffs()
    {
        var tracker = new PublishTracker();
        tracker.Track("quiet", [R1, R2]);
        tracker.Track("limited", [R1]);
        for (int i = 0; i < 4; i++)
            tracker.OnNoAnswer("quiet", R1, T0); // sent into a socket that died silently: the retry drifts to 30 s
        tracker.OnNoAnswer("quiet", R2, T0);
        tracker.OnOk("limited", R1, false, "rate-limited: x", T0);

        tracker.OnReconnected(R1);

        Assert.Null(tracker.PerRelay("quiet")[R1].RetryAt); // the new socket gets it as soon as the budget allows
        Assert.Equal(PublishTracker.NoAnswerReason, tracker.PerRelay("quiet")[R1].Reason);
        Assert.Equal(T0.AddSeconds(2), tracker.PerRelay("quiet")[R2].RetryAt); // another relay's socket is untouched
        Assert.Equal(T0.AddSeconds(2), tracker.PerRelay("limited")[R1].RetryAt);
    }

    [Fact]
    public void Track_IsIdempotent_AndNextRetryIsTheEarliest()
    {
        var tracker = new PublishTracker();
        tracker.Track("a", [R1]);
        tracker.OnOk("a", R1, false, "rate-limited: x", T0);
        tracker.OnOk("a", R1, false, "rate-limited: x", T0); // second limit: 4 s
        tracker.Track("a", [R1, R2]); // no reset
        tracker.Track("b", [R1]);
        tracker.OnOk("b", R1, false, "rate-limited: x", T0); // 2 s

        Assert.Equal(T0.AddSeconds(4), tracker.PerRelay("a")[R1].RetryAt);
        Assert.Single(tracker.PerRelay("a"));
        Assert.Equal(T0.AddSeconds(2), tracker.NextRetryAt());
    }

    [Fact]
    public void LateRefusal_NeverUnsends_AndUnknownIdsAreIgnored()
    {
        var tracker = new PublishTracker();
        tracker.Track("e", [R1]);
        tracker.OnOk("e", R1, true, "", T0);
        tracker.OnOk("e", R1, false, "blocked: late", T0);
        tracker.OnOk("other", R1, true, "", T0);
        tracker.OnOk("e", R2, true, "", T0);

        Assert.Equal(PublishOutcome.Sent, tracker.Outcome("e"));
        Assert.Equal(PublishOutcome.Pending, tracker.Outcome("other"));
        Assert.False(tracker.IsTracked("other"));
        Assert.Null(tracker.NextRetryAt());
    }
}

public class PublishBudgetTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AtMostFourPerSlidingTenSeconds()
    {
        var budget = new PublishBudget();
        for (int i = 0; i < 4; i++)
            Assert.True(budget.TryTake(T0.AddSeconds(i)));

        Assert.False(budget.TryTake(T0.AddSeconds(4)));
        Assert.False(budget.TryTake(T0.AddSeconds(9.999)));
        Assert.Equal(T0.AddSeconds(10), budget.NextAvailable(T0.AddSeconds(5)));
        Assert.True(budget.TryTake(T0.AddSeconds(10))); // the first one aged out
        Assert.False(budget.TryTake(T0.AddSeconds(10.5)));
        Assert.True(budget.TryTake(T0.AddSeconds(11)));
        Assert.Equal(T0.AddSeconds(12), budget.NextAvailable(T0.AddSeconds(11)));
        Assert.Equal(T0.AddSeconds(30), budget.NextAvailable(T0.AddSeconds(30))); // everything aged out
    }

    [Fact]
    public void ARetry_LeavesTheLastSlotForAFirstSend()
    {
        var budget = new PublishBudget();
        for (int i = 0; i < 3; i++)
            Assert.True(budget.TryTake(T0.AddSeconds(i), reserve: PublishBudget.FirstSendReserve));

        Assert.False(budget.TryTake(T0.AddSeconds(3), reserve: PublishBudget.FirstSendReserve));
        Assert.Equal(T0.AddSeconds(10), budget.NextAvailable(T0.AddSeconds(3), reserve: PublishBudget.FirstSendReserve));
        Assert.Equal(T0.AddSeconds(3), budget.NextAvailable(T0.AddSeconds(3)));
        Assert.True(budget.TryTake(T0.AddSeconds(3))); // a first send still goes out at once
        Assert.False(budget.TryTake(T0.AddSeconds(4)));
        Assert.Equal(T0.AddSeconds(11), budget.NextAvailable(T0.AddSeconds(4), reserve: PublishBudget.FirstSendReserve));
    }

    [Fact]
    public void NeverMoreThanFourInAnyWindow_Randomised()
    {
        int seed = 1234;
        var random = new Random(seed);
        var budget = new PublishBudget();
        var taken = new List<DateTimeOffset>();
        DateTimeOffset now = T0;
        for (int i = 0; i < 2000; i++)
        {
            now = now.AddMilliseconds(random.Next(0, 3000));
            if (budget.TryTake(now))
                taken.Add(now);
        }

        foreach (DateTimeOffset start in taken)
            Assert.True(taken.Count(t => t >= start && t < start + PublishBudget.Window) <= 4, $"seed {seed}");
        Assert.True(budget.NextAvailable(now) >= now);
    }
}

public class SubscriptionPlanTests
{
    private static readonly Guid Game = Guid.Parse("0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e");
    private static readonly PublicKey Me = Key("me"), Opponent = Key("opponent");

    private static PublicKey Key(string label) => SecretKey.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes(label))).PublicKey;

    [Fact]
    public void Build_TwoFilters_WithSinceMinus30s()
    {
        RelayMessage.Req req = SubscriptionPlan.Build(Game, Me, Opponent, maxCreatedAt: 1_000);

        Assert.Equal("tak-0b7e3d4c", req.SubscriptionId);
        Assert.Equal(2, req.Filters.Count);
        NostrFilter fromOpponent = req.Filters[0], mine = req.Filters[1];
        Assert.Equal([3825], fromOpponent.Kinds!);
        Assert.Equal([Opponent.ToHex()], fromOpponent.Authors!);
        Assert.Equal([Me.ToHex()], fromOpponent.TagP!);
        Assert.Equal(["0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e"], fromOpponent.TagG!);
        Assert.Equal(970, fromOpponent.Since);
        Assert.Equal(500, fromOpponent.Limit);
        Assert.Equal([3825], mine.Kinds!);
        Assert.Equal([Me.ToHex()], mine.Authors!);
        Assert.Null(mine.TagP);
        Assert.Equal(["0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e"], mine.TagG!);
        Assert.Equal(970, mine.Since);
        Assert.Equal(500, mine.Limit);
    }

    [Fact]
    public void ColdRebuild_HasNoSince_AndInvitingHostDropsAuthors()
    {
        RelayMessage.Req req = SubscriptionPlan.Build(Game, Me, opponent: null, maxCreatedAt: null);

        Assert.Null(req.Filters[0].Since);
        Assert.Null(req.Filters[1].Since);
        Assert.Null(req.Filters[0].Authors);
        Assert.Equal([Me.ToHex()], req.Filters[1].Authors!);
        Assert.Equal(0, SubscriptionPlan.Build(Game, Me, Opponent, maxCreatedAt: 10).Filters[0].Since);
    }

    [Fact]
    public void IdLookups_AreChunkedToFive()
    {
        var ids = Enumerable.Range(0, 12).Select(i => i.ToString("x64")).ToList();

        IReadOnlyList<NostrFilter> filters = SubscriptionPlan.IdLookups([.. ids, ids[0]]);

        Assert.Equal([5, 5, 2], filters.Select(f => f.Ids!.Count));
        Assert.Equal(ids, filters.SelectMany(f => f.Ids!));
    }

    [Fact]
    public void Probe_IsAReqThatMatchesNothing_OnItsOwnSubscription_AndItsClose()
    {
        RelayMessage.Req probe = SubscriptionPlan.Probe();

        Assert.Equal(SubscriptionPlan.ProbeSubscriptionId, probe.SubscriptionId);
        Assert.NotEqual(SubscriptionPlan.SubscriptionId(Game), probe.SubscriptionId);
        Assert.Equal("[\"REQ\",\"tak-probe\",{\"ids\":[\"" + new string('0', 64) + "\"],\"kinds\":[3825],\"limit\":0}]", probe.Format());
    }
}

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
        link.OnSubscribed();
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
        link.OnSubscribed();
        link.OnDisconnected(T0);

        Assert.Equal(T0.AddSeconds(1), link.RetryAt);
    }

    [Fact]
    public void ClosedAuthRequired_MakesReadImpossible_ButWritable()
    {
        var link = new RelayLink(Url);
        link.OnConnectStarted();
        link.OnSubscribed();

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
        link.OnSubscribed();
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
}

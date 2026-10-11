using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using TakEngine.Crypto;
using TakEngine.Testing;
using TakEngine.Transport.Nostr;
using TakEngine.Transport.Relay;

namespace TakEngine.Transport.Tests;

/// <summary>F-034 part 3: the relay pool over two in-memory relays and a fake clock.</summary>
public sealed class RelayPoolTests : IAsyncDisposable
{
    private static readonly Guid Game = Guid.Parse("0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e");
    private static readonly SecretKey Me = Key("me"), Opponent = Key("opponent");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryRelay _r1, _r2;
    private readonly RelayPool _pool;
    private int _created;

    public RelayPoolTests()
    {
        _r1 = new InMemoryRelay("wss://r1", _time);
        _r2 = new InMemoryRelay("wss://r2", _time);
        _pool = new RelayPool([_r1.Url, _r2.Url], new FakeRelaySocketFactory(_r1, _r2), _time, Me.PublicKey.ToHex());
    }

    public async ValueTask DisposeAsync() => await _pool.DisposeAsync();

    private static SecretKey Key(string label) => SecretKey.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes("pool " + label)));

    private NostrEvent Evt(SecretKey author, long? createdAt = null) =>
        NostrEvents.Sign(author, createdAt ?? _time.GetUtcNow().ToUnixTimeSeconds() + _created++, 3825,
            [new[] { "p", (author == Me ? Opponent : Me).PublicKey.ToHex() }, new[] { "g", Game.ToString("D") }], "c" + _created, new byte[32]);

    private RelayMessage.Req? Subscription() =>
        SubscriptionPlan.Build(Game, Me.PublicKey, Opponent.PublicKey, _pool.MaxCreatedAt == 0 ? null : _pool.MaxCreatedAt);

    private void Start() => _pool.Start(Subscription);

    /// <summary>Waits (real time, for async hand-offs only; protocol time is the fake clock) until <paramref name="condition"/> holds.</summary>
    private static async Task Eventually(Func<bool> condition, string what)
    {
        for (int i = 0; i < 500 && !condition(); i++)
            await Task.Delay(5);
        Assert.True(condition(), "timed out waiting for: " + what);
    }

    private async Task Advance(TimeSpan by)
    {
        _time.Advance(by);
        await Task.Delay(20); // let timer continuations run
    }

    private List<ReceivedEvent> Drain()
    {
        var list = new List<ReceivedEvent>();
        while (_pool.Events.TryRead(out ReceivedEvent? e))
            list.Add(e);
        return list;
    }

    private Task Subscribed() => Eventually(() => _pool.Links.All(l => l.State == RelayLinkState.CaughtUp), "both relays caught up");

    [Fact]
    public async Task Publish_IsSent_WhenAnyRelaySaysOk()
    {
        _r1.Refuse(_ => true, "blocked: not here");
        Start();
        await Subscribed();
        NostrEvent e = Evt(Me);

        _pool.Publish(e);

        await Eventually(() => _pool.Outcome(e.Id) == PublishOutcome.Sent, "sent");
        await Eventually(() => _pool.PerRelay(e.Id)[_r1.Url].Status == RelayPublishStatus.Refused, "r1 refused");
        Assert.Equal("blocked: not here", _pool.PerRelay(e.Id)[_r1.Url].Reason);
        Assert.Equal(RelayPublishStatus.Sent, _pool.PerRelay(e.Id)[_r2.Url].Status);
    }

    [Fact]
    public async Task Publish_Fails_WhenEveryRelayRefuses()
    {
        _r1.Refuse(_ => true, "blocked: a");
        _r2.Refuse(_ => true, "blocked: b");
        Start();
        await Subscribed();
        NostrEvent e = Evt(Me);

        _pool.Publish(e);

        await Eventually(() => _pool.Outcome(e.Id) == PublishOutcome.Failed, "failed");
    }

    [Fact]
    public async Task RateLimited_IsRefused_RetriedWithBackoff_AndNeverExceedsTheBudget()
    {
        // r1 refuses each event's first attempt with rate-limited, like damus in a burst; r2 is down so only r1 can send.
        var attempts = new Dictionary<string, int>();
        _r1.Refuse(e =>
        {
            int n = attempts.GetValueOrDefault(e.Id) + 1;
            attempts[e.Id] = n;
            return n == 1;
        }, "rate-limited: slow down");
        _r2.Partition();
        Start();
        await Eventually(() => _pool.Links[0].State == RelayLinkState.CaughtUp, "r1 caught up");
        var events = Enumerable.Range(0, 10).Select(_ => Evt(Me)).ToList();

        foreach (NostrEvent e in events)
            _pool.Publish(e);
        await Eventually(() => events.Any(e => _pool.PerRelay(e.Id)[_r1.Url].Reason?.StartsWith("rate-limited") == true), "a rate-limited refusal");
        Assert.Equal(PublishOutcome.Pending, _pool.Outcome(events[0].Id));

        for (int i = 0; i < 120 && !events.All(e => _pool.Outcome(e.Id) == PublishOutcome.Sent); i++)
            await Advance(TimeSpan.FromSeconds(1));

        Assert.All(events, e => Assert.Equal(PublishOutcome.Sent, _pool.Outcome(e.Id)));
        var sends = _r1.FrameLog.Where(f => f.Frame.StartsWith("[\"EVENT\"", StringComparison.Ordinal)).Select(f => f.At).ToList();
        Assert.Equal(20, sends.Count); // every event twice: the refused attempt and the retry
        foreach (DateTimeOffset start in sends)
            Assert.True(sends.Count(t => t >= start && t < start + TimeSpan.FromSeconds(10)) <= 4, $"more than 4 events in the 10 s from {start:O}");
    }

    [Fact]
    public async Task DroppedSocket_ReconnectsWithBackoff_AndResubscribesWithSince()
    {
        NostrEvent old = Evt(Opponent, createdAt: 1_000_000);
        _r1.Seed(old);
        Start();
        await Subscribed();
        await Eventually(() => _pool.MaxCreatedAt == 1_000_000, "history delivered");
        Assert.Single(Drain());

        _r1.DropConnections();
        await Eventually(() => _pool.Links[0].State == RelayLinkState.Backoff, "r1 backing off");
        Assert.Equal(_time.GetUtcNow().AddSeconds(1), _pool.Links[0].RetryAt);
        await Advance(TimeSpan.FromMilliseconds(900));
        Assert.Equal(RelayLinkState.Backoff, _pool.Links[0].State);
        await Advance(TimeSpan.FromMilliseconds(100));
        await Eventually(() => _pool.Links[0].State == RelayLinkState.CaughtUp, "r1 back");

        var reqs = _r1.ReceivedFrames.Where(f => f.StartsWith("[\"REQ\"", StringComparison.Ordinal)).Select(f => (RelayMessage.Req)RelayMessage.Parse(f)).ToList();
        Assert.Equal(2, reqs.Count);
        Assert.Null(reqs[0].Filters[0].Since);
        Assert.Equal(1_000_000 - 30, reqs[1].Filters[0].Since);
        Assert.Equal(1_000_000 - 30, reqs[1].Filters[1].Since);
        Assert.Empty(Drain()); // the replayed history is not delivered again
    }

    [Fact]
    public async Task Upgrade503_IsRetried_WithBackoff()
    {
        _r1.RejectUpgrade(503, times: 2);
        Start();
        await Eventually(() => _pool.Links[0].State == RelayLinkState.Backoff && _pool.Links[0].Failures == 1, "first 503");
        Assert.Contains("503", _pool.Links[0].LastMessage);
        await Advance(TimeSpan.FromSeconds(1));
        await Eventually(() => _pool.Links[0].Failures == 2, "second 503");
        Assert.Equal(_time.GetUtcNow().AddSeconds(2), _pool.Links[0].RetryAt);
        await Advance(TimeSpan.FromSeconds(2));

        await Eventually(() => _pool.Links[0].State == RelayLinkState.CaughtUp, "connected on the third try");
        Assert.Equal(3, _r1.ConnectAttempts);
        Assert.Equal(0, _pool.Links[0].Failures);
    }

    [Fact]
    public async Task AuthRequired_MarksOneRelayReadImpossible_TheOtherCarriesOn()
    {
        _r1.AuthRequiredForReads();
        NostrEvent fromOpponent = Evt(Opponent);
        _r1.Seed(fromOpponent);
        _r2.Seed(fromOpponent);
        Start();

        await Eventually(() => _pool.Links[0].ReadImpossible, "r1 read-impossible");
        await Eventually(() => _pool.Links[1].State == RelayLinkState.CaughtUp, "r2 caught up");
        ReceivedEvent got = Assert.Single(Drain());
        Assert.Equal(_r2.Url, got.Relay);
        Assert.StartsWith("auth-required", _pool.Links[0].LastMessage);

        NostrEvent mine = Evt(Me);
        _pool.Publish(mine);
        await Eventually(() => _pool.PerRelay(mine.Id).Values.All(s => s.Status == RelayPublishStatus.Sent), "written to both");
    }

    [Fact]
    public async Task SameEventFromTwoRelays_IsDeliveredOnce()
    {
        Start();
        await Subscribed();
        NostrEvent e = Evt(Opponent);

        _r1.Seed(e);
        _r2.Seed(e);
        _r1.Inject(new RelayMessage.Event("tak-0b7e3d4c", e).Format());
        _r2.Inject(new RelayMessage.Event("tak-0b7e3d4c", e).Format());
        _r2.Inject(new RelayMessage.Event("tak-0b7e3d4c", e).Format());

        await Eventually(() => _pool.MaxCreatedAt == e.CreatedAt, "delivered");
        await Task.Delay(30);
        ReceivedEvent got = Assert.Single(Drain());
        Assert.False(got.IsOwn);
    }

    [Fact]
    public async Task OwnEcho_IsDelivered_FlaggedAsOwn()
    {
        // Published by this key before a restart: it comes back from history and the exchange must see it is ours.
        NostrEvent earlier = Evt(Me);
        _r1.Seed(earlier);
        Start();

        await Eventually(() => _pool.MaxCreatedAt == earlier.CreatedAt, "history delivered");
        ReceivedEvent got = Assert.Single(Drain());
        Assert.True(got.IsOwn);
    }

    [Fact]
    public async Task OwnFreshPublish_IsNotEchoedBack()
    {
        Start();
        await Subscribed();
        NostrEvent e = Evt(Me);

        _pool.Publish(e);

        await Eventually(() => _pool.Outcome(e.Id) == PublishOutcome.Sent, "sent");
        await Task.Delay(30);
        Assert.Empty(Drain());
    }

    [Fact]
    public async Task NoEventDeliveredTwice_AcrossReconnects()
    {
        Start();
        await Subscribed();
        var all = new List<ReceivedEvent>();
        for (int round = 0; round < 3; round++)
        {
            NostrEvent e = Evt(Opponent);
            _r1.Seed(e);
            _r2.Seed(e);
            _r1.DropConnections();
            _r2.DropConnections();
            await Eventually(() => _pool.Links.All(l => l.State == RelayLinkState.Backoff), "both backing off");
            await Advance(TimeSpan.FromSeconds(1));
            await Subscribed();
            await Eventually(() => _pool.MaxCreatedAt == e.CreatedAt, "round event delivered");
            all.AddRange(Drain());
        }

        Assert.Equal(3, all.Count);
        Assert.Equal(3, all.Select(r => r.Event.Id).Distinct().Count());
    }

    [Fact]
    public async Task UnacknowledgedPublish_IsResent_AfterReconnect()
    {
        _r2.Partition();
        Start();
        await Eventually(() => _pool.Links[0].State == RelayLinkState.CaughtUp, "r1 up");
        NostrEvent e = Evt(Me);
        _r1.Withhold(e.Id); // irrelevant to OK; just keeps it out of r1's live stream

        _pool.Publish(e);
        await Eventually(() => _pool.Outcome(e.Id) == PublishOutcome.Sent, "sent on r1");
        Assert.Equal(RelayPublishStatus.Pending, _pool.PerRelay(e.Id)[_r2.Url].Status);

        _r2.Heal();
        for (int i = 0; i < 40 && _pool.PerRelay(e.Id)[_r2.Url].Status != RelayPublishStatus.Sent; i++)
            await Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(RelayPublishStatus.Sent, _pool.PerRelay(e.Id)[_r2.Url].Status);
        Assert.Single(_r2.StoredEvents);
    }

    [Fact]
    public async Task ForgedEvents_AreDroppedAndCounted_AndCannotShadowTheRealOne()
    {
        Start();
        await Subscribed();
        NostrEvent real = Evt(Opponent);
        var forged = new NostrEvent { Id = real.Id, Pubkey = real.Pubkey, CreatedAt = real.CreatedAt, Kind = real.Kind, Tags = real.Tags, Content = "forged", Sig = real.Sig };

        _r1.Inject(new RelayMessage.Event("tak-0b7e3d4c", forged).Format());
        _r1.Inject("not even json");
        await Eventually(() => _pool.InvalidEventsDropped(_r1.Url) == 1, "forgery counted");
        _r2.Inject(new RelayMessage.Event("tak-0b7e3d4c", real).Format());

        await Eventually(() => _pool.MaxCreatedAt == real.CreatedAt, "real one delivered");
        Assert.Equal(real.Content, Assert.Single(Drain()).Event.Content);
    }
}

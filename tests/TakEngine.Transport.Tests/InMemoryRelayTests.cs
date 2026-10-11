using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using TakEngine.Crypto;
using TakEngine.Testing;
using TakEngine.Transport.Nostr;
using TakEngine.Transport.Relay;

namespace TakEngine.Transport.Tests;

/// <summary>Smoke tests for the fake relay every relay-client and game test stands on (F-034).</summary>
public class InMemoryRelayTests
{
    private const string Url = "wss://fake.relay";
    private static readonly SecretKey Alice = SecretKey.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes("relay test alice")));
    private static readonly SecretKey Bob = SecretKey.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes("relay test bob")));

    private static NostrEvent Evt(SecretKey key, long createdAt, string content = "x", string g = "game") =>
        NostrEvents.Sign(key, createdAt, 3825, [new[] { "p", Bob.PublicKey.ToHex() }, new[] { "g", g }], content, new byte[32]);

    private static async Task<FakeRelaySocket> Connect(InMemoryRelay relay)
    {
        var socket = (FakeRelaySocket)new FakeRelaySocketFactory(relay).Create(relay.Url);
        await socket.ConnectAsync(CancellationToken.None);
        return socket;
    }

    private static async Task<List<RelayMessage>> Send(FakeRelaySocket socket, RelayMessage message)
    {
        Assert.True(await socket.SendAsync(message.Format(), CancellationToken.None));
        return socket.Drain();
    }

    private static RelayMessage.Req Req(string sub, NostrFilter filter) => new(sub, [filter]);

    [Fact]
    public async Task ValidEvent_IsStored_AndAcknowledged()
    {
        var relay = new InMemoryRelay(Url);
        var socket = await Connect(relay);
        NostrEvent e = Evt(Alice, 100);

        var replies = await Send(socket, new RelayMessage.Publish(e));

        Assert.Equal(new RelayMessage.Ok(e.Id, true, ""), Assert.Single(replies));
        Assert.Single(relay.StoredEvents);
        Assert.Equal("duplicate", Assert.IsType<RelayMessage.Ok>(Assert.Single(await Send(socket, new RelayMessage.Publish(e)))).Prefix);
        Assert.Single(relay.StoredEvents);
    }

    [Fact]
    public async Task InvalidIdOrSig_IsRefusedInvalid()
    {
        var relay = new InMemoryRelay(Url);
        var socket = await Connect(relay);
        NostrEvent e = Evt(Alice, 100);
        e.Content = "tampered";

        var ok = Assert.IsType<RelayMessage.Ok>(Assert.Single(await Send(socket, new RelayMessage.Publish(e))));

        Assert.False(ok.Accepted);
        Assert.Equal("invalid", ok.Prefix);
        Assert.Empty(relay.StoredEvents);
    }

    [Fact]
    public async Task Req_ReturnsHistoryNewestFirst_ThenEose_ThenLive()
    {
        var relay = new InMemoryRelay(Url);
        NostrEvent old = Evt(Alice, 100), mid = Evt(Alice, 200), other = Evt(Bob, 300);
        relay.Seed(old);
        relay.Seed(mid);
        relay.Seed(other);
        var socket = await Connect(relay);

        var replies = await Send(socket, Req("s", new NostrFilter { Authors = [Alice.PublicKey.ToHex()] }));

        Assert.Equal([mid.Id, old.Id], replies.OfType<RelayMessage.Event>().Select(m => m.NostrEvent.Id));
        Assert.IsType<RelayMessage.Eose>(replies[^1]);

        var publisher = await Connect(relay);
        NostrEvent live = Evt(Alice, 400);
        await Send(publisher, new RelayMessage.Publish(live));
        Assert.Equal(live.Id, Assert.IsType<RelayMessage.Event>(Assert.Single(socket.Drain())).NostrEvent.Id);
    }

    [Fact]
    public async Task Filters_FollowNip01()
    {
        var relay = new InMemoryRelay(Url);
        NostrEvent a = Evt(Alice, 100, g: "g1"), b = Evt(Alice, 200, g: "g2"), c = Evt(Alice, 300, g: "g1");
        foreach (var e in new[] { a, b, c })
            relay.Seed(e);
        var socket = await Connect(relay);

        async Task<IEnumerable<string>> Ids(NostrFilter f) =>
            (await Send(socket, Req("s", f))).OfType<RelayMessage.Event>().Select(m => m.NostrEvent.Id);

        Assert.Equal([c.Id, a.Id], await Ids(new NostrFilter { TagG = ["g1"] }));
        Assert.Equal([c.Id, b.Id], await Ids(new NostrFilter { Since = 200 }));
        Assert.Equal([b.Id, a.Id], await Ids(new NostrFilter { Until = 200 }));
        Assert.Equal([c.Id], await Ids(new NostrFilter { Kinds = [3825], Limit = 1 }));
        Assert.Equal([b.Id], await Ids(new NostrFilter { Ids = [b.Id] }));
        Assert.Empty(await Ids(new NostrFilter { TagP = [Alice.PublicKey.ToHex()] }));
        Assert.Empty(await Ids(new NostrFilter { Kinds = [1] }));
    }

    [Fact]
    public async Task Close_StopsDelivery()
    {
        var relay = new InMemoryRelay(Url);
        var reader = await Connect(relay);
        await Send(reader, Req("s", new NostrFilter { Kinds = [3825] }));
        Assert.Empty(await Send(reader, new RelayMessage.Close("s")));

        var publisher = await Connect(relay);
        await Send(publisher, new RelayMessage.Publish(Evt(Alice, 1)));

        Assert.Empty(reader.Drain());
    }

    [Fact]
    public async Task Refuse_AnswersOkFalseWithTheReason()
    {
        var relay = new InMemoryRelay(Url);
        relay.Refuse(e => e.Content == "no", "blocked: not here");
        var socket = await Connect(relay);

        var ok = Assert.IsType<RelayMessage.Ok>(Assert.Single(await Send(socket, new RelayMessage.Publish(Evt(Alice, 1, "no")))));

        Assert.Equal("blocked", ok.Prefix);
        Assert.Empty(relay.StoredEvents);
    }

    [Fact]
    public async Task RateLimitLikeDamus_RefusesTheSixthEventInABurst_ThenRecovers()
    {
        var time = new FakeTimeProvider();
        var relay = new InMemoryRelay(Url, time);
        relay.RateLimitLikeDamus();
        var socket = await Connect(relay);

        var oks = new List<RelayMessage.Ok>();
        for (int i = 0; i < 6; i++)
            oks.Add(Assert.IsType<RelayMessage.Ok>(Assert.Single(await Send(socket, new RelayMessage.Publish(Evt(Alice, i))))));

        Assert.All(oks.Take(5), ok => Assert.True(ok.Accepted));
        Assert.Equal("rate-limited", oks[5].Prefix);

        time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(Assert.IsType<RelayMessage.Ok>(Assert.Single(await Send(socket, new RelayMessage.Publish(Evt(Alice, 99))))).Accepted);
    }

    [Fact]
    public async Task RejectUpgrade_FailsConnects_WithTheStatus()
    {
        var relay = new InMemoryRelay(Url);
        relay.RejectUpgrade(503, times: 2);
        var factory = new FakeRelaySocketFactory(relay);

        for (int i = 0; i < 2; i++)
        {
            var ex = await Assert.ThrowsAsync<RelayConnectException>(() => factory.Create(relay.Url).ConnectAsync(CancellationToken.None));
            Assert.Equal(503, ex.HttpStatus);
        }
        await factory.Create(relay.Url).ConnectAsync(CancellationToken.None);
        Assert.Equal(3, relay.ConnectAttempts);
    }

    [Fact]
    public async Task UnknownUrl_IsUnreachable()
    {
        var factory = new FakeRelaySocketFactory(new InMemoryRelay(Url));
        await Assert.ThrowsAsync<RelayConnectException>(() => factory.Create(new Uri("wss://nowhere")).ConnectAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DropConnections_ClosesSockets_ButNewConnectionsWork()
    {
        var relay = new InMemoryRelay(Url);
        var socket = await Connect(relay);

        relay.DropConnections();

        Assert.Null(await socket.ReceiveAsync(CancellationToken.None));
        Assert.False(await socket.SendAsync(new RelayMessage.Close("s").Format(), CancellationToken.None));
        Assert.Equal(0, relay.OpenConnections);
        await Connect(relay);
        Assert.Equal(1, relay.OpenConnections);
    }

    [Fact]
    public async Task Partition_DropsAndRefuses_UntilHealed()
    {
        var relay = new InMemoryRelay(Url);
        var socket = await Connect(relay);

        relay.Partition();

        Assert.Null(await socket.ReceiveAsync(CancellationToken.None));
        await Assert.ThrowsAsync<RelayConnectException>(() => Connect(relay));
        relay.Heal();
        await Connect(relay);
    }

    [Fact]
    public async Task DuplicateDeliveries_SendsEachLiveEventNTimes()
    {
        var relay = new InMemoryRelay(Url);
        relay.DuplicateDeliveries(3);
        var reader = await Connect(relay);
        await Send(reader, Req("s", new NostrFilter { Kinds = [3825] }));
        var publisher = await Connect(relay);

        await Send(publisher, new RelayMessage.Publish(Evt(Alice, 1)));

        Assert.Equal(3, reader.Drain().OfType<RelayMessage.Event>().Count());
    }

    [Fact]
    public async Task ReorderLive_ReleasesAWindowInASeededShuffle()
    {
        async Task<List<long>> Run(int seed)
        {
            var relay = new InMemoryRelay(Url, seed: seed);
            relay.ReorderLive(window: 4);
            var reader = await Connect(relay);
            await Send(reader, Req("s", new NostrFilter { Kinds = [3825] }));
            var publisher = await Connect(relay);
            for (int i = 1; i <= 3; i++)
                await Send(publisher, new RelayMessage.Publish(Evt(Alice, i)));
            Assert.Empty(reader.Drain()); // held until the window fills
            await Send(publisher, new RelayMessage.Publish(Evt(Alice, 4)));
            return reader.Drain().OfType<RelayMessage.Event>().Select(m => m.NostrEvent.CreatedAt).ToList();
        }

        List<long> first = await Run(seed: 7);
        Assert.Equal([1L, 2, 3, 4], first.Order());
        Assert.Equal(first, await Run(seed: 7)); // deterministic
        var orders = new HashSet<string>();
        for (int seed = 0; seed < 10; seed++)
            orders.Add(string.Join(",", await Run(seed)));
        Assert.True(orders.Count > 1, "the shuffle never changed the order");
    }

    [Fact]
    public async Task Withhold_HidesAnEvent_UntilReleased()
    {
        var relay = new InMemoryRelay(Url);
        NostrEvent e = Evt(Alice, 1);
        relay.Withhold(e.Id);
        var reader = await Connect(relay);
        await Send(reader, Req("live", new NostrFilter { Kinds = [3825] }));
        var publisher = await Connect(relay);

        Assert.True(Assert.IsType<RelayMessage.Ok>(Assert.Single(await Send(publisher, new RelayMessage.Publish(e)))).Accepted);
        Assert.Empty(reader.Drain());
        Assert.Single((await Send(reader, Req("history", new NostrFilter { Kinds = [3825] }))).OfType<RelayMessage.Eose>());

        relay.Release(e.Id);
        Assert.Equal(e.Id, Assert.IsType<RelayMessage.Event>(Assert.Single(reader.Drain(), m => m is RelayMessage.Event { SubscriptionId: "live" })).NostrEvent.Id);
    }

    [Fact]
    public async Task AuthRequiredForReads_ClosesReqs_ButAcceptsEvents()
    {
        var relay = new InMemoryRelay(Url);
        relay.AuthRequiredForReads();
        var socket = await Connect(relay);

        var replies = await Send(socket, Req("s", new NostrFilter { Kinds = [3825] }));

        Assert.IsType<RelayMessage.AuthChallenge>(replies[0]);
        Assert.Equal("auth-required", Assert.IsType<RelayMessage.Closed>(replies[1]).Prefix);
        Assert.True(Assert.IsType<RelayMessage.Ok>(Assert.Single(await Send(socket, new RelayMessage.Publish(Evt(Alice, 1))))).Accepted);
    }

    [Fact]
    public async Task ReceivedFrames_CountsEveryClientFrame()
    {
        var relay = new InMemoryRelay(Url);
        var socket = await Connect(relay);
        await Send(socket, Req("s", new NostrFilter()));
        await Send(socket, new RelayMessage.Close("s"));

        Assert.Equal(2, relay.ReceivedFrames.Count);
        Assert.IsType<RelayMessage.Notice>(Assert.Single(await Send(socket, new RelayMessage.Notice("junk from a client"))));
    }
}

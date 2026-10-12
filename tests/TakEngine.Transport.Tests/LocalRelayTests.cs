using System.Security.Cryptography;
using System.Text;
using TakEngine.Crypto;
using TakEngine.Transport.Nostr;
using TakEngine.Transport.Relay;
using TakRelay.Local;

namespace TakEngine.Transport.Tests;

/// <summary>
/// tools/TakRelay.Local over real loopback WebSockets with the real <see cref="ClientWebSocketRelaySocket"/>: what the CLI and the
/// two-process tests use instead of a public relay. Loopback only, so it runs in CI.
/// </summary>
public sealed class LocalRelayTests : IAsyncLifetime
{
    private static readonly SecretKey Alice = Key("alice"), Bob = Key("bob");
    private static readonly Guid Game = Guid.Parse("b8f0c9a2-2b8d-4b1e-9d4c-7f1d2a0e3c55");
    private LocalRelayHost _host = null!;

    private static SecretKey Key(string label) => SecretKey.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes("local relay " + label)));

    public async Task InitializeAsync() => _host = await LocalRelayHost.StartAsync(new LocalRelayOptions(Port: 0));

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private RelayPool Pool(SecretKey me) =>
        new(_host.Urls, new ClientWebSocketRelaySocketFactory(), TimeProvider.System, me.PublicKey.ToHex());

    private static RelayMessage.Req For(SecretKey me, SecretKey other) => SubscriptionPlan.Build(Game, me.PublicKey, other.PublicKey, null);

    private static NostrEvent Evt(SecretKey from, SecretKey to, string content) =>
        NostrEvents.Sign(from, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 3825,
            [new[] { "p", to.PublicKey.ToHex() }, new[] { "g", Game.ToString("D") }], content, new byte[32]);

    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20);
        Assert.True(condition(), "timed out waiting for: " + what);
    }

    [Fact]
    public async Task TwoPools_OverRealWebSockets_PublishSubscribeAndGetOkTrue()
    {
        await using RelayPool alice = Pool(Alice);
        await using RelayPool bob = Pool(Bob);
        var outcomes = new List<PublishOutcome>();
        alice.PublishStatusChanged += (_, o) => { lock (outcomes) outcomes.Add(o); };
        alice.Start(() => For(Alice, Bob));
        bob.Start(() => For(Bob, Alice));
        await Until(() => bob.Links.All(l => l.State == RelayLinkState.CaughtUp), "bob caught up on both relays");

        NostrEvent evt = Evt(Alice, Bob, "hello over loopback");
        alice.Publish(evt);

        await Until(() => { lock (outcomes) return outcomes.Contains(PublishOutcome.Sent); }, "OK true");
        ReceivedEvent received = await bob.Events.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(evt.Id, received.Event.Id);
        Assert.Equal("hello over loopback", received.Event.Content);
        // "Sent" needs one OK true, so the second relay may still be storing it.
        foreach (string name in new[] { "r1", "r2" })
            await Until(() => _host.Relays[name].StoredEvents.Any(e => e.Id == evt.Id), name + " stored the event");
        await Task.Delay(100);
        Assert.False(bob.Events.TryRead(out _), "the second relay's copy is deduped");
    }

    [Fact]
    public async Task ABadSignature_IsRefusedInvalid_LikeAPublicRelay()
    {
        await using RelayPool alice = Pool(Alice);
        var outcomes = new List<PublishOutcome>();
        alice.PublishStatusChanged += (_, o) => { lock (outcomes) outcomes.Add(o); };
        alice.Start(() => For(Alice, Bob));
        NostrEvent good = Evt(Alice, Bob, "x");
        var bad = new NostrEvent
        {
            Id = good.Id,
            Pubkey = good.Pubkey,
            CreatedAt = good.CreatedAt,
            Kind = good.Kind,
            Tags = good.Tags,
            Content = good.Content,
            Sig = (good.Sig[0] == '0' ? "1" : "0") + good.Sig[1..],
        };

        alice.Publish(bad);

        await Until(() => { lock (outcomes) return outcomes.Contains(PublishOutcome.Failed); }, "refused by both relays");
        Assert.Empty(_host.Relays["r1"].StoredEvents);
    }

    [Theory]
    [InlineData(new string[0], 7447, 2, false)]
    [InlineData(new[] { "--port", "0", "--relays", "a,b,c", "--rate-limit-like-damus" }, 0, 3, true)]
    public void Options_Parse(string[] args, int port, int relays, bool damus)
    {
        LocalRelayOptions o = LocalRelayOptions.Parse(args, out string? error)!;

        Assert.Null(error);
        Assert.Equal((port, relays, damus), (o.Port, o.RelayNames.Count, o.RateLimitLikeDamus));
    }

    [Fact]
    public void Options_Unknown_IsUsage() =>
        Assert.Null(LocalRelayOptions.Parse(["--nope"], out _));
}

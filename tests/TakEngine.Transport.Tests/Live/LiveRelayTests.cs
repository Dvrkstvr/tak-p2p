using System.Security.Cryptography;
using TakEngine.Crypto;
using TakEngine.Testing;
using TakEngine.Transport.Nostr;
using TakEngine.Transport.Relay;

namespace TakEngine.Transport.Tests.Live;

/// <summary>
/// F-014 and F-034's live row, against public relays (default wss://nos.lol and wss://relay.damus.io, or <c>TAK_LIVE_RELAYS</c>).
/// By hand only, after the user approved publishing (D-032): <c>TAK_LIVE_RELAY=1 dotnet test TakGame.Ci.slnf --filter "Category=LiveRelay"</c>.
/// Throwaway keys, kind 3825 only; every published event is logged in pipeline/evidence/live-relay-log.md.
/// </summary>
[Trait("Category", "LiveRelay")]
public sealed class LiveRelayTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static IReadOnlyList<Uri> Relays() =>
        (Environment.GetEnvironmentVariable("TAK_LIVE_RELAYS") ?? "wss://nos.lol,wss://relay.damus.io")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(r => new Uri(r))
        .ToList();

    private static SecretKey Throwaway() => SecretKey.FromBytes(RandomNumberGenerator.GetBytes(32));

    private static NostrEvent Event(SecretKey author, SecretKey to, Guid game, string content) =>
        NostrEvents.Sign(author, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 3825,
            [new[] { "p", to.PublicKey.ToHex() }, new[] { "g", game.ToString("D") }], content, RandomNumberGenerator.GetBytes(32));

    private static RelayPool Pool(SecretKey me) =>
        new(Relays(), new ClientWebSocketRelaySocketFactory(), TimeProvider.System, me.PublicKey.ToHex());

    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < Patience)
            await Task.Delay(100);
        Assert.True(condition(), $"timed out after {Patience.TotalSeconds} s waiting for: {what}");
    }

    private static string Answers(RelayPool pool, string id) =>
        string.Join("; ", pool.PerRelay(id).Select(p => $"{p.Key.Host}: {p.Value.Status}{(p.Value.Reason is { } r ? " " + r : "")}"));

    private static async Task<List<ReceivedEvent>> Fetch(SecretKey reader, string id)
    {
        await using RelayPool pool = Pool(reader);
        var got = new List<ReceivedEvent>();
        pool.Start(() => new RelayMessage.Req("tak-f014", SubscriptionPlan.IdLookups([id]).ToList()));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (got.Count == 0 && sw.Elapsed < Patience)
        {
            while (pool.Events.TryRead(out ReceivedEvent? e))
                got.Add(e);
            await Task.Delay(100);
        }
        return got;
    }

    [Fact]
    public async Task PublishOne_IsAcceptedByEachRelay_AndFetchableById()
    {
        LiveRelayLog.Reserve(1);
        SecretKey me = Throwaway(), opponent = Throwaway();
        // Plaintext edge cases for the hand-written NIP-01 serializer: + < > & " an emoji, DEL and U+0001.
        NostrEvent evt = Event(me, opponent, Guid.NewGuid(), "tak f014 + < > & \" \U0001F422 \u007f \u0001");
        await using RelayPool pool = Pool(me);
        pool.Start(() => null);

        pool.Publish(evt);
        await Until(() => pool.PerRelay(evt.Id).Values.All(s => s.Status != RelayPublishStatus.Pending), "every relay answered");
        LiveRelayLog.Record(nameof(PublishOne_IsAcceptedByEachRelay_AndFetchableById), evt.Id, Answers(pool, evt.Id));

        Assert.All(pool.PerRelay(evt.Id), p => Assert.Equal(RelayPublishStatus.Sent, p.Value.Status));
        List<ReceivedEvent> fetched = await Fetch(Throwaway(), evt.Id);
        ReceivedEvent back = Assert.Single(fetched);
        Assert.True(NostrEvents.Verify(back.Event));
        Assert.Equal(evt.Content, back.Event.Content);
    }

    [Fact]
    public async Task CorruptedSig_IsRefusedInvalid_NotSent()
    {
        LiveRelayLog.Reserve(1);
        SecretKey me = Throwaway();
        NostrEvent good = Event(me, Throwaway(), Guid.NewGuid(), "tak f014 bad sig");
        char flipped = good.Sig[^1] == '0' ? '1' : '0';
        var bad = new NostrEvent
        {
            Id = good.Id,
            Pubkey = good.Pubkey,
            CreatedAt = good.CreatedAt,
            Kind = good.Kind,
            Tags = good.Tags,
            Content = good.Content,
            Sig = good.Sig[..^1] + flipped,
        };
        await using RelayPool pool = Pool(me);
        pool.Start(() => null);

        pool.Publish(bad);
        await Until(() => pool.Outcome(bad.Id) != PublishOutcome.Pending, "every relay answered");
        LiveRelayLog.Record(nameof(CorruptedSig_IsRefusedInvalid_NotSent), bad.Id, Answers(pool, bad.Id));

        Assert.Equal(PublishOutcome.Failed, pool.Outcome(bad.Id));
        Assert.All(pool.PerRelay(bad.Id), p => Assert.Equal(RelayPublishStatus.Refused, p.Value.Status));
    }

    [Fact]
    public async Task TwoRelaysTogether_DeliverEachEventOnce()
    {
        LiveRelayLog.Reserve(2);
        SecretKey me = Throwaway(), opponent = Throwaway();
        var game = Guid.NewGuid();
        await using RelayPool mine = Pool(me);
        await using RelayPool theirs = Pool(opponent);
        mine.Start(() => SubscriptionPlan.Build(game, me.PublicKey, opponent.PublicKey, null));
        theirs.Start(() => null);
        await Until(() => mine.Links.All(l => l.State == RelayLinkState.CaughtUp), "both relays caught up");
        var sent = new[] { Event(opponent, me, game, "tak f034 one"), Event(opponent, me, game, "tak f034 two") };

        foreach (NostrEvent e in sent)
            theirs.Publish(e);
        var received = new List<ReceivedEvent>();
        await Until(() =>
        {
            while (mine.Events.TryRead(out ReceivedEvent? e))
                received.Add(e);
            return received.Count >= 2 && sent.All(s => theirs.Outcome(s.Id) != PublishOutcome.Pending);
        }, "both events delivered");
        await Task.Delay(2000); // the second relay's copies arrive too, and must be deduped
        while (mine.Events.TryRead(out ReceivedEvent? e))
            received.Add(e);
        foreach (NostrEvent e in sent)
            LiveRelayLog.Record(nameof(TwoRelaysTogether_DeliverEachEventOnce), e.Id, Answers(theirs, e.Id));

        Assert.Equal(sent.Select(e => e.Id).Order(), received.Select(r => r.Event.Id).Order());
    }
}

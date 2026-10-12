using TakEngine.Abstractions;
using TakEngine.Crypto;
using TakEngine.Multiplayer.Handshake;
using TakEngine.Testing;
using TakEngine.Transport.Envelope;
using TakEngine.Transport.Matchmaking;
using TakEngine.Transport.Nostr;

namespace TakEngine.Multiplayer.Tests;

/// <summary>D-045: the pure cold-rebuild plan, fed with the real history of a game played over the in-memory relays.</summary>
public class RebuildPlanTests
{
    private static readonly IReadOnlyList<string> Script = ScriptedGames.FiveByFiveRoadWin;

    private sealed record Played(InviteCode Invite, SecretKey Host, SecretKey Guest, string Hash, PlayerColor GuestColor,
        IReadOnlyList<NostrEvent> Events);

    private static async Task<Played> PlayFour()
    {
        await using var t = new Table(seed: 21);
        var (host, guest) = await t.Connect();
        await t.Play(host, guest, Script.Take(4));
        return new Played(host.Invite, t.HostOptions.Key, t.GuestOptions.Key, guest.StateHash, guest.MyColor!.Value, t.R1.StoredEvents);
    }

    /// <summary>Decodes the relay's events as <paramref name="me"/> would (rows 1, 3 and 4; authors are the plan's to judge).</summary>
    private static List<HistoryEntry> History(Played p, SecretKey me) =>
        [.. p.Events.Select(evt =>
            {
                PublicKey? author = evt.Pubkey == me.PublicKey.ToHex() ? null : PublicKey.FromHex(evt.Pubkey);
                DecodeResult d = EnvelopeCodec.Decode(evt, new DecodeContext(me, p.Invite.GameId, author));
                return d.IsAccepted ? new HistoryEntry(d.Action!, evt.Id, new RelayMessage.Publish(evt).Format()) : null;
            })
            .OfType<HistoryEntry>()];

    private static SecretKey Owner(Played p, int turn) =>
        (TurnExchange.TurnOwner(turn) == p.GuestColor) ? p.Guest : p.Host;

    [Fact]
    public async Task GuestAndHost_RebuildToTheSameHash()
    {
        Played p = await PlayFour();

        foreach (SecretKey me in new[] { p.Guest, p.Host })
        {
            RebuildResult r = RebuildPlan.Build(p.Invite, me.PublicKey, History(p, me));

            Assert.Null(r.Error);
            Assert.Equal(4, r.Progress!.AppliedTurn);
            Assert.Equal(p.Hash, r.Progress.StateHash);
            Assert.Equal(HandshakeState.InGame, r.Record!.State);
            Assert.Equal("rebuilt from relays", r.Record.Note);
            Assert.InRange(r.Progress.PublishedTurn, 3, 4);
            Assert.Same(me, Owner(p, r.Progress.PublishedTurn));
        }
    }

    [Fact]
    public async Task TheLastMoveMissing_RebuildsTheGameUpToIt()
    {
        Played p = await PlayFour();
        List<HistoryEntry> h = History(p, p.Guest);
        h.RemoveAll(e => e.Action.ActionType == GameActionType.Move && e.Action.Turn == 4);

        RebuildResult r = RebuildPlan.Build(p.Invite, p.Guest.PublicKey, h);

        Assert.Null(r.Error);
        Assert.Equal(3, r.Progress!.AppliedTurn);
    }

    [Fact]
    public async Task AMissingMoveWithLaterMoves_Fails()
    {
        Played p = await PlayFour();
        List<HistoryEntry> h = History(p, p.Guest);
        h.RemoveAll(e => e.Action.ActionType == GameActionType.Move && e.Action.Turn == 3);

        RebuildResult r = RebuildPlan.Build(p.Invite, p.Guest.PublicKey, h);

        Assert.StartsWith("move 3 not found on any relay", r.Error, StringComparison.Ordinal);
        Assert.Null(r.Record);
    }

    [Fact]
    public async Task AMoveThatDoesNotChain_IsAFork()
    {
        Played p = await PlayFour();
        List<HistoryEntry> h = History(p, p.Guest);
        int i = h.FindIndex(e => e.Action.ActionType == GameActionType.Move && e.Action.Turn == 3);
        SignedGameAction forged = SignedScript.Sign(Owner(p, 3), h[i].Action with { PrevStateHash = new string('0', 64), Signature = "" });
        h[i] = h[i] with { Action = forged };

        RebuildResult r = RebuildPlan.Build(p.Invite, p.Guest.PublicKey, h);

        Assert.StartsWith("fork: move 3", r.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnIllegalMoveSignedByItsOwner_IsAFork()
    {
        Played p = await PlayFour();
        List<HistoryEntry> h = History(p, p.Guest);
        int i = h.FindIndex(e => e.Action.ActionType == GameActionType.Move && e.Action.Turn == 3);
        SignedGameAction a = h[i].Action;
        SignedGameAction illegal = SignedScript.Sign(Owner(p, 3),
            SignedGameAction.UnsignedMove(a.GameId, 3, a.PlayerPubKey, a.PrevStateHash, Script[0])); // that square is taken

        h[i] = h[i] with { Action = illegal };
        RebuildResult r = RebuildPlan.Build(p.Invite, p.Guest.PublicKey, h);

        Assert.StartsWith("fork: move 3 on the relays is illegal", r.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStrangersMove_IsNotPartOfTheGame()
    {
        Played p = await PlayFour();
        List<HistoryEntry> h = History(p, p.Guest);
        SignedGameAction a = h.First(e => e.Action.ActionType == GameActionType.Move && e.Action.Turn == 4).Action;
        SignedGameAction stranger = SignedScript.Sign(TestKeys.Stranger,
            SignedGameAction.UnsignedMove(a.GameId, 6, TestKeys.Stranger.PublicKey.ToHex(), p.Hash, "c3")); // past a gap: it must not make turn 5 "missing"
        h.Insert(0, new HistoryEntry(stranger, "stranger", "{}"));

        RebuildResult r = RebuildPlan.Build(p.Invite, p.Guest.PublicKey, h);

        Assert.Null(r.Error);
        Assert.Equal(4, r.Progress!.AppliedTurn);
    }

    [Fact]
    public async Task AnOpponentsFarFutureMove_DoesNotBlockTheRebuild_ItWaitsAtTheGap()
    {
        Played p = await PlayFour();
        List<HistoryEntry> h = History(p, p.Guest);
        int far = Owner(p, 41) == p.Host ? 41 : 42; // a turn of the host's, far past the real game
        SignedGameAction spam = SignedScript.Sign(p.Host,
            SignedGameAction.UnsignedMove(p.Invite.GameId, far, p.Host.PublicKey.ToHex(), p.Hash, "c3"));
        h.Add(new HistoryEntry(spam, "spam", "{}"));

        RebuildResult r = RebuildPlan.Build(p.Invite, p.Guest.PublicKey, h);

        Assert.Null(r.Error);
        Assert.Equal(4, r.Progress!.AppliedTurn);
    }

    [Fact]
    public async Task WithoutOurOwnJoin_TheGuestCannotRebuild()
    {
        Played p = await PlayFour();
        List<HistoryEntry> h = History(p, p.Guest);
        h.RemoveAll(e => e.Action.ActionType == GameActionType.Join);

        RebuildResult r = RebuildPlan.Build(p.Invite, p.Guest.PublicKey, h);

        Assert.Contains("join for this game is not on any relay", r.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutOurOwnAccept_TheHostCannotRebuild()
    {
        Played p = await PlayFour();
        List<HistoryEntry> h = History(p, p.Host);
        h.RemoveAll(e => e.Action.ActionType == GameActionType.Accept);

        RebuildResult r = RebuildPlan.Build(p.Invite, p.Host.PublicKey, h);

        Assert.Contains("answer to a guest is not on any relay", r.Error, StringComparison.Ordinal);
    }

    /// <summary>A second, validly signed move for <paramref name="turn"/> by its owner, on the same prev hash as the real one.</summary>
    private static HistoryEntry Equivocate(Played p, List<HistoryEntry> h, int turn, string eventId)
    {
        SignedGameAction real = h.First(e => e.Action.ActionType == GameActionType.Move && e.Action.Turn == turn).Action;
        SignedGameAction other = SignedScript.Sign(Owner(p, turn),
            SignedGameAction.UnsignedMove(p.Invite.GameId, turn, real.PlayerPubKey, real.PrevStateHash, "c3")); // c3 is empty through turn 4
        return new HistoryEntry(other, eventId, "{}");
    }

    private static SecretKey Opponent(Played p, SecretKey me) => me == p.Host ? p.Guest : p.Host;

    [Fact]
    public async Task AnEquivocationMidGame_RebuildsTheGameOurNextMoveChainsFrom_AndFlagsTheTurn()
    {
        Played p = await PlayFour();

        foreach (SecretKey me in new[] { p.Guest, p.Host })
        {
            // The opponent's turn followed by one of ours: our signed move t+1 pins which move t we applied.
            int t = Enumerable.Range(1, 3).First(turn => Owner(p, turn) == Opponent(p, me));
            List<HistoryEntry> h = History(p, me);
            HistoryEntry forged = Equivocate(p, h, t, new string('0', 64)); // sorts first, and the relays return it first
            h.Insert(0, forged);

            RebuildResult r = RebuildPlan.Build(p.Invite, me.PublicKey, h);

            Assert.Null(r.Error);
            Assert.Equal(4, r.Progress!.AppliedTurn);
            Assert.Equal(p.Hash, r.Progress.StateHash);
            RebuildFlag flag = Assert.Single(r.Flags);
            Assert.Equal(t, flag.Turn);
            Assert.Equal(forged.EventId, flag.EventId);
            Assert.Equal(Opponent(p, me).PublicKey.ToHex(), flag.Author);
            Assert.Contains($"conflicting move for turn {t}", flag.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnEquivocationOnTheLastTurn_RebuildsDeterministically_AndFlagsTheTurn()
    {
        Played p = await PlayFour();
        SecretKey me = Opponent(p, Owner(p, 4)); // the opponent owns turn 4, and nothing follows it
        List<HistoryEntry> h = History(p, me);
        string realId = h.First(e => e.Action.ActionType == GameActionType.Move && e.Action.Turn == 4).EventId;

        foreach (string forgedId in new[] { new string('0', 64), new string('f', 64) })
        {
            HistoryEntry forged = Equivocate(p, h, 4, forgedId);
            string chosen = string.CompareOrdinal(forgedId, realId) < 0 ? forgedId : realId;
            string? hash = null;
            for (int seed = 0; seed < 8; seed++)
            {
                var rng = new Random(seed);
                List<HistoryEntry> shuffled = [.. h.Append(forged).OrderBy(_ => rng.Next())];

                RebuildResult r = RebuildPlan.Build(p.Invite, me.PublicKey, shuffled);

                Assert.True(r.Error is null, $"{r.Error} (seed {seed})");
                Assert.Equal(4, r.Progress!.AppliedTurn);
                Assert.Equal(chosen, r.Applied[3].EventId);
                hash ??= r.Progress.StateHash;
                Assert.Equal(hash, r.Progress.StateHash);
                RebuildFlag flag = Assert.Single(r.Flags);
                Assert.Equal(4, flag.Turn);
                Assert.Equal(chosen == forgedId ? realId : forgedId, flag.EventId);
                Assert.Contains("conflicting move for turn 4", flag.Reason, StringComparison.Ordinal);
            }
            Assert.Equal(chosen == realId, hash == p.Hash);
        }
    }

    [Fact]
    public async Task ARepublishedMove_IsADuplicate_NotAnEquivocation()
    {
        Played p = await PlayFour();
        List<HistoryEntry> h = History(p, p.Guest);
        HistoryEntry real = h.First(e => e.Action.ActionType == GameActionType.Move && e.Action.Turn == 2);
        SignedGameAction resigned = SignedScript.Sign(Owner(p, 2), real.Action with { Signature = "" });
        h.Insert(0, new HistoryEntry(resigned, new string('0', 64), "{}"));

        RebuildResult r = RebuildPlan.Build(p.Invite, p.Guest.PublicKey, h);

        Assert.Null(r.Error);
        Assert.Equal(p.Hash, r.Progress!.StateHash);
        Assert.Empty(r.Flags);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task ASecondJoinFromTheGuest_RebuildsEveryTime(int seed)
    {
        Played p = await PlayFour();
        var rng = new Random(seed);
        byte[] nonce = new byte[ColorSeed.NonceLength];
        rng.NextBytes(nonce);
        SignedGameAction second = SignedScript.Sign(p.Guest, HandshakeMessages.UnsignedJoin(p.Invite.GameId, p.Guest.PublicKey.ToHex(),
            p.Invite.BoardSize, nonce, p.Invite.SeedCommit));
        PlayerColor hostColor = p.GuestColor == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;

        foreach (SecretKey me in new[] { p.Host, p.Guest })
        {
            foreach (string id in new[] { new string('0', 64), new string('f', 64) })
            {
                List<HistoryEntry> h = History(p, me);
                h.Insert(0, new HistoryEntry(second, id, "{}"));

                RebuildResult r = RebuildPlan.Build(p.Invite, me.PublicKey, h);

                string who = me == p.Host ? "host" : "guest";
                Assert.True(r.Error is null, $"{r.Error} (seed {seed}, {who}, second join id {id[0]}…)");
                Assert.Equal(p.Hash, r.Progress!.StateHash);
                Assert.Equal(me == p.Host ? hostColor : p.GuestColor, r.Record!.MyColor);
            }
        }
    }
}

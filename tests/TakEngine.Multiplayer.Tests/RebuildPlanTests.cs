using TakEngine.Abstractions;
using TakEngine.Crypto;
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
}

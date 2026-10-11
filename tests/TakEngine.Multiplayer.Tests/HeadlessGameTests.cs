using TakEngine.Abstractions;
using TakEngine.Testing;

namespace TakEngine.Multiplayer.Tests;

/// <summary>
/// F-016, end of M0-A: two MultiplayerGames with their own keys and stores play over two in-memory relays, from the invite
/// handshake to the end, and agree on the result and the final hash.
/// </summary>
public class HeadlessGameTests
{
    private static async Task<(MultiplayerGame Host, MultiplayerGame Guest)> PlayToTheEnd(Table t, IReadOnlyList<string> script,
        BoardSize size = BoardSize.Five, int komi = 0, Action? nudge = null)
    {
        var (host, guest) = await t.Connect(size, komi, nudge);
        var ended = new List<GameResult>();
        host.Ended += ended.Add;
        guest.Ended += ended.Add;

        await t.Play(host, guest, script, nudge: nudge);

        await Table.Eventually(() => ended.Count == 2, $"both games end (seed {t.Seed})", nudge: nudge);
        Assert.Equal(ended[0], ended[1]);
        Assert.Equal(host.Result, guest.Result);
        Assert.Equal(host.StateHash, guest.StateHash);
        Assert.Equal(script.Count, host.AppliedTurn);
        Assert.Equal(script.Count, guest.AppliedTurn);
        return (host, guest);
    }

    [Fact]
    public async Task ScriptedFiveByFive_RoadWin_EqualResultAndFinalHash()
    {
        await using var t = new Table(seed: 1);
        var (host, guest) = await PlayToTheEnd(t, ScriptedGames.FiveByFiveRoadWin);

        Assert.Equal(GameEndReason.Road, host.Result!.Reason);
        Assert.Equal(PlayerColor.White, host.Result.Winner);
        foreach (var (game, store) in new[] { (host, t.HostStore), (guest, t.GuestStore) })
        {
            StoredGame stored = (await store.LoadAsync(game.GameId))!;
            Assert.Equal(HandshakeState.Completed, stored.Record.State);
            Assert.Equal(game.Result, stored.Record.Result);
            Assert.Equal(9, stored.Progress.AppliedTurn);
            Assert.Equal(game.StateHash, stored.Progress.StateHash);
            Assert.Equal(Enumerable.Range(1, 9), stored.Applied.Select(a => a.Action.Turn));
            Assert.Empty(stored.Flags);
        }
    }

    [Theory]
    [InlineData(11)]
    [InlineData(23)]
    [InlineData(2026)]
    public async Task SameGame_WithDuplicatesAndReordering_EndsAtTheSameHash(int seed)
    {
        await using var t = new Table(seed);
        foreach (InMemoryRelay r in new[] { t.R1, t.R2 })
        {
            r.DuplicateDeliveries(2);
            r.ReorderLive(window: 3);
        }
        void Flush()
        {
            t.R1.FlushReordered();
            t.R2.FlushReordered();
        }

        var (host, guest) = await PlayToTheEnd(t, ScriptedGames.FiveByFiveRoadWin, nudge: Flush);

        Assert.Equal(GameEndReason.Road, host.Result!.Reason);
        Assert.Empty((await t.HostStore.LoadAsync(host.GameId))!.Flags);
        Assert.Empty((await t.GuestStore.LoadAsync(guest.GameId))!.Flags);
    }

    [Fact]
    public async Task SevenBySeven_WithKomi_RoadWin_ThroughTheNetwork()
    {
        await using var t = new Table(seed: 3);
        string[] script = ["g7", "a1", "b1", "g6", "c1", "g5", "d1", "g4", "e1", "g3", "f1", "g2", "g1"];

        var (host, guest) = await PlayToTheEnd(t, script, BoardSize.Seven, komi: 5);

        Assert.Equal((BoardSize.Seven, 5), (guest.Record.Size, guest.Record.KomiHalves));
        Assert.Equal(GameEndReason.Road, host.Result!.Reason);
        Assert.Equal(PlayerColor.White, guest.Result!.Winner);
    }

    [Theory]
    [InlineData(0, PlayerColor.White)] // 5 flats to 4
    [InlineData(5, PlayerColor.Black)] // 4 + 2.5 komi beats 5
    public async Task ThreeByThree_FullBoard_KomiDecidesTheFlatWin(int komi, PlayerColor winner)
    {
        await using var t = new Table(seed: 5);
        // A checkerboard: White's five flats (corners and centre) and Black's four never touch, so nobody has a road.
        string[] script = ["b1", "a1", "c1", "a2", "b2", "c2", "a3", "b3", "c3"];

        var (host, guest) = await PlayToTheEnd(t, script, BoardSize.Three, komi);

        Assert.Equal(GameEndReason.FlatCount, host.Result!.Reason);
        Assert.Equal(winner, host.Result.Winner);
        Assert.Equal(winner, guest.Result!.Winner);
    }

    [Fact]
    public async Task IllegalMove_RefusedLocally_NothingPublished()
    {
        await using var t = new Table(seed: 9);
        var (host, guest) = await t.Connect();
        await t.Play(host, guest, ["e5", "a1"]);
        MultiplayerGame white = host.MyColor == PlayerColor.White ? host : guest;
        MultiplayerGame black = white == host ? guest : host;
        await Task.Delay(50);
        int stored = t.R1.StoredEvents.Count;

        CommandResult onTop = await white.SubmitAsync("Sa1"); // a1 is taken
        CommandResult outOfTurn = await black.SubmitAsync("b2");

        Assert.False(onTop.IsSuccess);
        Assert.False(outOfTurn.IsSuccess);
        await Task.Delay(50);
        Assert.Equal(stored, t.R1.StoredEvents.Count);
        Assert.Equal(2, white.AppliedTurn);
        Assert.Equal(white.StateHash, black.StateHash);

        await t.Play(host, guest, ["b1"], from: 3);
        Assert.Equal(3, black.AppliedTurn);
    }
}

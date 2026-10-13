using TakApp.Blazor.Services;
using TakEngine.Abstractions;
using TakEngine.Core.Session;

namespace TakApp.Blazor.Tests;

/// <summary>
/// F-041: the Blazor head plays local and vs-AI games through <see cref="TakGameSession"/>. The adapter only forwards moves and
/// exposes what the views draw; every legality, road and hash answer comes from the engine.
/// </summary>
public class LocalGameAdapterTests
{
    private static LocalGameAdapter NewAdapter() => new(_ => Task.CompletedTask);

    private static CommandResult Play(LocalGameAdapter adapter, string ptn) =>
        adapter.ExecuteMove(TakEngine.Core.Serialization.PtnParser.ParseMove(ptn));

    [Fact]
    public void LocalMatch_StartsAtTheEngineGenesis()
    {
        var adapter = NewAdapter();
        adapter.StartLocalMatch(BoardSize.Five, "Ann", null);

        var reference = TakGameSession.CreateLocal(BoardSize.Five);
        Assert.Equal(reference.CurrentStateHash, adapter.StateHash);
        Assert.Equal(GamePhase.FirstTurnPlacement, adapter.Board!.Phase);
        Assert.Equal("Ann", adapter.WhitePlayerName);
        Assert.Equal("Black", adapter.BlackPlayerName);
        Assert.True(adapter.IsLocalOnly);
        Assert.False(adapter.IsBotMatch);
        Assert.Empty(adapter.MoveHistoryPtn);
    }

    [Fact]
    public void Moves_FollowTheEngineHashChain()
    {
        var adapter = NewAdapter();
        adapter.StartLocalMatch(BoardSize.Five);
        var reference = TakGameSession.CreateLocal(BoardSize.Five);

        foreach (var ptn in new[] { "a1", "e5", "b3", "a5", "b2", "b5", "b2+", "d5", "Sc4", "a4", "2b3>11" })
        {
            Assert.True(Play(adapter, ptn).IsSuccess, ptn);
            var move = TakEngine.Core.Serialization.PtnParser.ParseMove(ptn);
            var res = move switch
            {
                PlaceMove p => reference.SubmitPlacement(p.Target, p.PieceType),
                SlideMove s => reference.SubmitMove(s.Origin, s.Direction, s.Drops),
                _ => throw new InvalidOperationException(),
            };
            Assert.True(res.IsSuccess, ptn);
            Assert.Equal(reference.CurrentStateHash, adapter.StateHash);
        }

        Assert.Equal(new[] { "a1", "e5", "b3", "a5", "b2", "b5", "b2+", "d5", "Sc4", "a4", "2b3>11" }, adapter.MoveHistoryPtn);
        Assert.Equal(reference.CurrentTps, TakEngine.Core.Serialization.TpsSerializer.Serialize(adapter.Board!));
    }

    [Fact]
    public void IllegalPlacement_ReturnsTheEngineError_AndChangesNothing()
    {
        var adapter = NewAdapter();
        adapter.StartLocalMatch(BoardSize.Five);
        string hash = adapter.StateHash!;
        int notified = 0;
        adapter.OnStateChanged += () => notified++;

        var res = adapter.ExecuteMove(new PlaceMove(new Coord(2, 2), PieceType.Standing));

        Assert.False(res.IsSuccess);
        Assert.Equal("First turn placement must be a Flat stone.", res.ErrorMessage);
        Assert.Equal(hash, adapter.StateHash);
        Assert.Empty(adapter.MoveHistoryPtn);
        Assert.True(adapter.Board!.GetStack(new Coord(2, 2)).IsEmpty);
        Assert.Equal(0, notified);
    }

    [Fact]
    public void IllegalSlide_ReturnsTheEngineError_AndChangesNothing()
    {
        var adapter = NewAdapter();
        adapter.StartLocalMatch(BoardSize.Five);
        foreach (var ptn in new[] { "a1", "e5", "b3", "Cc3" })
        {
            Assert.True(Play(adapter, ptn).IsSuccess, ptn);
        }
        string hash = adapter.StateHash!;

        // White's b3 flat onto Black's capstone at c3.
        var res = adapter.ExecuteMove(new SlideMove(new Coord(1, 2), Direction.East, 1, [1]));

        Assert.False(res.IsSuccess);
        Assert.Contains("Capstone", res.ErrorMessage);
        Assert.Equal(hash, adapter.StateHash);
        Assert.Equal(4, adapter.MoveHistoryPtn.Count);
        Assert.Equal(1, adapter.Board!.GetStack(new Coord(1, 2)).Height);
    }

    [Fact]
    public void BoardView_IsACopy_SoAViewCannotMoveTheGame()
    {
        var adapter = NewAdapter();
        adapter.StartLocalMatch(BoardSize.Five);
        string hash = adapter.StateHash!;

        adapter.Board!.Place(new Coord(0, 0), PieceType.Flat);

        Assert.Equal(hash, adapter.StateHash);
        Assert.Empty(adapter.MoveHistoryPtn);
        Assert.True(adapter.LegalMovesFor(new Coord(0, 0)).OfType<PlaceMove>().Any());
    }

    [Fact]
    public void RoadWin_IsReportedWithTheEngineRoad_AndFurtherMovesAreRefused()
    {
        var adapter = NewAdapter();
        adapter.StartLocalMatch(BoardSize.Four);
        foreach (var ptn in new[] { "a4", "d1", "a2", "b4", "b2", "c4", "c2", "d3", "d2" })
        {
            Assert.True(Play(adapter, ptn).IsSuccess, ptn);
        }

        Assert.Equal(GamePhase.Completed, adapter.Board!.Phase);
        Assert.Equal(GameEndReason.Road, adapter.Board.Result!.Reason);
        Assert.Equal(PlayerColor.White, adapter.Board.Result.Winner);
        Assert.Equal(
            new HashSet<Coord> { new(0, 1), new(1, 1), new(2, 1), new(3, 1) },
            adapter.WinningRoadCoords);

        var res = Play(adapter, "a1");
        Assert.False(res.IsSuccess);
        Assert.Equal(9, adapter.MoveHistoryPtn.Count);
    }

    [Fact]
    public void Resign_EndsTheGameForTheSideToMove()
    {
        var adapter = NewAdapter();
        adapter.StartLocalMatch(BoardSize.Five);
        Assert.True(Play(adapter, "a1").IsSuccess);

        Assert.True(adapter.Resign().IsSuccess);

        Assert.Equal(GameEndReason.Resignation, adapter.Board!.Result!.Reason);
        Assert.Equal(PlayerColor.White, adapter.Board.Result.Winner);
        Assert.Empty(adapter.WinningRoadCoords);
        Assert.False(adapter.Resign().IsSuccess);
    }

    [Fact]
    public void BotMatch_TheBotAnswersEachHumanMoveThroughTheSession()
    {
        var adapter = NewAdapter();
        adapter.StartBotMatch(BoardSize.Five, BotDifficulty.Easy, PlayerColor.White, "Ann");

        Assert.True(adapter.IsBotMatch);
        Assert.Equal("Ann", adapter.WhitePlayerName);
        Assert.Equal("AI (Easy)", adapter.BlackPlayerName);

        Assert.True(Play(adapter, "a1").IsSuccess);

        Assert.Equal(2, adapter.MoveHistoryPtn.Count);
        Assert.False(adapter.IsBotThinking);
        Assert.Equal(PlayerColor.White, adapter.Board!.ActivePlayer);

        // The bot's move is in the engine's chain: replaying the history on a fresh session lands on the same hash.
        var reference = TakGameSession.CreateLocal(BoardSize.Five);
        foreach (var ptn in adapter.MoveHistoryPtn)
        {
            var p = (PlaceMove)TakEngine.Core.Serialization.PtnParser.ParseMove(ptn);
            Assert.True(reference.SubmitPlacement(p.Target, p.PieceType).IsSuccess);
        }
        Assert.Equal(reference.CurrentStateHash, adapter.StateHash);
    }

    [Fact]
    public void BotMatch_WithTheBotAsWhite_TheBotOpens()
    {
        var adapter = NewAdapter();
        adapter.StartBotMatch(BoardSize.Five, BotDifficulty.Easy, PlayerColor.Black);

        Assert.Single(adapter.MoveHistoryPtn);
        Assert.Equal(PlayerColor.Black, adapter.Board!.ActivePlayer);
    }

    [Fact]
    public async Task BotMatch_ShowsThinkingWhileTheBotWaits()
    {
        var gate = new TaskCompletionSource();
        var adapter = new LocalGameAdapter(_ => gate.Task);
        adapter.StartBotMatch(BoardSize.Five, BotDifficulty.Easy, PlayerColor.White);

        Assert.True(Play(adapter, "a1").IsSuccess);
        Assert.True(adapter.IsBotThinking);
        Assert.Single(adapter.MoveHistoryPtn);

        gate.SetResult();
        await adapter.BotTurn!;

        Assert.False(adapter.IsBotThinking);
        Assert.Equal(2, adapter.MoveHistoryPtn.Count);
    }

    [Fact]
    public async Task NewMatch_DuringABotWait_DropsTheStaleBotMove()
    {
        var gate = new TaskCompletionSource();
        var adapter = new LocalGameAdapter(_ => gate.Task);
        adapter.StartBotMatch(BoardSize.Five, BotDifficulty.Easy, PlayerColor.White);
        Assert.True(Play(adapter, "a1").IsSuccess);
        var staleTurn = adapter.BotTurn!;

        adapter.StartLocalMatch(BoardSize.Five);
        gate.SetResult();
        await staleTurn;

        Assert.Empty(adapter.MoveHistoryPtn);
        Assert.False(adapter.IsBotThinking);
    }
}

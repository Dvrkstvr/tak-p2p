using TakEngine.Abstractions;
using TakEngine.Core.Session;
using TakEngine.Testing;

namespace TakEngine.Multiplayer.Tests;

/// <summary>Review M0-code finding 1: a reopen derives the end from the stored board, since TPS carries no result.</summary>
public class FinishedBoardTests
{
    private static ExchangeProgress At(int turn, string tps) => new(turn, "hash", tps, 0, 0);

    [Fact]
    public void RoadWin_IsSeen_ThePlyBeforeIsNot()
    {
        IReadOnlyList<string> script = ScriptedGames.FiveByFiveRoadWin;
        var (_, _, result) = SignedScript.Play(script);

        Assert.Null(FinishedBoard.Result(At(script.Count - 1, Replay(script.Take(script.Count - 1).ToList())), 0));
        Assert.Equal(new GameResult(false, PlayerColor.White, GameEndReason.Road), result);
        Assert.Equal(result, FinishedBoard.Result(At(script.Count, Replay(script)), 0));
    }

    [Theory]
    [InlineData(0, false, PlayerColor.White)]
    [InlineData(2, true, null)]
    [InlineData(3, false, PlayerColor.Black)]
    public void FullBoard_EndsOnFlats_WithKomi(int komiHalves, bool draw, PlayerColor? winner)
    {
        // 3x3, White 5 flats, Black 4 flats, no road for either side.
        Assert.Equal(new GameResult(draw, winner, GameEndReason.FlatCount),
            FinishedBoard.Result(At(9, "1,2,1/2,1,2/1,2,1 2 5"), komiHalves));
    }

    [Fact]
    public void NoPlyYet_IsNotOver()
    {
        Assert.Null(FinishedBoard.Result(At(0, "x5/x5/x5/x5/x5 1 1"), 0));
    }

    private static string Replay(IReadOnlyList<string> ptns)
    {
        TakGameSession white = SignedScript.Session(PlayerColor.White);
        TakGameSession black = SignedScript.Session(PlayerColor.Black);
        SignedGameAction? last = null;
        white.OnLocalActionSigned += a => last = a;
        black.OnLocalActionSigned += a => last = a;
        for (int i = 0; i < ptns.Count; i++)
        {
            var (mover, other) = i % 2 == 0 ? (white, black) : (black, white);
            Assert.True(LocalMoves.Submit(mover, ptns[i]).IsSuccess);
            Assert.True(other.ProcessRemoteAction(last).IsSuccess);
        }
        return white.CurrentTps;
    }
}

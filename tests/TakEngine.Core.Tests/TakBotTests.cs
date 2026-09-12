using System;
using System.Diagnostics;
using System.Linq;
using TakEngine.Abstractions;
using TakEngine.Core.AI;
using TakEngine.Core.Board;
using TakEngine.Core.Rules;
using Xunit;

namespace TakEngine.Core.Tests;

public class TakBotTests
{
    [Theory]
    [InlineData(BotDifficulty.Easy)]
    [InlineData(BotDifficulty.Medium)]
    [InlineData(BotDifficulty.Hard)]
    public void SelectMove_Turn1_SelectsLegalFlatPlacement(BotDifficulty difficulty)
    {
        var board = new GameBoard(BoardSize.Five);
        var bot = new MinimaxTakBot(difficulty);

        var move = bot.SelectMove(board);

        Assert.NotNull(move);
        Assert.IsType<PlaceMove>(move);
        var pm = (PlaceMove)move;
        Assert.Equal(PieceType.Flat, pm.PieceType);

        var exec = board.Execute(move);
        Assert.True(exec.IsSuccess);
    }

    [Fact]
    public void SelectMove_PlayingPhase_SelectsLegalMove()
    {
        var board = new GameBoard(BoardSize.Four);
        // Turn 1
        board.Execute(new PlaceMove(new Coord(0, 0), PieceType.Flat));
        board.Execute(new PlaceMove(new Coord(3, 3), PieceType.Flat));

        // Turn 2
        var bot = new MinimaxTakBot(BotDifficulty.Medium);
        var move = bot.SelectMove(board);

        Assert.NotNull(move);
        var exec = board.Execute(move);
        Assert.True(exec.IsSuccess);
    }

    [Fact]
    public void SelectMove_SeizesImmediateRoadVictory()
    {
        // 4x4 board: White needs 1 move to complete horizontal road across y=2: (0,2), (1,2), (2,2) -> place at (3,2)
        var board = new GameBoard(BoardSize.Four);
        // Turn 1 swap
        board.Execute(new PlaceMove(new Coord(0, 0), PieceType.Flat)); // Places Black on (0,0)
        board.Execute(new PlaceMove(new Coord(3, 3), PieceType.Flat)); // Places White on (3,3)

        // Turn 2
        board.Execute(new PlaceMove(new Coord(0, 2), PieceType.Flat)); // White on (0,2)
        board.Execute(new PlaceMove(new Coord(0, 1), PieceType.Flat)); // Black on (0,1)

        // Turn 3
        board.Execute(new PlaceMove(new Coord(1, 2), PieceType.Flat)); // White on (1,2)
        board.Execute(new PlaceMove(new Coord(1, 1), PieceType.Flat)); // Black on (1,1)

        // Turn 4
        board.Execute(new PlaceMove(new Coord(2, 2), PieceType.Flat)); // White on (2,2)
        board.Execute(new PlaceMove(new Coord(2, 1), PieceType.Flat)); // Black on (2,1)

        // Now Turn 5: White can win immediately by placing at (3,2)!
        Assert.Equal(PlayerColor.White, board.ActivePlayer);

        var bot = new MinimaxTakBot(BotDifficulty.Medium);
        var move = bot.SelectMove(board);

        Assert.NotNull(move);
        var result = board.Execute(move);
        Assert.True(result.IsSuccess);
        Assert.Equal(GamePhase.Completed, board.Phase);
        Assert.Equal(PlayerColor.White, board.Result?.Winner);
        Assert.Equal(GameEndReason.Road, board.Result?.Reason);
    }

    [Fact]
    public void SelectMove_BlocksImmediateOpponentRoadThreat()
    {
        // 4x4 board: White has 3 of 4 squares on row 2: (0,2), (1,2), (2,2).
        // Black's turn: Black must neutralize White's road threat (either by placing on blocking squares or covering a road piece)
        var board = new GameBoard(BoardSize.Four);
        // Turn 1 swap
        board.Execute(new PlaceMove(new Coord(0, 0), PieceType.Flat));
        board.Execute(new PlaceMove(new Coord(3, 3), PieceType.Flat));

        // Turn 2
        board.Execute(new PlaceMove(new Coord(0, 2), PieceType.Flat));
        board.Execute(new PlaceMove(new Coord(0, 1), PieceType.Flat));

        // Turn 3
        board.Execute(new PlaceMove(new Coord(1, 2), PieceType.Flat));
        board.Execute(new PlaceMove(new Coord(1, 1), PieceType.Flat));

        // Turn 4: White places (2,2)
        board.Execute(new PlaceMove(new Coord(2, 2), PieceType.Flat));

        // It is now Black's turn (Turn 4, Black). White is threatening to win.
        Assert.Equal(PlayerColor.Black, board.ActivePlayer);

        var bot = new MinimaxTakBot(BotDifficulty.Medium);
        var move = bot.SelectMove(board);

        Assert.NotNull(move);
        var execResult = board.Execute(move);
        Assert.True(execResult.IsSuccess);

        // Verify that after Black's move, White CANNOT win on the subsequent turn
        Assert.Equal(PlayerColor.White, board.ActivePlayer);
        var whiteResponses = MoveValidator.GetAllLegalMoves(board);
        foreach (var whiteMove in whiteResponses)
        {
            var testClone = board.Clone();
            var res = testClone.Execute(whiteMove);
            if (res.IsSuccess)
            {
                Assert.False(
                    testClone.Phase == GamePhase.Completed && testClone.Result?.Winner == PlayerColor.White,
                    $"Threat was not neutralized: White can win immediately with {whiteMove.ToPtn()}");
            }
        }
    }

    [Fact]
    public void SelectMove_PerformanceBenchmark_SubSecondExecution()
    {
        var board = new GameBoard(BoardSize.Five);
        // Set up a realistic mid-game position
        board.Execute(new PlaceMove(new Coord(0, 0), PieceType.Flat));
        board.Execute(new PlaceMove(new Coord(4, 4), PieceType.Flat));
        board.Execute(new PlaceMove(new Coord(2, 2), PieceType.Flat));
        board.Execute(new PlaceMove(new Coord(2, 3), PieceType.Flat));

        var bot = new MinimaxTakBot(BotDifficulty.Medium);

        var sw = Stopwatch.StartNew();
        var move = bot.SelectMove(board);
        sw.Stop();

        Assert.NotNull(move);
        // In-memory evaluation and search must be well under 500ms for smooth mobile/WASM UX
        Assert.True(sw.ElapsedMilliseconds < 500, $"Bot took {sw.ElapsedMilliseconds}ms, expected < 500ms");
    }
}

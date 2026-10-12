using System;
using System.Linq;
using TakEngine.Abstractions;
using TakEngine.Core.AI;
using TakEngine.Core.Board;
using TakEngine.Core.Rules;
using TakEngine.Core.Serialization;
using Xunit;

namespace TakEngine.Core.Tests;

/// <summary>F-057: the alpha-beta search picks a move as good as an exhaustive search of the same depth would.</summary>
public class BotSearchTests
{
    private const int WinBase = 100_000; // the bot's in-search win score (plus remaining depth)

    [Theory]
    [InlineData(BoardSize.Three, 1)]
    [InlineData(BoardSize.Three, 2)]
    [InlineData(BoardSize.Three, 3)]
    [InlineData(BoardSize.Four, 4)]
    [InlineData(BoardSize.Four, 5)]
    [InlineData(BoardSize.Four, 6)]
    [InlineData(BoardSize.Four, 7)]
    [InlineData(BoardSize.Five, 8)]
    public void MediumBot_ChoosesAMoveWithTheBestExhaustiveScore(BoardSize size, int seed)
    {
        GameBoard board = RandomMidgame(size, seed, plies: 6);
        Assert.True(board.Phase == GamePhase.Playing, $"seed {seed}: position ended early");
        const int depth = 2; // Medium

        var bot = new MinimaxTakBot(BotDifficulty.Medium);
        TakMove chosen = bot.SelectMove(board);

        var values = MoveValidator.GetAllLegalMoves(board)
            .Select(m => (move: m, value: RootValue(board, m, depth)))
            .Where(x => x.value.HasValue)
            .ToList();
        int best = values.Max(x => x.value!.Value);
        int chosenValue = values.Single(x => x.move.ToPtn() == chosen.ToPtn()).value!.Value;

        Assert.True(chosenValue == best,
            $"seed {seed}, {TpsSerializer.Serialize(board)}: bot chose {chosen.ToPtn()} ({chosenValue}), best is {best} " +
            $"({string.Join(", ", values.Where(x => x.value == best).Select(x => x.move.ToPtn()))})");
    }

    private static GameBoard RandomMidgame(BoardSize size, int seed, int plies)
    {
        var random = new Random(seed);
        var board = new GameBoard(size);
        for (int i = 0; i < plies && board.Phase != GamePhase.Completed; i++)
        {
            var moves = MoveValidator.GetAllLegalMoves(board);
            Assert.True(board.Execute(moves[random.Next(moves.Count)]).IsSuccess);
        }
        return board;
    }

    private static int? RootValue(GameBoard board, TakMove move, int depth)
    {
        var clone = board.Clone();
        if (!clone.Execute(move).IsSuccess)
            return null;
        if (clone.Phase == GamePhase.Completed && clone.Result?.Winner == board.ActivePlayer)
            return int.MaxValue; // the bot always takes an immediate win
        return -Negamax(clone, depth - 1, Opponent(board.ActivePlayer));
    }

    private static PlayerColor Opponent(PlayerColor c) => c == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;

    // Plain negamax with the bot's scoring and no pruning.
    private static int Negamax(GameBoard board, int depth, PlayerColor perspective)
    {
        if (depth == 0 || board.Phase == GamePhase.Completed)
            return TakEvaluator.Evaluate(board, perspective);

        int? best = null;
        foreach (var move in MoveValidator.GetAllLegalMoves(board))
        {
            var clone = board.Clone();
            if (!clone.Execute(move).IsSuccess)
                continue;
            if (clone.Phase == GamePhase.Completed && clone.Result?.Winner == perspective)
                return WinBase + depth;
            int score = -Negamax(clone, depth - 1, Opponent(perspective));
            best = best is null ? score : Math.Max(best.Value, score);
        }
        return best ?? TakEvaluator.Evaluate(board, perspective);
    }
}

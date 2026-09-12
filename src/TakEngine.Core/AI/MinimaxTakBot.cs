using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Rules;

namespace TakEngine.Core.AI;

public sealed class MinimaxTakBot : ITakBot
{
    private readonly Random _random = new();

    public BotDifficulty Difficulty { get; }

    public MinimaxTakBot(BotDifficulty difficulty = BotDifficulty.Medium)
    {
        Difficulty = difficulty;
    }

    public TakMove SelectMove(TakBoardSnapshot snapshot, IReadOnlyList<TakMove> legalMoves)
    {
        var board = GameBoard.FromSnapshot(snapshot);
        return SelectMove(board, legalMoves, CancellationToken.None);
    }

    public Task<TakMove> SelectMoveAsync(
        TakBoardSnapshot snapshot,
        IReadOnlyList<TakMove> legalMoves,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => SelectMove(snapshot, legalMoves), cancellationToken);
    }

    public TakMove SelectMove(GameBoard board, CancellationToken cancellationToken = default)
    {
        var legalMoves = MoveValidator.GetAllLegalMoves(board);
        return SelectMove(board, legalMoves, cancellationToken);
    }

    public TakMove SelectMove(
        GameBoard board,
        IReadOnlyList<TakMove> legalMoves,
        CancellationToken cancellationToken = default)
    {
        if (legalMoves == null || legalMoves.Count == 0)
            throw new InvalidOperationException("Cannot select move: no legal moves available.");

        if (legalMoves.Count == 1)
            return legalMoves[0];

        PlayerColor perspective = board.ActivePlayer;

        // Turn 1 & 2 swap rule optimization: place opponent stone on corners or edges
        if (board.Phase == GamePhase.FirstTurnPlacement)
        {
            var preferredSwapMove = SelectFirstTurnPlacement(board, legalMoves);
            if (preferredSwapMove != null)
                return preferredSwapMove;
        }

        // Configure search depth based on difficulty and board size
        int maxDepth = Difficulty switch
        {
            BotDifficulty.Easy => 1,
            BotDifficulty.Medium => 2,
            BotDifficulty.Hard => board.Size == BoardSize.Four ? 4 : 3,
            _ => 2
        };

        // If Easy: score 1 ply and pick randomly among top 3
        if (Difficulty == BotDifficulty.Easy)
        {
            return SelectEasyMove(board, legalMoves, perspective, cancellationToken);
        }

        // Medium / Hard: Alpha-Beta Minimax
        TakMove bestMove = legalMoves[0];
        int bestScore = int.MinValue;
        int alpha = int.MinValue;
        int beta = int.MaxValue;

        var orderedMoves = OrderMoves(board, legalMoves, perspective);

        foreach (var move in orderedMoves)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var clone = board.Clone();
            var exec = clone.Execute(move);
            if (!exec.IsSuccess)
                continue;

            // Immediate win check
            if (clone.Phase == GamePhase.Completed && clone.Result?.Winner == perspective)
            {
                return move;
            }

            int score = -Minimax(clone, maxDepth - 1, -beta, -alpha, GetOpponent(perspective), cancellationToken);

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }

            if (bestScore > alpha)
            {
                alpha = bestScore;
            }

            if (alpha >= beta)
            {
                break;
            }
        }

        return bestMove;
    }

    private int Minimax(
        GameBoard board,
        int depth,
        int alpha,
        int beta,
        PlayerColor currentPerspective,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (depth == 0 || board.Phase == GamePhase.Completed)
        {
            return TakEvaluator.Evaluate(board, currentPerspective);
        }

        var legalMoves = MoveValidator.GetAllLegalMoves(board);
        if (legalMoves.Count == 0)
        {
            return TakEvaluator.Evaluate(board, currentPerspective);
        }

        var orderedMoves = OrderMoves(board, legalMoves, currentPerspective);
        int maxScore = int.MinValue;

        foreach (var move in orderedMoves)
        {
            var clone = board.Clone();
            var exec = clone.Execute(move);
            if (!exec.IsSuccess)
                continue;

            if (clone.Phase == GamePhase.Completed && clone.Result?.Winner == currentPerspective)
            {
                return 100_000 + depth; // Favor faster wins
            }

            int score = -Minimax(clone, depth - 1, -beta, -alpha, GetOpponent(currentPerspective), cancellationToken);

            if (score > maxScore)
            {
                maxScore = score;
            }

            if (maxScore > alpha)
            {
                alpha = maxScore;
            }

            if (alpha >= beta)
            {
                break; // Alpha-beta cutoff
            }
        }

        return maxScore;
    }

    private TakMove SelectEasyMove(
        GameBoard board,
        IReadOnlyList<TakMove> legalMoves,
        PlayerColor perspective,
        CancellationToken cancellationToken)
    {
        var scoredMoves = new List<(TakMove move, int score)>();

        foreach (var move in legalMoves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clone = board.Clone();
            var exec = clone.Execute(move);
            if (!exec.IsSuccess)
                continue;

            if (clone.Phase == GamePhase.Completed && clone.Result?.Winner == perspective)
            {
                return move; // Always seize immediate win
            }

            int score = TakEvaluator.Evaluate(clone, perspective);
            scoredMoves.Add((move, score));
        }

        if (scoredMoves.Count == 0)
            return legalMoves[0];

        // Sort descending by score
        scoredMoves.Sort((a, b) => b.score.CompareTo(a.score));

        // Pick uniformly from top 3 candidates (or fewer if less available)
        int poolSize = Math.Min(3, scoredMoves.Count);
        int chosenIndex = _random.Next(poolSize);
        return scoredMoves[chosenIndex].move;
    }

    private static TakMove? SelectFirstTurnPlacement(GameBoard board, IReadOnlyList<TakMove> legalMoves)
    {
        int size = (int)board.Size;
        // Preferred squares: corners first, then perimeter edges
        var corners = new[]
        {
            new Coord(0, 0),
            new Coord(0, size - 1),
            new Coord(size - 1, 0),
            new Coord(size - 1, size - 1)
        };

        foreach (var corner in corners)
        {
            var match = legalMoves.OfType<PlaceMove>().FirstOrDefault(m => m.Target == corner);
            if (match != null)
                return match;
        }

        return null;
    }

    private static IEnumerable<TakMove> OrderMoves(
        GameBoard board,
        IReadOnlyList<TakMove> moves,
        PlayerColor perspective)
    {
        // Simple priority heuristic:
        // 1. Capstone placements / moves (high impact)
        // 2. Center flat placements
        // 3. Slides
        // 4. Other placements
        int size = (int)board.Size;
        float center = (size - 1) / 2.0f;

        return moves.OrderByDescending(m =>
        {
            if (m is PlaceMove pm)
            {
                if (pm.PieceType == PieceType.Capstone) return 100;
                float dist = Math.Abs(pm.Target.X - center) + Math.Abs(pm.Target.Y - center);
                return 50 - (int)dist;
            }
            if (m is SlideMove sm)
            {
                return 40;
            }
            return 0;
        });
    }

    private static PlayerColor GetOpponent(PlayerColor player) =>
        player == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
}

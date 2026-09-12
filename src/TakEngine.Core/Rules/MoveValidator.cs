using System;
using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Core.Board;

namespace TakEngine.Core.Rules;

public static class MoveValidator
{
    private static readonly Direction[] Directions =
    [
        Direction.North,
        Direction.South,
        Direction.East,
        Direction.West
    ];

    public static IReadOnlyList<TakMove> GetLegalMovesForSquare(GameBoard board, Coord coord)
    {
        var legalMoves = new List<TakMove>();

        if (board.Phase == GamePhase.Completed || !board.IsInBounds(coord))
            return legalMoves;

        var stack = board.GetStack(coord);

        // Empty square: can place
        if (stack.IsEmpty)
        {
            if (board.Phase == GamePhase.FirstTurnPlacement)
            {
                legalMoves.Add(new PlaceMove(coord, PieceType.Flat));
                return legalMoves;
            }

            var reserves = board.ActivePlayer == PlayerColor.White ? board.WhiteReserves : board.BlackReserves;

            if (reserves.Stones > 0)
            {
                legalMoves.Add(new PlaceMove(coord, PieceType.Flat));
                legalMoves.Add(new PlaceMove(coord, PieceType.Standing));
            }

            if (reserves.Capstones > 0)
            {
                legalMoves.Add(new PlaceMove(coord, PieceType.Capstone));
            }

            return legalMoves;
        }

        // Occupied square: can move stack if owned by active player and in Playing phase
        if (board.Phase == GamePhase.Playing && stack.Owner == board.ActivePlayer)
        {
            int maxLift = Math.Min(stack.Height, board.CarryLimit);

            foreach (var dir in Directions)
            {
                for (int lift = 1; lift <= maxLift; lift++)
                {
                    GenerateSlideMoves(board, coord, dir, lift, stack, legalMoves);
                }
            }
        }

        return legalMoves;
    }

    private static void GenerateSlideMoves(
        GameBoard board,
        Coord origin,
        Direction direction,
        int liftCount,
        PieceStack originStack,
        List<TakMove> legalMoves)
    {
        (int dx, int dy) = direction switch
        {
            Direction.North => (0, 1),
            Direction.South => (0, -1),
            Direction.East => (1, 0),
            Direction.West => (-1, 0),
            _ => (0, 0)
        };

        // Determine max distance possible in this direction within board bounds
        int maxDist = 0;
        int cx = origin.X;
        int cy = origin.Y;

        while (true)
        {
            cx += dx;
            cy += dy;
            if (!board.IsInBounds(new Coord(cx, cy)))
                break;
            maxDist++;
        }

        if (maxDist == 0)
            return;

        // Recursively partition liftCount into at most maxDist drop steps
        var currentDrops = new List<int>();
        FindDropPartitions(board, origin, dx, dy, liftCount, 1, maxDist, currentDrops, originStack, direction, legalMoves);
    }

    private static void FindDropPartitions(
        GameBoard board,
        Coord origin,
        int dx,
        int dy,
        int remainingPieces,
        int step,
        int maxDist,
        List<int> currentDrops,
        PieceStack originStack,
        Direction direction,
        List<TakMove> legalMoves)
    {
        if (step > maxDist)
            return;

        var targetCoord = new Coord(origin.X + dx * step, origin.Y + dy * step);
        var targetStack = board.GetStack(targetCoord);

        // Check if this square is blocked
        bool isBlocked = false;
        bool isStanding = false;

        if (!targetStack.IsEmpty)
        {
            var top = targetStack.TopPiece!.Value;
            if (top.Type == PieceType.Capstone)
            {
                isBlocked = true;
            }
            else if (top.Type == PieceType.Standing)
            {
                isStanding = true;
            }
        }

        if (isBlocked)
            return;

        // If it's a standing wall, we can ONLY land on it if this is the final step, we drop exactly 1 piece, and that piece is a Capstone
        if (isStanding)
        {
            if (remainingPieces == 1)
            {
                // The last piece dropped is the top of the origin stack
                var pieceToDrop = originStack.Pieces[^1];
                if (pieceToDrop.Type == PieceType.Capstone)
                {
                    currentDrops.Add(1);
                    legalMoves.Add(new SlideMove(origin, direction, Sum(currentDrops), currentDrops.ToArray()));
                    currentDrops.RemoveAt(currentDrops.Count - 1);
                }
            }
            // Cannot pass through a standing wall
            return;
        }

        // Square is empty or has a flat top piece: can drop 1 to remainingPieces
        for (int drop = 1; drop <= remainingPieces; drop++)
        {
            currentDrops.Add(drop);

            if (drop == remainingPieces)
            {
                // Completed partition
                legalMoves.Add(new SlideMove(origin, direction, Sum(currentDrops), currentDrops.ToArray()));
            }
            else
            {
                // Continue to next square
                FindDropPartitions(
                    board,
                    origin,
                    dx,
                    dy,
                    remainingPieces - drop,
                    step + 1,
                    maxDist,
                    currentDrops,
                    originStack,
                    direction,
                    legalMoves);
            }

            currentDrops.RemoveAt(currentDrops.Count - 1);
        }
    }

    private static int Sum(List<int> list)
    {
        int total = 0;
        for (int i = 0; i < list.Count; i++)
            total += list[i];
        return total;
    }
}

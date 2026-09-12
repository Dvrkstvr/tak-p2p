using System;
using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Core.Board;

namespace TakEngine.Core.AI;

public static class TakEvaluator
{
    private const int WinScore = 100_000;
    private const int RoadThreatBonus = 2_500;
    private const int FlatStoneWeight = 100;
    private const int ControlledStackPieceWeight = 15;
    private const int CenterControlWeight = 30;
    private const int CapstoneWeight = 150;
    private const int StandingWallWeight = 40;
    private const int ReserveStoneWeight = 10;
    private const int ReserveCapstoneWeight = 50;

    public static int Evaluate(GameBoard board, PlayerColor perspective)
    {
        if (board.Phase == GamePhase.Completed && board.Result != null)
        {
            if (board.Result.Winner == perspective)
                return WinScore;
            if (board.Result.Winner == GetOpponent(perspective))
                return -WinScore;
            return 0; // Draw
        }

        PlayerColor opponent = GetOpponent(perspective);

        int score = 0;
        int size = (int)board.Size;
        float centerCoord = (size - 1) / 2.0f;

        int myFlats = 0;
        int oppFlats = 0;

        // Scan board grid
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                var stack = board.GetStack(new Coord(x, y));
                if (stack.IsEmpty)
                    continue;

                var topPiece = stack.TopPiece!.Value;
                bool isMine = topPiece.Color == perspective;

                // Center proximity bonus (closer to center gives higher bonus)
                float distToCenter = Math.Abs(x - centerCoord) + Math.Abs(y - centerCoord);
                int centerBonus = (int)Math.Max(0, (size - distToCenter) * CenterControlWeight);

                // Controlled stack height bonus
                int stackSubPieceBonus = (stack.Height - 1) * ControlledStackPieceWeight;

                int squareValue = 0;

                switch (topPiece.Type)
                {
                    case PieceType.Flat:
                        squareValue += FlatStoneWeight + centerBonus + stackSubPieceBonus;
                        if (isMine) myFlats++; else oppFlats++;
                        break;

                    case PieceType.Standing:
                        squareValue += StandingWallWeight + (centerBonus / 2) + stackSubPieceBonus;
                        break;

                    case PieceType.Capstone:
                        squareValue += CapstoneWeight + centerBonus + stackSubPieceBonus;
                        break;
                }

                if (isMine)
                {
                    score += squareValue;
                }
                else
                {
                    score -= squareValue;
                }
            }
        }

        // Road span evaluation
        int myRoadSpan = CalculateMaxRoadSpan(board, perspective);
        int oppRoadSpan = CalculateMaxRoadSpan(board, opponent);

        // Near-completion road threat bonuses
        if (myRoadSpan >= size - 1)
            score += RoadThreatBonus;
        else
            score += myRoadSpan * 200;

        if (oppRoadSpan >= size - 1)
            score -= (RoadThreatBonus + 500); // Prioritize blocking opponent threat
        else
            score -= oppRoadSpan * 200;

        // Reserves evaluation
        var myReserves = perspective == PlayerColor.White ? board.WhiteReserves : board.BlackReserves;
        var oppReserves = opponent == PlayerColor.White ? board.WhiteReserves : board.BlackReserves;

        score += myReserves.Stones * ReserveStoneWeight;
        score += myReserves.Capstones * ReserveCapstoneWeight;
        score -= oppReserves.Stones * ReserveStoneWeight;
        score -= oppReserves.Capstones * ReserveCapstoneWeight;

        return score;
    }

    private static int CalculateMaxRoadSpan(GameBoard board, PlayerColor player)
    {
        int size = (int)board.Size;
        bool[,] visitedH = new bool[size, size];
        bool[,] visitedV = new bool[size, size];

        int maxSpan = 0;

        // Check horizontal span (connected columns from left to right)
        for (int y = 0; y < size; y++)
        {
            var start = new Coord(0, y);
            if (!visitedH[0, y] && IsRoadPiece(board, start, player))
            {
                int span = TraverseRoadSpan(board, start, player, visitedH, isHorizontal: true);
                if (span > maxSpan) maxSpan = span;
            }
        }

        // Check vertical span (connected rows from bottom to top)
        for (int x = 0; x < size; x++)
        {
            var start = new Coord(x, 0);
            if (!visitedV[x, 0] && IsRoadPiece(board, start, player))
            {
                int span = TraverseRoadSpan(board, start, player, visitedV, isHorizontal: false);
                if (span > maxSpan) maxSpan = span;
            }
        }

        return maxSpan;
    }

    private static int TraverseRoadSpan(GameBoard board, Coord start, PlayerColor player, bool[,] visited, bool isHorizontal)
    {
        int size = (int)board.Size;
        var queue = new Queue<Coord>();
        queue.Enqueue(start);
        visited[start.X, start.Y] = true;

        int minCoord = isHorizontal ? start.X : start.Y;
        int maxCoord = isHorizontal ? start.X : start.Y;

        ReadOnlySpan<(int dx, int dy)> neighbors =
        [
            (1, 0), (-1, 0), (0, 1), (0, -1)
        ];

        while (queue.Count > 0)
        {
            var curr = queue.Dequeue();
            int currentCoord = isHorizontal ? curr.X : curr.Y;
            if (currentCoord < minCoord) minCoord = currentCoord;
            if (currentCoord > maxCoord) maxCoord = currentCoord;

            for (int i = 0; i < neighbors.Length; i++)
            {
                int nx = curr.X + neighbors[i].dx;
                int ny = curr.Y + neighbors[i].dy;

                if (nx >= 0 && nx < size && ny >= 0 && ny < size && !visited[nx, ny])
                {
                    var neighborCoord = new Coord(nx, ny);
                    if (IsRoadPiece(board, neighborCoord, player))
                    {
                        visited[nx, ny] = true;
                        queue.Enqueue(neighborCoord);
                    }
                }
            }
        }

        return (maxCoord - minCoord) + 1;
    }

    private static bool IsRoadPiece(GameBoard board, Coord coord, PlayerColor player)
    {
        var stack = board.GetStack(coord);
        if (stack.IsEmpty) return false;

        var top = stack.TopPiece!.Value;
        return top.Color == player && (top.Type == PieceType.Flat || top.Type == PieceType.Capstone);
    }

    private static PlayerColor GetOpponent(PlayerColor player) =>
        player == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
}

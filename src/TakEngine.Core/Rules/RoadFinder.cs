using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Core.Board;

namespace TakEngine.Core.Rules;

public static class RoadFinder
{
    private static readonly (int dx, int dy)[] OrthogonalNeighbors =
    [
        (1, 0),
        (-1, 0),
        (0, 1),
        (0, -1)
    ];

    public static bool HasRoad(PieceStack[,] grid, int size, PlayerColor player)
    {
        return HasNorthSouthRoad(grid, size, player) || HasEastWestRoad(grid, size, player);
    }

    public static bool HasNorthSouthRoad(PieceStack[,] grid, int size, PlayerColor player)
    {
        var visited = new bool[size, size];
        var queue = new Queue<Coord>();

        // Find starting points at South (Y = 0)
        for (int x = 0; x < size; x++)
        {
            if (CanCarryRoad(grid[x, 0], player))
            {
                visited[x, 0] = true;
                queue.Enqueue(new Coord(x, 0));
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Y == size - 1)
                return true;

            foreach (var (dx, dy) in OrthogonalNeighbors)
            {
                int nx = current.X + dx;
                int ny = current.Y + dy;

                if (nx >= 0 && nx < size && ny >= 0 && ny < size && !visited[nx, ny])
                {
                    if (CanCarryRoad(grid[nx, ny], player))
                    {
                        visited[nx, ny] = true;
                        queue.Enqueue(new Coord(nx, ny));
                    }
                }
            }
        }

        return false;
    }

    public static bool HasEastWestRoad(PieceStack[,] grid, int size, PlayerColor player)
    {
        var visited = new bool[size, size];
        var queue = new Queue<Coord>();

        // Find starting points at West (X = 0)
        for (int y = 0; y < size; y++)
        {
            if (CanCarryRoad(grid[0, y], player))
            {
                visited[0, y] = true;
                queue.Enqueue(new Coord(0, y));
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.X == size - 1)
                return true;

            foreach (var (dx, dy) in OrthogonalNeighbors)
            {
                int nx = current.X + dx;
                int ny = current.Y + dy;

                if (nx >= 0 && nx < size && ny >= 0 && ny < size && !visited[nx, ny])
                {
                    if (CanCarryRoad(grid[nx, ny], player))
                    {
                        visited[nx, ny] = true;
                        queue.Enqueue(new Coord(nx, ny));
                    }
                }
            }
        }

        return false;
    }

    public static bool CanCarryRoad(PieceStack stack, PlayerColor player)
    {
        if (stack.IsEmpty)
            return false;

        var top = stack.TopPiece;
        if (!top.HasValue)
            return false;

        return top.Value.Color == player &&
               (top.Value.Type == PieceType.Flat || top.Value.Type == PieceType.Capstone);
    }
}

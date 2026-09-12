using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Rules;
using Xunit;

namespace TakEngine.Core.Tests;

public class RoadFinderTests
{
    [Fact]
    public void VerticalRoad_NorthToSouth_Detected()
    {
        var board = new GameBoard(BoardSize.Five);

        // Build a complete column of White flats on file 2 (X = 2, Y = 0 to 4)
        for (int y = 0; y < 5; y++)
        {
            var stack = board.GetStack(new Coord(2, y));
            stack.Push(new Piece(PlayerColor.White, PieceType.Flat));
        }

        Assert.True(RoadFinder.HasNorthSouthRoad(GetGrid(board), 5, PlayerColor.White));
        Assert.False(RoadFinder.HasEastWestRoad(GetGrid(board), 5, PlayerColor.White));
        Assert.True(RoadFinder.HasRoad(GetGrid(board), 5, PlayerColor.White));
        Assert.False(RoadFinder.HasRoad(GetGrid(board), 5, PlayerColor.Black));
    }

    [Fact]
    public void HorizontalRoad_EastToWest_Detected()
    {
        var board = new GameBoard(BoardSize.Five);

        // Build a complete row of Black flats on rank 3 (Y = 3, X = 0 to 4)
        for (int x = 0; x < 5; x++)
        {
            var stack = board.GetStack(new Coord(x, 3));
            stack.Push(new Piece(PlayerColor.Black, PieceType.Flat));
        }

        Assert.True(RoadFinder.HasEastWestRoad(GetGrid(board), 5, PlayerColor.Black));
        Assert.False(RoadFinder.HasNorthSouthRoad(GetGrid(board), 5, PlayerColor.Black));
        Assert.True(RoadFinder.HasRoad(GetGrid(board), 5, PlayerColor.Black));
    }

    [Fact]
    public void SerpentineZigzagRoad_Detected()
    {
        // Board size 4
        // Path: (0,0) -> (1,0) -> (1,1) -> (2,1) -> (2,2) -> (3,2) -> (3,3)
        // Starts at Y = 0 and ends at Y = 3 (North-South connection!)
        // Also starts at X = 0 and ends at X = 3 (East-West connection too!)
        var board = new GameBoard(BoardSize.Four);

        Coord[] path =
        [
            new(0, 0),
            new(1, 0),
            new(1, 1),
            new(2, 1),
            new(2, 2),
            new(3, 2),
            new(3, 3)
        ];

        foreach (var c in path)
        {
            board.GetStack(c).Push(new Piece(PlayerColor.White, PieceType.Flat));
        }

        Assert.True(RoadFinder.HasRoad(GetGrid(board), 4, PlayerColor.White));
    }

    [Fact]
    public void Road_CanIncludeCapstones()
    {
        var board = new GameBoard(BoardSize.Five);

        // Straight road with a Capstone in the middle
        for (int y = 0; y < 5; y++)
        {
            var type = (y == 2) ? PieceType.Capstone : PieceType.Flat;
            board.GetStack(new Coord(1, y)).Push(new Piece(PlayerColor.White, type));
        }

        Assert.True(RoadFinder.HasRoad(GetGrid(board), 5, PlayerColor.White));
    }

    [Fact]
    public void StandingWall_DoesNotContributeToRoad_AndBlocksIt()
    {
        var board = new GameBoard(BoardSize.Five);

        for (int y = 0; y < 5; y++)
        {
            // Position y=2 is a standing wall
            var type = (y == 2) ? PieceType.Standing : PieceType.Flat;
            board.GetStack(new Coord(1, y)).Push(new Piece(PlayerColor.White, type));
        }

        Assert.False(RoadFinder.HasRoad(GetGrid(board), 5, PlayerColor.White));
    }

    [Fact]
    public void OpponentStone_BlocksRoad()
    {
        var board = new GameBoard(BoardSize.Five);

        for (int y = 0; y < 5; y++)
        {
            var color = (y == 2) ? PlayerColor.Black : PlayerColor.White;
            board.GetStack(new Coord(1, y)).Push(new Piece(color, PieceType.Flat));
        }

        Assert.False(RoadFinder.HasRoad(GetGrid(board), 5, PlayerColor.White));
    }

    [Fact]
    public void Diagonals_DoNotConnect()
    {
        var board = new GameBoard(BoardSize.Four);

        // (0,0), (1,1), (2,2), (3,3) - only diagonal touching, no orthogonal connection!
        for (int i = 0; i < 4; i++)
        {
            board.GetStack(new Coord(i, i)).Push(new Piece(PlayerColor.White, PieceType.Flat));
        }

        Assert.False(RoadFinder.HasRoad(GetGrid(board), 4, PlayerColor.White));
    }

    [Fact]
    public void InGameMove_DetectsRoadWin_AndEndsGame()
    {
        var board = new GameBoard(BoardSize.Four);
        // Turn 1 swap rule
        board.Place(new Coord(0, 3), PieceType.Flat); // Black flat at 0,3
        board.Place(new Coord(3, 0), PieceType.Flat); // White flat at 3,0

        // Set up White road almost complete on column 1:
        board.GetStack(new Coord(1, 0)).Push(new Piece(PlayerColor.White, PieceType.Flat));
        board.GetStack(new Coord(1, 1)).Push(new Piece(PlayerColor.White, PieceType.Flat));
        board.GetStack(new Coord(1, 2)).Push(new Piece(PlayerColor.White, PieceType.Flat));

        // White turn: place at 1,3 completing the road
        var placeResult = board.Place(new Coord(1, 3), PieceType.Flat);
        Assert.True(placeResult.IsSuccess, placeResult.ErrorMessage);

        Assert.Equal(GamePhase.Completed, board.Phase);
        Assert.NotNull(board.Result);
        Assert.Equal(PlayerColor.White, board.Result!.Winner);
        Assert.Equal(GameEndReason.Road, board.Result!.Reason);
    }

    private static PieceStack[,] GetGrid(GameBoard board)
    {
        int size = (int)board.Size;
        var grid = new PieceStack[size, size];
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                grid[x, y] = board.GetStack(new Coord(x, y));
            }
        }
        return grid;
    }
}

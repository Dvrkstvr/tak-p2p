using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Rules;
using Xunit;

namespace TakEngine.Core.Tests;

public class MovementTests
{
    private GameBoard SetupPlayingBoard()
    {
        var board = new GameBoard(BoardSize.Five);
        // First turns
        board.Place(new Coord(0, 0), PieceType.Flat); // Places Black flat on 0,0
        board.Place(new Coord(4, 4), PieceType.Flat); // Places White flat on 4,4
        // Now Phase == Playing, ActivePlayer == White
        return board;
    }

    [Fact]
    public void SlideMove_SimpleSingleSquare_Succeeds()
    {
        var board = SetupPlayingBoard();

        // 4,4 has White flat. Move North? Out of bounds!
        // Move West to 3,4
        var moveResult = board.Move(new Coord(4, 4), Direction.West, [1]);
        Assert.True(moveResult.IsSuccess, moveResult.ErrorMessage);

        Assert.True(board.GetStack(new Coord(4, 4)).IsEmpty);
        var targetStack = board.GetStack(new Coord(3, 4));
        Assert.Equal(1, targetStack.Height);
        Assert.Equal(PlayerColor.White, targetStack.Owner);
    }

    [Fact]
    public void SlideMove_CannotExceedCarryLimit()
    {
        var board = new GameBoard(BoardSize.Four); // Carry limit: 4
        board.Place(new Coord(0, 0), PieceType.Flat);
        board.Place(new Coord(3, 3), PieceType.Flat);

        // Manually build a stack of 5 pieces at 3,3
        var stack = board.GetStack(new Coord(3, 3));
        for (int i = 0; i < 4; i++)
            stack.Push(new Piece(PlayerColor.White, PieceType.Flat));

        Assert.Equal(5, stack.Height);

        // Try lifting 5 pieces (carry limit is 4)
        var result = board.Move(new Coord(3, 3), Direction.West, [2, 3]);
        Assert.False(result.IsSuccess);
        Assert.Contains("carry limit", result.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SlideMove_CannotMoveOntoOrThroughCapstone()
    {
        var board = SetupPlayingBoard();

        // White places Capstone at 1,1
        board.Place(new Coord(1, 1), PieceType.Capstone);

        // Black turn: places flat at 1,0
        board.Place(new Coord(1, 0), PieceType.Flat);

        // White turn: passes or places flat at 2,1
        board.Place(new Coord(2, 1), PieceType.Flat);

        // Black turn: moves 1,0 North onto Capstone at 1,1
        var illegalMove = board.Move(new Coord(1, 0), Direction.North, [1]);
        Assert.False(illegalMove.IsSuccess);
        Assert.Contains("Capstone", illegalMove.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SlideMove_CannotMoveThroughStandingWall()
    {
        var board = SetupPlayingBoard();

        // White places Standing Wall at 2,2
        board.Place(new Coord(2, 2), PieceType.Standing);

        // Black places flat at 2,0
        board.Place(new Coord(2, 0), PieceType.Flat);

        // Manually add pieces to Black stack at 2,0 to lift 3
        var stack = board.GetStack(new Coord(2, 0));
        stack.Push(new Piece(PlayerColor.Black, PieceType.Flat));
        stack.Push(new Piece(PlayerColor.Black, PieceType.Flat));

        // White places at 0,1
        board.Place(new Coord(0, 1), PieceType.Flat);

        // Black tries to move 2,0 North: drop 1 at 2,1, drop 1 at 2,2 (wall), drop 1 at 2,3
        var result = board.Move(new Coord(2, 0), Direction.North, [1, 1, 1]);
        Assert.False(result.IsSuccess);
        Assert.Contains("Standing wall", result.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capstone_FlattensStandingWall_WhenSolePieceOnFinalDrop()
    {
        var board = SetupPlayingBoard();

        // White places Capstone at 2,1
        board.Place(new Coord(2, 1), PieceType.Capstone);

        // Black places Standing Wall at 2,2
        board.Place(new Coord(2, 2), PieceType.Standing);

        // White moves Capstone North to 2,2: lift 1, drop 1
        var flattenResult = board.Move(new Coord(2, 1), Direction.North, [1]);
        Assert.True(flattenResult.IsSuccess, flattenResult.ErrorMessage);

        var targetStack = board.GetStack(new Coord(2, 2));
        Assert.Equal(2, targetStack.Height);
        // Bottom piece was the flattened wall -> now Flat
        Assert.Equal(PieceType.Flat, targetStack.Pieces[0].Type);
        Assert.Equal(PlayerColor.Black, targetStack.Pieces[0].Color);
        // Top piece is White Capstone
        Assert.Equal(PieceType.Capstone, targetStack.TopPiece!.Value.Type);
        Assert.Equal(PlayerColor.White, targetStack.TopPiece!.Value.Color);
    }

    [Fact]
    public void NonCapstone_CannotFlattenStandingWall()
    {
        var board = SetupPlayingBoard();

        // White places Flat at 2,1
        board.Place(new Coord(2, 1), PieceType.Flat);

        // Black places Standing Wall at 2,2
        board.Place(new Coord(2, 2), PieceType.Standing);

        // White moves Flat North onto Standing wall
        var result = board.Move(new Coord(2, 1), Direction.North, [1]);
        Assert.False(result.IsSuccess);
        Assert.Contains("Capstone", result.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MoveValidator_GeneratesCorrectLegalMoves()
    {
        var board = SetupPlayingBoard();

        // Square 4,4 has a White Flat piece
        var moves = MoveValidator.GetLegalMovesForSquare(board, new Coord(4, 4));

        // From 4,4, White can move South (4,3) or West (3,4) with lift 1, drops [1]
        Assert.Equal(2, moves.Count);

        // An empty square (e.g. 2,2) can have Flat, Standing, and Capstone placements
        var emptyMoves = MoveValidator.GetLegalMovesForSquare(board, new Coord(2, 2));
        Assert.Equal(3, emptyMoves.Count);
    }
}

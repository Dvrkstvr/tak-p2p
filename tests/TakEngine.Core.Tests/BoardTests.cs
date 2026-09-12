using System;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using Xunit;

namespace TakEngine.Core.Tests;

public class BoardTests
{
    [Theory]
    [InlineData(BoardSize.Four, 15, 0, 4)]
    [InlineData(BoardSize.Five, 21, 1, 5)]
    [InlineData(BoardSize.Six, 30, 1, 6)]
    public void Board_Initializes_WithCorrectReservesAndCarryLimit(
        BoardSize size,
        int expectedStones,
        int expectedCapstones,
        int expectedCarry)
    {
        var board = new GameBoard(size);

        Assert.Equal(size, board.Size);
        Assert.Equal(expectedCarry, board.CarryLimit);
        Assert.Equal(1, board.TurnNumber);
        Assert.Equal(PlayerColor.White, board.ActivePlayer);
        Assert.Equal(GamePhase.FirstTurnPlacement, board.Phase);

        Assert.Equal(expectedStones, board.WhiteReserves.Stones);
        Assert.Equal(expectedCapstones, board.WhiteReserves.Capstones);
        Assert.Equal(expectedStones, board.BlackReserves.Stones);
        Assert.Equal(expectedCapstones, board.BlackReserves.Capstones);
    }

    [Fact]
    public void FirstTurn_FollowsSwapRule_PlacesOpponentFlatStones()
    {
        var board = new GameBoard(BoardSize.Five);

        // Turn 1: White places a piece (swap rule -> Black flat stone)
        var whiteResult = board.Place(new Coord(0, 0), PieceType.Flat);
        Assert.True(whiteResult.IsSuccess, whiteResult.ErrorMessage);

        var stack00 = board.GetStack(new Coord(0, 0));
        Assert.Equal(PlayerColor.Black, stack00.Owner);
        Assert.Equal(PieceType.Flat, stack00.TopPiece!.Value.Type);
        Assert.Equal(PlayerColor.Black, board.ActivePlayer);
        Assert.Equal(GamePhase.FirstTurnPlacement, board.Phase);
        Assert.Equal(20, board.BlackReserves.Stones); // Deducted from Black

        // Turn 1: Black places a piece (swap rule -> White flat stone)
        var blackResult = board.Place(new Coord(4, 4), PieceType.Flat);
        Assert.True(blackResult.IsSuccess, blackResult.ErrorMessage);

        var stack44 = board.GetStack(new Coord(4, 4));
        Assert.Equal(PlayerColor.White, stack44.Owner);
        Assert.Equal(PieceType.Flat, stack44.TopPiece!.Value.Type);
        Assert.Equal(PlayerColor.White, board.ActivePlayer);
        Assert.Equal(GamePhase.Playing, board.Phase);
        Assert.Equal(2, board.TurnNumber);
        Assert.Equal(20, board.WhiteReserves.Stones); // Deducted from White
    }

    [Fact]
    public void FirstTurn_CannotPlaceStandingOrCapstone()
    {
        var board = new GameBoard(BoardSize.Five);

        var standingRes = board.Place(new Coord(0, 0), PieceType.Standing);
        Assert.False(standingRes.IsSuccess);

        var capstoneRes = board.Place(new Coord(0, 0), PieceType.Capstone);
        Assert.False(capstoneRes.IsSuccess);
    }

    [Fact]
    public void CannotPlace_OnOccupiedSquare()
    {
        var board = new GameBoard(BoardSize.Five);
        board.Place(new Coord(1, 1), PieceType.Flat);

        var failResult = board.Place(new Coord(1, 1), PieceType.Flat);
        Assert.False(failResult.IsSuccess);
        Assert.Contains("occupied", failResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CannotPlace_OutOfBounds()
    {
        var board = new GameBoard(BoardSize.Five);

        var outOfBounds = board.Place(new Coord(5, 5), PieceType.Flat);
        Assert.False(outOfBounds.IsSuccess);
        Assert.Contains("bounds", outOfBounds.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resign_EndsGameImmediately()
    {
        var board = new GameBoard(BoardSize.Five);

        var result = board.Resign(PlayerColor.White);
        Assert.True(result.IsSuccess);
        Assert.Equal(GamePhase.Completed, board.Phase);
        Assert.NotNull(board.Result);
        Assert.Equal(PlayerColor.Black, board.Result!.Winner);
        Assert.Equal(GameEndReason.Resignation, board.Result!.Reason);
    }
}

using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Serialization;
using Xunit;

namespace TakEngine.Core.Tests;

public class PtnTests
{
    [Theory]
    [InlineData("a1", 0, 0, PieceType.Flat)]
    [InlineData("c3", 2, 2, PieceType.Flat)]
    [InlineData("Fa1", 0, 0, PieceType.Flat)]
    [InlineData("Sa1", 0, 0, PieceType.Standing)]
    [InlineData("Sc3", 2, 2, PieceType.Standing)]
    [InlineData("Ca1", 0, 0, PieceType.Capstone)]
    [InlineData("Ce5", 4, 4, PieceType.Capstone)]
    public void ParseMove_Placements_ReturnsCorrectPlaceMove(
        string ptn,
        int expectedX,
        int expectedY,
        PieceType expectedType)
    {
        var move = PtnParser.ParseMove(ptn);

        var place = Assert.IsType<PlaceMove>(move);
        Assert.Equal(new Coord(expectedX, expectedY), place.Target);
        Assert.Equal(expectedType, place.PieceType);
    }

    [Theory]
    [InlineData("c3+", 2, 2, Direction.North, 1, new[] { 1 })]
    [InlineData("a1>", 0, 0, Direction.East, 1, new[] { 1 })]
    [InlineData("3c3+12", 2, 2, Direction.North, 3, new[] { 1, 2 })]
    [InlineData("4b2>112", 1, 1, Direction.East, 4, new[] { 1, 1, 2 })]
    [InlineData("2d4-11*", 3, 3, Direction.South, 2, new[] { 1, 1 })]
    [InlineData("c4<", 2, 3, Direction.West, 1, new[] { 1 })]
    public void ParseMove_Slides_ReturnsCorrectSlideMove(
        string ptn,
        int expectedX,
        int expectedY,
        Direction expectedDir,
        int expectedLift,
        int[] expectedDrops)
    {
        var move = PtnParser.ParseMove(ptn);

        var slide = Assert.IsType<SlideMove>(move);
        Assert.Equal(new Coord(expectedX, expectedY), slide.Origin);
        Assert.Equal(expectedDir, slide.Direction);
        Assert.Equal(expectedLift, slide.LiftCount);
        Assert.Equal(expectedDrops, slide.Drops);
    }

    [Fact]
    public void ParseGame_ParsesHeadersMovesAndResult()
    {
        string ptn = """
            [Event "Casual Game"]
            [Site "Tak P2P"]
            [Date "2026.09.12"]
            [Player1 "Alice"]
            [Player2 "Bob"]
            [Size "5"]
            [Result "R-0"]

            1. a1 e5
            2. c3 c4
            3. 3c3+12 d3
            R-0
            """;

        var game = PtnParser.ParseGame(ptn);

        Assert.Equal("Casual Game", game.Headers["Event"]);
        Assert.Equal("5", game.Headers["Size"]);
        Assert.Equal("R-0", game.Result);
        Assert.Equal(6, game.Moves.Count);

        // Move 1: a1 (White)
        var m1 = Assert.IsType<PlaceMove>(game.Moves[0]);
        Assert.Equal(new Coord(0, 0), m1.Target);

        // Move 2: e5 (Black)
        var m2 = Assert.IsType<PlaceMove>(game.Moves[1]);
        Assert.Equal(new Coord(4, 4), m2.Target);

        // Move 5: 3c3+12 (White)
        var m5 = Assert.IsType<SlideMove>(game.Moves[4]);
        Assert.Equal(new Coord(2, 2), m5.Origin);
        Assert.Equal(Direction.North, m5.Direction);
        Assert.Equal(3, m5.LiftCount);
        Assert.Equal([1, 2], m5.Drops);
    }

    [Fact]
    public void ReplayGame_ExecutesPtnMovesDeterministically()
    {
        string ptn = """
            [Size "4"]
            1. a1 d4
            2. b2 c3
            """;

        var game = PtnParser.ParseGame(ptn);
        var board = new GameBoard(BoardSize.Four);

        foreach (var move in game.Moves)
        {
            if (move is PlaceMove place)
            {
                var res = board.Place(place.Target, place.PieceType);
                Assert.True(res.IsSuccess, res.ErrorMessage);
            }
            else if (move is SlideMove slide)
            {
                var res = board.Move(slide.Origin, slide.Direction, slide.Drops);
                Assert.True(res.IsSuccess, res.ErrorMessage);
            }
        }

        // Check Turn 1 swap rule took effect
        Assert.Equal(PlayerColor.Black, board.GetStack(new Coord(0, 0)).Owner);
        Assert.Equal(PlayerColor.White, board.GetStack(new Coord(3, 3)).Owner);

        // Turn 2 normal placements
        Assert.Equal(PlayerColor.White, board.GetStack(new Coord(1, 1)).Owner);
        Assert.Equal(PlayerColor.Black, board.GetStack(new Coord(2, 2)).Owner);

        Assert.Equal(3, board.TurnNumber);
        Assert.Equal(PlayerColor.White, board.ActivePlayer);
    }
}

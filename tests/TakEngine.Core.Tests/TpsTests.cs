using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Serialization;
using Xunit;

namespace TakEngine.Core.Tests;

public class TpsTests
{
    [Theory]
    [InlineData(BoardSize.Four, "x4/x4/x4/x4 1 1")]
    [InlineData(BoardSize.Five, "x5/x5/x5/x5/x5 1 1")]
    [InlineData(BoardSize.Six, "x6/x6/x6/x6/x6/x6 1 1")]
    public void Serialize_EmptyBoard_ReturnsExpectedTps(BoardSize size, string expectedTps)
    {
        var board = new GameBoard(size);
        string tps = TpsSerializer.Serialize(board);

        Assert.Equal(expectedTps, tps);
    }

    [Fact]
    public void Serialize_BoardWithMixedPiecesAndStacks_ProducesAccurateTps()
    {
        var board = new GameBoard(BoardSize.Five);

        // Turn 1 swap
        board.Place(new Coord(0, 0), PieceType.Flat); // Black flat at a1
        board.Place(new Coord(4, 4), PieceType.Flat); // White flat at e5

        // Turn 2
        board.Place(new Coord(2, 2), PieceType.Standing); // White standing wall at c3
        board.Place(new Coord(1, 1), PieceType.Capstone); // Black capstone at b2

        // Stack at c3: White standing on top of nothing -> "1S"
        // At e5 (rank 5): x4,1
        // At c3 (rank 3): x2,1S,x2
        // At b2 (rank 2): x,2C,x3
        // At a1 (rank 1): 2,x4
        string tps = TpsSerializer.Serialize(board);

        Assert.Equal("x4,1/x5/x2,1S,x2/x,2C,x3/2,x4 1 3", tps);
    }

    [Fact]
    public void Deserialize_ProducesMatchingBoardState()
    {
        string originalTps = "x4,1/x5/x2,1S,x2/x,2C,x3/2,x4 1 3";

        var board = TpsSerializer.Deserialize(originalTps);

        Assert.Equal(BoardSize.Five, board.Size);
        Assert.Equal(PlayerColor.White, board.ActivePlayer);
        Assert.Equal(3, board.TurnNumber);

        // e5: White Flat
        var e5 = board.GetStack(new Coord(4, 4));
        Assert.Equal(1, e5.Height);
        Assert.Equal(PlayerColor.White, e5.Owner);
        Assert.Equal(PieceType.Flat, e5.TopPiece!.Value.Type);

        // c3: White Standing
        var c3 = board.GetStack(new Coord(2, 2));
        Assert.Equal(1, c3.Height);
        Assert.Equal(PlayerColor.White, c3.Owner);
        Assert.Equal(PieceType.Standing, c3.TopPiece!.Value.Type);

        // b2: Black Capstone
        var b2 = board.GetStack(new Coord(1, 1));
        Assert.Equal(1, b2.Height);
        Assert.Equal(PlayerColor.Black, b2.Owner);
        Assert.Equal(PieceType.Capstone, b2.TopPiece!.Value.Type);

        // a1: Black Flat
        var a1 = board.GetStack(new Coord(0, 0));
        Assert.Equal(1, a1.Height);
        Assert.Equal(PlayerColor.Black, a1.Owner);
        Assert.Equal(PieceType.Flat, a1.TopPiece!.Value.Type);
    }

    [Fact]
    public void RoundTrip_SerializeAndDeserialize_AreIdentical()
    {
        string tps = "1,x,2,x,1/x,121,x3/x2,2S,x2/x3,1C,x/2,x4 2 12";

        var board = TpsSerializer.Deserialize(tps);
        string reserialized = TpsSerializer.Serialize(board);

        Assert.Equal(tps, reserialized);
    }
}

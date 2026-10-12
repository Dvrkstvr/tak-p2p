using System;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Serialization;
using TakEngine.Core.Session;
using Xunit;

namespace TakEngine.Core.Tests;

/// <summary>F-058: every standard board size (3x3 to 8x8) and komi.</summary>
public class BoardSizeAndKomiTests
{
    [Theory]
    [InlineData(BoardSize.Three, 10, 0)]
    [InlineData(BoardSize.Four, 15, 0)]
    [InlineData(BoardSize.Five, 21, 1)]
    [InlineData(BoardSize.Six, 30, 1)]
    [InlineData(BoardSize.Seven, 40, 2)]
    [InlineData(BoardSize.Eight, 50, 2)]
    public void NewBoard_HasTheStandardReservesAndCarryLimit(BoardSize size, int stones, int capstones)
    {
        var board = new GameBoard(size);

        Assert.Equal(new PlayerReserves(stones, capstones), board.WhiteReserves);
        Assert.Equal(new PlayerReserves(stones, capstones), board.BlackReserves);
        Assert.Equal((int)size, board.CarryLimit);
    }

    [Theory]
    [InlineData(BoardSize.Three)]
    [InlineData(BoardSize.Seven)]
    [InlineData(BoardSize.Eight)]
    public void Tps_RoundTripsTheNewSizes(BoardSize size)
    {
        var board = new GameBoard(size);
        int last = (int)size - 1;
        Assert.True(board.Place(new Coord(0, 0), PieceType.Flat).IsSuccess);
        Assert.True(board.Place(new Coord(last, last), PieceType.Flat).IsSuccess);
        Assert.True(board.Place(new Coord(1, 0), PieceType.Standing).IsSuccess);

        string tps = TpsSerializer.Serialize(board);
        var restored = TpsSerializer.Deserialize(tps);

        Assert.Equal(size, restored.Size);
        Assert.Equal(tps, TpsSerializer.Serialize(restored));
        Assert.Equal(board.WhiteReserves, restored.WhiteReserves);
        Assert.Equal(board.BlackReserves, restored.BlackReserves);
    }

    [Fact]
    public void EightByEight_RoadAcrossTheBoard_Wins()
    {
        // White flats on a1..g1, White to move: h1 completes the road.
        var board = TpsSerializer.Deserialize("x8/x8/x8/x8/x8/x8/2,2,2,2,2,2,x2/1,1,1,1,1,1,1,x 1 8");

        Assert.True(board.Place(new Coord(7, 0), PieceType.Flat).IsSuccess);

        Assert.Equal(GamePhase.Completed, board.Phase);
        Assert.Equal(new GameResult(false, PlayerColor.White, GameEndReason.Road), board.Result);
    }

    [Fact]
    public void SevenBySeven_HasTwoCapstonesEach()
    {
        var board = TpsSerializer.Deserialize("x7/x7/x7/x7/x7/x7/x7 1 2");

        Assert.True(board.Place(new Coord(0, 0), PieceType.Capstone).IsSuccess);
        Assert.True(board.Place(new Coord(6, 6), PieceType.Flat).IsSuccess);
        Assert.True(board.Place(new Coord(1, 0), PieceType.Capstone).IsSuccess);
        Assert.True(board.Place(new Coord(5, 6), PieceType.Flat).IsSuccess);
        Assert.False(board.Place(new Coord(2, 0), PieceType.Capstone).IsSuccess); // White has used both
    }

    [Theory]
    [InlineData(0, null)]                // 5 vs 4: White
    [InlineData(1, null)]                // 5 vs 4.5: White
    [InlineData(2, "draw")]              // 5 vs 5: draw
    [InlineData(3, "black")]             // 5 vs 5.5: Black
    [InlineData(4, "black")]             // 5 vs 6: Black
    public void FlatCount_AddsKomiToBlack(int komiHalves, string? expected)
    {
        // 3x3, White to move, all reserves irrelevant: White's flat on c1 fills the board with 5 White and 4 Black flats.
        var board = TpsSerializer.Deserialize("1,2,1/2,1,2/1,2,x 1 5", komiHalves);

        Assert.True(board.Place(new Coord(2, 0), PieceType.Flat).IsSuccess);

        Assert.NotNull(board.Result);
        Assert.Equal(GameEndReason.FlatCount, board.Result.Reason);
        switch (expected)
        {
            case "draw":
                Assert.True(board.Result.IsDraw);
                break;
            case "black":
                Assert.Equal(PlayerColor.Black, board.Result.Winner);
                break;
            default:
                Assert.Equal(PlayerColor.White, board.Result.Winner);
                break;
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(GameBoard.MaxKomiHalves + 1)]
    public void Komi_OutOfRange_IsRejected(int komiHalves)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameBoard(BoardSize.Five, komiHalves));
    }

    [Fact]
    public void Komi_SurvivesSnapshotAndClone()
    {
        var board = new GameBoard(BoardSize.Six, komiHalves: 4);

        Assert.Equal(4, board.ToSnapshot().KomiHalves);
        Assert.Equal(4, GameBoard.FromSnapshot(board.ToSnapshot()).KomiHalves);
        Assert.Equal(4, board.Clone().KomiHalves);
    }

    [Fact]
    public void LocalSession_PlaysWithKomi()
    {
        var session = TakGameSession.CreateLocal(BoardSize.Six, komiHalves: 4);

        Assert.Equal(4, session.CurrentBoard.KomiHalves);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("2", 4)]
    [InlineData("2.5", 5)]
    [InlineData("0.5", 1)]
    public void PtnKomiHeader_ParsesToHalfFlats(string header, int halves)
    {
        Assert.Equal(halves, PtnParser.ParseKomiHalves(header));
        Assert.Equal(header, PtnParser.FormatKomi(halves));
    }

    [Theory]
    [InlineData("2.25")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void PtnKomiHeader_RejectsInvalidValues(string header)
    {
        Assert.Throws<FormatException>(() => PtnParser.ParseKomiHalves(header));
    }

    [Fact]
    public void FormatGame_WritesTheKomiHeaderOnlyWhenSet()
    {
        Assert.Contains("[Komi \"2.5\"]", PtnParser.FormatGame(new GameBoard(BoardSize.Six, 5), []));
        Assert.DoesNotContain("Komi", PtnParser.FormatGame(new GameBoard(BoardSize.Six), []));
    }
}

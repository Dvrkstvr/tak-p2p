using TakApp.Blazor.Services;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Rules;
using TakEngine.Core.Serialization;

namespace TakApp.Blazor.Tests;

/// <summary>
/// The gesture state machine every game screen and both board views share (pipeline/design/gestures.md). What it offers comes from
/// the engine's legal moves; a completed gesture returns the move to send. Row ids in the test names are the gestures.md rows.
/// </summary>
public class MoveInputTests
{
    private static GameBoard Board(string tps) => TpsSerializer.Deserialize(tps);

    private static MoveInput Input(GameBoard board, bool canAct = true)
    {
        var input = new MoveInput();
        input.Sync(board, c => MoveValidator.GetLegalMovesForSquare(board, c), canAct);
        return input;
    }

    private static Coord C(string s) => Coord.FromAlgebraic(s);

    [Fact]
    public void G01_NewGame_OffersEveryEmptySquareForAFlat()
    {
        var input = Input(new GameBoard(BoardSize.Five));
        Assert.Equal(PieceType.Flat, input.Piece);
        Assert.Equal(25, input.PlaceTargets.Count);
        Assert.Null(input.Slide);
    }

    [Fact]
    public void G04_WallInTheOpening_OffersNothing_ButAClickStillSendsIt()
    {
        var input = Input(new GameBoard(BoardSize.Five));
        input.SelectPiece(PieceType.Standing);
        Assert.Empty(input.PlaceTargets);
        // G-10b: the engine refuses it, not the input.
        Assert.Equal(new PlaceMove(C("c3"), PieceType.Standing), input.Click(C("c3")));
    }

    [Fact]
    public void G11_RightClickOnEmpty_SendsAWall_AndKeepsTheSelectedPiece()
    {
        var input = Input(Board("x5/x5/x5/x5/2,x3,1 1 2"));
        Assert.Equal(new PlaceMove(C("c3"), PieceType.Standing), input.RightClick(C("c3")));
        Assert.Equal(PieceType.Flat, input.Piece);
    }

    [Fact]
    public void G12_G21_SingleStone_MovesAsSoonAsItLands()
    {
        // White flat on b2, white to move.
        var input = Input(Board("x5/x5/x5/x,1,x3/2,x3,1 1 3"));
        Assert.Null(input.Click(C("b2")));
        Assert.Equal(C("b2"), input.Slide!.Origin);
        Assert.Equal(1, input.Slide.Carry);
        Assert.Contains(C("b3"), input.SlideTargets);
        Assert.Equal("b2+", input.Click(C("b3"))!.ToPtn());
        Assert.Null(input.Slide);
    }

    [Fact]
    public void G20_ClickingTheOrigin_LeavesOneMoreBehind_AndWraps()
    {
        // Three white flats on b3.
        var input = Input(Board("x5/x5/x,111,x3/x5/2,x3,2 1 6"));
        input.Click(C("b3"));
        Assert.Equal(3, input.Slide!.Carry);
        input.Click(C("b3"));
        Assert.Equal(2, input.Slide.Carry);
        input.Click(C("b3"));
        input.Click(C("b3"));
        Assert.Equal(3, input.Slide.Carry);
    }

    [Fact]
    public void G30_G31_SteppingAndStacking_BuildTheSplit()
    {
        var input = Input(Board("x5/x5/x,111,x3/x5/2,x3,2 1 6"));
        input.Click(C("b3"));
        Assert.Null(input.Click(C("c3"))); // one on c3, two in hand
        Assert.Equal(C("c3"), input.CurrentStep);
        Assert.Equal(C("d3"), input.NextStep);
        Assert.Null(input.Click(C("c3"))); // a second on c3
        var move = input.Click(C("d3"));
        Assert.Equal("3b3>21", move!.ToPtn());
    }

    [Fact]
    public void G32_ClickingOffThePath_CancelsTheWholeMove()
    {
        var input = Input(Board("x5/x5/x,111,x3/x5/2,x3,2 1 6"));
        input.Click(C("b3"));
        input.Click(C("c3"));
        Assert.Null(input.Click(C("a5")));
        Assert.Null(input.Slide);
        Assert.Equal(22, input.PlaceTargets.Count);
    }

    [Fact]
    public void G13_OpponentStack_OpensTheInspector_AndAClickClosesIt()
    {
        var input = Input(Board("x5/x5/x5/x5/2,x3,1 1 2"));
        Assert.Null(input.Click(C("a1")));
        Assert.Equal(C("a1"), input.Inspecting);
        Assert.Equal(new PlaceMove(C("c3"), PieceType.Flat), input.Click(C("c3")));
        Assert.Null(input.Inspecting);
    }

    [Fact]
    public void Waiting_ForTheOtherSide_OffersNothing_AndSendsNothing()
    {
        var input = Input(Board("x5/x5/x5/x,1,x3/2,x3,1 1 3"), canAct: false);
        Assert.Empty(input.PlaceTargets);
        Assert.Null(input.Click(C("c3")));
        Assert.Null(input.RightClick(C("c3")));
        Assert.Null(input.Click(C("b2"))); // own stack: looked at, not picked up
        Assert.Null(input.Slide);
        Assert.Equal(C("b2"), input.Inspecting);
    }

    [Fact]
    public void ANewPosition_EndsAHalfBuiltMove()
    {
        var board = Board("x5/x5/x,111,x3/x5/2,x3,2 1 6");
        var input = Input(board);
        input.Click(C("b3"));
        input.Click(C("c3"));
        var next = Board("x5/x5/x,111,x3/x5/2,x3,2 1 6");
        input.Sync(next, c => MoveValidator.GetLegalMovesForSquare(next, c), true);
        Assert.Null(input.Slide);
    }

    [Fact]
    public void Scene_ShowsTheDroppedTiles_AndTheHandAboveTheCurrentSquare()
    {
        var board = Board("x5/x5/x,111,x3/x5/2,x3,2 1 6");
        var input = Input(board);
        input.Click(C("b3"));
        input.Click(C("c3"));
        var scene = BoardScene.Build(board, input, null, []);
        Assert.Empty(scene.At(C("b3")).Pieces);
        Assert.Single(scene.At(C("c3")).Pieces);
        Assert.Equal(1, scene.At(C("c3")).Dropped);
        Assert.Equal(C("c3"), scene.HandAt);
        Assert.Equal(2, scene.Hand.Count);
        Assert.True(scene.At(C("d3")).NextStep);
    }

    [Theory]
    [InlineData("c3", "c3")]
    [InlineData("Sb2", "b2")]
    [InlineData("3b3>111", "e3")]
    [InlineData("2c5-11", "c3")]
    public void Target_IsWhereTheMoveEnds(string ptn, string square) =>
        Assert.Equal(C(square), BoardScene.Target(PtnParser.ParseMove(ptn)));
}

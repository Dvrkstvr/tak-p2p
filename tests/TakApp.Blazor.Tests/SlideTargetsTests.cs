using TakApp.Blazor.Services;
using TakEngine.Abstractions;
using TakEngine.Core.Rules;
using TakEngine.Core.Serialization;

namespace TakApp.Blazor.Tests;

/// <summary>
/// The stepped tower gesture (pipeline/design/gestures.md G-21, G-30, G-31) offers a square only when the engine's legal move list
/// holds a move that starts with the drops so far. These pin that the offer matches what the page offered before F-041.
/// </summary>
public class SlideTargetsTests
{
    private static List<SlideMove> LegalFrom(string tps, string square) =>
        MoveValidator.GetLegalMovesForSquare(TpsSerializer.Deserialize(tps), Coord.FromAlgebraic(square)).OfType<SlideMove>().ToList();

    [Fact]
    public void ThreeStack_OnAnOpenRank_OffersEverySplitThatFits()
    {
        var legal = LegalFrom("x5/x5/x5/x5/111,x4 1 5", "a1");

        Assert.True(SlideTargets.Fits(legal, 3, Direction.East, [1]));
        Assert.True(SlideTargets.Fits(legal, 3, Direction.East, [2]));
        Assert.True(SlideTargets.Fits(legal, 3, Direction.East, [3]));
        Assert.True(SlideTargets.Fits(legal, 3, Direction.East, [1, 1]));
        Assert.True(SlideTargets.Fits(legal, 3, Direction.East, [1, 1, 1]));
        Assert.True(SlideTargets.Fits(legal, 3, Direction.East, [2, 1]));
        Assert.False(SlideTargets.Fits(legal, 3, Direction.East, [4]));
        Assert.False(SlideTargets.Fits(legal, 3, Direction.East, [1, 1, 1, 1]));
        Assert.False(SlideTargets.Fits(legal, 3, Direction.West, [1]));
        Assert.False(SlideTargets.Fits(legal, 3, Direction.South, [1]));
        Assert.True(SlideTargets.Fits(legal, 2, Direction.North, [1, 1]));
    }

    [Fact]
    public void TheBoardEdgeEndsThePath()
    {
        var legal = LegalFrom("x5/x5/x5/x5/x3,111,x 1 5", "d1");

        Assert.True(SlideTargets.Fits(legal, 3, Direction.East, [1]));
        Assert.True(SlideTargets.Fits(legal, 3, Direction.East, [3]));
        Assert.False(SlideTargets.Fits(legal, 3, Direction.East, [1, 1]));
    }

    [Fact]
    public void AWallStopsAFlat()
    {
        var legal = LegalFrom("x5/x5/x5/x5/1,2S,x3 1 5", "a1");

        Assert.False(SlideTargets.Fits(legal, 1, Direction.East, [1]));
        Assert.True(SlideTargets.Fits(legal, 1, Direction.North, [1]));
    }

    [Fact]
    public void ACapstoneFlattensAWallOnlyAlone()
    {
        var legal = LegalFrom("x5/x5/x5/x5/11C,2S,x3 1 5", "a1");

        Assert.True(SlideTargets.Fits(legal, 1, Direction.East, [1]));
        Assert.False(SlideTargets.Fits(legal, 2, Direction.East, [1]));
    }

    [Fact]
    public void ACapstoneMayStepOntoAFlatAndThenFlattenTheWallBehindIt()
    {
        var legal = LegalFrom("x5/x5/x5/x5/11C,2,2S,x2 1 5", "a1");

        Assert.True(SlideTargets.Fits(legal, 2, Direction.East, [1]));
        Assert.True(SlideTargets.Fits(legal, 2, Direction.East, [1, 1]));
        Assert.True(SlideTargets.Fits(legal, 2, Direction.East, [2]));
    }

    [Fact]
    public void ACapstoneOnTheNextSquareBlocks()
    {
        var legal = LegalFrom("x5/x5/x5/x5/11,2C,x3 1 5", "a1");

        Assert.False(SlideTargets.Fits(legal, 1, Direction.East, [1]));
        Assert.False(SlideTargets.Fits(legal, 2, Direction.East, [1]));
        Assert.True(SlideTargets.Fits(legal, 2, Direction.North, [2]));
    }
}

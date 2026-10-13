using TakApp.Blazor.Services;
using TakEngine.Abstractions;
using TakEngine.Core.Rules;
using TakEngine.Core.Serialization;

namespace TakApp.Blazor.Tests;

/// <summary>
/// The slide bar's choice (pipeline/design/gestures.md G-43, D-050): a new origin starts the bar afresh, so Confirm never names a
/// move from another stack.
/// </summary>
public class SlideBarChoiceTests
{
    // White stacks of two at a1 and b3 on an otherwise open board.
    private const string Tps = "x5/x5/x,11,x3/x5/11,x4 1 5";

    private static List<SlideMove> LegalFrom(string square) =>
        MoveValidator.GetLegalMovesForSquare(TpsSerializer.Deserialize(Tps), Coord.FromAlgebraic(square)).OfType<SlideMove>().ToList();

    [Fact]
    public void FirstShow_PicksTheFirstLegalMove()
    {
        var a1 = LegalFrom("a1");
        var choice = new SlideBarChoice();

        choice.Show(Coord.FromAlgebraic("a1"), null, a1);

        Assert.Equal(a1[0], choice.Move);
        Assert.Equal(a1[0].Direction, choice.Direction);
    }

    [Fact]
    public void ANewOrigin_ResetsAChoiceMadeForTheOldOne()
    {
        var a1 = LegalFrom("a1");
        var b3 = LegalFrom("b3");
        var choice = new SlideBarChoice();
        choice.Show(Coord.FromAlgebraic("a1"), null, a1);
        var picked = a1.Last(m => m.Direction == Direction.East);
        choice.PickDirection(Direction.East, a1);
        choice.PickMove(picked);

        choice.Show(Coord.FromAlgebraic("b3"), null, b3);

        Assert.NotNull(choice.Move);
        Assert.Equal(Coord.FromAlgebraic("b3"), choice.Move!.Origin);
        Assert.Equal(b3[0], choice.Move);
    }

    [Fact]
    public void TheSameOrigin_KeepsThePlayersChoice()
    {
        var a1 = LegalFrom("a1");
        var choice = new SlideBarChoice();
        choice.Show(Coord.FromAlgebraic("a1"), null, a1);
        var picked = a1.Last(m => m.Direction == Direction.East);
        choice.PickDirection(Direction.East, a1);
        choice.PickMove(picked);

        choice.Show(Coord.FromAlgebraic("a1"), null, a1);

        Assert.Equal(picked, choice.Move);
    }

    [Fact]
    public void ADirectionFromTheBoardGesture_SelectsItsFirstMove()
    {
        var a1 = LegalFrom("a1");
        var choice = new SlideBarChoice();

        choice.Show(Coord.FromAlgebraic("a1"), Direction.North, a1);

        Assert.Equal(Direction.North, choice.Direction);
        Assert.Equal(a1.First(m => m.Direction == Direction.North), choice.Move);
    }

    [Fact]
    public void NoOrigin_ClearsTheChoice()
    {
        var a1 = LegalFrom("a1");
        var choice = new SlideBarChoice();
        choice.Show(Coord.FromAlgebraic("a1"), null, a1);

        choice.Show(null, null, []);

        Assert.Null(choice.Direction);
        Assert.Null(choice.Move);
    }
}

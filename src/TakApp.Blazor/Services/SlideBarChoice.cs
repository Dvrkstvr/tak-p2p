using TakEngine.Abstractions;

namespace TakApp.Blazor.Services;

/// <summary>
/// The slide bar's selected direction and move (StackSlideBar). The moves offered are the engine's legal list for the origin; this
/// only remembers which one the player picked. A new origin (or none) starts afresh, so a choice made for another stack is never
/// confirmed for this one (pipeline/design/gestures.md G-43, D-050).
/// </summary>
public sealed class SlideBarChoice
{
    private Coord? _shownOrigin;

    public Direction? Direction { get; private set; }
    public SlideMove? Move { get; private set; }

    /// <summary>The bar is shown for <paramref name="origin"/>; <paramref name="activeDirection"/> comes from the board gesture.</summary>
    public void Show(Coord? origin, Direction? activeDirection, IReadOnlyList<SlideMove> available)
    {
        if (origin != _shownOrigin)
        {
            _shownOrigin = origin;
            Clear();
        }

        if (activeDirection.HasValue && available.Any(m => m.Direction == activeDirection.Value))
        {
            PickDirection(activeDirection.Value, available);
        }
        else if (origin.HasValue && Direction == null && available.Count > 0)
        {
            PickDirection(available[0].Direction, available);
        }
    }

    public void PickDirection(Direction direction, IReadOnlyList<SlideMove> available)
    {
        Direction = direction;
        Move = available.FirstOrDefault(m => m.Direction == direction);
    }

    public void PickMove(SlideMove move) => Move = move;

    public void Clear()
    {
        Direction = null;
        Move = null;
    }
}

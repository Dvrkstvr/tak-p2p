using TakEngine.Abstractions;

namespace TakApp.Blazor.Services;

/// <summary>
/// Which squares the stepped tower gesture offers (pipeline/design/gestures.md G-21, G-30, G-31), read off the engine's legal
/// move list for the origin. A partial move is offered only when some legal move starts with it; no rule is decided here.
/// </summary>
public static class SlideTargets
{
    /// <summary>
    /// True when a legal move lifts <paramref name="carry"/> pieces towards <paramref name="direction"/> and starts with
    /// <paramref name="drops"/>: equal on every square before the last, and at least the last count on the last square (more
    /// pieces may still be stacked there).
    /// </summary>
    public static bool Fits(IEnumerable<SlideMove> legal, int carry, Direction direction, IReadOnlyList<int> drops)
    {
        foreach (var move in legal)
        {
            if (move.LiftCount != carry || move.Direction != direction || move.Drops.Count < drops.Count)
                continue;

            bool prefix = true;
            for (int i = 0; i < drops.Count && prefix; i++)
            {
                prefix = i == drops.Count - 1 ? move.Drops[i] >= drops[i] : move.Drops[i] == drops[i];
            }

            if (prefix)
                return true;
        }

        return false;
    }
}

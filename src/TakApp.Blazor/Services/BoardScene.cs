using TakEngine.Abstractions;
using TakEngine.Core.Board;

namespace TakApp.Blazor.Services;

/// <summary>One square as the board views draw it: its pieces (with a half-built tower move already shown) and its marks.</summary>
public sealed record SquareView(
    Coord Coord,
    IReadOnlyList<Piece> Pieces,
    bool Selected,
    bool PlaceTarget,
    bool SlideTarget,
    bool CurrentStep,
    bool NextStep,
    bool LastMove,
    bool Road,
    bool Inspected,
    int Dropped);

/// <summary>
/// What the 2D and the 3D board both draw, built from the board, the gesture state and the last move. A preview only: the tiles
/// a half-built tower move has dropped are drawn where they would land, and the tiles still in hand float above the square the
/// walk is on. The real position is the engine's; nothing here changes it.
/// </summary>
public sealed class BoardScene
{
    private BoardScene(int size, IReadOnlyList<SquareView> squares)
    {
        Size = size;
        Squares = squares;
    }

    public int Size { get; }

    /// <summary>Every square, file by file (a1, a2, …, b1, …).</summary>
    public IReadOnlyList<SquareView> Squares { get; }

    /// <summary>Where the tiles still in hand hover, and which they are (bottom first).</summary>
    public Coord? HandAt { get; private init; }
    public IReadOnlyList<Piece> Hand { get; private init; } = [];

    /// <summary>The origin of a tower move and how many tiles it carries (the ×N badge).</summary>
    public Coord? Origin { get; private init; }
    public int Carry { get; private init; }

    /// <summary>True before the first step, when clicking the origin still changes the carry.</summary>
    public bool CarryAdjustable { get; private init; }

    public SquareView At(Coord c) => Squares[c.X * Size + c.Y];

    public static BoardScene Build(GameBoard board, MoveInput input, Coord? lastMove, IReadOnlyCollection<Coord> road)
    {
        int size = (int)board.Size;
        var slide = input.Slide;
        var dropped = new Dictionary<Coord, int>();
        var ground = new Dictionary<Coord, List<Piece>>();
        List<Piece> hand = [];
        Coord? handAt = null;

        if (slide is not null)
        {
            int total = slide.OriginalPieces.Count;
            int carry = Math.Clamp(slide.Carry, 0, total);
            int leave = total - carry;
            var carried = slide.OriginalPieces.Skip(leave).ToList();
            ground[slide.Origin] = [.. slide.OriginalPieces.Take(leave)];

            int used = 0;
            for (int step = 1; step <= slide.Drops.Count && slide.Direction is not null; step++)
            {
                var at = slide.StepAt(step);
                int n = Math.Min(slide.Drops[step - 1], carried.Count - used);
                var landing = board.GetStack(at).Pieces.ToList();
                var these = carried.GetRange(used, Math.Max(0, n));
                used += Math.Max(0, n);
                // A capstone landing alone on a wall flattens it.
                if (landing.Count > 0 && landing[^1].Type == PieceType.Standing && these.Count > 0 && these[^1].Type == PieceType.Capstone)
                    landing[^1] = landing[^1] with { Type = PieceType.Flat };
                ground[at] = [.. landing, .. these];
                dropped[at] = slide.Drops[step - 1];
            }

            hand = carried.Skip(used).ToList();
            if (hand.Count > 0)
                handAt = input.CurrentStep ?? slide.Origin;
        }

        var cur = input.CurrentStep;
        var next = input.NextStep;
        var squares = new List<SquareView>(size * size);
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                var c = new Coord(x, y);
                IReadOnlyList<Piece> pieces = ground.TryGetValue(c, out var preview) ? preview : board.GetStack(c).Pieces;
                squares.Add(new SquareView(
                    c,
                    pieces,
                    Selected: slide?.Origin == c || input.Inspecting == c,
                    PlaceTarget: input.PlaceTargets.Contains(c),
                    SlideTarget: input.SlideTargets.Contains(c),
                    CurrentStep: cur == c && input.SlideTargets.Contains(c),
                    NextStep: next == c,
                    LastMove: lastMove == c,
                    Road: road.Contains(c),
                    Inspected: input.Inspecting == c,
                    Dropped: dropped.GetValueOrDefault(c)));
            }
        }

        return new BoardScene(size, squares)
        {
            HandAt = handAt,
            Hand = hand,
            Origin = slide?.Origin,
            Carry = slide?.Carry ?? 0,
            CarryAdjustable = slide is { Direction: null, MaxLift: > 1 },
        };
    }

    /// <summary>The square a move ends on: the placed square, or the last square a tower walk drops on.</summary>
    public static Coord Target(TakMove move) => move switch
    {
        PlaceMove p => p.Target,
        SlideMove s => new Coord(
            s.Origin.X + MoveInput.Offset(s.Direction).Dx * s.Drops.Count,
            s.Origin.Y + MoveInput.Offset(s.Direction).Dy * s.Drops.Count),
        _ => default,
    };
}

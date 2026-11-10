using TakEngine.Abstractions;
using TakEngine.Core.Board;

namespace TakApp.Blazor.Services;

/// <summary>
/// The board gestures of pipeline/design/gestures.md as one state machine, shared by every game screen (local, vs AI, online)
/// and by both board views (2D and 3D). It decides no rule: what it offers is read off the engine's legal move list, and what
/// it sends goes to the engine, which may still refuse it. A click returns the move to send, or null.
/// </summary>
public sealed class MoveInput
{
    private static readonly (Direction Dir, int Dx, int Dy)[] Neighbours =
    [
        (Direction.North, 0, 1), (Direction.South, 0, -1), (Direction.East, 1, 0), (Direction.West, -1, 0),
    ];

    private GameBoard? _board;
    private Func<Coord, IReadOnlyList<TakMove>> _legal = _ => [];
    private bool _canAct;

    /// <summary>A tower move being built: origin picked, then a direction and the tiles dropped so far.</summary>
    public sealed class SlideDraft
    {
        public required Coord Origin { get; init; }
        public required int MaxLift { get; init; }
        public required IReadOnlyList<Piece> OriginalPieces { get; init; }

        /// <summary>The engine's legal moves from the origin, read when the move started.</summary>
        public required IReadOnlyList<SlideMove> Legal { get; init; }

        public int Carry { get; set; }
        public Direction? Direction { get; set; }
        public List<int> Drops { get; } = [];
        public int InHand => Carry - Drops.Sum();

        public Coord StepAt(int step)
        {
            var (dx, dy) = Offset(Direction);
            return new Coord(Origin.X + dx * step, Origin.Y + dy * step);
        }
    }

    public PieceType Piece { get; private set; } = PieceType.Flat;
    public SlideDraft? Slide { get; private set; }
    public Coord? Inspecting { get; private set; }

    /// <summary>Empty squares where the engine allows the selected piece now (G-01, G-04).</summary>
    public HashSet<Coord> PlaceTargets { get; } = [];

    /// <summary>Squares the tower may step to or stack on next (G-12, G-30, G-31).</summary>
    public HashSet<Coord> SlideTargets { get; } = [];

    /// <summary>The square the last tile was dropped on, while stepping.</summary>
    public Coord? CurrentStep => Slide is { Direction: not null, Drops.Count: > 0 } s ? s.StepAt(s.Drops.Count) : null;

    /// <summary>The next square along the direction, when a legal move continues there.</summary>
    public Coord? NextStep =>
        Slide is { Direction: { } dir, InHand: > 0 } s && SlideTargetsFit(s, dir, [.. s.Drops, 1]) ? s.StepAt(s.Drops.Count + 1) : null;

    /// <summary>
    /// Points the input at the current position. A new position (another board object) ends a half-built move: it was built
    /// against a board that is gone.
    /// </summary>
    public void Sync(GameBoard? board, Func<Coord, IReadOnlyList<TakMove>> legal, bool canAct)
    {
        if (!ReferenceEquals(board, _board))
        {
            Slide = null;
            if (Inspecting is { } c && (board is null || board.GetStack(c).IsEmpty))
                Inspecting = null;
        }
        _board = board;
        _legal = legal;
        _canAct = canAct;
        if (!canAct)
            Slide = null;
        Refresh();
    }

    /// <summary>G-02..G-05: a new piece ends a started move and closes the inspector.</summary>
    public void SelectPiece(PieceType piece)
    {
        Piece = piece;
        Slide = null;
        Inspecting = null;
        Refresh();
    }

    /// <summary>G-06, G-18, G-41: back to idle.</summary>
    public void Cancel()
    {
        Slide = null;
        Inspecting = null;
        Refresh();
    }

    public void DismissInspect() => Inspecting = null;

    /// <summary>A left click (or tap) on a square. Returns the move to send, if the gesture completed one.</summary>
    public TakMove? Click(Coord coord)
    {
        Inspecting = null;
        if (_board is not { } board)
            return null;

        if (!_canAct)
        {
            // Waiting for the other side: stacks can still be looked at.
            if (!board.GetStack(coord).IsEmpty)
                Inspecting = coord;
            return null;
        }

        if (Slide is { } slide)
            return ClickWhileSliding(board, slide, coord);

        var stack = board.GetStack(coord);
        if (stack.IsEmpty)
            return new PlaceMove(coord, Piece); // G-10 / G-10b: the engine decides; a refusal is shown by the caller.

        if (board.Phase == GamePhase.Playing && stack.Owner == board.ActivePlayer)
            StartSlide(board, coord); // G-12
        else
            Inspecting = coord; // G-13

        Refresh();
        return null;
    }

    /// <summary>A right click (or long press). G-11 quick wall, G-14 inspect, G-16/G-25 cancel.</summary>
    public TakMove? RightClick(Coord coord)
    {
        if (_board is not { } board)
            return null;

        if (Slide is not null)
        {
            Slide = null;
            Refresh();
            return null;
        }

        if (Inspecting is not null)
        {
            Inspecting = null;
            return null;
        }

        if (!board.GetStack(coord).IsEmpty)
        {
            Inspecting = coord;
            return null;
        }

        return _canAct ? new PlaceMove(coord, PieceType.Standing) : null;
    }

    private TakMove? ClickWhileSliding(GameBoard board, SlideDraft slide, Coord coord)
    {
        if (slide.Direction is not { } dir)
        {
            // G-20: the origin again leaves one more tile behind; below one wraps to the maximum.
            if (coord == slide.Origin)
            {
                slide.Carry = slide.Carry <= 1 ? slide.MaxLift : slide.Carry - 1;
                Refresh();
                return null;
            }

            // G-21: a neighbour the carried tiles may enter starts the walk with one tile there.
            if (DirectionTo(slide.Origin, coord) is { } towards && SlideTargetsFit(slide, towards, [1]))
            {
                slide.Direction = towards;
                slide.Drops.Add(1);
                return FinishIfEmpty(slide);
            }

            // G-22: another own stack restarts there.
            var clicked = board.GetStack(coord);
            if (!clicked.IsEmpty && board.Phase == GamePhase.Playing && clicked.Owner == board.ActivePlayer)
            {
                StartSlide(board, coord);
                Refresh();
                return null;
            }

            // G-23 / G-24: anything else cancels.
            Slide = null;
            Refresh();
            return null;
        }

        // G-30: the current square takes one more tile.
        if (coord == CurrentStep && CanStackOnCurrent(slide, dir))
        {
            slide.Drops[^1]++;
            return FinishIfEmpty(slide);
        }

        // G-31: the next square takes one tile.
        if (NextStep is { } next && coord == next)
        {
            slide.Drops.Add(1);
            return FinishIfEmpty(slide);
        }

        // G-32: anything else cancels the whole move.
        Slide = null;
        Refresh();
        return null;
    }

    private TakMove? FinishIfEmpty(SlideDraft slide)
    {
        if (slide.InHand > 0 || slide.Direction is not { } dir)
        {
            Refresh();
            return null;
        }
        var move = new SlideMove(slide.Origin, dir, slide.Carry, [.. slide.Drops]);
        Slide = null;
        Refresh();
        return move;
    }

    private void StartSlide(GameBoard board, Coord coord)
    {
        var stack = board.GetStack(coord);
        int max = Math.Min(stack.Height, board.CarryLimit);
        Slide = new SlideDraft
        {
            Origin = coord,
            MaxLift = max,
            Carry = max,
            OriginalPieces = [.. stack.Pieces],
            Legal = [.. _legal(coord).OfType<SlideMove>()],
        };
        Inspecting = null;
    }

    private static bool CanStackOnCurrent(SlideDraft s, Direction dir) =>
        s.Drops.Count > 0 && s.InHand > 0 && SlideTargetsFit(s, dir, [.. s.Drops.Take(s.Drops.Count - 1), s.Drops[^1] + 1]);

    private static bool SlideTargetsFit(SlideDraft s, Direction dir, IReadOnlyList<int> drops) =>
        Services.SlideTargets.Fits(s.Legal, s.Carry, dir, drops);

    private void Refresh()
    {
        PlaceTargets.Clear();
        SlideTargets.Clear();
        if (_board is not { } board || board.Phase == GamePhase.Completed || !_canAct)
            return;

        if (Slide is { } s)
        {
            if (s.Direction is not { } dir)
            {
                foreach (var (d, dx, dy) in Neighbours)
                    if (SlideTargetsFit(s, d, [1]))
                        SlideTargets.Add(new Coord(s.Origin.X + dx, s.Origin.Y + dy));
            }
            else
            {
                if (CurrentStep is { } cur && CanStackOnCurrent(s, dir))
                    SlideTargets.Add(cur);
                if (NextStep is { } next)
                    SlideTargets.Add(next);
            }
            return;
        }

        int size = (int)board.Size;
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                var c = new Coord(x, y);
                if (board.GetStack(c).IsEmpty && _legal(c).Any(m => m is PlaceMove p && p.PieceType == Piece))
                    PlaceTargets.Add(c);
            }
        }
    }

    private static Direction? DirectionTo(Coord from, Coord to) => (to.X - from.X, to.Y - from.Y) switch
    {
        (0, 1) => Direction.North,
        (0, -1) => Direction.South,
        (1, 0) => Direction.East,
        (-1, 0) => Direction.West,
        _ => null,
    };

    internal static (int Dx, int Dy) Offset(Direction? direction) => direction switch
    {
        Direction.North => (0, 1),
        Direction.South => (0, -1),
        Direction.East => (1, 0),
        Direction.West => (-1, 0),
        _ => (0, 0),
    };
}

using System;
using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Core.Rules;

namespace TakEngine.Core.Board;

public sealed class GameBoard
{
    private readonly PieceStack[,] _grid;
    private readonly int _sizeInt;

    public BoardSize Size { get; }
    public int CarryLimit => _sizeInt;
    public int TurnNumber { get; private set; } = 1;
    public PlayerColor ActivePlayer { get; private set; } = PlayerColor.White;
    public GamePhase Phase { get; private set; } = GamePhase.FirstTurnPlacement;
    public GameResult? Result { get; private set; }

    public PlayerReserves WhiteReserves { get; private set; }
    public PlayerReserves BlackReserves { get; private set; }

    public GameBoard(BoardSize size)
    {
        Size = size;
        _sizeInt = (int)size;
        _grid = new PieceStack[_sizeInt, _sizeInt];

        for (int x = 0; x < _sizeInt; x++)
        {
            for (int y = 0; y < _sizeInt; y++)
            {
                _grid[x, y] = new PieceStack();
            }
        }

        (int stones, int capstones) = size switch
        {
            BoardSize.Four => (15, 0),
            BoardSize.Five => (21, 1),
            BoardSize.Six => (30, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(size), $"Unsupported board size: {size}")
        };

        WhiteReserves = new PlayerReserves(stones, capstones);
        BlackReserves = new PlayerReserves(stones, capstones);
    }

    public PieceStack GetStack(Coord coord)
    {
        ValidateBounds(coord);
        return _grid[coord.X, coord.Y];
    }

    public bool IsInBounds(Coord coord) =>
        coord.X >= 0 && coord.X < _sizeInt && coord.Y >= 0 && coord.Y < _sizeInt;

    public CommandResult Place(Coord target, PieceType pieceType)
    {
        if (Phase == GamePhase.Completed)
            return CommandResult.Fail("Game is already completed.");

        if (!IsInBounds(target))
            return CommandResult.Fail($"Coordinate {target} is out of bounds.");

        var stack = _grid[target.X, target.Y];
        if (!stack.IsEmpty)
            return CommandResult.Fail($"Square {target} is already occupied.");

        // First turn swap rule
        if (Phase == GamePhase.FirstTurnPlacement)
        {
            if (pieceType != PieceType.Flat)
                return CommandResult.Fail("First turn placement must be a Flat stone.");

            PlayerColor stoneColor = ActivePlayer == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;

            if (!DeductReserve(stoneColor, PieceType.Flat))
                return CommandResult.Fail($"Insufficient reserve stones for {stoneColor}.");

            stack.Push(new Piece(stoneColor, PieceType.Flat));

            AdvanceFirstTurns();
            return CommandResult.Success();
        }

        // Regular placement
        if (!DeductReserve(ActivePlayer, pieceType))
            return CommandResult.Fail($"Insufficient reserves for {ActivePlayer} to place {pieceType}.");

        stack.Push(new Piece(ActivePlayer, pieceType));

        CheckGameEndAfterMove();
        if (Phase != GamePhase.Completed)
        {
            AdvanceTurn();
        }

        return CommandResult.Success();
    }

    public CommandResult Move(Coord origin, Direction direction, IReadOnlyList<int> drops)
    {
        if (Phase == GamePhase.Completed)
            return CommandResult.Fail("Game is already completed.");

        if (Phase == GamePhase.FirstTurnPlacement)
            return CommandResult.Fail("Cannot move stacks during the first turn placement phase.");

        if (!IsInBounds(origin))
            return CommandResult.Fail($"Origin {origin} is out of bounds.");

        var originStack = _grid[origin.X, origin.Y];
        if (originStack.IsEmpty)
            return CommandResult.Fail($"No pieces to move at {origin}.");

        if (originStack.Owner != ActivePlayer)
            return CommandResult.Fail($"Stack at {origin} is not owned by active player {ActivePlayer}.");

        if (drops == null || drops.Count == 0)
            return CommandResult.Fail("Drops list cannot be empty.");

        int totalLift = 0;
        foreach (int d in drops)
        {
            if (d < 1)
                return CommandResult.Fail("Must drop at least 1 piece per square.");
            totalLift += d;
        }

        if (totalLift > originStack.Height)
            return CommandResult.Fail($"Cannot lift {totalLift} pieces from stack of height {originStack.Height}.");

        if (totalLift > CarryLimit)
            return CommandResult.Fail($"Cannot lift {totalLift} pieces; carry limit for {Size} board is {CarryLimit}.");

        (int dx, int dy) = direction switch
        {
            Direction.North => (0, 1),
            Direction.South => (0, -1),
            Direction.East => (1, 0),
            Direction.West => (-1, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

        // Validate destination path
        int curX = origin.X;
        int curY = origin.Y;
        int dropCount = drops.Count;

        for (int step = 0; step < dropCount; step++)
        {
            curX += dx;
            curY += dy;
            var targetCoord = new Coord(curX, curY);

            if (!IsInBounds(targetCoord))
                return CommandResult.Fail($"Move extends beyond board boundaries at step {step + 1}.");

            var targetStack = _grid[curX, curY];
            bool isLastStep = (step == dropCount - 1);

            if (!targetStack.IsEmpty)
            {
                var topPiece = targetStack.TopPiece!.Value;

                if (topPiece.Type == PieceType.Capstone)
                    return CommandResult.Fail($"Cannot move onto or through a Capstone at {targetCoord}.");

                if (topPiece.Type == PieceType.Standing)
                {
                    if (!isLastStep)
                        return CommandResult.Fail($"Cannot move through a Standing wall at {targetCoord}.");

                    // Can only flatten on last step if dropping exactly 1 piece and that piece is a Capstone
                    if (drops[step] != 1)
                        return CommandResult.Fail($"Cannot drop more than 1 piece when flattening a Standing wall.");

                    // Check if the top piece of the lifted stack (which is the piece dropped last) is a Capstone
                    // Lifted pieces are indexed 0 (bottom) to totalLift - 1 (top)
                    // The last piece dropped is the top of the lifted stack!
                    var sourcePieces = originStack.Pieces;
                    var pieceToDrop = sourcePieces[^1];

                    if (pieceToDrop.Type != PieceType.Capstone)
                        return CommandResult.Fail($"Only a Capstone can flatten a Standing wall.");
                }
            }
        }

        // Execute move
        var liftedPieces = originStack.Lift(totalLift);

        curX = origin.X;
        curY = origin.Y;
        int pieceIndex = 0; // Starts from bottom of lifted sub-stack

        for (int step = 0; step < dropCount; step++)
        {
            curX += dx;
            curY += dy;
            var targetStack = _grid[curX, curY];
            int dropAmount = drops[step];

            bool isLastStep = (step == dropCount - 1);
            if (isLastStep && !targetStack.IsEmpty && targetStack.TopPiece!.Value.Type == PieceType.Standing)
            {
                targetStack.FlattenTop();
            }

            for (int i = 0; i < dropAmount; i++)
            {
                targetStack.Push(liftedPieces[pieceIndex++]);
            }
        }

        CheckGameEndAfterMove();
        if (Phase != GamePhase.Completed)
        {
            AdvanceTurn();
        }

        return CommandResult.Success();
    }

    public CommandResult Resign(PlayerColor player)
    {
        if (Phase == GamePhase.Completed)
            return CommandResult.Fail("Game is already completed.");

        PlayerColor winner = player == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
        Result = new GameResult(false, winner, GameEndReason.Resignation);
        Phase = GamePhase.Completed;
        return CommandResult.Success();
    }

    private void AdvanceFirstTurns()
    {
        if (ActivePlayer == PlayerColor.White)
        {
            ActivePlayer = PlayerColor.Black;
        }
        else
        {
            ActivePlayer = PlayerColor.White;
            Phase = GamePhase.Playing;
            TurnNumber = 2;
        }
    }

    private void AdvanceTurn()
    {
        if (ActivePlayer == PlayerColor.White)
        {
            ActivePlayer = PlayerColor.Black;
        }
        else
        {
            ActivePlayer = PlayerColor.White;
            TurnNumber++;
        }
    }

    private void CheckGameEndAfterMove()
    {
        bool whiteRoad = RoadFinder.HasRoad(_grid, _sizeInt, PlayerColor.White);
        bool blackRoad = RoadFinder.HasRoad(_grid, _sizeInt, PlayerColor.Black);

        if (whiteRoad || blackRoad)
        {
            // Active player who made the move wins if both have roads
            PlayerColor winner;
            if (whiteRoad && blackRoad)
            {
                winner = ActivePlayer;
            }
            else
            {
                winner = whiteRoad ? PlayerColor.White : PlayerColor.Black;
            }

            Result = new GameResult(false, winner, GameEndReason.Road);
            Phase = GamePhase.Completed;
            return;
        }

        // Check if either player is out of pieces or board is full
        bool boardFull = IsBoardFull();
        bool activeOutOfPieces = (ActivePlayer == PlayerColor.White)
            ? (WhiteReserves.Stones == 0 && WhiteReserves.Capstones == 0)
            : (BlackReserves.Stones == 0 && BlackReserves.Capstones == 0);

        if (boardFull || activeOutOfPieces)
        {
            Result = DetermineFlatCountResult();
            Phase = GamePhase.Completed;
        }
    }

    public bool IsBoardFull()
    {
        for (int x = 0; x < _sizeInt; x++)
        {
            for (int y = 0; y < _sizeInt; y++)
            {
                if (_grid[x, y].IsEmpty)
                    return false;
            }
        }
        return true;
    }

    public GameResult DetermineFlatCountResult()
    {
        int whiteFlats = 0;
        int blackFlats = 0;

        for (int x = 0; x < _sizeInt; x++)
        {
            for (int y = 0; y < _sizeInt; y++)
            {
                var top = _grid[x, y].TopPiece;
                if (top.HasValue && top.Value.Type == PieceType.Flat)
                {
                    if (top.Value.Color == PlayerColor.White)
                        whiteFlats++;
                    else
                        blackFlats++;
                }
            }
        }

        if (whiteFlats > blackFlats)
            return new GameResult(false, PlayerColor.White, GameEndReason.FlatCount);
        if (blackFlats > whiteFlats)
            return new GameResult(false, PlayerColor.Black, GameEndReason.FlatCount);

        return new GameResult(true, null, GameEndReason.FlatCount);
    }

    private bool DeductReserve(PlayerColor player, PieceType type)
    {
        if (player == PlayerColor.White)
        {
            if (type == PieceType.Capstone)
            {
                if (WhiteReserves.Capstones <= 0) return false;
                WhiteReserves = WhiteReserves with { Capstones = WhiteReserves.Capstones - 1 };
            }
            else
            {
                if (WhiteReserves.Stones <= 0) return false;
                WhiteReserves = WhiteReserves with { Stones = WhiteReserves.Stones - 1 };
            }
        }
        else
        {
            if (type == PieceType.Capstone)
            {
                if (BlackReserves.Capstones <= 0) return false;
                BlackReserves = BlackReserves with { Capstones = BlackReserves.Capstones - 1 };
            }
            else
            {
                if (BlackReserves.Stones <= 0) return false;
                BlackReserves = BlackReserves with { Stones = BlackReserves.Stones - 1 };
            }
        }

        return true;
    }

    public TakBoardSnapshot ToSnapshot()
    {
        var stacksDict = new Dictionary<Coord, StackSnapshot>();
        for (int x = 0; x < _sizeInt; x++)
        {
            for (int y = 0; y < _sizeInt; y++)
            {
                var stack = _grid[x, y];
                if (!stack.IsEmpty)
                {
                    var coord = new Coord(x, y);
                    stacksDict[coord] = stack.ToSnapshot(coord);
                }
            }
        }

        return new TakBoardSnapshot(
            Size,
            TurnNumber,
            ActivePlayer,
            stacksDict,
            WhiteReserves,
            BlackReserves);
    }

    private void ValidateBounds(Coord coord)
    {
        if (!IsInBounds(coord))
            throw new ArgumentOutOfRangeException(nameof(coord), $"Coordinate {coord} is out of bounds for {Size} board.");
    }
}

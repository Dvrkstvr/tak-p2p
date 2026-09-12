using System;
using System.Collections.Generic;

namespace TakEngine.Abstractions;

public readonly record struct GameId(Guid Value)
{
    public static GameId New() => new(Guid.NewGuid());
    public static GameId Parse(string s) => new(Guid.Parse(s));
    public override string ToString() => Value.ToString();
}

public readonly record struct Coord(int X, int Y)
{
    public static Coord FromAlgebraic(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length < 2)
            throw new ArgumentException("Invalid coordinate format", nameof(s));

        int x = char.ToLowerInvariant(s[0]) - 'a';
        int y = int.Parse(s[1..]) - 1;
        return new Coord(x, y);
    }

    public string ToAlgebraic() => $"{(char)('a' + X)}{Y + 1}";

    public override string ToString() => ToAlgebraic();
}

public readonly record struct Piece(PlayerColor Color, PieceType Type);

public sealed record StackSnapshot(
    Coord Position,
    IReadOnlyList<Piece> Pieces)
{
    public Piece? TopPiece => Pieces.Count > 0 ? Pieces[^1] : null;
    public int Height => Pieces.Count;
    public bool IsEmpty => Pieces.Count == 0;
}

public sealed record PlayerReserves(
    int Stones,
    int Capstones);

public sealed record TakBoardSnapshot(
    BoardSize Size,
    int TurnNumber,
    PlayerColor ActivePlayer,
    IReadOnlyDictionary<Coord, StackSnapshot> Stacks,
    PlayerReserves WhiteReserves,
    PlayerReserves BlackReserves);

public abstract record TakMove
{
    public abstract string ToPtn();
}

public sealed record PlaceMove(
    Coord Target,
    PieceType PieceType) : TakMove
{
    public override string ToPtn()
    {
        string prefix = PieceType switch
        {
            PieceType.Flat => "",
            PieceType.Standing => "S",
            PieceType.Capstone => "C",
            _ => ""
        };
        return $"{prefix}{Target.ToAlgebraic()}";
    }
}

public sealed record SlideMove(
    Coord Origin,
    Direction Direction,
    int LiftCount,
    IReadOnlyList<int> Drops) : TakMove
{
    public override string ToPtn()
    {
        char dirChar = Direction switch
        {
            Direction.North => '+',
            Direction.South => '-',
            Direction.East => '>',
            Direction.West => '<',
            _ => '+'
        };

        string dropStr = Drops.Count > 1 ? string.Concat(Drops) : "";
        string liftStr = LiftCount > 1 ? LiftCount.ToString() : "";
        return $"{liftStr}{Origin.ToAlgebraic()}{dirChar}{dropStr}";
    }
}

public sealed record CommandResult(bool IsSuccess, string? ErrorMessage = null)
{
    public static CommandResult Success() => new(true);
    public static CommandResult Fail(string error) => new(false, error);
}

public sealed record GameResult(
    bool IsDraw,
    PlayerColor? Winner,
    GameEndReason Reason);

public class ProtocolViolationException : Exception
{
    public ProtocolViolationException(string message) : base(message) { }
    public ProtocolViolationException(string message, Exception innerException) : base(message, innerException) { }
}

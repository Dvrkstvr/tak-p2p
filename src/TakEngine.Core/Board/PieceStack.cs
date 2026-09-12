using System;
using System.Collections.Generic;
using TakEngine.Abstractions;

namespace TakEngine.Core.Board;

public sealed class PieceStack
{
    private readonly List<Piece> _pieces = new();

    public int Height => _pieces.Count;
    public bool IsEmpty => _pieces.Count == 0;
    public Piece? TopPiece => _pieces.Count > 0 ? _pieces[^1] : null;
    public PlayerColor? Owner => TopPiece?.Color;

    public IReadOnlyList<Piece> Pieces => _pieces.AsReadOnly();

    public void Push(Piece piece)
    {
        _pieces.Add(piece);
    }

    public Piece Pop()
    {
        if (_pieces.Count == 0)
            throw new InvalidOperationException("Cannot pop from an empty stack.");

        int lastIndex = _pieces.Count - 1;
        Piece piece = _pieces[lastIndex];
        _pieces.RemoveAt(lastIndex);
        return piece;
    }

    public List<Piece> Lift(int count)
    {
        if (count <= 0 || count > _pieces.Count)
            throw new ArgumentOutOfRangeException(nameof(count), $"Lift count {count} is invalid for stack height {_pieces.Count}.");

        int startIndex = _pieces.Count - count;
        var lifted = _pieces.GetRange(startIndex, count);
        _pieces.RemoveRange(startIndex, count);
        return lifted;
    }

    public void FlattenTop()
    {
        if (_pieces.Count == 0)
            throw new InvalidOperationException("Cannot flatten an empty stack.");

        Piece top = _pieces[^1];
        if (top.Type != PieceType.Standing)
            throw new InvalidOperationException($"Cannot flatten piece of type {top.Type}.");

        _pieces[^1] = new Piece(top.Color, PieceType.Flat);
    }

    public StackSnapshot ToSnapshot(Coord position)
    {
        return new StackSnapshot(position, _pieces.ToArray());
    }

    public PieceStack Clone()
    {
        var clone = new PieceStack();
        for (int i = 0; i < _pieces.Count; i++)
        {
            clone.Push(_pieces[i]);
        }
        return clone;
    }
}

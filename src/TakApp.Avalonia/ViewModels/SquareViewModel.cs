using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TakEngine.Abstractions;

namespace TakApp.Avalonia.ViewModels;

public partial class SquareViewModel : ViewModelBase
{
    private readonly Action<SquareViewModel> _onSelected;

    public Coord Coord { get; }
    public string AlgebraicCoord => Coord.ToAlgebraic();

    [ObservableProperty]
    public partial int StackHeight { get; set; }

    [ObservableProperty]
    public partial PieceType? TopPieceType { get; set; }

    [ObservableProperty]
    public partial PlayerColor? TopPieceColor { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsLegalTarget { get; set; }

    [ObservableProperty]
    public partial bool IsLastMoveOrigin { get; set; }

    [ObservableProperty]
    public partial bool IsLastMoveTarget { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<Piece> StackPieces { get; set; } = Array.Empty<Piece>();

    public SquareViewModel(Coord coord, Action<SquareViewModel> onSelected)
    {
        Coord = coord;
        _onSelected = onSelected;
    }

    [RelayCommand]
    private void Select()
    {
        _onSelected(this);
    }

    public void Update(StackSnapshot? snapshot)
    {
        if (snapshot == null || snapshot.IsEmpty)
        {
            StackHeight = 0;
            TopPieceType = null;
            TopPieceColor = null;
            IsEmpty = true;
            StackPieces = Array.Empty<Piece>();
        }
        else
        {
            StackHeight = snapshot.Height;
            TopPieceType = snapshot.TopPiece?.Type;
            TopPieceColor = snapshot.TopPiece?.Color;
            IsEmpty = false;
            StackPieces = snapshot.Pieces;
        }
    }
}

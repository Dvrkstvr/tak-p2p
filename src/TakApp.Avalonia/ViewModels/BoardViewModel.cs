using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TakEngine.Abstractions;

namespace TakApp.Avalonia.ViewModels;

public partial class BoardViewModel : ViewModelBase
{
    private readonly Func<Coord, IReadOnlyList<TakMove>> _legalMovesProvider;
    private readonly Action<Coord, PieceType> _onPlacement;
    private readonly Action<Coord, Direction, IReadOnlyList<int>> _onSlide;

    public BoardSize Size { get; }
    public int Dimension => (int)Size;

    public ObservableCollection<SquareViewModel> Squares { get; } = new();

    [ObservableProperty]
    public partial SquareViewModel? SelectedSquare { get; set; }

    [ObservableProperty]
    public partial PieceType SelectedPlacementType { get; set; } = PieceType.Flat;

    [ObservableProperty]
    public partial bool IsSlideMode { get; set; }

    [ObservableProperty]
    public partial int SelectedLiftCount { get; set; } = 1;

    [ObservableProperty]
    public partial int MaxLiftCount { get; set; } = 1;

    [ObservableProperty]
    public partial Direction SelectedDirection { get; set; } = Direction.North;

    [ObservableProperty]
    public partial string SlideDropsText { get; set; } = "1";

    public BoardViewModel(
        BoardSize size,
        Func<Coord, IReadOnlyList<TakMove>> legalMovesProvider,
        Action<Coord, PieceType> onPlacement,
        Action<Coord, Direction, IReadOnlyList<int>> onSlide)
    {
        Size = size;
        _legalMovesProvider = legalMovesProvider;
        _onPlacement = onPlacement;
        _onSlide = onSlide;

        int dim = (int)size;
        // Build squares in display order: top rank (y = dim-1) down to bottom rank (y = 0)
        for (int y = dim - 1; y >= 0; y--)
        {
            for (int x = 0; x < dim; x++)
            {
                var coord = new Coord(x, y);
                Squares.Add(new SquareViewModel(coord, OnSquareClicked));
            }
        }
    }

    public SquareViewModel? GetSquare(Coord coord)
    {
        foreach (var sq in Squares)
        {
            if (sq.Coord == coord)
                return sq;
        }
        return null;
    }

    public void UpdateBoard(TakBoardSnapshot snapshot)
    {
        foreach (var sq in Squares)
        {
            snapshot.Stacks.TryGetValue(sq.Coord, out var stack);
            sq.Update(stack);
        }
        ClearHighlights();
    }

    public void OnSquareClicked(SquareViewModel clickedSquare)
    {
        // 1. If currently in slide mode and clicked a legal target square
        if (IsSlideMode && SelectedSquare != null && clickedSquare.IsLegalTarget)
        {
            var origin = SelectedSquare.Coord;
            var target = clickedSquare.Coord;

            var (dx, dy) = (target.X - origin.X, target.Y - origin.Y);
            Direction? dir = null;
            if (dx == 0 && dy > 0) dir = Direction.North;
            else if (dx == 0 && dy < 0) dir = Direction.South;
            else if (dx > 0 && dy == 0) dir = Direction.East;
            else if (dx < 0 && dy == 0) dir = Direction.West;

            if (dir.HasValue)
            {
                int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                var drops = GenerateDefaultDrops(SelectedLiftCount, distance);
                _onSlide(origin, dir.Value, drops);
                CancelSelection();
                return;
            }
        }

        // 2. If clicking on an empty square, execute placement
        if (clickedSquare.IsEmpty)
        {
            _onPlacement(clickedSquare.Coord, SelectedPlacementType);
            CancelSelection();
            return;
        }

        // 3. If clicking on an occupied square, select it for slide
        ClearHighlights();
        SelectedSquare = clickedSquare;
        clickedSquare.IsSelected = true;

        var moves = _legalMovesProvider(clickedSquare.Coord);
        if (moves.Count > 0)
        {
            IsSlideMode = true;
            MaxLiftCount = Math.Min(clickedSquare.StackHeight, Dimension);
            SelectedLiftCount = MaxLiftCount;

            foreach (var m in moves)
            {
                if (m is SlideMove sm)
                {
                    // Calculate step destinations
                    int curX = sm.Origin.X;
                    int curY = sm.Origin.Y;
                    var (stepX, stepY) = sm.Direction switch
                    {
                        Direction.North => (0, 1),
                        Direction.South => (0, -1),
                        Direction.East => (1, 0),
                        Direction.West => (-1, 0),
                        _ => (0, 0)
                    };

                    for (int i = 0; i < sm.Drops.Count; i++)
                    {
                        curX += stepX;
                        curY += stepY;
                        var targetSquare = GetSquare(new Coord(curX, curY));
                        if (targetSquare != null)
                        {
                            targetSquare.IsLegalTarget = true;
                        }
                    }
                }
            }
        }
        else
        {
            IsSlideMode = false;
        }
    }

    [RelayCommand]
    public void SelectPieceType(string typeName)
    {
        if (Enum.TryParse<PieceType>(typeName, true, out var type))
        {
            SelectedPlacementType = type;
        }
    }

    [RelayCommand]
    public void SetDirection(string dirName)
    {
        if (Enum.TryParse<Direction>(dirName, true, out var dir))
        {
            SelectedDirection = dir;
        }
    }

    [RelayCommand]
    public void ExecuteSlideCommand()
    {
        if (SelectedSquare == null || !IsSlideMode)
            return;

        var drops = ParseDrops(SlideDropsText, SelectedLiftCount);
        _onSlide(SelectedSquare.Coord, SelectedDirection, drops);
        CancelSelection();
    }

    [RelayCommand]
    public void CancelSelection()
    {
        ClearHighlights();
        SelectedSquare = null;
        IsSlideMode = false;
    }

    public void SetLastMove(Coord? origin, Coord? target)
    {
        foreach (var sq in Squares)
        {
            sq.IsLastMoveOrigin = origin.HasValue && sq.Coord == origin.Value;
            sq.IsLastMoveTarget = target.HasValue && sq.Coord == target.Value;
        }
    }

    private void ClearHighlights()
    {
        foreach (var sq in Squares)
        {
            sq.IsSelected = false;
            sq.IsLegalTarget = false;
        }
    }

    private static List<int> GenerateDefaultDrops(int lift, int distance)
    {
        var drops = new List<int>();
        if (distance <= 1)
        {
            drops.Add(lift);
            return drops;
        }

        int remaining = lift;
        for (int i = 0; i < distance - 1; i++)
        {
            drops.Add(1);
            remaining -= 1;
        }
        drops.Add(Math.Max(1, remaining));
        return drops;
    }

    private static List<int> ParseDrops(string text, int lift)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(text))
        {
            result.Add(lift);
            return result;
        }

        foreach (char c in text)
        {
            if (char.IsDigit(c) && c > '0')
            {
                result.Add(c - '0');
            }
        }

        if (result.Count == 0)
        {
            result.Add(lift);
        }

        return result;
    }
}

using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using TakApp.Avalonia.ViewModels;
using TakEngine.Abstractions;

namespace TakApp.Avalonia.Views;

public partial class SquareView : UserControl
{
    private static readonly IBrush WhitePieceFill = new SolidColorBrush(Color.Parse("#F5EFE6"));
    private static readonly IBrush WhitePieceBorder = new SolidColorBrush(Color.Parse("#C8BEAB"));
    private static readonly IBrush WhiteCrownFill = new SolidColorBrush(Color.Parse("#B7950B"));

    private static readonly IBrush BlackPieceFill = new SolidColorBrush(Color.Parse("#22201F"));
    private static readonly IBrush BlackPieceBorder = new SolidColorBrush(Color.Parse("#4A4644"));
    private static readonly IBrush BlackCrownFill = new SolidColorBrush(Color.Parse("#E67E22"));

    private static readonly IBrush SelectedBorderBrush = new SolidColorBrush(Color.Parse("#F39C12"));
    private static readonly IBrush DefaultBorderBrush = new SolidColorBrush(Color.Parse("#292019"));
    private static readonly IBrush LegalTargetFill = new SolidColorBrush(Color.Parse("#1E3F2B"));
    private static readonly IBrush LastMoveBorderBrush = new SolidColorBrush(Color.Parse("#3498DB"));

    public SquareView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SquareViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateVisuals(vm);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is SquareViewModel vm)
        {
            UpdateVisuals(vm);
        }
    }

    private void UpdateVisuals(SquareViewModel vm)
    {
        var button = this.FindControl<Button>("SquareButton");
        var highlight = this.FindControl<Border>("HighlightBorder");
        var legalDot = this.FindControl<Ellipse>("LegalTargetDot");
        var flatShape = this.FindControl<Border>("FlatStoneShape");
        var standingShape = this.FindControl<Border>("StandingWallShape");
        var capstoneShape = this.FindControl<Border>("CapstoneShape");
        var crownIcon = this.FindControl<Path>("CapstoneCrownIcon");
        var stackBadge = this.FindControl<Border>("StackBadge");
        var stackBadgeText = this.FindControl<TextBlock>("StackBadgeText");

        if (button == null) return;

        // Selection border styling
        if (vm.IsSelected)
        {
            button.BorderBrush = SelectedBorderBrush;
            button.BorderThickness = new global::Avalonia.Thickness(3);
        }
        else if (vm.IsLastMoveTarget || vm.IsLastMoveOrigin)
        {
            button.BorderBrush = LastMoveBorderBrush;
            button.BorderThickness = new global::Avalonia.Thickness(2);
        }
        else
        {
            button.BorderBrush = DefaultBorderBrush;
            button.BorderThickness = new global::Avalonia.Thickness(2);
        }

        // Legal target styling
        if (vm.IsLegalTarget)
        {
            if (highlight != null)
            {
                highlight.Background = LegalTargetFill;
                highlight.Opacity = 0.4;
            }
            if (legalDot != null && vm.IsEmpty)
            {
                legalDot.IsVisible = true;
            }
        }
        else
        {
            if (highlight != null)
            {
                highlight.Background = null;
                highlight.Opacity = 0;
            }
            if (legalDot != null)
            {
                legalDot.IsVisible = false;
            }
        }

        // Hide all piece shapes initially
        if (flatShape != null) flatShape.IsVisible = false;
        if (standingShape != null) standingShape.IsVisible = false;
        if (capstoneShape != null) capstoneShape.IsVisible = false;

        if (!vm.IsEmpty && vm.TopPieceType.HasValue && vm.TopPieceColor.HasValue)
        {
            bool isWhite = vm.TopPieceColor.Value == PlayerColor.White;
            var fill = isWhite ? WhitePieceFill : BlackPieceFill;
            var border = isWhite ? WhitePieceBorder : BlackPieceBorder;

            switch (vm.TopPieceType.Value)
            {
                case PieceType.Flat:
                    if (flatShape != null)
                    {
                        flatShape.Background = fill;
                        flatShape.BorderBrush = border;
                        flatShape.IsVisible = true;
                    }
                    break;

                case PieceType.Standing:
                    if (standingShape != null)
                    {
                        standingShape.Background = fill;
                        standingShape.BorderBrush = border;
                        standingShape.IsVisible = true;
                    }
                    break;

                case PieceType.Capstone:
                    if (capstoneShape != null)
                    {
                        capstoneShape.Background = fill;
                        capstoneShape.BorderBrush = border;
                        if (crownIcon != null)
                        {
                            crownIcon.Fill = isWhite ? WhiteCrownFill : BlackCrownFill;
                        }
                        capstoneShape.IsVisible = true;
                    }
                    break;
            }

            // Tower stack badge
            if (stackBadge != null && stackBadgeText != null)
            {
                if (vm.StackHeight > 1)
                {
                    stackBadgeText.Text = vm.StackHeight.ToString();
                    stackBadge.IsVisible = true;
                }
                else
                {
                    stackBadge.IsVisible = false;
                }
            }
        }
        else
        {
            if (stackBadge != null) stackBadge.IsVisible = false;
        }
    }
}

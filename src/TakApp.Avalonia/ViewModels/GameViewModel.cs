using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TakEngine.Abstractions;

namespace TakApp.Avalonia.ViewModels;

public partial class GameViewModel : ViewModelBase
{
    private readonly ITakGameSession _session;
    private readonly Action _onNewGameRequested;

    public BoardViewModel Board { get; }

    [ObservableProperty]
    public partial PlayerColor ActivePlayer { get; set; } = PlayerColor.White;

    [ObservableProperty]
    public partial GamePhase CurrentPhase { get; set; } = GamePhase.FirstTurnPlacement;

    [ObservableProperty]
    public partial int TurnNumber { get; set; } = 1;

    [ObservableProperty]
    public partial int WhiteStones { get; set; }

    [ObservableProperty]
    public partial int WhiteCapstones { get; set; }

    [ObservableProperty]
    public partial int BlackStones { get; set; }

    [ObservableProperty]
    public partial int BlackCapstones { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "White to move";

    [ObservableProperty]
    public partial string TransportStatus { get; set; } = "Local Pass & Play";

    [ObservableProperty]
    public partial bool IsGameOver { get; set; }

    [ObservableProperty]
    public partial string? GameOverSummary { get; set; }

    [ObservableProperty]
    public partial string? StaleWarningText { get; set; }

    public ObservableCollection<string> MoveHistory { get; } = new();

    public GameViewModel(ITakGameSession session, Action onNewGameRequested)
    {
        _session = session;
        _onNewGameRequested = onNewGameRequested;

        Board = new BoardViewModel(
            session.Size,
            session.GetLegalMovesForSquare,
            OnPlacementSubmitted,
            OnSlideSubmitted);

        // Wire reactive session events
        _session.OnMoveExecuted += HandleMoveExecuted;
        _session.OnGameEnded += HandleGameEnded;
        _session.OnStaleWarning += HandleStaleWarning;
        _session.OnTransportStatusChanged += HandleTransportStatusChanged;
        _session.OnProtocolViolationDetected += HandleProtocolViolationDetected;

        // Initialize snapshot
        UpdateStateFromSnapshot(_session.CurrentBoard);
    }

    private void OnPlacementSubmitted(Coord target, PieceType piece)
    {
        var result = _session.SubmitPlacement(target, piece);
        if (!result.IsSuccess)
        {
            StatusMessage = $"Invalid move: {result.ErrorMessage}";
        }
    }

    private void OnSlideSubmitted(Coord origin, Direction direction, System.Collections.Generic.IReadOnlyList<int> drops)
    {
        var result = _session.SubmitMove(origin, direction, drops);
        if (!result.IsSuccess)
        {
            StatusMessage = $"Invalid move: {result.ErrorMessage}";
        }
    }

    private void HandleMoveExecuted(TakBoardSnapshot snapshot, TakMove move)
    {
        UpdateStateFromSnapshot(snapshot);

        string ptn = move.ToPtn();
        string entry = snapshot.ActivePlayer == PlayerColor.White
            ? $"{snapshot.TurnNumber}. ... {ptn}"
            : $"{snapshot.TurnNumber}. {ptn}";

        MoveHistory.Add(entry);

        Coord? origin = move is SlideMove sm ? sm.Origin : null;
        Coord? target = move switch
        {
            PlaceMove pm => pm.Target,
            SlideMove sm2 => sm2.Origin,
            _ => null
        };
        Board.SetLastMove(origin, target);
    }

    private void HandleGameEnded(TakBoardSnapshot snapshot, GameResult result)
    {
        UpdateStateFromSnapshot(snapshot);
        IsGameOver = true;

        if (result.IsDraw)
        {
            GameOverSummary = $"Game Ended in a Draw! ({result.Reason})";
        }
        else
        {
            string winnerName = result.Winner == PlayerColor.White ? "White" : "Black";
            string reasonName = result.Reason switch
            {
                GameEndReason.Road => "Road Victory",
                GameEndReason.FlatCount => "Flat Count Majority",
                GameEndReason.Resignation => "Resignation",
                GameEndReason.TimeoutDraw => "Timeout",
                _ => "Victory"
            };
            GameOverSummary = $"{winnerName} Wins by {reasonName}!";
        }

        StatusMessage = GameOverSummary;
    }

    private void HandleStaleWarning(TimeSpan remaining)
    {
        StaleWarningText = $"Warning: Turn inactivity! Match will draw in {remaining.Days} days.";
    }

    private void HandleTransportStatusChanged(string status)
    {
        TransportStatus = status;
    }

    private void HandleProtocolViolationDetected(ProtocolViolationException ex)
    {
        StatusMessage = $"Protocol Violation: {ex.Message}";
    }

    private void UpdateStateFromSnapshot(TakBoardSnapshot snapshot)
    {
        ActivePlayer = snapshot.ActivePlayer;
        CurrentPhase = _session.CurrentPhase;
        TurnNumber = snapshot.TurnNumber;

        WhiteStones = snapshot.WhiteReserves.Stones;
        WhiteCapstones = snapshot.WhiteReserves.Capstones;
        BlackStones = snapshot.BlackReserves.Stones;
        BlackCapstones = snapshot.BlackReserves.Capstones;

        Board.UpdateBoard(snapshot);

        if (!IsGameOver)
        {
            string phaseDesc = CurrentPhase == GamePhase.FirstTurnPlacement
                ? " (Turn 1: placing opponent stone)"
                : "";
            StatusMessage = $"{ActivePlayer} to move{phaseDesc}";
        }
    }

    [RelayCommand]
    private void Resign()
    {
        _session.Resign();
    }

    [RelayCommand]
    private void NewGame()
    {
        _onNewGameRequested();
    }
}

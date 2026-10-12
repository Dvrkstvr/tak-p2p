using System;
using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Cryptography;
using TakEngine.Core.Serialization;
using TakEngine.Crypto;

namespace TakEngine.Core.Session;

public sealed class SpectatorGameSession : ISpectatorGameSession
{
    private GameBoard _board;
    private readonly List<TakMove> _moveHistory = [];
    private readonly Dictionary<int, TakBoardSnapshot> _historicalSnapshots = [];
    private string _lastStateHash;
    private bool _isDisposed;

    public Guid GameId { get; }
    public string? TournamentId { get; }
    public BoardSize Size => _board.Size;
    public PlayerColor CurrentTurnColor => _board.ActivePlayer;
    /// <summary>Plies applied so far (D-040); the next MOVE carries <c>CurrentTurnIndex + 1</c>.</summary>
    public int CurrentTurnIndex => _moveHistory.Count;

    public string WhitePlayerPubKey { get; }
    public string BlackPlayerPubKey { get; }
    public int? WhitePlayerElo { get; }
    public int? BlackPlayerElo { get; }

    public TakBoardSnapshot CurrentBoard => _board.ToSnapshot();
    public IReadOnlyList<TakMove> MoveHistory => _moveHistory.AsReadOnly();
    public string LastStateHash => _lastStateHash;

    public event Action<TakBoardSnapshot, TakMove>? OnMoveReceived;
    public event Action<TakBoardSnapshot, GameResult>? OnGameCompleted;
    public event Action<string>? OnSpectatorStatusChanged;
    public event Action<ProtocolViolationException>? OnStateDesyncDetected;

    public SpectatorGameSession(
        Guid gameId,
        BoardSize size,
        string whitePlayerPubKey,
        string blackPlayerPubKey,
        string? tournamentId = null,
        int? whitePlayerElo = null,
        int? blackPlayerElo = null,
        int komiHalves = 0)
    {
        GameId = gameId;
        WhitePlayerPubKey = whitePlayerPubKey ?? throw new ArgumentNullException(nameof(whitePlayerPubKey));
        BlackPlayerPubKey = blackPlayerPubKey ?? throw new ArgumentNullException(nameof(blackPlayerPubKey));
        TournamentId = tournamentId;
        WhitePlayerElo = whitePlayerElo;
        BlackPlayerElo = blackPlayerElo;

        _board = new GameBoard(size, komiHalves);
        _lastStateHash = StateHasher.ComputeGenesisHash(size);
        _historicalSnapshots[0] = _board.ToSnapshot();
    }

    public TakBoardSnapshot GetHistoricalSnapshot(int turnIndex)
    {
        if (_historicalSnapshots.TryGetValue(turnIndex, out var snapshot))
            return snapshot;

        throw new ArgumentOutOfRangeException(nameof(turnIndex), $"No historical snapshot for turn {turnIndex}.");
    }

    /// <summary>
    /// Verifies and applies one player action (D-040: <c>turn</c> is the 1-based ply). Returns false and raises
    /// <see cref="OnStateDesyncDetected"/> on any failed check; the board and hash are then unchanged.
    /// </summary>
    public bool IngestEnvelope(BroadcastEnvelope envelope)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(envelope);
        SignedGameAction action = envelope.Action;

        if (action.GameId != GameId)
            return RaiseDesync($"GameId mismatch: expected {GameId}, got {action.GameId}");

        if (_board.Phase == GamePhase.Completed)
            return RaiseDesync("Game has already completed.");

        int expectedTurn = action.ActionType == GameActionType.Resign ? CurrentTurnIndex : CurrentTurnIndex + 1;
        if (action.Turn != expectedTurn)
            return RaiseDesync($"Turn sequence error: expected turn {expectedTurn}, got {action.Turn}");

        if (action.ActionType == GameActionType.Move)
        {
            string expectedPubKey = CurrentTurnColor == PlayerColor.White ? WhitePlayerPubKey : BlackPlayerPubKey;
            if (!string.Equals(action.PlayerPubKey, expectedPubKey, StringComparison.Ordinal))
                return RaiseDesync($"Player pubkey mismatch for {CurrentTurnColor}: expected {expectedPubKey}, got {action.PlayerPubKey}");
        }
        else if (action.ActionType == GameActionType.Resign)
        {
            if (!string.Equals(action.PlayerPubKey, WhitePlayerPubKey, StringComparison.Ordinal)
                && !string.Equals(action.PlayerPubKey, BlackPlayerPubKey, StringComparison.Ordinal))
                return RaiseDesync($"Player pubkey mismatch: {action.PlayerPubKey} is not a player in this game");
        }
        else
        {
            return RaiseDesync($"A {action.ActionType.ToWire()} action is not a game move.");
        }

        if (!string.Equals(action.PrevStateHash, _lastStateHash, StringComparison.Ordinal))
            return RaiseDesync($"State hash chain broken: expected previous hash {_lastStateHash}, got {action.PrevStateHash}");

        if (!ActionDigest.Verify(action))
            return RaiseDesync($"Invalid cryptographic signature on turn {action.Turn} from {action.PlayerPubKey}");

        if (action.ActionType == GameActionType.Resign)
        {
            PlayerColor resigner = string.Equals(action.PlayerPubKey, WhitePlayerPubKey, StringComparison.Ordinal) ? PlayerColor.White : PlayerColor.Black;
            _board.Resign(resigner);
            if (_board.Result != null)
                OnGameCompleted?.Invoke(_board.ToSnapshot(), _board.Result);
            return true;
        }

        string? ptnMove = action.Ptn;
        TakMove move;
        try
        {
            move = PtnParser.ParseMove(ptnMove ?? throw new FormatException("missing ptn"));
        }
        catch (Exception ex)
        {
            return RaiseDesync($"Malformed PTN move '{ptnMove}': {ex.Message}");
        }

        if (!string.Equals(move.ToPtn(), ptnMove, StringComparison.Ordinal))
            return RaiseDesync($"PTN move '{ptnMove}' is not in canonical form '{move.ToPtn()}'.");

        GameBoard next = _board.Clone();
        var result = next.Execute(move);
        if (!result.IsSuccess)
            return RaiseDesync($"Illegal move attempted: {result.ErrorMessage}");

        string currentTps = TpsSerializer.Serialize(next);
        string computedStateHash = StateHasher.ComputeStateHash(_lastStateHash, action.Turn, action.PlayerPubKey, ptnMove, currentTps);

        if (!string.IsNullOrEmpty(envelope.StateHash) &&
            !string.Equals(envelope.StateHash, computedStateHash, StringComparison.OrdinalIgnoreCase))
        {
            return RaiseDesync($"State hash verification failed: expected {computedStateHash}, got {envelope.StateHash}");
        }

        _board = next;
        _lastStateHash = computedStateHash;
        _moveHistory.Add(move);
        var snapshot = _board.ToSnapshot();
        _historicalSnapshots[_moveHistory.Count] = snapshot;

        OnMoveReceived?.Invoke(snapshot, move);

        if (_board.Phase == GamePhase.Completed && _board.Result != null)
        {
            OnGameCompleted?.Invoke(snapshot, _board.Result);
        }

        return true;
    }

    private bool RaiseDesync(string message)
    {
        OnSpectatorStatusChanged?.Invoke($"Desync: {message}");
        OnStateDesyncDetected?.Invoke(new ProtocolViolationException(message));
        return false;
    }

    public void Dispose()
    {
        _isDisposed = true;
    }
}

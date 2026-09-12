using System;
using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Cryptography;
using TakEngine.Core.Serialization;

namespace TakEngine.Core.Session;

public sealed class SpectatorGameSession : ISpectatorGameSession
{
    private readonly GameBoard _board;
    private readonly List<TakMove> _moveHistory = [];
    private readonly Dictionary<int, TakBoardSnapshot> _historicalSnapshots = [];
    private string _lastStateHash;
    private bool _isDisposed;

    public Guid GameId { get; }
    public string? TournamentId { get; }
    public BoardSize Size => _board.Size;
    public PlayerColor CurrentTurnColor => _board.ActivePlayer;
    public int CurrentTurnIndex => _board.TurnNumber;

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
        int? blackPlayerElo = null)
    {
        GameId = gameId;
        WhitePlayerPubKey = whitePlayerPubKey ?? throw new ArgumentNullException(nameof(whitePlayerPubKey));
        BlackPlayerPubKey = blackPlayerPubKey ?? throw new ArgumentNullException(nameof(blackPlayerPubKey));
        TournamentId = tournamentId;
        WhitePlayerElo = whitePlayerElo;
        BlackPlayerElo = blackPlayerElo;

        _board = new GameBoard(size);
        _lastStateHash = StateHasher.ComputeGenesisHash(size);
        _historicalSnapshots[0] = _board.ToSnapshot();
    }

    public TakBoardSnapshot GetHistoricalSnapshot(int turnIndex)
    {
        if (_historicalSnapshots.TryGetValue(turnIndex, out var snapshot))
            return snapshot;

        throw new ArgumentOutOfRangeException(nameof(turnIndex), $"No historical snapshot for turn {turnIndex}.");
    }

    public bool IngestEnvelope(BroadcastEnvelope envelope)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(envelope);

        // 1. Verify GameId match
        if (envelope.GameId != GameId)
        {
            RaiseDesync($"GameId mismatch: expected {GameId}, got {envelope.GameId}");
            return false;
        }

        // 2. Verify Turn index
        if (envelope.TurnIndex != CurrentTurnIndex)
        {
            RaiseDesync($"Turn sequence error: expected turn {CurrentTurnIndex}, got {envelope.TurnIndex}");
            return false;
        }

        // 3. Verify Active Player PubKey
        string expectedPubKey = CurrentTurnColor == PlayerColor.White ? WhitePlayerPubKey : BlackPlayerPubKey;
        if (!string.Equals(envelope.PlayerPubKey, expectedPubKey, StringComparison.OrdinalIgnoreCase))
        {
            RaiseDesync($"Player pubkey mismatch for {CurrentTurnColor}: expected {expectedPubKey}, got {envelope.PlayerPubKey}");
            return false;
        }

        // 4. Verify PrevStateHash links to current _lastStateHash
        if (!string.Equals(envelope.PrevStateHash, _lastStateHash, StringComparison.OrdinalIgnoreCase))
        {
            RaiseDesync($"State hash chain broken: expected previous hash {_lastStateHash}, got {envelope.PrevStateHash}");
            return false;
        }

        // 5. Verify Cryptographic Signature
        string signingPayload = envelope.GetSigningPayload();
        if (!CryptoSigner.Verify(envelope.PlayerPubKey, signingPayload, envelope.Signature))
        {
            RaiseDesync($"Invalid cryptographic signature on turn {envelope.TurnIndex} from {envelope.PlayerPubKey}");
            return false;
        }

        // 6. Parse and apply the move
        TakMove move;
        try
        {
            move = PtnParser.ParseMove(envelope.PtnMove);
        }
        catch (Exception ex)
        {
            RaiseDesync($"Malformed PTN move '{envelope.PtnMove}': {ex.Message}");
            return false;
        }

        var result = _board.Execute(move);
        if (!result.IsSuccess)
        {
            RaiseDesync($"Illegal move attempted: {result.ErrorMessage}");
            return false;
        }

        // 7. Update state hash
        string currentTps = TpsSerializer.Serialize(_board);
        string computedStateHash = StateHasher.ComputeStateHash(
            _lastStateHash,
            envelope.TurnIndex,
            envelope.PlayerPubKey,
            envelope.PtnMove,
            currentTps);

        if (!string.IsNullOrEmpty(envelope.StateHash) &&
            !string.Equals(envelope.StateHash, computedStateHash, StringComparison.OrdinalIgnoreCase))
        {
            RaiseDesync($"State hash verification failed: expected {computedStateHash}, got {envelope.StateHash}");
            return false;
        }

        _lastStateHash = computedStateHash;
        _moveHistory.Add(move);
        var snapshot = _board.ToSnapshot();
        _historicalSnapshots[_moveHistory.Count] = snapshot;

        // 8. Fire events
        OnMoveReceived?.Invoke(snapshot, move);

        if (_board.Phase == GamePhase.Completed && _board.Result != null)
        {
            OnGameCompleted?.Invoke(snapshot, _board.Result);
        }

        return true;
    }

    private void RaiseDesync(string message)
    {
        OnSpectatorStatusChanged?.Invoke($"Desync: {message}");
        OnStateDesyncDetected?.Invoke(new ProtocolViolationException(message));
    }

    public void Dispose()
    {
        _isDisposed = true;
    }
}

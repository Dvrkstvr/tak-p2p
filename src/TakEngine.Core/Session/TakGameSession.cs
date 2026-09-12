using System;
using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Cryptography;
using TakEngine.Core.Rules;
using TakEngine.Core.Serialization;

namespace TakEngine.Core.Session;

public sealed class TakGameSession : ITakGameSession
{
    private readonly GameBoard _board;
    private readonly bool _isRemote;
    private readonly string? _localPrivateKeyHex;
    private readonly string? _opponentPubKeyHex;
    private string _currentStateHash;

    public GameId Id { get; }
    public BoardSize Size => _board.Size;
    public PlayerColor LocalColor { get; }
    public GamePhase CurrentPhase => _board.Phase;
    public TakBoardSnapshot CurrentBoard => _board.ToSnapshot();
    public string CurrentStateHash => _currentStateHash;
    public GameResult? Result => _board.Result;

    public event Action<TakBoardSnapshot, TakMove>? OnMoveExecuted;
    public event Action<TakBoardSnapshot, GameResult>? OnGameEnded;
    public event Action<TimeSpan>? OnStaleWarning;
    public event Action<string>? OnTransportStatusChanged;
    public event Action<ProtocolViolationException>? OnProtocolViolationDetected;

    /// <summary>
    /// Event fired when an outgoing remote move envelope is generated and ready to be transmitted over Nostr.
    /// </summary>
    public event Action<string>? OnRemoteEnvelopeReady;

    private TakGameSession(
        GameId id,
        BoardSize size,
        PlayerColor localColor,
        bool isRemote,
        string? localPrivateKeyHex = null,
        string? opponentPubKeyHex = null)
    {
        Id = id;
        LocalColor = localColor;
        _isRemote = isRemote;
        _localPrivateKeyHex = localPrivateKeyHex;
        _opponentPubKeyHex = opponentPubKeyHex;
        _board = new GameBoard(size);
        _currentStateHash = StateHasher.ComputeGenesisHash(size);
    }

    /// <summary>
    /// Creates a local pass-and-play session where both players share the screen.
    /// </summary>
    public static TakGameSession CreateLocal(BoardSize size = BoardSize.Five)
    {
        return new TakGameSession(
            GameId.New(),
            size,
            PlayerColor.White,
            isRemote: false);
    }

    /// <summary>
    /// Creates a remote P2P session with deterministic local color and cryptographic signing.
    /// </summary>
    public static TakGameSession CreateRemote(
        GameId id,
        BoardSize size,
        PlayerColor localColor,
        string localPrivateKeyHex,
        string opponentPubKeyHex)
    {
        return new TakGameSession(
            id,
            size,
            localColor,
            isRemote: true,
            localPrivateKeyHex: localPrivateKeyHex,
            opponentPubKeyHex: opponentPubKeyHex);
    }

    public IReadOnlyList<TakMove> GetLegalMovesForSquare(Coord coord)
    {
        return MoveValidator.GetLegalMovesForSquare(_board, coord);
    }

    public CommandResult SubmitPlacement(Coord target, PieceType piece)
    {
        if (_board.Phase == GamePhase.Completed)
            return CommandResult.Fail("Game has already completed.");

        if (_isRemote && _board.ActivePlayer != LocalColor)
            return CommandResult.Fail("It is not your turn.");

        var move = new PlaceMove(target, piece);
        var res = _board.Place(target, piece);
        if (!res.IsSuccess)
            return res;

        ProcessMoveSuccess(move);
        return CommandResult.Success();
    }

    public CommandResult SubmitMove(Coord origin, Direction direction, IReadOnlyList<int> drops)
    {
        if (_board.Phase == GamePhase.Completed)
            return CommandResult.Fail("Game has already completed.");

        if (_isRemote && _board.ActivePlayer != LocalColor)
            return CommandResult.Fail("It is not your turn.");

        int totalLift = 0;
        foreach (var d in drops) totalLift += d;

        var move = new SlideMove(origin, direction, totalLift, drops);
        var res = _board.Slide(origin, direction, totalLift, drops);
        if (!res.IsSuccess)
            return res;

        ProcessMoveSuccess(move);
        return CommandResult.Success();
    }

    public CommandResult Resign()
    {
        if (_board.Phase == GamePhase.Completed)
            return CommandResult.Fail("Game has already completed.");

        PlayerColor resigningPlayer = _isRemote ? LocalColor : _board.ActivePlayer;
        var res = _board.Resign(resigningPlayer);
        if (!res.IsSuccess)
            return res;

        var snapshot = _board.ToSnapshot();
        if (_board.Result != null)
        {
            OnGameEnded?.Invoke(snapshot, _board.Result);
        }

        return CommandResult.Success();
    }

    public void NotifyStaleWarning(TimeSpan remainingTime)
    {
        OnStaleWarning?.Invoke(remainingTime);
    }

    public void UpdateTransportStatus(string status)
    {
        OnTransportStatusChanged?.Invoke(status);
    }

    /// <summary>
    /// Processes an incoming remote move payload received over Nostr transport.
    /// </summary>
    public CommandResult ProcessRemoteMove(
        string playerPubKey,
        string prevStateHash,
        string ptnMove,
        string signature)
    {
        if (!_isRemote)
            return CommandResult.Fail("Cannot process remote moves in a local game session.");

        if (_board.Phase == GamePhase.Completed)
            return CommandResult.Fail("Game has already completed.");

        if (_board.ActivePlayer == LocalColor)
        {
            var ex = new ProtocolViolationException("Received unexpected move from remote peer when it was local player's turn.");
            OnProtocolViolationDetected?.Invoke(ex);
            return CommandResult.Fail(ex.Message);
        }

        if (_opponentPubKeyHex != null && !string.Equals(playerPubKey, _opponentPubKeyHex, StringComparison.OrdinalIgnoreCase))
        {
            var ex = new ProtocolViolationException($"Received move from unauthorized pubkey: {playerPubKey}");
            OnProtocolViolationDetected?.Invoke(ex);
            return CommandResult.Fail(ex.Message);
        }

        // Verify state hash chain
        if (!string.Equals(prevStateHash, _currentStateHash, StringComparison.OrdinalIgnoreCase))
        {
            var ex = new ProtocolViolationException($"Hash chain mismatch. Expected: {_currentStateHash}, Received: {prevStateHash}");
            OnProtocolViolationDetected?.Invoke(ex);
            return CommandResult.Fail(ex.Message);
        }

        // Verify Ed25519 signature over (prevStateHash + ptnMove)
        string payload = $"{prevStateHash}:{ptnMove}";
        if (!CryptoSigner.Verify(playerPubKey, payload, signature))
        {
            var ex = new ProtocolViolationException("Invalid cryptographic signature on remote move payload.");
            OnProtocolViolationDetected?.Invoke(ex);
            return CommandResult.Fail(ex.Message);
        }

        // Parse and execute PTN move
        TakMove move;
        try
        {
            move = PtnParser.ParseMove(ptnMove);
        }
        catch (Exception ex)
        {
            var protoEx = new ProtocolViolationException($"Failed to parse remote PTN move '{ptnMove}': {ex.Message}", ex);
            OnProtocolViolationDetected?.Invoke(protoEx);
            return CommandResult.Fail(protoEx.Message);
        }

        var execResult = _board.Execute(move);
        if (!execResult.IsSuccess)
        {
            var protoEx = new ProtocolViolationException($"Remote peer executed illegal move: {execResult.ErrorMessage}");
            OnProtocolViolationDetected?.Invoke(protoEx);
            return execResult;
        }

        // Update state hash chain
        var snapshot = _board.ToSnapshot();
        string tps = TpsSerializer.Serialize(_board);
        _currentStateHash = StateHasher.ComputeStateHash(_currentStateHash, _board.TurnNumber, playerPubKey, ptnMove, tps);

        OnMoveExecuted?.Invoke(snapshot, move);

        if (_board.Result != null)
        {
            OnGameEnded?.Invoke(snapshot, _board.Result);
        }

        return CommandResult.Success();
    }

    private void ProcessMoveSuccess(TakMove move)
    {
        var snapshot = _board.ToSnapshot();
        string ptn = move.ToPtn();
        string tps = TpsSerializer.Serialize(_board);

        string playerPubKey = _isRemote && _localPrivateKeyHex != null
            ? CryptoSigner.GetPublicKeyHex(_localPrivateKeyHex)
            : "local-player";

        string prevHash = _currentStateHash;
        _currentStateHash = StateHasher.ComputeStateHash(prevHash, _board.TurnNumber, playerPubKey, ptn, tps);

        OnMoveExecuted?.Invoke(snapshot, move);

        if (_isRemote && _localPrivateKeyHex != null)
        {
            // Compute signature over previous state hash + ptn move
            string payload = $"{prevHash}:{ptn}";
            string sig = CryptoSigner.Sign(_localPrivateKeyHex, payload);
            OnRemoteEnvelopeReady?.Invoke(sig);
        }

        if (_board.Result != null)
        {
            OnGameEnded?.Invoke(snapshot, _board.Result);
        }
    }
}

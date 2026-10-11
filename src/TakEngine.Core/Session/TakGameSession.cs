using System;
using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Cryptography;
using TakEngine.Core.Rules;
using TakEngine.Core.Serialization;
using TakEngine.Crypto;

namespace TakEngine.Core.Session;

public sealed class TakGameSession : ITakGameSession
{
    private readonly GameBoard _board;
    private readonly bool _isRemote;
    private readonly SecretKey? _localKey;
    private readonly PublicKey? _opponentPubKey;
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
        SecretKey? localKey = null,
        PublicKey? opponentPubKey = null,
        int komiHalves = 0)
    {
        Id = id;
        LocalColor = localColor;
        _isRemote = isRemote;
        _localKey = localKey;
        _opponentPubKey = opponentPubKey;
        _board = new GameBoard(size, komiHalves);
        _currentStateHash = StateHasher.ComputeGenesisHash(size);
    }

    /// <summary>
    /// Creates a local pass-and-play session where both players share the screen.
    /// </summary>
    public static TakGameSession CreateLocal(BoardSize size = BoardSize.Five, int komiHalves = 0)
    {
        return new TakGameSession(
            GameId.New(),
            size,
            PlayerColor.White,
            isRemote: false,
            komiHalves: komiHalves);
    }

    /// <summary>
    /// Creates a remote P2P session: local moves are signed with the player's secp256k1 key, remote moves must come
    /// from <paramref name="opponentPubKey"/>.
    /// </summary>
    public static TakGameSession CreateRemote(
        GameId id,
        BoardSize size,
        PlayerColor localColor,
        SecretKey localKey,
        PublicKey opponentPubKey,
        int komiHalves = 0)
    {
        ArgumentNullException.ThrowIfNull(localKey);
        ArgumentNullException.ThrowIfNull(opponentPubKey);
        return new TakGameSession(
            id,
            size,
            localColor,
            isRemote: true,
            localKey: localKey,
            opponentPubKey: opponentPubKey,
            komiHalves: komiHalves);
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

        if (_opponentPubKey != null && !string.Equals(playerPubKey, _opponentPubKey.ToHex(), StringComparison.OrdinalIgnoreCase))
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

        // Verify the BIP-340 signature over (prevStateHash + ptnMove); F-033 replaces this payload with ActionDigest
        string payload = $"{prevStateHash}:{ptnMove}";
        if (!PayloadSignature.Verify(playerPubKey, payload, signature))
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

        // The chain hashes the PTN string, so a second spelling of the same move ("Fa1" for "a1") would split the peers' hashes.
        if (!string.Equals(move.ToPtn(), ptnMove, StringComparison.Ordinal))
        {
            var protoEx = new ProtocolViolationException($"Remote PTN move '{ptnMove}' is not in canonical form '{move.ToPtn()}'.");
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
        _currentStateHash = StateHasher.ComputeStateHash(_currentStateHash, _board.TurnNumber, _opponentPubKey?.ToHex() ?? playerPubKey, ptnMove, tps);

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

        string playerPubKey = _isRemote && _localKey != null
            ? _localKey.PublicKey.ToHex()
            : "local-player";

        string prevHash = _currentStateHash;
        _currentStateHash = StateHasher.ComputeStateHash(prevHash, _board.TurnNumber, playerPubKey, ptn, tps);

        OnMoveExecuted?.Invoke(snapshot, move);

        if (_isRemote && _localKey != null)
        {
            // Compute signature over previous state hash + ptn move
            string payload = $"{prevHash}:{ptn}";
            string sig = PayloadSignature.Sign(_localKey, payload);
            OnRemoteEnvelopeReady?.Invoke(sig);
        }

        if (_board.Result != null)
        {
            OnGameEnded?.Invoke(snapshot, _board.Result);
        }
    }
}

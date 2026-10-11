using System;
using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Cryptography;
using TakEngine.Core.Rules;
using TakEngine.Core.Serialization;
using TakEngine.Crypto;

namespace TakEngine.Core.Session;

/// <summary>
/// One game on this device. Local sessions are pass-and-play; remote sessions sign every local action as a
/// <see cref="SignedGameAction"/> (BIP-340 over <see cref="ActionDigest"/>, D-011) and accept a remote action only when author,
/// signature, game id, previous hash, turn number, turn owner and move legality all check out (F-033).
/// The hash chain's turn input is the 1-based ply number of the move (D-040). Pure: aux randomness comes from the caller.
/// </summary>
public sealed class TakGameSession : ITakGameSession
{
    public const string LocalPlayerPubKey = "local-player";

    private readonly GameBoard _board;
    private readonly bool _isRemote;
    private readonly SecretKey? _localKey;
    private readonly PublicKey? _opponentPubKey;
    private readonly Func<byte[]>? _aux32;
    private string _currentStateHash;
    private int _appliedTurn;

    public GameId Id { get; }
    public BoardSize Size => _board.Size;
    public PlayerColor LocalColor { get; }
    public GamePhase CurrentPhase => _board.Phase;
    public TakBoardSnapshot CurrentBoard => _board.ToSnapshot();
    public string CurrentStateHash => _currentStateHash;
    public GameResult? Result => _board.Result;
    public int KomiHalves => _board.KomiHalves;

    /// <summary>Plies applied so far (D-040); the next move carries <c>AppliedTurn + 1</c>.</summary>
    public int AppliedTurn => _appliedTurn;

    /// <summary>The current position as TPS (what <see cref="Restore"/> takes back).</summary>
    public string CurrentTps => TpsSerializer.Serialize(_board);

    public event Action<TakBoardSnapshot, TakMove>? OnMoveExecuted;
    public event Action<TakBoardSnapshot, GameResult>? OnGameEnded;
    public event Action<TimeSpan>? OnStaleWarning;
    public event Action<string>? OnTransportStatusChanged;
    public event Action<ProtocolViolationException>? OnProtocolViolationDetected;

    /// <summary>A local MOVE or RESIGN was applied and signed; the shell persists and publishes it.</summary>
    public event Action<SignedGameAction>? OnLocalActionSigned;

    private TakGameSession(
        GameId id,
        GameBoard board,
        PlayerColor localColor,
        bool isRemote,
        string stateHash,
        int appliedTurn,
        SecretKey? localKey = null,
        PublicKey? opponentPubKey = null,
        Func<byte[]>? aux32 = null)
    {
        Id = id;
        LocalColor = localColor;
        _isRemote = isRemote;
        _localKey = localKey;
        _opponentPubKey = opponentPubKey;
        _aux32 = aux32;
        _board = board;
        _currentStateHash = stateHash;
        _appliedTurn = appliedTurn;
    }

    /// <summary>
    /// Creates a local pass-and-play session where both players share the screen.
    /// </summary>
    public static TakGameSession CreateLocal(BoardSize size = BoardSize.Five, int komiHalves = 0)
    {
        return new TakGameSession(
            GameId.New(),
            new GameBoard(size, komiHalves),
            PlayerColor.White,
            isRemote: false,
            StateHasher.ComputeGenesisHash(size),
            appliedTurn: 0);
    }

    /// <summary>
    /// Creates a remote P2P session: local actions are signed with <paramref name="localKey"/> using BIP-340 aux bytes from
    /// <paramref name="aux32"/> (shell: the system RNG; tests: a seeded source); remote actions must come from
    /// <paramref name="opponentPubKey"/>.
    /// </summary>
    public static TakGameSession CreateRemote(
        GameId id,
        BoardSize size,
        PlayerColor localColor,
        SecretKey localKey,
        PublicKey opponentPubKey,
        Func<byte[]> aux32,
        int komiHalves = 0)
    {
        ArgumentNullException.ThrowIfNull(localKey);
        ArgumentNullException.ThrowIfNull(opponentPubKey);
        ArgumentNullException.ThrowIfNull(aux32);
        return new TakGameSession(
            id,
            new GameBoard(size, komiHalves),
            localColor,
            isRemote: true,
            StateHasher.ComputeGenesisHash(size),
            appliedTurn: 0,
            localKey,
            opponentPubKey,
            aux32);
    }

    /// <summary>
    /// Restores an unfinished remote session from what the store kept after the last applied ply (F-037): the TPS, the state
    /// hash and the ply count. The restored session continues the same chain.
    /// </summary>
    public static TakGameSession Restore(
        GameId id,
        PlayerColor localColor,
        SecretKey localKey,
        PublicKey opponentPubKey,
        Func<byte[]> aux32,
        string tps,
        string stateHash,
        int appliedTurn,
        int komiHalves = 0)
    {
        ArgumentNullException.ThrowIfNull(localKey);
        ArgumentNullException.ThrowIfNull(opponentPubKey);
        ArgumentNullException.ThrowIfNull(aux32);
        ArgumentNullException.ThrowIfNull(tps);
        ArgumentNullException.ThrowIfNull(stateHash);
        ArgumentOutOfRangeException.ThrowIfNegative(appliedTurn);
        GameBoard board = TpsSerializer.Deserialize(tps, komiHalves);
        return new TakGameSession(id, board, localColor, isRemote: true, stateHash, appliedTurn, localKey, opponentPubKey, aux32);
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

        ProcessLocalMoveSuccess(move);
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

        ProcessLocalMoveSuccess(move);
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

        if (_isRemote)
        {
            var resign = SignedGameAction.Unsigned(GameActionType.Resign, Id.Value, _appliedTurn, _localKey!.PublicKey.ToHex(), _currentStateHash);
            OnLocalActionSigned?.Invoke(ActionDigest.Sign(_localKey, resign, _aux32!()));
        }

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
    /// Applies an opponent's MOVE or RESIGN. Any failed check returns a failure, raises
    /// <see cref="OnProtocolViolationDetected"/> and leaves the board, hash and ply count unchanged. Never throws on peer input.
    /// </summary>
    public CommandResult ProcessRemoteAction(SignedGameAction? action)
    {
        if (!_isRemote)
            return CommandResult.Fail("Cannot process remote actions in a local game session.");

        if (action is null)
            return Violation("Received an empty action.");

        if (_board.Phase == GamePhase.Completed)
            return CommandResult.Fail("Game has already completed.");

        if (action.Pv != SignedGameAction.CurrentPv)
            return Violation($"Unsupported protocol version {action.Pv}.");

        if (action.GameId != Id.Value)
            return Violation($"Action is for game {action.GameId}, not {Id.Value}.");

        if (!string.Equals(action.PlayerPubKey, _opponentPubKey!.ToHex(), StringComparison.Ordinal))
            return Violation($"Received action from unauthorized pubkey: {action.PlayerPubKey}");

        if (!ActionDigest.Verify(action))
            return Violation("Invalid cryptographic signature on remote action.");

        if (!string.Equals(action.PrevStateHash, _currentStateHash, StringComparison.Ordinal))
            return Violation($"Hash chain mismatch. Expected: {_currentStateHash}, Received: {action.PrevStateHash}");

        return action.ActionType switch
        {
            GameActionType.Move => ApplyRemoteMove(action),
            GameActionType.Resign => ApplyRemoteResign(action),
            _ => Violation($"A {action.ActionType.ToWire()} action is not a game move."),
        };
    }

    private CommandResult ApplyRemoteMove(SignedGameAction action)
    {
        if (action.Turn != _appliedTurn + 1)
            return Violation($"Wrong turn number. Expected: {_appliedTurn + 1}, Received: {action.Turn}");

        if (_board.ActivePlayer == LocalColor)
            return Violation("Received a move from the remote peer on the local player's turn.");

        string? ptnMove = action.Ptn;
        if (ptnMove is null || action.ActionData.Count != 1)
            return Violation("A MOVE action must carry exactly one 'ptn' string.");

        TakMove move;
        try
        {
            move = PtnParser.ParseMove(ptnMove);
        }
        catch (Exception ex)
        {
            return Violation($"Failed to parse remote PTN move '{ptnMove}': {ex.Message}", ex);
        }

        // The chain hashes the PTN string, so a second spelling of the same move ("Fa1" for "a1") would split the peers' hashes.
        if (!string.Equals(move.ToPtn(), ptnMove, StringComparison.Ordinal))
            return Violation($"Remote PTN move '{ptnMove}' is not in canonical form '{move.ToPtn()}'.");

        var execResult = _board.Execute(move);
        if (!execResult.IsSuccess)
        {
            Violation($"Remote peer executed illegal move: {execResult.ErrorMessage}");
            return execResult;
        }

        _appliedTurn++;
        string tps = TpsSerializer.Serialize(_board);
        _currentStateHash = StateHasher.ComputeStateHash(_currentStateHash, _appliedTurn, action.PlayerPubKey, ptnMove, tps);

        var snapshot = _board.ToSnapshot();
        OnMoveExecuted?.Invoke(snapshot, move);
        if (_board.Result != null)
            OnGameEnded?.Invoke(snapshot, _board.Result);

        return CommandResult.Success();
    }

    private CommandResult ApplyRemoteResign(SignedGameAction action)
    {
        if (action.Turn != _appliedTurn)
            return Violation($"Wrong turn number on RESIGN. Expected: {_appliedTurn}, Received: {action.Turn}");
        if (action.ActionData.Count != 0)
            return Violation("A RESIGN action carries no data.");

        PlayerColor opponent = LocalColor == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
        var res = _board.Resign(opponent);
        if (!res.IsSuccess)
            return res;

        var snapshot = _board.ToSnapshot();
        if (_board.Result != null)
            OnGameEnded?.Invoke(snapshot, _board.Result);
        return CommandResult.Success();
    }

    private CommandResult Violation(string message, Exception? inner = null)
    {
        var ex = inner is null ? new ProtocolViolationException(message) : new ProtocolViolationException(message, inner);
        OnProtocolViolationDetected?.Invoke(ex);
        return CommandResult.Fail(message);
    }

    private void ProcessLocalMoveSuccess(TakMove move)
    {
        string ptn = move.ToPtn();
        string tps = TpsSerializer.Serialize(_board);
        string playerPubKey = _isRemote ? _localKey!.PublicKey.ToHex() : LocalPlayerPubKey;

        string prevHash = _currentStateHash;
        _appliedTurn++;
        _currentStateHash = StateHasher.ComputeStateHash(prevHash, _appliedTurn, playerPubKey, ptn, tps);

        var snapshot = _board.ToSnapshot();
        OnMoveExecuted?.Invoke(snapshot, move);

        if (_isRemote)
        {
            var unsigned = SignedGameAction.UnsignedMove(Id.Value, _appliedTurn, playerPubKey, prevHash, ptn);
            OnLocalActionSigned?.Invoke(ActionDigest.Sign(_localKey!, unsigned, _aux32!()));
        }

        if (_board.Result != null)
        {
            OnGameEnded?.Invoke(snapshot, _board.Result);
        }
    }
}

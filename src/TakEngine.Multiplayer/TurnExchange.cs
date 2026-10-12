using TakEngine.Abstractions;
using TakEngine.Core.Session;
using TakEngine.Crypto;

namespace TakEngine.Multiplayer;

public enum ReceiveOutcome
{
    /// <summary>Applied (and anything it unblocked in the buffer after it).</summary>
    Applied,

    /// <summary>Ahead of the next turn: held until the turns before it arrive.</summary>
    Buffered,

    /// <summary>Same turn and same ActionDigest as one already applied or buffered (relay fan-out, a republish): ignored silently.</summary>
    Duplicate,

    /// <summary>Our own event echoed back by a relay.</summary>
    OwnEcho,

    /// <summary>The game is already over: the result stands.</summary>
    Late,

    /// <summary>Not a game action (JOIN/ACCEPT belong to the handshake).</summary>
    Ignored,

    /// <summary>Dropped and flagged (rows 5-7); the board and hash are unchanged.</summary>
    Flagged,
}

public sealed record ExchangeFlag(int Turn, string Reason, string? EventId);

/// <summary>What one input did.</summary>
/// <param name="Applied">Moves applied by this step, in turn order: the received one and any buffered ones it unblocked.</param>
/// <param name="Flags">Rejected actions: the received one (outcome <see cref="ReceiveOutcome.Flagged"/>), or buffered ones that
/// failed once their turn came.</param>
/// <param name="Ended">The result, if the game ended in this step.</param>
public sealed record ExchangeStep(
    ReceiveOutcome Outcome,
    IReadOnlyList<AppliedAction> Applied,
    IReadOnlyList<ExchangeFlag> Flags,
    GameResult? Ended = null)
{
    public static ExchangeStep Of(ReceiveOutcome outcome) => new(outcome, [], []);
}

/// <summary>
/// Orders, dedupes and applies the opponent's MOVE and RESIGN actions (F-035; scope.md "Turn exchange"). Input has already passed
/// tamper rows 1-4 (<see cref="Transport.Envelope.EnvelopeCodec"/>); this class decides rows 5-7. Ordering is by <c>turn</c> only.
/// Turn t+2 before t+1 is buffered (at most <see cref="BufferCapacity"/>), and if the gap stays open <see cref="GapTimeout"/>,
/// <see cref="Tick"/> asks for a catch-up. A rejected action never changes the board or the hash. Pure: owns a
/// <see cref="TakGameSession"/> (itself pure), never awaits or reads a clock; time is passed in.
/// </summary>
public sealed class TurnExchange
{
    public const int BufferCapacity = 64;
    public static readonly TimeSpan GapTimeout = TimeSpan.FromSeconds(20);

    private readonly TakGameSession _session;
    private readonly string _me;
    private readonly PlayerColor _opponentColor;
    private readonly Dictionary<int, string> _appliedDigests = [];
    private readonly SortedDictionary<int, (SignedGameAction Action, string EventId, string Digest)> _buffer = [];
    private (SignedGameAction Action, string EventId)? _pendingResign;
    private DateTimeOffset? _gapSince;

    /// <param name="history">Moves already applied (on a restart), so a re-delivered one is recognised as a duplicate.</param>
    public TurnExchange(TakGameSession session, PublicKey me, IEnumerable<SignedGameAction>? history = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(me);
        _session = session;
        _me = me.ToHex();
        _opponentColor = session.LocalColor == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
        foreach (SignedGameAction action in history ?? [])
        {
            if (action.ActionType == GameActionType.Move)
                _appliedDigests[action.Turn] = Digest(action);
        }
    }

    public TakGameSession Session => _session;

    public int AppliedTurn => _session.AppliedTurn;

    public bool IsCompleted => _session.CurrentPhase == GamePhase.Completed;

    /// <summary>The turn the buffer is waiting for, or null when nothing is held.</summary>
    public int? WaitingFor => _buffer.Count > 0 || _pendingResign is not null ? AppliedTurn + 1 : null;

    public int BufferedCount => _buffer.Count;

    /// <summary>When <see cref="Tick"/> will next ask for a catch-up, or null if no gap is open.</summary>
    public DateTimeOffset? NextDeadline => _gapSince + GapTimeout;

    /// <summary>The player who moves at ply <paramref name="turn"/> (1-based): White on odd plies, Black on even ones.</summary>
    public static PlayerColor TurnOwner(int turn) => turn % 2 == 1 ? PlayerColor.White : PlayerColor.Black;

    /// <summary>Handles one decoded action that passed rows 1-4.</summary>
    public ExchangeStep Receive(SignedGameAction action, string eventId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (string.Equals(action.PlayerPubKey, _me, StringComparison.Ordinal))
            return ExchangeStep.Of(ReceiveOutcome.OwnEcho);
        if (IsCompleted)
            return ExchangeStep.Of(ReceiveOutcome.Late);

        return action.ActionType switch
        {
            GameActionType.Move => ReceiveMove(action, eventId, now),
            GameActionType.Resign => ReceiveResign(action, eventId, now),
            _ => ExchangeStep.Of(ReceiveOutcome.Ignored),
        };
    }

    /// <summary>
    /// Records our own move, signed by the session just now, and applies anything it unblocked. Returns the own move as the first
    /// applied entry.
    /// </summary>
    public ExchangeStep RecordLocal(SignedGameAction own, string eventId)
    {
        ArgumentNullException.ThrowIfNull(own);
        if (own.ActionType != GameActionType.Move)
            return ExchangeStep.Of(ReceiveOutcome.Ignored);
        _appliedDigests[own.Turn] = Digest(own);
        var applied = new List<AppliedAction> { new(own, eventId, _session.CurrentTps, _session.CurrentStateHash) };
        var flags = new List<ExchangeFlag>();
        return new ExchangeStep(ReceiveOutcome.Applied, applied, flags, Drain(applied, flags));
    }

    /// <summary>Asks for a catch-up when turn n+1 has been missing for <see cref="GapTimeout"/>; then waits another timeout.</summary>
    public bool Tick(DateTimeOffset now)
    {
        if (_gapSince is not { } since || now - since < GapTimeout)
            return false;
        _gapSince = now;
        return true;
    }

    private ExchangeStep ReceiveMove(SignedGameAction action, string eventId, DateTimeOffset now)
    {
        int t = action.Turn;
        int n = AppliedTurn;
        string digest = Digest(action);

        if (t < 1 || TurnOwner(t) != _opponentColor)
            return Flag(t, IncomingClassifier.IllegalMove($"turn {t} is not the opponent's turn"), eventId);

        if (t <= n)
        {
            return _appliedDigests.TryGetValue(t, out string? applied) && applied == digest
                ? ExchangeStep.Of(ReceiveOutcome.Duplicate)
                : Flag(t, IncomingClassifier.ConflictingMove(t), eventId);
        }

        if (t > n + 1)
        {
            if (_buffer.TryGetValue(t, out var held))
            {
                return held.Digest == digest
                    ? ExchangeStep.Of(ReceiveOutcome.Duplicate)
                    : Flag(t, IncomingClassifier.ConflictingMove(t), eventId);
            }
            if (_buffer.Count >= BufferCapacity)
            {
                int furthest = _buffer.Keys.Max();
                if (t > furthest)
                    return ExchangeStep.Of(ReceiveOutcome.Buffered); // dropped: further ahead than everything held
                _buffer.Remove(furthest);
            }
            _buffer[t] = (action, eventId, digest);
            _gapSince ??= now;
            return ExchangeStep.Of(ReceiveOutcome.Buffered);
        }

        var appliedNow = new List<AppliedAction>();
        ExchangeFlag? flag = Apply(action, eventId, digest, appliedNow);
        if (flag is not null)
            return new ExchangeStep(ReceiveOutcome.Flagged, [], [flag]);
        var flags = new List<ExchangeFlag>();
        return new ExchangeStep(ReceiveOutcome.Applied, appliedNow, flags, Drain(appliedNow, flags));
    }

    /// <summary>
    /// A RESIGN carries the resigner's <c>appliedTurn</c> k. When k is ahead of ours, the resigner's own moves up to k are still in
    /// flight (they were saved and published before the RESIGN, so they will come): hold it until turn k is applied, so both sides
    /// end on the hash after turn k (review M0-code finding 2). The gap gets the usual catch-up.
    /// </summary>
    private ExchangeStep ReceiveResign(SignedGameAction action, string eventId, DateTimeOffset now)
    {
        if (action.Turn > AppliedTurn)
        {
            if (_pendingResign is null)
                _pendingResign = (action, eventId);
            _gapSince ??= now;
            return ExchangeStep.Of(ReceiveOutcome.Buffered);
        }
        return ApplyResign(action, eventId);
    }

    private ExchangeStep ApplyResign(SignedGameAction action, string eventId)
    {
        _pendingResign = null;
        var result = _session.ProcessRemoteAction(action);
        if (!result.IsSuccess)
            return Flag(action.Turn, IncomingClassifier.IllegalMove(result.ErrorMessage!), eventId);
        ClearHeld();
        return new ExchangeStep(ReceiveOutcome.Applied, [], [], _session.Result);
    }

    /// <summary>Applies the action for turn n+1: row 5 (prev hash), then row 6 (the session's checks). Null on success.</summary>
    private ExchangeFlag? Apply(SignedGameAction action, string eventId, string digest, List<AppliedAction> applied)
    {
        if (!string.Equals(action.PrevStateHash, _session.CurrentStateHash, StringComparison.Ordinal))
            return new ExchangeFlag(action.Turn, IncomingClassifier.HashChainMismatch(action.Turn), eventId);

        var result = _session.ProcessRemoteAction(action);
        if (!result.IsSuccess)
            return new ExchangeFlag(action.Turn, IncomingClassifier.IllegalMove(result.ErrorMessage!), eventId);

        _appliedDigests[action.Turn] = digest;
        applied.Add(new AppliedAction(action, eventId, _session.CurrentTps, _session.CurrentStateHash));
        return null;
    }

    /// <summary>
    /// After a ply: applies the held action for the next turn, if any. Turns alternate, so at most one held opponent move can follow
    /// a ply; after it comes our own. A held move that fails is dropped and flagged. Returns the result if the game is over.
    /// </summary>
    private GameResult? Drain(List<AppliedAction> applied, List<ExchangeFlag> flags)
    {
        if (!IsCompleted && _buffer.Remove(AppliedTurn + 1, out var held) && Apply(held.Action, held.EventId, held.Digest, applied) is { } flag)
            flags.Add(flag);
        if (!IsCompleted && _pendingResign is { } resign && resign.Action.Turn <= AppliedTurn)
            flags.AddRange(ApplyResign(resign.Action, resign.EventId).Flags);
        if (IsCompleted)
            ClearHeld();
        else if (_buffer.Count == 0 && _pendingResign is null)
            _gapSince = null;
        return _session.Result;
    }

    private void ClearHeld()
    {
        _buffer.Clear();
        _pendingResign = null;
        _gapSince = null;
    }

    private static ExchangeStep Flag(int turn, string reason, string eventId) =>
        new(ReceiveOutcome.Flagged, [], [new ExchangeFlag(turn, reason, eventId)]);

    private static string Digest(SignedGameAction action) => Convert.ToHexStringLower(ActionDigest.Compute(action));
}

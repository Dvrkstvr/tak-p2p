using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Cryptography;
using TakEngine.Core.Serialization;
using TakEngine.Crypto;
using TakEngine.Multiplayer.Handshake;
using TakEngine.Transport.Matchmaking;

namespace TakEngine.Multiplayer;

/// <summary>An action found on the relays during a cold rebuild, already through tamper rows 1-4.</summary>
public sealed record HistoryEntry(SignedGameAction Action, string EventId, string EventJson);

/// <summary>A rebuilt game ready to store, or why it can't be rebuilt (then nothing is stored).</summary>
/// <param name="Outbox">Our own events found on the relays (stored as sent: an ACCEPT to resend on a retried JOIN, our last move).</param>
/// <param name="Flags">The opponent's events the rebuild dropped (tamper rows 5-7), for the caller to store as flags.</param>
public sealed record RebuildResult(
    NetGameRecord? Record,
    IReadOnlyList<AppliedAction> Applied,
    ExchangeProgress? Progress,
    IReadOnlyList<OutboxEntry> Outbox,
    string? Error,
    IReadOnlyList<RebuildFlag> Flags)
{
    public static RebuildResult Fail(string error) => new(null, [], null, [], error, []);
}

/// <summary>An event a cold rebuild dropped. Pure, so no time: the caller stamps it when it stores a <see cref="FlagEntry"/>.</summary>
public sealed record RebuildFlag(int Turn, string Reason, string EventId, string Author);

/// <summary>
/// Cold rebuild (F-037, scope.md "Reconnect and catch-up", D-045): a device that has its key and the invite but lost its game state
/// rebuilds the game from relay history. The handshake is replayed from our own JOIN (guest nonce) or ACCEPT (host nonce), with the
/// same checks as a live handshake; then every MOVE is replayed in turn order through the rules and the hash chain. A missing turn
/// with later turns present, or a turn that doesn't chain, stops the rebuild: nothing is guessed or skipped. Pure.
/// <para>
/// History comes in relay order, which proves nothing, so every choice below is made on content and never on order (M0-security
/// finding 3). Several JOINs from the guest (a reload, a second press of Join): the one the stored ACCEPT answered. Several distinct
/// signed moves for one turn (equivocation): the one the next turn's move chains from, since that move was signed on top of the one
/// its author applied; with no next move, the lowest event id. The others are dropped and flagged, never a reason to fail.
/// </para>
/// </summary>
public static class RebuildPlan
{
    public static RebuildResult Build(InviteCode invite, PublicKey me, IReadOnlyList<HistoryEntry> history)
    {
        ArgumentNullException.ThrowIfNull(invite);
        ArgumentNullException.ThrowIfNull(me);
        ArgumentNullException.ThrowIfNull(history);
        string meHex = me.ToHex();
        bool host = string.Equals(invite.HostPubKey, meHex, StringComparison.OrdinalIgnoreCase);
        history = [.. history.OrderBy(h => h.EventId, StringComparer.Ordinal)];

        NetGameRecord? record = host ? HostRecord(invite, me, history, out string? error) : GuestRecord(invite, me, history, out error);
        if (record is null)
            return RebuildResult.Fail(error!);

        var outbox = history.Where(h => h.Action.PlayerPubKey == meHex && h.Action.ActionType != GameActionType.Move
                && (h.Action.ActionType != GameActionType.Join || NonceOf(h) == record.GuestNonce)) // not a JOIN the host didn't answer
            .GroupBy(h => h.EventId).Select(g => g.First())
            .Select(h => new OutboxEntry(h.EventId, h.Action.ActionType, h.Action.Turn, h.EventJson, Sent: true))
            .ToList();
        return Replay(record, meHex, history, outbox);
    }

    private static string? NonceOf(HistoryEntry join) =>
        HandshakeMessages.ReadJoin(join.Action) is { } terms ? Convert.ToHexStringLower(terms.GuestNonce) : null;

    private static NetGameRecord? GuestRecord(InviteCode invite, PublicKey me, IReadOnlyList<HistoryEntry> history, out string? error)
    {
        error = null;
        string meHex = me.ToHex();
        var nonces = history.Where(h => h.Action.ActionType == GameActionType.Join && h.Action.PlayerPubKey == meHex)
            .Select(NonceOf).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (nonces.Count == 0)
        {
            error = "Your join for this game is not on any relay, so the game can't be rebuilt.";
            return null;
        }
        var accepts = history.Where(h => h.Action.ActionType == GameActionType.Accept).ToList();
        string? failure = null;
        // Each JOIN against each ACCEPT on a fresh handshake: the ACCEPT's colours only agree with the nonce it answered.
        foreach (string nonce in nonces)
        {
            foreach (HistoryEntry accept in accepts)
            {
                GuestStart start = GuestHandshake.Start(invite.ToCompactCode(), me, Convert.FromHexString(nonce), []);
                if (start.Handshake is null)
                {
                    error = start.Error;
                    return null;
                }
                GuestDecision d = start.Handshake.OnAccept(accept.Action);
                if (d.Kind == GuestDecisionKind.Start)
                    return start.Handshake.Record;
                if (d.Kind == GuestDecisionKind.Fail)
                    failure ??= d.Message;
            }
        }
        error = failure ?? "The host's answer to your join is not on any relay, so the game can't be rebuilt.";
        return null;
    }

    private static NetGameRecord? HostRecord(InviteCode invite, PublicKey me, IReadOnlyList<HistoryEntry> history, out string? error)
    {
        error = null;
        string meHex = me.ToHex();
        HistoryEntry? accept = history.FirstOrDefault(h => h.Action.ActionType == GameActionType.Accept && h.Action.PlayerPubKey == meHex);
        if (accept is null || HandshakeMessages.ReadAccept(accept.Action) is not { } terms)
        {
            error = "Your answer to a guest is not on any relay (no one joined, or the relays dropped it), so the game can't be rebuilt.";
            return null;
        }
        if (!ColorSeed.Verify(invite.SeedCommit, terms.HostNonce))
        {
            error = "Your stored answer does not match this invite, so the game can't be rebuilt.";
            return null;
        }
        string guestKey = terms.White == meHex ? terms.Black : terms.White;
        var joins = history.Where(h => h.Action.ActionType == GameActionType.Join && h.Action.PlayerPubKey == guestKey).ToList();
        if (joins.Count == 0)
        {
            error = "Your guest's join is not on any relay, so the game can't be rebuilt.";
            return null;
        }
        // The guest may have joined more than once with new nonces: keep the JOIN whose answer, recomputed, is our stored ACCEPT.
        byte[] stored = ActionDigest.Compute(accept.Action);
        foreach (HistoryEntry join in joins)
        {
            HostHandshake handshake = HostHandshake.Create(invite.GameId, me, invite.BoardSize, invite.KomiHalves, terms.HostNonce,
                invite.Relays, invite.HostNickname);
            HostDecision d = handshake.OnJoin(join.Action);
            if (d.Kind == HostDecisionKind.Accept && ActionDigest.Compute(d.Accept!).AsSpan().SequenceEqual(stored))
                return handshake.Record;
        }
        error = "The stored join and answer don't agree, so the game can't be rebuilt.";
        return null;
    }

    /// <summary>One candidate move for a turn, tried on a copy of the board: the result, or why it can't be applied.</summary>
    private sealed record Step(HistoryEntry Entry, GameBoard? Board, string? Hash, string? Tps, string? Error, string? FlagReason);

    private static RebuildResult Replay(NetGameRecord record, string meHex, IReadOnlyList<HistoryEntry> history, List<OutboxEntry> outbox)
    {
        var board = new GameBoard(record.Size, record.KomiHalves);
        string hash = StateHasher.ComputeGenesisHash(record.Size);
        string white = record.MyColor == PlayerColor.White ? meHex : record.OpponentPubKey!;
        string black = record.MyColor == PlayerColor.White ? record.OpponentPubKey! : meHex;
        var moves = history.Where(h => h.Action.ActionType == GameActionType.Move && h.Action.Turn >= 1
            && (h.Action.PlayerPubKey == white || h.Action.PlayerPubKey == black)).ToList();
        var applied = new List<AppliedAction>();
        var flags = new List<RebuildFlag>();
        int publishedTurn = 0;

        for (int turn = 1; board.Result is null; turn++)
        {
            var candidates = Candidates(moves, turn, white, black);
            if (candidates.Count == 0)
            {
                // A later move of ours means we played on past the gap: stopping here would have us sign a turn again. Later moves
                // that are only the opponent's prove nothing (one signed far-future move must not block every rebuild): wait at the gap.
                if (moves.Any(m => m.Action.Turn > turn && m.Action.PlayerPubKey == meHex && ActionDigest.Verify(m.Action)))
                    return RebuildResult.Fail($"move {turn} not found on any relay, but later moves are: the game can't be rebuilt without guessing.");
                break;
            }

            var steps = candidates.Select(c => Try(board, hash, turn, c)).ToList();
            var valid = steps.Where(s => s.Error is null).ToList();
            if (valid.Count == 0)
                return RebuildResult.Fail(steps[0].Error!);

            // The next turn's move was signed on top of the move its author applied: that is the one the game went on from.
            var pins = Candidates(moves, turn + 1, white, black).Select(m => m.Action.PrevStateHash).ToHashSet(StringComparer.Ordinal);
            Step chosen = valid.FirstOrDefault(s => pins.Contains(s.Hash!)) ?? valid[0];
            foreach (Step other in steps.Where(s => s != chosen))
            {
                flags.Add(new RebuildFlag(turn, other.FlagReason ?? $"conflicting move for turn {turn}", other.Entry.EventId,
                    other.Entry.Action.PlayerPubKey));
            }

            SignedGameAction a = chosen.Entry.Action;
            board = chosen.Board!;
            hash = chosen.Hash!;
            applied.Add(new AppliedAction(a, chosen.Entry.EventId, chosen.Tps!, hash));
            if (a.PlayerPubKey == meHex)
            {
                publishedTurn = turn;
                outbox.Add(new OutboxEntry(chosen.Entry.EventId, GameActionType.Move, turn, chosen.Entry.EventJson, Sent: true));
            }
        }

        if (board.Result is null && history.FirstOrDefault(h => h.Action.ActionType == GameActionType.Resign
                && (h.Action.PlayerPubKey == white || h.Action.PlayerPubKey == black) && ActionDigest.Verify(h.Action)
                && h.Action.Turn <= applied.Count + 1) is { } resign)
        {
            board.Resign(resign.Action.PlayerPubKey == white ? PlayerColor.White : PlayerColor.Black);
        }

        NetGameRecord rebuilt = record with
        {
            State = board.Result is null ? HandshakeState.InGame : HandshakeState.Completed,
            Result = board.Result,
            Note = "rebuilt from relays",
        };
        var progress = new ExchangeProgress(applied.Count, hash, TpsSerializer.Serialize(board), publishedTurn, 0);
        return new RebuildResult(rebuilt, applied, progress, outbox, null, flags);
    }

    /// <summary>
    /// The turn owner's validly signed moves for <paramref name="turn"/>, one per distinct content (a re-signed republish is the same
    /// move, not an equivocation), lowest event id first. <paramref name="moves"/> is already in event id order.
    /// </summary>
    private static List<HistoryEntry> Candidates(List<HistoryEntry> moves, int turn, string white, string black)
    {
        string owner = TurnExchange.TurnOwner(turn) == PlayerColor.White ? white : black;
        return [.. moves.Where(m => m.Action.Turn == turn && m.Action.PlayerPubKey == owner && ActionDigest.Verify(m.Action))
            .DistinctBy(m => Convert.ToHexString(ActionDigest.Compute(m.Action)))];
    }

    private static Step Try(GameBoard board, string hash, int turn, HistoryEntry entry)
    {
        SignedGameAction a = entry.Action;
        if (!string.Equals(a.PrevStateHash, hash, StringComparison.Ordinal))
            return new Step(entry, null, null, null, $"fork: move {turn} does not follow from move {turn - 1} on the relays.", $"hash chain mismatch at turn {turn}");
        if (a.Ptn is not { } ptn || a.ActionData.Count != 1)
            return new Step(entry, null, null, null, $"fork: move {turn} on the relays is malformed.", $"malformed move for turn {turn}");
        GameBoard next = board.Clone();
        CommandResult executed;
        try
        {
            TakMove move = PtnParser.ParseMove(ptn);
            executed = move.ToPtn() == ptn ? next.Execute(move) : CommandResult.Fail("not canonical PTN");
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            executed = CommandResult.Fail(ex.Message);
        }
        if (!executed.IsSuccess)
            return new Step(entry, null, null, null, $"fork: move {turn} on the relays is illegal: {executed.ErrorMessage}", $"illegal move for turn {turn}: {executed.ErrorMessage}");

        string tps = TpsSerializer.Serialize(next);
        return new Step(entry, next, StateHasher.ComputeStateHash(hash, turn, a.PlayerPubKey, ptn, tps), tps, null, null);
    }
}

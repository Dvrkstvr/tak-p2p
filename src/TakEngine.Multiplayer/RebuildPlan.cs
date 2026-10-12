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
public sealed record RebuildResult(
    NetGameRecord? Record,
    IReadOnlyList<AppliedAction> Applied,
    ExchangeProgress? Progress,
    IReadOnlyList<OutboxEntry> Outbox,
    string? Error)
{
    public static RebuildResult Fail(string error) => new(null, [], null, [], error);
}

/// <summary>
/// Cold rebuild (F-037, scope.md "Reconnect and catch-up", D-045): a device that has its key and the invite but lost its game state
/// rebuilds the game from relay history. The handshake is replayed from our own JOIN (guest nonce) or ACCEPT (host nonce), with the
/// same checks as a live handshake; then every MOVE is replayed in turn order through the rules and the hash chain. A missing turn
/// with later turns present, or a turn that doesn't chain, stops the rebuild: nothing is guessed or skipped. Pure.
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

        NetGameRecord? record = host ? HostRecord(invite, me, history, out string? error) : GuestRecord(invite, me, history, out error);
        if (record is null)
            return RebuildResult.Fail(error!);

        var outbox = history.Where(h => h.Action.PlayerPubKey == meHex && h.Action.ActionType != GameActionType.Move)
            .GroupBy(h => h.EventId).Select(g => g.First())
            .Select(h => new OutboxEntry(h.EventId, h.Action.ActionType, h.Action.Turn, h.EventJson, Sent: true))
            .ToList();
        return Replay(record, meHex, history, outbox);
    }

    private static NetGameRecord? GuestRecord(InviteCode invite, PublicKey me, IReadOnlyList<HistoryEntry> history, out string? error)
    {
        error = null;
        HistoryEntry? join = history.FirstOrDefault(h => h.Action.ActionType == GameActionType.Join && h.Action.PlayerPubKey == me.ToHex());
        if (join is null || HandshakeMessages.ReadJoin(join.Action) is not { } terms)
        {
            error = "Your join for this game is not on any relay, so the game can't be rebuilt.";
            return null;
        }
        GuestStart start = GuestHandshake.Start(invite.ToCompactCode(), me, terms.GuestNonce, []);
        if (start.Handshake is null)
        {
            error = start.Error;
            return null;
        }
        GuestHandshake guest = start.Handshake;
        foreach (HistoryEntry accept in history.Where(h => h.Action.ActionType == GameActionType.Accept))
        {
            GuestDecision d = guest.OnAccept(accept.Action);
            if (d.Kind == GuestDecisionKind.Start)
                return guest.Record;
            if (d.Kind == GuestDecisionKind.Fail)
            {
                error = d.Message;
                return null;
            }
        }
        error = "The host's answer to your join is not on any relay, so the game can't be rebuilt.";
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
        HistoryEntry? join = history.FirstOrDefault(h => h.Action.ActionType == GameActionType.Join && h.Action.PlayerPubKey == guestKey);
        if (join is null)
        {
            error = "Your guest's join is not on any relay, so the game can't be rebuilt.";
            return null;
        }
        HostHandshake handshake = HostHandshake.Create(invite.GameId, me, invite.BoardSize, invite.KomiHalves, terms.HostNonce,
            invite.Relays, invite.HostNickname);
        HostDecision d = handshake.OnJoin(join.Action);
        if (d.Kind != HostDecisionKind.Accept || ActionDigest.Compute(d.Accept!).AsSpan().SequenceEqual(ActionDigest.Compute(accept.Action with { Signature = "" })) is false)
        {
            error = "The stored join and answer don't agree, so the game can't be rebuilt.";
            return null;
        }
        return handshake.Record;
    }

    private static RebuildResult Replay(NetGameRecord record, string meHex, IReadOnlyList<HistoryEntry> history, List<OutboxEntry> outbox)
    {
        var board = new GameBoard(record.Size, record.KomiHalves);
        string hash = StateHasher.ComputeGenesisHash(record.Size);
        string white = record.MyColor == PlayerColor.White ? meHex : record.OpponentPubKey!;
        string black = record.MyColor == PlayerColor.White ? record.OpponentPubKey! : meHex;
        var moves = history.Where(h => h.Action.ActionType == GameActionType.Move && h.Action.Turn >= 1
            && (h.Action.PlayerPubKey == white || h.Action.PlayerPubKey == black)).ToList();
        var applied = new List<AppliedAction>();
        int publishedTurn = 0;

        for (int turn = 1; board.Result is null; turn++)
        {
            string owner = TurnExchange.TurnOwner(turn) == PlayerColor.White ? white : black;
            HistoryEntry? entry = moves.FirstOrDefault(m => m.Action.Turn == turn && m.Action.PlayerPubKey == owner && ActionDigest.Verify(m.Action));
            if (entry is null)
            {
                // A later move of ours means we played on past the gap: stopping here would have us sign a turn again. Later moves
                // that are only the opponent's prove nothing (one signed far-future move must not block every rebuild): wait at the gap.
                if (moves.Any(m => m.Action.Turn > turn && m.Action.PlayerPubKey == meHex && ActionDigest.Verify(m.Action)))
                    return RebuildResult.Fail($"move {turn} not found on any relay, but later moves are: the game can't be rebuilt without guessing.");
                break;
            }
            SignedGameAction a = entry.Action;
            if (!string.Equals(a.PrevStateHash, hash, StringComparison.Ordinal))
                return RebuildResult.Fail($"fork: move {turn} does not follow from move {turn - 1} on the relays.");
            if (a.Ptn is not { } ptn || a.ActionData.Count != 1)
                return RebuildResult.Fail($"fork: move {turn} on the relays is malformed.");
            CommandResult executed;
            try
            {
                TakMove move = PtnParser.ParseMove(ptn);
                executed = move.ToPtn() == ptn ? board.Execute(move) : CommandResult.Fail("not canonical PTN");
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
            {
                executed = CommandResult.Fail(ex.Message);
            }
            if (!executed.IsSuccess)
                return RebuildResult.Fail($"fork: move {turn} on the relays is illegal: {executed.ErrorMessage}");

            string tps = TpsSerializer.Serialize(board);
            hash = StateHasher.ComputeStateHash(hash, turn, a.PlayerPubKey, ptn, tps);
            applied.Add(new AppliedAction(a, entry.EventId, tps, hash));
            if (a.PlayerPubKey == meHex)
            {
                publishedTurn = turn;
                outbox.Add(new OutboxEntry(entry.EventId, GameActionType.Move, turn, entry.EventJson, Sent: true));
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
        return new RebuildResult(rebuilt, applied, progress, outbox, null);
    }
}

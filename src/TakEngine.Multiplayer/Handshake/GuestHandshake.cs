using TakEngine.Abstractions;
using TakEngine.Crypto;
using TakEngine.Transport.Matchmaking;

namespace TakEngine.Multiplayer.Handshake;

public enum GuestDecisionKind
{
    /// <summary>The ACCEPT checks out: the game starts with <see cref="GuestHandshake.Record"/>'s colour.</summary>
    Start,

    /// <summary>Not acted on (a repeated ACCEPT, anything after the handshake ended).</summary>
    Ignore,

    /// <summary>Dropped and flagged: an ACCEPT signed by someone who is not the host.</summary>
    DropAndFlag,

    /// <summary>The handshake failed with <see cref="GuestDecision.Message"/>; nothing is played.</summary>
    Fail,
}

public sealed record GuestDecision(GuestDecisionKind Kind, string? Message = null);

/// <summary>The guest's first step: either a handshake to run, or the reason the invite can't be used (and no traffic was sent).</summary>
public sealed record GuestStart(GuestHandshake? Handshake, string? Error);

/// <summary>
/// The guest's side of the invite (F-017): Idle -> InviteParsed -> Joining -> InGame | Failed. An invite that fails validation
/// fails before any network traffic. The guest checks the ACCEPT's author, the host nonce against the invite's commitment, size
/// and komi against the invite, and the colours against its own draw. Pure: the nonce is passed in, decisions come out.
/// </summary>
public sealed class GuestHandshake
{
    /// <summary>JOIN goes out at 0 s, then again at 5, 15 and 45 s, then every 60 s until an ACCEPT arrives.</summary>
    private static readonly int[] FirstRepublishes = [5, 15, 45];

    private GuestHandshake(NetGameRecord record, InviteCode invite)
    {
        Record = record;
        Invite = invite;
    }

    public NetGameRecord Record { get; private set; }

    public InviteCode Invite { get; }

    public HandshakeState State => Record.State;

    /// <summary>The JOIN to sign and publish (and republish verbatim on the schedule).</summary>
    public SignedGameAction UnsignedJoin =>
        HandshakeMessages.UnsignedJoin(Record.GameId, Record.MyPubKey, Record.Size, Convert.FromHexString(Record.GuestNonce!), Record.SeedCommit);

    /// <param name="defaultRelays">Added after the invite's relay hints.</param>
    public static GuestStart Start(string inviteText, PublicKey me, byte[] guestNonce, IReadOnlyList<string> defaultRelays)
    {
        ArgumentNullException.ThrowIfNull(me);
        ArgumentNullException.ThrowIfNull(defaultRelays);
        if (!InviteCode.TryParse(inviteText, out InviteCode? invite, out string? error))
            return new GuestStart(null, error);
        if (invite!.CheckJoinableBy(me) is { } own)
            return new GuestStart(null, own);
        if (guestNonce is null || guestNonce.Length != ColorSeed.NonceLength)
            throw new ArgumentException("The guest nonce is 32 bytes.", nameof(guestNonce));

        var relays = invite.Relays.Concat(defaultRelays).Distinct(StringComparer.Ordinal).ToList();
        var record = new NetGameRecord(
            invite.GameId, NetRole.Guest, me.ToHex(), invite.HostPubKey.ToLowerInvariant(), invite.HostPubKey.ToLowerInvariant(),
            invite.BoardSize, invite.KomiHalves, invite.SeedCommit.ToLowerInvariant(), null, Convert.ToHexStringLower(guestNonce),
            relays, HandshakeState.Joining);
        return new GuestStart(new GuestHandshake(record, invite), null);
    }

    /// <summary>After a restart: carries on from the stored record.</summary>
    public static GuestHandshake Restore(NetGameRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Role != NetRole.Guest || record.GuestNonce is null)
            throw new ArgumentException("Not a guest's game record.", nameof(record));
        return new GuestHandshake(record, new InviteCode(record.GameId, record.HostPubKey, record.Size, record.KomiHalves, record.Relays, record.SeedCommit));
    }

    /// <summary>When to send the JOIN again, given when it was first sent and how many times it has gone out so far.</summary>
    public static DateTimeOffset NextJoinAt(DateTimeOffset firstSentAt, int timesSent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timesSent, 1);
        int seconds = timesSent <= FirstRepublishes.Length
            ? FirstRepublishes[timesSent - 1]
            : FirstRepublishes[^1] + 60 * (timesSent - FirstRepublishes.Length);
        return firstSentAt.AddSeconds(seconds);
    }

    /// <summary>An ACCEPT that passed tamper rows 1-4 for this game.</summary>
    public GuestDecision OnAccept(SignedGameAction accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        if (Record.State != HandshakeState.Joining)
            return new GuestDecision(GuestDecisionKind.Ignore);
        if (!string.Equals(accept.PlayerPubKey, Record.HostPubKey, StringComparison.Ordinal))
            return new GuestDecision(GuestDecisionKind.DropAndFlag, "ACCEPT from a key that is not the host's");
        if (HandshakeMessages.ReadAccept(accept) is not { } terms)
            return Fail("The host's answer is malformed.");
        if (!ColorSeed.Verify(Record.SeedCommit, terms.HostNonce))
            return Fail("The host's colour nonce does not match its invite, so the colours could have been chosen. Not playing this game.");
        if (terms.Size != Record.Size)
            return Fail($"The host answered with board size {(int)terms.Size}, but the invite says {(int)Record.Size}.");
        if (terms.KomiHalves != Record.KomiHalves)
            return Fail($"The host answered with komi {terms.KomiHalves}, but the invite says {Record.KomiHalves} (half flats).");

        string seed = ColorSeed.Derive(terms.HostNonce, Convert.FromHexString(Record.GuestNonce!));
        var (hostColor, myColor) = ColorResolver.ResolveColors(seed, Record.HostPubKey, Record.MyPubKey);
        string white = hostColor == PlayerColor.White ? Record.HostPubKey : Record.MyPubKey;
        string black = hostColor == PlayerColor.White ? Record.MyPubKey : Record.HostPubKey;
        if (!string.Equals(terms.White, white, StringComparison.Ordinal) || !string.Equals(terms.Black, black, StringComparison.Ordinal))
            return Fail("host and guest disagree on colours");

        Record = Record with { HostNonce = Convert.ToHexStringLower(terms.HostNonce), MyColor = myColor, State = HandshakeState.InGame };
        return new GuestDecision(GuestDecisionKind.Start);
    }

    /// <summary>The user cancels while joining: Failed locally; a JOIN that already reached the host still locks its game.</summary>
    public bool Cancel()
    {
        if (Record.State != HandshakeState.Joining)
            return false;
        Record = Record with { State = HandshakeState.Cancelled, Note = "join cancelled" };
        return true;
    }

    private GuestDecision Fail(string message)
    {
        Record = Record with { State = HandshakeState.Failed, Note = message };
        return new GuestDecision(GuestDecisionKind.Fail, message);
    }
}

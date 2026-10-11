using TakEngine.Abstractions;
using TakEngine.Crypto;
using TakEngine.Transport.Matchmaking;

namespace TakEngine.Multiplayer.Handshake;

public enum HostDecisionKind
{
    /// <summary>The first valid JOIN locked the game: sign and publish <see cref="HostDecision.Accept"/>, save the record.</summary>
    Accept,

    /// <summary>The locked guest retried its JOIN: republish the stored ACCEPT verbatim.</summary>
    ResendAccept,

    /// <summary>Not acted on; <see cref="HostDecision.Message"/> says why when the user should know (a second guest).</summary>
    Ignore,
}

/// <param name="Accept">The unsigned ACCEPT to sign, for <see cref="HostDecisionKind.Accept"/>.</param>
public sealed record HostDecision(HostDecisionKind Kind, SignedGameAction? Accept = null, string? Message = null);

/// <summary>
/// The host's side of the invite (F-017; scope.md "Invite create and accept handshake"): Idle -> Inviting -> Locked(G) -> InGame,
/// or Cancelled. Locking and accepting happen in one step, so the stored states are Inviting, InGame and Cancelled. The host
/// locks to the first JOIN's key and nonce; a retried JOIN gets the same ACCEPT. Pure: the nonce is passed in, decisions come out.
/// </summary>
public sealed class HostHandshake
{
    private HostHandshake(NetGameRecord record) => Record = record;

    public NetGameRecord Record { get; private set; }

    public HandshakeState State => Record.State;

    /// <summary>Idle -> Inviting: the record to save and the invite to show. Throws <see cref="InviteFormatException"/> for a size
    /// or komi out of range (nothing is saved or sent).</summary>
    public static HostHandshake Create(
        Guid gameId,
        PublicKey host,
        BoardSize size,
        int komiHalves,
        byte[] hostNonce,
        IReadOnlyList<string> relays,
        string? nickname = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(relays);
        var invite = new InviteCode(gameId, host.ToHex(), size, komiHalves, relays, ColorSeed.Commit(hostNonce), nickname);
        invite.Validate();
        var record = new NetGameRecord(
            gameId, NetRole.Host, host.ToHex(), host.ToHex(), null, size, komiHalves, invite.SeedCommit,
            Convert.ToHexStringLower(hostNonce), null, relays, HandshakeState.Inviting);
        return new HostHandshake(record) { Invite = invite };
    }

    /// <summary>After a restart: carries on from the stored record (Cancelled stays dead).</summary>
    public static HostHandshake Restore(NetGameRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Role != NetRole.Host || record.HostNonce is null)
            throw new ArgumentException("Not a host's game record.", nameof(record));
        var invite = new InviteCode(record.GameId, record.HostPubKey, record.Size, record.KomiHalves, record.Relays, record.SeedCommit);
        return new HostHandshake(record) { Invite = invite };
    }

    public InviteCode Invite { get; private init; } = null!;

    /// <summary>A JOIN that passed tamper rows 1-4 (any author but us while inviting).</summary>
    public HostDecision OnJoin(SignedGameAction join)
    {
        ArgumentNullException.ThrowIfNull(join);
        string author = join.PlayerPubKey;
        switch (Record.State)
        {
            case HandshakeState.Inviting:
                if (string.Equals(author, Record.MyPubKey, StringComparison.Ordinal))
                    return new HostDecision(HostDecisionKind.Ignore);
                if (HandshakeMessages.ReadJoin(join) is not { } terms)
                    return new HostDecision(HostDecisionKind.Ignore, Message: "ignored a malformed join");
                if (!ColorSeed.VerifyJoinProof(terms.InviteProof, Record.SeedCommit, author))
                    return new HostDecision(HostDecisionKind.Ignore, Message: $"ignored join from {Npub(author)}: it does not match this invite");
                return Lock(author, terms.GuestNonce);

            case HandshakeState.InGame or HandshakeState.Completed:
                if (string.Equals(author, Record.OpponentPubKey, StringComparison.Ordinal))
                    return new HostDecision(HostDecisionKind.ResendAccept);
                return new HostDecision(HostDecisionKind.Ignore, Message: $"ignored join from {Npub(author)}: game already taken");

            default:
                return new HostDecision(HostDecisionKind.Ignore);
        }
    }

    /// <summary>Cancel before a guest joined: Cancelled, nothing published. Returns false once a guest is locked in (resign instead).</summary>
    public bool Cancel()
    {
        if (Record.State != HandshakeState.Inviting)
            return false;
        Record = Record with { State = HandshakeState.Cancelled, Note = "invite cancelled" };
        return true;
    }

    private HostDecision Lock(string guest, byte[] guestNonce)
    {
        byte[] hostNonce = Convert.FromHexString(Record.HostNonce!);
        var (hostColor, _) = ColorResolver.ResolveColors(ColorSeed.Derive(hostNonce, guestNonce), Record.HostPubKey, guest);
        string white = hostColor == PlayerColor.White ? Record.HostPubKey : guest;
        string black = hostColor == PlayerColor.White ? guest : Record.HostPubKey;
        Record = Record with
        {
            OpponentPubKey = guest,
            GuestNonce = Convert.ToHexStringLower(guestNonce),
            State = HandshakeState.InGame,
            MyColor = hostColor,
        };
        var accept = HandshakeMessages.UnsignedAccept(Record.GameId, Record.HostPubKey,
            new AcceptTerms(white, black, hostNonce, Record.Size, Record.KomiHalves));
        return new HostDecision(HostDecisionKind.Accept, accept);
    }

    private static string Npub(string hex) => PublicKey.TryFromHex(hex, out PublicKey? key) ? key.ToNpub() : hex;
}

using TakEngine.Abstractions;
using TakEngine.Crypto;
using TakEngine.Transport.Envelope;
using TakEngine.Transport.Nostr;

namespace TakEngine.Testing;

/// <summary>What <see cref="TamperForge.Forge"/> builds: one case per tamper row a real relay would carry (scope.md rows 3-7).</summary>
public enum ForgeKind
{
    /// <summary>A correctly signed move the rules refuse (row 6).</summary>
    Illegal,

    /// <summary>A correctly signed move whose previous hash is not the game's (row 5).</summary>
    Fork,

    /// <summary>A different, correctly signed move for a turn already played (row 7); the caller passes that turn and its prev hash.</summary>
    Equivocate,

    /// <summary>A validly signed event from the author whose content the recipient can't decrypt (row 3).</summary>
    Undecryptable,

    /// <summary>A decryptable envelope whose inner action signature is wrong (row 4).</summary>
    BadEnvelopeSig,
}

/// <summary>
/// Builds tampered game events with the real <see cref="EnvelopeCodec"/>, for the tamper matrix and <c>tools/TakTestPeer</c>. Every
/// event is a valid Nostr event signed by <c>author</c> (relays accept it); what is wrong is inside. Test code only: never ships.
/// </summary>
public static class TamperForge
{
    public static NostrEvent Forge(ForgeKind kind, SecretKey author, PublicKey recipient, Guid gameId, int turn, string prevStateHash,
        string ptn, long createdAt, Func<byte[]> random32)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(random32);
        string prev = kind == ForgeKind.Fork ? new string('0', 64) : prevStateHash;
        SignedGameAction action = ActionDigest.Sign(author,
            SignedGameAction.UnsignedMove(gameId, turn, author.PublicKey.ToHex(), prev, ptn), random32());

        switch (kind)
        {
            case ForgeKind.Undecryptable:
                {
                    // Encrypted to someone else: the recipient's conversation key fails the MAC.
                    PublicKey elsewhere = SecretKey.FromBytes(random32()).PublicKey;
                    string content = Nip44.Encrypt(EnvelopeCodec.ToPlaintext(action), Nip44.ConversationKey(author, elsewhere), random32());
                    return NostrEvents.Sign(author, createdAt, EnvelopeCodec.Kind, Tags(recipient, gameId), content, random32());
                }
            case ForgeKind.BadEnvelopeSig:
                {
                    char last = action.Signature[^1];
                    action = action with { Signature = action.Signature[..^1] + (last == '0' ? '1' : '0') };
                    return EnvelopeCodec.Encode(author, recipient, action, createdAt, random32(), random32());
                }
            default:
                return EnvelopeCodec.Encode(author, recipient, action, createdAt, random32(), random32());
        }
    }

    /// <summary>The same event with one bit of its BIP-340 signature flipped (row 1; a real relay refuses it).</summary>
    public static NostrEvent WithBadSig(NostrEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        char first = evt.Sig[0];
        return new NostrEvent
        {
            Id = evt.Id,
            Pubkey = evt.Pubkey,
            CreatedAt = evt.CreatedAt,
            Kind = evt.Kind,
            Tags = evt.Tags,
            Content = evt.Content,
            Sig = (first == '0' ? '1' : '0') + evt.Sig[1..],
        };
    }

    private static List<IReadOnlyList<string>> Tags(PublicKey recipient, Guid gameId) =>
        [new[] { "p", recipient.ToHex() }, new[] { "g", gameId.ToString("D") }];
}

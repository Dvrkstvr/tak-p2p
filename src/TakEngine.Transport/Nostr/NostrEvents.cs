using System.Buffers;
using TakEngine.Crypto;

namespace TakEngine.Transport.Nostr;

/// <summary>
/// Signing and verification of Nostr events (NIP-01 id + BIP-340 sig). Pure: <c>created_at</c> and the BIP-340 aux
/// randomness are arguments. Every received event goes through <see cref="Verify"/> before anything else is done with it.
/// </summary>
public static class NostrEvents
{
    /// <summary>Builds a signed event: pubkey = <paramref name="key"/>'s x-only hex, id = NIP-01 id, sig = BIP-340 over the id.</summary>
    public static NostrEvent Sign(
        SecretKey key,
        long createdAt,
        int kind,
        IReadOnlyList<IReadOnlyList<string>> tags,
        string content,
        ReadOnlySpan<byte> aux32)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(content);

        string pubkey = key.PublicKey.ToHex();
        byte[] id = Nip01Serializer.ComputeIdBytes(pubkey, createdAt, kind, tags, content);
        byte[] sig = Schnorr.Sign(key, id, aux32);
        return new NostrEvent
        {
            Id = Convert.ToHexStringLower(id),
            Pubkey = pubkey,
            CreatedAt = createdAt,
            Kind = kind,
            Tags = tags.Select(tag => tag.ToList()).ToList(),
            Content = content,
            Sig = Convert.ToHexStringLower(sig),
        };
    }

    /// <summary>
    /// True only if <c>id</c> is the NIP-01 id of the event's fields (64 lowercase hex), <c>pubkey</c> is a valid 32-byte
    /// x-only key in lowercase hex, and <c>sig</c> is a valid BIP-340 signature over the id. Never throws: relay junk returns false.
    /// </summary>
    public static bool Verify(NostrEvent? evt)
    {
        if (evt is null || evt.Id is null || evt.Pubkey is null || evt.Sig is null || evt.Content is null || evt.Tags is null)
            return false;
        if (evt.Tags.Any(tag => tag is null || tag.Any(value => value is null)))
            return false;
        if (!PublicKey.TryFromHex(evt.Pubkey, out PublicKey? author) || !string.Equals(author.ToHex(), evt.Pubkey, StringComparison.Ordinal))
            return false; // not a valid 32-byte x-only key in lowercase hex (NIP-01)

        byte[] id = Nip01Serializer.ComputeIdBytes(evt.Pubkey, evt.CreatedAt, evt.Kind, evt.Tags, evt.Content);
        if (!string.Equals(Convert.ToHexStringLower(id), evt.Id, StringComparison.Ordinal))
            return false;

        byte[] sig = new byte[Schnorr.SignatureLength];
        if (evt.Sig.Length != Schnorr.SignatureLength * 2 || Convert.FromHexString(evt.Sig, sig, out _, out _) != OperationStatus.Done)
            return false;
        return Schnorr.Verify(author, id, sig);
    }
}

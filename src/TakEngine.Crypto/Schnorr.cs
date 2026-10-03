using NBitcoin.Secp256k1;

namespace TakEngine.Crypto;

/// <summary>BIP-340 Schnorr signatures over 32-byte digests (Nostr event ids, game action digests).</summary>
public static class Schnorr
{
    public const int DigestLength = 32;
    public const int AuxLength = 32;
    public const int SignatureLength = 64;

    /// <summary>
    /// Signs a 32-byte digest. <paramref name="aux32"/> is BIP-340 auxiliary randomness supplied by the caller
    /// (shell: <c>RandomNumberGenerator.GetBytes(32)</c>; tests: fixed bytes so official vectors pin the output).
    /// </summary>
    public static byte[] Sign(SecretKey key, ReadOnlySpan<byte> digest32, ReadOnlySpan<byte> aux32)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (digest32.Length != DigestLength)
            throw new ArgumentException($"Digest must be {DigestLength} bytes, got {digest32.Length}.", nameof(digest32));
        if (aux32.Length != AuxLength)
            throw new ArgumentException($"Aux randomness must be {AuxLength} bytes, got {aux32.Length}.", nameof(aux32));

        return key.Inner.SignBIP340(digest32, aux32.ToArray()).ToBytes();
    }

    /// <summary>True only for a valid BIP-340 signature by <paramref name="key"/> over <paramref name="digest32"/>. Never throws on malformed input.</summary>
    public static bool Verify(PublicKey key, ReadOnlySpan<byte> digest32, ReadOnlySpan<byte> signature64)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (digest32.Length != DigestLength || signature64.Length != SignatureLength)
            return false;
        if (!SecpSchnorrSignature.TryCreate(signature64, out SecpSchnorrSignature? signature))
            return false;
        return key.XOnly.SigVerifyBIP340(signature!, digest32);
    }
}

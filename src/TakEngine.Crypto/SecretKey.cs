using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using NBitcoin.Secp256k1;

namespace TakEngine.Crypto;

/// <summary>
/// A player's secp256k1 secret (D-011): the one key behind the npub, BIP-340 event and action signatures and NIP-44.
/// Always valid: construction rejects anything that is not 32 bytes or is 0 or &gt;= the curve order n.
/// </summary>
public sealed class SecretKey
{
    public const int Length = 32;

    private readonly byte[] _bytes;

    private SecretKey(byte[] bytes, ECPrivKey inner)
    {
        _bytes = bytes;
        Inner = inner;
        PublicKey = PublicKey.FromBytes(inner.CreateXOnlyPubKey().ToBytes());
    }

    internal ECPrivKey Inner { get; }

    public PublicKey PublicKey { get; }

    public static SecretKey FromBytes(ReadOnlySpan<byte> secret32)
    {
        if (secret32.Length != Length)
            throw new InvalidKeyException($"Secret key must be {Length} bytes, got {secret32.Length}.");
        if (!ECPrivKey.TryCreate(secret32, out ECPrivKey? inner))
            throw new InvalidKeyException("Secret key is out of range (zero or not below the curve order).");
        return new SecretKey(secret32.ToArray(), inner!);
    }

    public static bool TryFromBytes(ReadOnlySpan<byte> secret32, [NotNullWhen(true)] out SecretKey? key)
    {
        try
        {
            key = FromBytes(secret32);
            return true;
        }
        catch (InvalidKeyException)
        {
            key = null;
            return false;
        }
    }

    /// <summary>Parses exactly 64 hex characters (either case).</summary>
    public static SecretKey FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length != Length * 2)
            throw new InvalidKeyException($"Secret key hex must be {Length * 2} characters, got {hex.Length}.");
        byte[] bytes = new byte[Length];
        if (Convert.FromHexString(hex, bytes, out _, out _) != OperationStatus.Done)
            throw new InvalidKeyException("Secret key is not valid hex.");
        return FromBytes(bytes);
    }

    /// <summary>Decodes an <c>nsec1…</c> string (NIP-19).</summary>
    public static SecretKey FromNsec(string nsec)
    {
        return FromBytes(Nip19.DecodeKey(nsec, Nip19.NsecPrefix));
    }

    /// <summary>
    /// Creates a key from caller-supplied randomness (pure: the shell passes
    /// <c>() =&gt; RandomNumberGenerator.GetBytes(32)</c>). Draws again in the negligible case of an out-of-range scalar.
    /// </summary>
    public static SecretKey Generate(Func<byte[]> random32)
    {
        ArgumentNullException.ThrowIfNull(random32);
        for (int attempt = 0; attempt < 64; attempt++)
        {
            if (TryFromBytes(random32(), out var key))
                return key;
        }
        throw new InvalidOperationException("The random source produced 64 invalid secret keys in a row.");
    }

    public byte[] ToBytes() => (byte[])_bytes.Clone();

    public string ToHex() => Convert.ToHexStringLower(_bytes);

    public string ToNsec() => Nip19.Encode(Nip19.NsecPrefix, _bytes);

    /// <summary>Never prints the secret.</summary>
    public override string ToString() => $"SecretKey(pub={PublicKey.ToHex()})";
}

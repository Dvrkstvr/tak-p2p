using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using NBitcoin.Secp256k1;

namespace TakEngine.Crypto;

/// <summary>
/// A player's 32-byte x-only secp256k1 public key (BIP-340 / Nostr "pubkey"). Always valid: construction rejects
/// any input that is not exactly 32 bytes or is not the x coordinate of a point on the curve.
/// </summary>
public sealed class PublicKey : IEquatable<PublicKey>
{
    public const int Length = 32;

    private readonly byte[] _bytes;

    private PublicKey(byte[] bytes, ECPubKey evenY)
    {
        _bytes = bytes;
        EvenY = evenY;
        XOnly = evenY.ToXOnlyPubKey();
    }

    internal ECXOnlyPubKey XOnly { get; }

    /// <summary>The full point with even y (BIP-340 lift_x); the NIP-44 ECDH multiplies this point.</summary>
    internal ECPubKey EvenY { get; }

    public static PublicKey FromBytes(ReadOnlySpan<byte> xOnly32)
    {
        if (xOnly32.Length != Length)
            throw new InvalidKeyException($"Public key must be {Length} bytes, got {xOnly32.Length}.");

        Span<byte> compressed = stackalloc byte[33];
        compressed[0] = 0x02;
        xOnly32.CopyTo(compressed[1..]);
        if (!ECPubKey.TryCreate(compressed, Context.Instance, out _, out ECPubKey? evenY))
            throw new InvalidKeyException("Public key is not on the secp256k1 curve.");

        return new PublicKey(xOnly32.ToArray(), evenY!);
    }

    public static bool TryFromBytes(ReadOnlySpan<byte> xOnly32, [NotNullWhen(true)] out PublicKey? key)
    {
        try
        {
            key = FromBytes(xOnly32);
            return true;
        }
        catch (InvalidKeyException)
        {
            key = null;
            return false;
        }
    }

    /// <summary>Parses exactly 64 hex characters (either case).</summary>
    public static PublicKey FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length != Length * 2)
            throw new InvalidKeyException($"Public key hex must be {Length * 2} characters, got {hex.Length}.");
        byte[] bytes = new byte[Length];
        if (Convert.FromHexString(hex, bytes, out _, out _) != OperationStatus.Done)
            throw new InvalidKeyException("Public key is not valid hex.");
        return FromBytes(bytes);
    }

    public static bool TryFromHex(string? hex, [NotNullWhen(true)] out PublicKey? key)
    {
        key = null;
        if (hex is null)
            return false;
        try
        {
            key = FromHex(hex);
            return true;
        }
        catch (InvalidKeyException)
        {
            return false;
        }
    }

    /// <summary>Decodes an <c>npub1…</c> string (NIP-19).</summary>
    public static PublicKey FromNpub(string npub)
    {
        return FromBytes(Nip19.DecodeKey(npub, Nip19.NpubPrefix));
    }

    public byte[] ToBytes() => (byte[])_bytes.Clone();

    /// <summary>Lowercase hex, 64 characters: the form used in Nostr events and tags.</summary>
    public string ToHex() => Convert.ToHexStringLower(_bytes);

    public string ToNpub() => Nip19.Encode(Nip19.NpubPrefix, _bytes);

    public bool Equals(PublicKey? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => Equals(obj as PublicKey);

    public override int GetHashCode() => BitConverter.ToInt32(_bytes, 0);

    public override string ToString() => ToHex();
}

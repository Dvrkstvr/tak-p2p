using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using HMACSHA256 = System.Security.Cryptography.HMACSHA256;

namespace TakEngine.Crypto;

/// <summary>Why a NIP-44 payload was rejected. Every payload-level decrypt failure maps to exactly one reason.</summary>
public enum Nip44Error
{
    UnknownVersion,
    InvalidPayloadSize,
    InvalidBase64,
    InvalidMac,
    InvalidPadding,
}

/// <summary>A NIP-44 payload could not be decrypted. <see cref="Error"/> says why.</summary>
public sealed class Nip44Exception(Nip44Error error, string message) : CryptographicException(message)
{
    public Nip44Error Error { get; } = error;
}

/// <summary>
/// NIP-44 v2 (nostr-protocol/nips 44.md), proven by paulmillr/nip44 nip44.vectors.json. Pure: the 32-byte nonce is an
/// argument (shell: <c>RandomNumberGenerator.GetBytes(32)</c>). Plaintext is capped at 65535 UTF-8 bytes
/// (2-byte length prefix only; docs/decisions/0002 "cap plaintext at 64 KiB").
/// </summary>
public static class Nip44
{
    public const byte Version = 2;
    public const int MinPlaintextBytes = 1;
    public const int MaxPlaintextBytes = 65535;
    public const int NonceLength = 32;
    public const int ConversationKeyLength = 32;
    private const int MacLength = 32;
    private const int MinPayloadChars = 132;
    private const int MaxPayloadChars = 87472;
    private const int MinDataBytes = 99;
    private const int MaxDataBytes = 65603;

    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("nip44-v2");

    /// <summary>
    /// conversation_key = HKDF-extract(salt = "nip44-v2", IKM = unhashed shared x of mine * theirs).
    /// Symmetric: ConversationKey(a, B) == ConversationKey(b, A).
    /// </summary>
    public static byte[] ConversationKey(SecretKey mine, PublicKey theirs)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(theirs);
        var shared = theirs.EvenY.GetSharedPubkey(mine.Inner); // raw point, NOT a hashing ECDH helper
        byte[] sharedX = shared.ToBytes(true)[1..];
        return HMACSHA256.HashData(Salt, sharedX);
    }

    /// <summary>HKDF-expand(conversation_key, info = nonce, 76) split into ChaCha20 key (32), ChaCha20 nonce (12), HMAC key (32).</summary>
    public static (byte[] ChaChaKey, byte[] ChaChaNonce, byte[] HmacKey) MessageKeys(byte[] conversationKey, byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(conversationKey);
        ArgumentNullException.ThrowIfNull(nonce);
        if (conversationKey.Length != ConversationKeyLength)
            throw new ArgumentException($"Conversation key must be {ConversationKeyLength} bytes.", nameof(conversationKey));
        if (nonce.Length != NonceLength)
            throw new ArgumentException($"Nonce must be {NonceLength} bytes.", nameof(nonce));

        byte[] keys = HKDF.Expand(HashAlgorithmName.SHA256, conversationKey, 76, nonce);
        return (keys[..32], keys[32..44], keys[44..76]);
    }

    public static int CalcPaddedLength(int unpaddedLength)
    {
        if (unpaddedLength < 1)
            throw new ArgumentOutOfRangeException(nameof(unpaddedLength), "Length must be at least 1.");
        if (unpaddedLength <= 32)
            return 32;
        int nextPower = 1 << (BitOperations.Log2((uint)(unpaddedLength - 1)) + 1);
        int chunk = nextPower <= 256 ? 32 : nextPower / 8;
        return chunk * (((unpaddedLength - 1) / chunk) + 1);
    }

    public static string Encrypt(string plaintext, byte[] conversationKey, byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var (chachaKey, chachaNonce, hmacKey) = MessageKeys(conversationKey, nonce);
        byte[] ciphertext = ChaCha20(chachaKey, chachaNonce, Pad(plaintext));
        byte[] mac = HmacAad(hmacKey, ciphertext, nonce);
        return Convert.ToBase64String([Version, .. nonce, .. ciphertext, .. mac]);
    }

    /// <summary>
    /// Decrypts and authenticates; nothing is returned unauthenticated. A bad payload throws <see cref="Nip44Exception"/>
    /// with the reason in <see cref="Nip44Exception.Error"/>. A conversation key that is not 32 bytes is a caller bug and
    /// throws <see cref="ArgumentException"/> instead, once the payload has passed the size, base64 and version checks.
    /// </summary>
    public static string Decrypt(string payload, byte[] conversationKey)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length > 0 && payload[0] == '#')
            throw new Nip44Exception(Nip44Error.UnknownVersion, "Unknown encryption version.");
        if (payload.Length < MinPayloadChars || payload.Length > MaxPayloadChars)
            throw new Nip44Exception(Nip44Error.InvalidPayloadSize, $"Invalid payload size {payload.Length}.");

        byte[] buffer = new byte[payload.Length];
        if (!Convert.TryFromBase64String(payload, buffer, out int written))
            throw new Nip44Exception(Nip44Error.InvalidBase64, "Payload is not valid base64.");
        byte[] data = buffer[..written];
        if (data.Length < MinDataBytes || data.Length > MaxDataBytes)
            throw new Nip44Exception(Nip44Error.InvalidPayloadSize, $"Invalid data size {data.Length}.");
        if (data[0] != Version)
            throw new Nip44Exception(Nip44Error.UnknownVersion, $"Unknown encryption version {data[0]}.");

        byte[] nonce = data[1..33];
        byte[] ciphertext = data[33..^MacLength];
        byte[] mac = data[^MacLength..];
        var (chachaKey, chachaNonce, hmacKey) = MessageKeys(conversationKey, nonce);
        if (!CryptographicOperations.FixedTimeEquals(HmacAad(hmacKey, ciphertext, nonce), mac))
            throw new Nip44Exception(Nip44Error.InvalidMac, "Invalid MAC.");

        return Unpad(ChaCha20(chachaKey, chachaNonce, ciphertext));
    }

    private static byte[] Pad(string plaintext)
    {
        byte[] unpadded = Encoding.UTF8.GetBytes(plaintext);
        if (unpadded.Length < MinPlaintextBytes || unpadded.Length > MaxPlaintextBytes)
            throw new ArgumentOutOfRangeException(
                nameof(plaintext),
                $"Plaintext must be {MinPlaintextBytes}..{MaxPlaintextBytes} UTF-8 bytes, got {unpadded.Length}.");

        byte[] padded = new byte[2 + CalcPaddedLength(unpadded.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(padded, (ushort)unpadded.Length);
        unpadded.CopyTo(padded, 2);
        return padded;
    }

    private static string Unpad(byte[] padded)
    {
        int length = BinaryPrimitives.ReadUInt16BigEndian(padded);
        if (length == 0 || padded.Length != 2 + CalcPaddedLength(length))
            throw new Nip44Exception(Nip44Error.InvalidPadding, "Invalid padding.");
        return Encoding.UTF8.GetString(padded, 2, length);
    }

    private static byte[] ChaCha20(byte[] key, byte[] nonce12, byte[] input)
    {
        var engine = new ChaCha7539Engine(); // RFC 8439 ChaCha20, counter starts at 0
        engine.Init(true, new ParametersWithIV(new KeyParameter(key), nonce12));
        byte[] output = new byte[input.Length];
        engine.ProcessBytes(input, 0, input.Length, output, 0);
        return output;
    }

    private static byte[] HmacAad(byte[] key, byte[] message, byte[] aad) =>
        HMACSHA256.HashData(key, (byte[])[.. aad, .. message]);
}

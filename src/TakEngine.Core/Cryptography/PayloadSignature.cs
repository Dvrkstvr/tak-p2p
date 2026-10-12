using System.Security.Cryptography;
using System.Text;
using TakEngine.Crypto;

namespace TakEngine.Core.Cryptography;

/// <summary>
/// BIP-340 signature over SHA-256(UTF-8(payload)) with the player's secp256k1 key (D-011, which retired the Ed25519
/// signer). Used for the session's "prevHash:ptn" payload and the spectator <c>BroadcastEnvelope</c> payload.
/// Transitional: F-033 moves the session to <c>ActionDigest</c>. Pure: aux is 32 zero bytes, which BIP-340 permits
/// (the nonce is still derived from the secret key and the message).
/// </summary>
public static class PayloadSignature
{
    private static readonly byte[] ZeroAux = new byte[Schnorr.AuxLength];

    public static string Sign(SecretKey key, string payload)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(payload);
        return Convert.ToHexStringLower(Schnorr.Sign(key, Digest(payload), ZeroAux));
    }

    /// <summary>False for any malformed input (bad hex, wrong lengths, off-curve key); never throws.</summary>
    public static bool Verify(string? publicKeyHex, string? payload, string? signatureHex)
    {
        if (payload is null || signatureHex is null || signatureHex.Length != Schnorr.SignatureLength * 2)
            return false;
        if (!PublicKey.TryFromHex(publicKeyHex, out PublicKey? key))
            return false;
        byte[] signature;
        try
        {
            signature = Convert.FromHexString(signatureHex);
        }
        catch (FormatException)
        {
            return false;
        }
        return Schnorr.Verify(key, Digest(payload), signature);
    }

    private static byte[] Digest(string payload) => SHA256.HashData(Encoding.UTF8.GetBytes(payload));
}

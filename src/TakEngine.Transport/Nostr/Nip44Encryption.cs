using System;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace TakEngine.Transport.Nostr;

public static class Nip44Encryption
{
    private const byte Nip44Version = 2;
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("nip44-v2");

    public static string Encrypt(string plaintext, byte[] sharedSecret)
    {
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);

        // Generate 12-byte nonce for ChaCha20-Poly1305
        byte[] nonce = RandomNumberGenerator.GetBytes(12);

        // Derive 32-byte encryption key using HKDF
        byte[] encKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: sharedSecret,
            outputLength: 32,
            salt: Salt,
            info: nonce);

        var cipher = new Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305();
        var parameters = new AeadParameters(new KeyParameter(encKey), 128, nonce);
        cipher.Init(true, parameters);

        byte[] output = new byte[cipher.GetOutputSize(plainBytes.Length)];
        int len = cipher.ProcessBytes(plainBytes, 0, plainBytes.Length, output, 0);
        cipher.DoFinal(output, len);

        // Wire format: Version(1 byte) + Nonce(12 bytes) + Ciphertext + Tag(16 bytes)
        byte[] payload = new byte[1 + nonce.Length + output.Length];
        payload[0] = Nip44Version;
        Buffer.BlockCopy(nonce, 0, payload, 1, nonce.Length);
        Buffer.BlockCopy(output, 0, payload, 1 + nonce.Length, output.Length);

        return Convert.ToBase64String(payload);
    }

    public static string Decrypt(string base64Payload, byte[] sharedSecret)
    {
        byte[] payload = Convert.FromBase64String(base64Payload);
        if (payload.Length < 1 + 12 + 16)
            throw new ArgumentException("Payload too short for NIP-44 format.", nameof(base64Payload));

        byte version = payload[0];
        if (version != Nip44Version)
            throw new NotSupportedException($"Unsupported NIP-44 version: {version}");

        byte[] nonce = new byte[12];
        Buffer.BlockCopy(payload, 1, nonce, 0, 12);

        int cipherLen = payload.Length - 1 - 12;
        byte[] cipherBytes = new byte[cipherLen];
        Buffer.BlockCopy(payload, 1 + 12, cipherBytes, 0, cipherLen);

        byte[] encKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: sharedSecret,
            outputLength: 32,
            salt: Salt,
            info: nonce);

        var cipher = new Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305();
        var parameters = new AeadParameters(new KeyParameter(encKey), 128, nonce);
        cipher.Init(false, parameters);

        byte[] plainBytes = new byte[cipher.GetOutputSize(cipherLen)];
        int len = cipher.ProcessBytes(cipherBytes, 0, cipherLen, plainBytes, 0);
        int finalLen = cipher.DoFinal(plainBytes, len);

        return Encoding.UTF8.GetString(plainBytes, 0, len + finalLen);
    }

    public static byte[] DeriveSharedSecret(string privateKeyHex, string peerPublicKeyHex)
    {
        // Deterministic ECDH shared secret abstraction:
        // Hash private key + peer public key to produce a 32-byte shared key
        byte[] privBytes = Convert.FromHexString(privateKeyHex);
        byte[] pubBytes = Convert.FromHexString(peerPublicKeyHex);

        byte[] combined = new byte[privBytes.Length + pubBytes.Length];
        Buffer.BlockCopy(privBytes, 0, combined, 0, privBytes.Length);
        Buffer.BlockCopy(pubBytes, 0, combined, privBytes.Length, pubBytes.Length);

        return SHA256.HashData(combined);
    }
}

using System;
using System.Security.Cryptography;
using TakEngine.Transport.Nostr;
using Xunit;

namespace TakEngine.Transport.Tests;

public class Nip44EncryptionTests
{
    [Fact]
    public void Nip44_EncryptAndDecrypt_RoundTripsSuccessfully()
    {
        // 32-byte private and public key pairs
        string alicePriv = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        string alicePub = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        string bobPriv = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        string bobPub = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        // Alice derives secret for Bob; Bob derives secret for Alice
        byte[] aliceSecret = Nip44Encryption.DeriveSharedSecret(alicePriv, bobPub);
        byte[] bobSecret = Nip44Encryption.DeriveSharedSecret(alicePriv, bobPub);

        string plaintext = "{\"action\":\"MOVE\",\"ptn\":\"3c3+12\"}";

        string ciphertext = Nip44Encryption.Encrypt(plaintext, aliceSecret);
        Assert.NotNull(ciphertext);
        Assert.NotEqual(plaintext, ciphertext);

        string decrypted = Nip44Encryption.Decrypt(ciphertext, bobSecret);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Nip44_TamperedCiphertext_FailsDecryption()
    {
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        string plaintext = "sensitive-turn-data";

        string ciphertext = Nip44Encryption.Encrypt(plaintext, secret);
        byte[] rawPayload = Convert.FromBase64String(ciphertext);

        // Tamper with one byte in ciphertext
        rawPayload[^2] ^= 0xFF;
        string tamperedBase64 = Convert.ToBase64String(rawPayload);

        Assert.ThrowsAny<Exception>(() => Nip44Encryption.Decrypt(tamperedBase64, secret));
    }
}

namespace TakEngine.Crypto.Tests;

/// <summary>F-032 (BIP-340 part): bitcoin/bips test-vectors.csv plus two independent keys.</summary>
public class SchnorrTests
{
    // Rows with a 32-byte message. Rows 15-18 sign 0/1/17/100-byte messages; Nostr only signs 32-byte digests.
    public static TheoryData<string, string, string, string, bool> VerifyVectors()
    {
        var data = new TheoryData<string, string, string, string, bool>();
        foreach (string[] row in VectorFiles.Bip340Rows().Where(r => r[4].Length == 64))
            data.Add(row[0], row[2], row[4], row[5], row[6] == "TRUE");
        return data;
    }

    public static TheoryData<string, string, string, string> SignVectors()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (string[] row in VectorFiles.Bip340Rows().Where(r => r[1].Length == 64 && r[4].Length == 64))
            data.Add(row[1], row[3], row[4], row[5]);
        return data;
    }

    [Fact]
    public void Csv_Has15VerifyVectorsAnd4SignVectors()
    {
        Assert.Equal(15, VerifyVectors().Count);
        Assert.Equal(4, SignVectors().Count);
    }

    [Theory]
    [MemberData(nameof(VerifyVectors))]
    public void Verify_MatchesBip340Vectors(string index, string publicHex, string messageHex, string signatureHex, bool expected)
    {
        bool actual = PublicKey.TryFromBytes(VectorFiles.Hex(publicHex), out var key)
            && Schnorr.Verify(key, VectorFiles.Hex(messageHex), VectorFiles.Hex(signatureHex));

        Assert.True(expected == actual, $"vector {index}: expected {expected}, got {actual}");
    }

    [Theory]
    [MemberData(nameof(SignVectors))]
    public void Sign_WithVectorAux_ProducesTheExactVectorSignature(string secretHex, string auxHex, string messageHex, string signatureHex)
    {
        var key = SecretKey.FromHex(secretHex);

        byte[] signature = Schnorr.Sign(key, VectorFiles.Hex(messageHex), VectorFiles.Hex(auxHex));

        Assert.Equal(signatureHex.ToLowerInvariant(), Convert.ToHexStringLower(signature));
        Assert.True(Schnorr.Verify(key.PublicKey, VectorFiles.Hex(messageHex), signature));
    }

    [Fact]
    public void SignatureByAlice_DoesNotVerifyUnderBob_OrForAnotherDigest_OrWithAFlippedBit()
    {
        var alice = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");
        var bob = SecretKey.FromHex("c90fdaa22168c234c4c6628b80dc1cd129024e088a67cc74020bbea63b14e5c9");
        byte[] digest = System.Security.Cryptography.SHA256.HashData("tak move a1"u8);
        byte[] otherDigest = System.Security.Cryptography.SHA256.HashData("tak move a2"u8);

        byte[] signature = Schnorr.Sign(alice, digest, new byte[32]);

        Assert.True(Schnorr.Verify(alice.PublicKey, digest, signature));
        Assert.False(Schnorr.Verify(bob.PublicKey, digest, signature));
        Assert.False(Schnorr.Verify(alice.PublicKey, otherDigest, signature));
        for (int bit = 0; bit < 512; bit += 37)
        {
            byte[] flipped = (byte[])signature.Clone();
            flipped[bit / 8] ^= (byte)(1 << (bit % 8));
            Assert.False(Schnorr.Verify(alice.PublicKey, digest, flipped), $"flipped bit {bit} still verified");
        }
    }

    [Fact]
    public void DifferentAux_GivesDifferentSignatures_ThatBothVerify()
    {
        var key = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");
        byte[] digest = new byte[32];
        byte[] aux1 = new byte[32];
        byte[] aux2 = new byte[32];
        aux2[31] = 1;

        byte[] sig1 = Schnorr.Sign(key, digest, aux1);
        byte[] sig2 = Schnorr.Sign(key, digest, aux2);

        Assert.NotEqual(sig1, sig2);
        Assert.True(Schnorr.Verify(key.PublicKey, digest, sig1));
        Assert.True(Schnorr.Verify(key.PublicKey, digest, sig2));
    }

    [Fact]
    public void NullKey_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Schnorr.Sign(null!, new byte[32], new byte[32]));
        Assert.Throws<ArgumentNullException>(() => Schnorr.Verify(null!, new byte[32], new byte[64]));
    }

    [Theory]
    [InlineData(31, 64)]
    [InlineData(33, 64)]
    [InlineData(32, 63)]
    [InlineData(32, 65)]
    [InlineData(0, 0)]
    public void Verify_ReturnsFalse_ForWrongLengths(int digestLength, int signatureLength)
    {
        var key = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");

        Assert.False(Schnorr.Verify(key.PublicKey, new byte[digestLength], new byte[signatureLength]));
    }

    [Theory]
    [InlineData(31, 32)]
    [InlineData(33, 32)]
    [InlineData(32, 31)]
    [InlineData(32, 0)]
    public void Sign_RejectsWrongDigestOrAuxLength(int digestLength, int auxLength)
    {
        var key = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");

        Assert.ThrowsAny<ArgumentException>(() => Schnorr.Sign(key, new byte[digestLength], new byte[auxLength]));
    }
}

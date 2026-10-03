namespace TakEngine.Crypto.Tests;

/// <summary>F-031: one secp256k1 key per player; keys are validated at the boundary.</summary>
public class KeyTests
{
    private const string CurveOrderHex = "fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141";

    // index, secret key, public key (rows 15-18 share one key; the index keeps the theory rows distinct)
    public static TheoryData<string, string, string> SecretToPublicVectors()
    {
        var data = new TheoryData<string, string, string>();
        foreach (string[] row in VectorFiles.Bip340Rows().Where(r => r[1].Length == 64))
            data.Add(row[0], row[1], row[2]);
        return data;
    }

    [Fact]
    public void Bip340Csv_HasEightSecretKeyRows()
    {
        Assert.Equal(8, SecretToPublicVectors().Count);
    }

    [Theory]
    [MemberData(nameof(SecretToPublicVectors))]
    public void SecretKey_DerivesXOnlyPublicKey_FromBip340Vectors(string index, string secretHex, string expectedPublicHex)
    {
        var key = SecretKey.FromHex(secretHex);

        Assert.True(expectedPublicHex.ToLowerInvariant() == key.PublicKey.ToHex(), $"vector {index}");
        Assert.Equal(VectorFiles.Hex(expectedPublicHex), key.PublicKey.ToBytes());
    }

    [Fact]
    public void Generate_UsesSuppliedRandomness_AndRedrawsOutOfRangeScalars()
    {
        var draws = new Queue<byte[]>([new byte[32], VectorFiles.Hex(CurveOrderHex), VectorFiles.Hex("0000000000000000000000000000000000000000000000000000000000000003")]);

        var key = SecretKey.Generate(draws.Dequeue);

        Assert.Empty(draws);
        Assert.Equal("f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9", key.PublicKey.ToHex());
    }

    [Fact]
    public void Generate_GivesUpAfter64InvalidDraws_WhenTheRandomSourceIsBroken()
    {
        int draws = 0;

        Assert.Throws<InvalidOperationException>(() => SecretKey.Generate(() =>
        {
            draws++;
            return new byte[32];
        }));
        Assert.Equal(64, draws);
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => SecretKey.FromHex(null!));
        Assert.Throws<ArgumentNullException>(() => SecretKey.FromNsec(null!));
        Assert.Throws<ArgumentNullException>(() => SecretKey.Generate(null!));
        Assert.Throws<ArgumentNullException>(() => PublicKey.FromHex(null!));
        Assert.Throws<ArgumentNullException>(() => PublicKey.FromNpub(null!));
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000", "out of range")]
    [InlineData(CurveOrderHex, "out of range")]
    [InlineData("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "out of range")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000003ab", "64 characters")]
    [InlineData("00000000000000000000000000000000000000000000000000000000000003", "64 characters")]
    [InlineData("zz00000000000000000000000000000000000000000000000000000000000003", "not valid hex")]
    public void SecretKey_RejectsInvalidInput_WithSpecificMessage(string hex, string expectedMessagePart)
    {
        var ex = Assert.Throws<InvalidKeyException>(() => SecretKey.FromHex(hex));
        Assert.Contains(expectedMessagePart, ex.Message);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(0)]
    public void SecretKey_RejectsWrongByteLength(int length)
    {
        byte[] bytes = new byte[length];
        if (length > 0)
            bytes[^1] = 3;

        var ex = Assert.Throws<InvalidKeyException>(() => SecretKey.FromBytes(bytes));
        Assert.Contains("32 bytes", ex.Message);
        Assert.False(SecretKey.TryFromBytes(bytes, out _));
    }

    [Fact]
    public void SecretKey_AcceptsLargestValidScalar()
    {
        var key = SecretKey.FromHex("fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364140");

        Assert.Equal(64, key.PublicKey.ToHex().Length);
    }

    [Theory]
    [InlineData("EEFDEA4CDB677750A420FEE807EACF21EB9898AE79B9768766E4FAA04A2D4A34")] // BIP-340 vector 5: not on the curve
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC30")] // BIP-340 vector 14: x >= field size
    [InlineData("1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef")] // NIP-44 invalid: no sqrt
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")] // NIP-44 invalid: point on the twist
    public void PublicKey_RejectsPointsNotOnTheCurve(string hex)
    {
        var ex = Assert.Throws<InvalidKeyException>(() => PublicKey.FromHex(hex));
        Assert.Contains("not on the secp256k1 curve", ex.Message);
        Assert.False(PublicKey.TryFromHex(hex, out _));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(0)]
    public void PublicKey_RejectsWrongByteLength(int length)
    {
        byte[] valid = SecretKey.FromHex("0000000000000000000000000000000000000000000000000000000000000003").PublicKey.ToBytes();
        byte[] bytes = new byte[length];
        valid.AsSpan(0, Math.Min(length, 32)).CopyTo(bytes);

        var ex = Assert.Throws<InvalidKeyException>(() => PublicKey.FromBytes(bytes));
        Assert.Contains("32 bytes", ex.Message);
        Assert.False(PublicKey.TryFromBytes(bytes, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036")]
    [InlineData("g9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9")]
    public void PublicKey_TryFromHex_ReturnsFalseForMalformedHex(string? hex)
    {
        Assert.False(PublicKey.TryFromHex(hex, out var key));
        Assert.Null(key);
    }

    [Fact]
    public void PublicKey_TryFromHex_ReturnsTheKey_ForValidHex()
    {
        Assert.True(PublicKey.TryFromHex("f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9", out var key));
        Assert.Equal("f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9", key.ToHex());
        Assert.True(PublicKey.TryFromBytes(key.ToBytes(), out var again));
        Assert.Equal(key, again);
        Assert.True(SecretKey.TryFromBytes(VectorFiles.Hex("0000000000000000000000000000000000000000000000000000000000000003"), out var secret));
        Assert.Equal(key, secret.PublicKey);
    }

    [Fact]
    public void PublicKey_ParsesEitherCase_AndPrintsLowercase()
    {
        var upper = PublicKey.FromHex("F9308A019258C31049344F85F89D5229B531C845836F99B08601F113BCE036F9");
        var lower = PublicKey.FromHex("f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9");

        Assert.Equal(lower, upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
        Assert.Equal("f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9", upper.ToHex());
        Assert.Equal(upper.ToHex(), upper.ToString());
    }

    [Fact]
    public void TwoIndependentKeys_HaveDifferentPublicKeys()
    {
        var alice = SecretKey.FromHex("0000000000000000000000000000000000000000000000000000000000000003");
        var bob = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");

        Assert.NotEqual(alice.PublicKey, bob.PublicKey);
        Assert.False(alice.PublicKey.Equals(null));
        Assert.False(alice.PublicKey.Equals((object)"not a key"));
    }

    [Fact]
    public void SecretKey_ToBytesAndToHex_RoundTrip_AndToStringHidesTheSecret()
    {
        const string hex = "b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef";
        var key = SecretKey.FromHex(hex);

        Assert.Equal(hex, key.ToHex());
        Assert.Equal(VectorFiles.Hex(hex), key.ToBytes());
        Assert.DoesNotContain(hex, key.ToString());
        Assert.Contains(key.PublicKey.ToHex(), key.ToString());
    }

    [Fact]
    public void ToBytes_ReturnsACopy()
    {
        var key = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");
        byte[] secret = key.ToBytes();
        byte[] pub = key.PublicKey.ToBytes();
        secret[0] ^= 0xff;
        pub[0] ^= 0xff;

        Assert.Equal("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef", key.ToHex());
        Assert.Equal("dff1d77f2a671c5f36183726db2341be58feae1da2deced843240f7b502ba659", key.PublicKey.ToHex());
    }
}

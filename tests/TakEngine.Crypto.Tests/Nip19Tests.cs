namespace TakEngine.Crypto.Tests;

/// <summary>F-031: npub/nsec (NIP-19), including the example pair from nostr-protocol/nips 19.md.</summary>
public class Nip19Tests
{
    private const string SpecNpub = "npub10elfcs4fr0l0r8af98jlmgdh9c8tcxjvz9qkw038js35mp4dma8qzvjptg";
    private const string SpecNpubHex = "7e7e9c42a91bfef19fa929e5fda1b72e0ebc1a4c1141673e2794234d86addf4e";
    private const string SpecNsec = "nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe5";
    private const string SpecNsecHex = "67dea2ed018072d675f5415ecfaed7d2597555e202d85b3d65ea4e58d2d92ffa";

    [Fact]
    public void SpecExamplePair_DecodesToTheDocumentedHex()
    {
        Assert.Equal(SpecNpubHex, PublicKey.FromNpub(SpecNpub).ToHex());
        Assert.Equal(SpecNsecHex, SecretKey.FromNsec(SpecNsec).ToHex());
        Assert.Equal(("npub", SpecNpubHex), Nip19.Decode(SpecNpub));
        Assert.Equal(("nsec", SpecNsecHex), Nip19.Decode(SpecNsec));
    }

    [Fact]
    public void SpecExamplePair_EncodesBackToTheDocumentedStrings()
    {
        Assert.Equal(SpecNpub, PublicKey.FromHex(SpecNpubHex).ToNpub());
        Assert.Equal(SpecNsec, SecretKey.FromHex(SpecNsecHex).ToNsec());
        Assert.Equal(SpecNpub, Nip19.ToNpub(SpecNpubHex));
        Assert.Equal(SpecNsec, Nip19.ToNsec(SpecNsecHex));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RandomKey_RoundTripsThroughNsecAndNpub(int seed)
    {
        var random = new Random(seed);
        var key = SecretKey.Generate(() =>
        {
            byte[] b = new byte[32];
            random.NextBytes(b);
            return b;
        });

        string nsec = key.ToNsec();
        string npub = key.PublicKey.ToNpub();

        Assert.StartsWith("nsec1", nsec);
        Assert.StartsWith("npub1", npub);
        Assert.Equal(key.ToHex(), SecretKey.FromNsec(nsec).ToHex());
        Assert.Equal(key.PublicKey, PublicKey.FromNpub(npub));
    }

    [Fact]
    public void BadChecksum_IsRejected()
    {
        string tampered = SpecNpub[..^1] + (SpecNpub[^1] == 'q' ? 'p' : 'q');

        Assert.Throws<FormatException>(() => Nip19.Decode(tampered));
        var ex = Assert.Throws<InvalidKeyException>(() => PublicKey.FromNpub(tampered));
        Assert.Contains("checksum", ex.Message);
    }

    [Fact]
    public void WrongPrefix_IsRejected()
    {
        var ex1 = Assert.Throws<InvalidKeyException>(() => PublicKey.FromNpub(SpecNsec));
        Assert.Contains("Expected an npub", ex1.Message);
        var ex2 = Assert.Throws<InvalidKeyException>(() => SecretKey.FromNsec(SpecNpub));
        Assert.Contains("Expected an nsec", ex2.Message);
    }

    [Fact]
    public void NsecOfWrongLength_IsRejectedWithLengthMessage()
    {
        string shortNsec = Nip19.Encode("nsec", new byte[31]);

        var ex = Assert.Throws<InvalidKeyException>(() => SecretKey.FromNsec(shortNsec));
        Assert.Contains("32 bytes", ex.Message);
    }

    [Fact]
    public void OneCharacterPrefix_AndEmptyPayload_DecodeToTheIndependentEncoding()
    {
        // Strings computed with a separate Python bech32 implementation.
        Assert.Equal("a1qqqd87cq", Nip19.Encode("a", [0]));
        var (hrp, data) = Nip19.DecodeToBytes("a1qqqd87cq");
        Assert.Equal("a", hrp);
        Assert.Equal(new byte[] { 0 }, data);
        Assert.Equal("npub106246s", Nip19.Encode("npub", []));
        Assert.Empty(Nip19.DecodeToBytes("npub106246s").Data);
        var ex = Assert.Throws<InvalidKeyException>(() => PublicKey.FromNpub("npub106246s"));
        Assert.Contains("32 bytes, got 0", ex.Message);
    }

    [Theory]
    [InlineData("nsec1qe882ll")]   // a single 5-bit group: not even one byte
    [InlineData("nsec1qpvjxt40")]  // two 5-bit groups with non-zero padding bits
    public void ChecksumValidStrings_WithBadPadding_AreRejected(string input)
    {
        var ex = Assert.Throws<FormatException>(() => Nip19.DecodeToBytes(input));
        Assert.Contains("padding", ex.Message);
        Assert.Throws<InvalidKeyException>(() => SecretKey.FromNsec(input));
    }

    [Fact]
    public void UppercaseInput_IsAccepted()
    {
        Assert.Equal(SpecNpubHex, PublicKey.FromNpub(SpecNpub.ToUpperInvariant()).ToHex());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("npub1")]
    [InlineData("npub1bbbbbb!")]
    public void Garbage_IsRejected(string input)
    {
        Assert.Throws<InvalidKeyException>(() => PublicKey.FromNpub(input));
    }
}

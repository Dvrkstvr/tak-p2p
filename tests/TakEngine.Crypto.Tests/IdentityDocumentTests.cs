namespace TakEngine.Crypto.Tests;

/// <summary>F-031: the versioned identity blob {"v":1,"nsec":"nsec1…"} and its specific errors.</summary>
public class IdentityDocumentTests
{
    private const string SpecNsec = "nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe5";
    private const string SpecNsecHex = "67dea2ed018072d675f5415ecfaed7d2597555e202d85b3d65ea4e58d2d92ffa";

    [Fact]
    public void Serialize_WritesVersionAndNsec()
    {
        Assert.Equal(
            "{\"v\":1,\"nsec\":\"" + SpecNsec + "\"}",
            IdentityDocument.Serialize(SecretKey.FromHex(SpecNsecHex)));
    }

    [Fact]
    public void Parse_ReadsWhatSerializeWrote()
    {
        var key = SecretKey.FromHex(SpecNsecHex);

        Assert.Equal(SpecNsecHex, IdentityDocument.Parse(IdentityDocument.Serialize(key)).ToHex());
    }

    [Fact]
    public void Parse_IgnoresWhitespaceAndUnknownFields()
    {
        var key = IdentityDocument.Parse("{ \"nsec\" : \"" + SpecNsec + "\", \"v\": 1, \"note\": \"x\" }\n");

        Assert.Equal(SpecNsecHex, key.ToHex());
    }

    [Theory]
    [InlineData("", "not valid JSON")]
    [InlineData("nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe5", "not valid JSON")]
    [InlineData("[1]", "not a JSON object")]
    [InlineData("{\"nsec\":\"" + SpecNsec + "\"}", "no integer version")]
    [InlineData("{\"v\":\"1\",\"nsec\":\"" + SpecNsec + "\"}", "no integer version")]
    [InlineData("{\"v\":2,\"nsec\":\"" + SpecNsec + "\"}", "newer version (v=2)")]
    [InlineData("{\"v\":0,\"nsec\":\"" + SpecNsec + "\"}", "Unknown identity version v=0")]
    [InlineData("{\"v\":1}", "no \"nsec\"")]
    [InlineData("{\"v\":1,\"nsec\":42}", "no \"nsec\"")]
    [InlineData("{\"v\":1,\"nsec\":\"npub10elfcs4fr0l0r8af98jlmgdh9c8tcxjvz9qkw038js35mp4dma8qzvjptg\"}", "Expected an nsec")]
    [InlineData("{\"v\":1,\"nsec\":\"nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe6\"}", "checksum")]
    public void Parse_RejectsBadDocuments_WithASpecificMessage(string document, string expectedMessagePart)
    {
        var ex = Assert.Throws<IdentityFormatException>(() => IdentityDocument.Parse(document));
        Assert.Contains(expectedMessagePart, ex.Message);
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => IdentityDocument.Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => IdentityDocument.Parse(null!));
    }

    [Fact]
    public void Parse_RejectsAnNsecOfTheWrongLength()
    {
        string document = "{\"v\":1,\"nsec\":\"" + Nip19.Encode("nsec", new byte[31]) + "\"}";

        var ex = Assert.Throws<IdentityFormatException>(() => IdentityDocument.Parse(document));
        Assert.Contains("32 bytes, got 31", ex.Message);
    }

    [Fact]
    public void Parse_RejectsAnOutOfRangeScalar()
    {
        byte[] curveOrder = Convert.FromHexString("fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141");
        string document = "{\"v\":1,\"nsec\":\"" + Nip19.Encode("nsec", curveOrder) + "\"}";

        var ex = Assert.Throws<IdentityFormatException>(() => IdentityDocument.Parse(document));
        Assert.Contains("out of range", ex.Message);
    }
}

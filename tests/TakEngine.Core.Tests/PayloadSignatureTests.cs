using TakEngine.Core.Cryptography;
using TakEngine.Crypto;
using Xunit;

namespace TakEngine.Core.Tests;

/// <summary>F-031: session and spectator payloads are signed with the player's secp256k1 key (replaces the Ed25519 test).</summary>
public class PayloadSignatureTests
{
    private const string Payload = "4f2c…:a1";

    [Fact]
    public void SignatureByAlice_VerifiesUnderAliceOnly()
    {
        SecretKey alice = TestKeys.Create(1);
        SecretKey bob = TestKeys.Create(2);

        string signature = PayloadSignature.Sign(alice, Payload);

        Assert.Equal(128, signature.Length);
        Assert.True(PayloadSignature.Verify(alice.PublicKey.ToHex(), Payload, signature));
        Assert.False(PayloadSignature.Verify(bob.PublicKey.ToHex(), Payload, signature));
        Assert.False(PayloadSignature.Verify(alice.PublicKey.ToHex(), Payload + "x", signature));
    }

    [Fact]
    public void Sign_IsDeterministic()
    {
        Assert.Equal(PayloadSignature.Sign(TestKeys.Create(1), Payload), PayloadSignature.Sign(TestKeys.Create(1), Payload));
    }

    [Theory]
    [InlineData(null, Payload, "00")]
    [InlineData("abcd", Payload, "00")]
    [InlineData("eefdea4cdb677750a420fee807eacf21eb9898ae79b9768766e4faa04a2d4a34", Payload, "00")]
    [InlineData("VALIDKEY", null, "SIG")]
    [InlineData("VALIDKEY", Payload, null)]
    [InlineData("VALIDKEY", Payload, "bad_signature_hex")]
    [InlineData("VALIDKEY", Payload, "SIG_NOT_HEX")]
    public void Verify_ReturnsFalse_ForMalformedInput(string? publicKeyHex, string? payload, string? signatureHex)
    {
        SecretKey alice = TestKeys.Create(1);
        string validSignature = PayloadSignature.Sign(alice, Payload);
        publicKeyHex = publicKeyHex == "VALIDKEY" ? alice.PublicKey.ToHex() : publicKeyHex;
        signatureHex = signatureHex switch
        {
            "SIG" => validSignature,
            "SIG_NOT_HEX" => new string('z', 128),
            _ => signatureHex,
        };

        Assert.False(PayloadSignature.Verify(publicKeyHex, payload, signatureHex));
    }
}

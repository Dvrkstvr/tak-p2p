using System;
using TakEngine.Core.Cryptography;
using Xunit;

namespace TakEngine.Core.Tests;

public class Nip19Tests
{
    [Fact]
    public void Nip19_ToNpubAndDecode_RoundTripsAccurately()
    {
        var keyPair = CryptoSigner.GenerateKeyPair();
        string npub = Nip19.ToNpub(keyPair.PublicKeyHex);

        Assert.StartsWith("npub1", npub);

        var (hrp, hex) = Nip19.Decode(npub);
        Assert.Equal("npub", hrp);
        Assert.Equal(keyPair.PublicKeyHex, hex);
    }

    [Fact]
    public void Nip19_ToNsecAndDecode_RoundTripsAccurately()
    {
        var keyPair = CryptoSigner.GenerateKeyPair();
        string nsec = Nip19.ToNsec(keyPair.PrivateKeyHex);

        Assert.StartsWith("nsec1", nsec);

        var (hrp, hex) = Nip19.Decode(nsec);
        Assert.Equal("nsec", hrp);
        Assert.Equal(keyPair.PrivateKeyHex, hex);
    }

    [Fact]
    public void Nip19_DecodeInvalidChecksum_ThrowsFormatException()
    {
        var keyPair = CryptoSigner.GenerateKeyPair();
        string npub = Nip19.ToNpub(keyPair.PublicKeyHex);
        string tampered = npub[..^1] + (npub[^1] == 'q' ? 'p' : 'q');

        Assert.Throws<FormatException>(() => Nip19.Decode(tampered));
    }
}

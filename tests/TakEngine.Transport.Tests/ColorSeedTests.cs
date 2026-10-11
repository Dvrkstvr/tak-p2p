using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TakEngine.Abstractions;
using TakEngine.Transport.Matchmaking;
using Xunit;

namespace TakEngine.Transport.Tests;

/// <summary>F-017 / D-038: the commit-reveal colour seed.</summary>
public class ColorSeedTests
{
    private static JsonElement Vectors() => JsonDocument.Parse(Nip01VectorFiles.ReadText("color-seed-vectors.json")).RootElement;

    private static byte[] Nonce(string label) => SHA256.HashData(Encoding.UTF8.GetBytes(label));

    [Fact]
    public void Derive_IsFixedByGoldenVector()
    {
        JsonElement root = Vectors();
        string host = root.GetProperty("host").GetString()!;
        string guest = root.GetProperty("guest").GetString()!;
        var cases = root.GetProperty("cases").EnumerateArray().ToList();
        Assert.NotEmpty(cases);
        foreach (JsonElement c in cases)
        {
            byte[] hostNonce = Convert.FromHexString(c.GetProperty("host_nonce").GetString()!);
            byte[] guestNonce = Convert.FromHexString(c.GetProperty("guest_nonce").GetString()!);

            Assert.Equal(c.GetProperty("seed_commit").GetString(), ColorSeed.Commit(hostNonce));
            string seed = ColorSeed.Derive(hostNonce, guestNonce);
            Assert.Equal(c.GetProperty("seed").GetString(), seed);

            var (hostColour, guestColour) = ColorResolver.ResolveColors(seed, host, guest);
            Assert.Equal(c.GetProperty("host_colour").GetString(), hostColour.ToString());
            Assert.Equal(c.GetProperty("guest_colour").GetString(), guestColour.ToString());
        }
        // The vectors exercise both outcomes, so a resolver stuck on one colour fails here.
        Assert.Contains(cases, c => c.GetProperty("host_colour").GetString() == "White");
        Assert.Contains(cases, c => c.GetProperty("host_colour").GetString() == "Black");
    }

    [Fact]
    public void Commit_VerifiesOnlyTheCommittedNonce()
    {
        byte[] nonce = Nonce("committed");
        string commit = ColorSeed.Commit(nonce);

        Assert.Matches("^[0-9a-f]{64}$", commit);
        Assert.True(ColorSeed.Verify(commit, nonce));
        Assert.True(ColorSeed.Verify(commit.ToUpperInvariant(), nonce));
        Assert.False(ColorSeed.Verify(commit, Nonce("another")));
        Assert.False(ColorSeed.Verify(commit, nonce[..31]));
        Assert.False(ColorSeed.Verify("not hex", nonce));
        Assert.False(ColorSeed.Verify(null, nonce));
        Assert.False(ColorSeed.Verify(commit, null));
    }

    [Fact]
    public void Derive_DependsOnBothNonces()
    {
        byte[] a = Nonce("a");
        byte[] b = Nonce("b");

        string ab = ColorSeed.Derive(a, b);

        Assert.NotEqual(ab, ColorSeed.Derive(b, a));
        Assert.NotEqual(ab, ColorSeed.Derive(a, Nonce("c")));
        Assert.NotEqual(ab, ColorSeed.Derive(Nonce("c"), b));
        Assert.NotEqual(ColorSeed.Commit(a), ColorSeed.Derive(a, b));
    }

    [Fact]
    public void Derive_RejectsNoncesThatAreNot32Bytes()
    {
        Assert.Throws<ArgumentException>(() => ColorSeed.Derive(new byte[31], new byte[32]));
        Assert.Throws<ArgumentException>(() => ColorSeed.Derive(new byte[32], new byte[33]));
        Assert.Throws<ArgumentException>(() => ColorSeed.Commit(new byte[16]));
    }

    [Fact]
    public void Colours_ComeOutOpposite_ForAnyNonces()
    {
        const int seed = 17;
        var rng = new Random(seed);
        string host = "79be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798";
        string guest = "c6047f9441ed7d6d3045406e95c07cd85c778e4b8cef3ca7abac09b95c709ee5";
        int hostWhite = 0;
        for (int i = 0; i < 200; i++)
        {
            byte[] hn = new byte[32];
            byte[] gn = new byte[32];
            rng.NextBytes(hn);
            rng.NextBytes(gn);
            var (h, g) = ColorResolver.ResolveColors(ColorSeed.Derive(hn, gn), host, guest);
            Assert.True(h != g, $"same colour for both at iteration {i} (seed {seed})");
            if (h == PlayerColor.White)
                hostWhite++;
        }
        Assert.InRange(hostWhite, 60, 140);
    }
}

using System;
using System.Security.Cryptography;
using System.Text;
using TakEngine.Abstractions;
using TakEngine.Crypto;
using TakEngine.Transport.Matchmaking;
using Xunit;

namespace TakEngine.Transport.Tests;

/// <summary>F-017 / F-010: invite v2 (seed commitment, size 3..8, komi 0..20 half flats) and its strict validation.</summary>
public class InviteCodeTests
{
    private static readonly SecretKey HostKey = SecretKey.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes("invite host")));
    private static readonly SecretKey GuestKey = SecretKey.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes("invite guest")));
    private static readonly string Commit = ColorSeed.Commit(SHA256.HashData(Encoding.UTF8.GetBytes("host nonce")));
    private static readonly Guid Game = Guid.Parse("0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e");

    private static InviteCode Sample(BoardSize size = BoardSize.Six, int komi = 5, string? nick = "Host & <Co> +1") =>
        new(Game, HostKey.PublicKey.ToHex(), size, komi, ["wss://nos.lol", "wss://relay.damus.io"], Commit, nick);

    private static void AssertSame(InviteCode expected, InviteCode actual)
    {
        Assert.Equal(expected.GameId, actual.GameId);
        Assert.Equal(expected.HostPubKey, actual.HostPubKey);
        Assert.Equal(expected.BoardSize, actual.BoardSize);
        Assert.Equal(expected.KomiHalves, actual.KomiHalves);
        Assert.Equal(expected.Relays, actual.Relays);
        Assert.Equal(expected.SeedCommit, actual.SeedCommit);
        Assert.Equal(expected.HostNickname, actual.HostNickname);
    }

    private static void AssertRefused(string text, string messagePart)
    {
        var ex = Assert.Throws<InviteFormatException>(() => InviteCode.Parse(text));
        Assert.Contains(messagePart, ex.Message, StringComparison.Ordinal);
        Assert.False(InviteCode.TryParse(text, out _, out string? error));
        Assert.Equal(ex.Message, error);
    }

    [Theory]
    [InlineData(BoardSize.Three, 0)]
    [InlineData(BoardSize.Six, 5)]
    [InlineData(BoardSize.Eight, 20)]
    public void CompactCode_RoundTrips(BoardSize size, int komi)
    {
        InviteCode original = Sample(size, komi);

        string code = original.ToCompactCode();

        Assert.StartsWith("TAK2_", code);
        AssertSame(original, InviteCode.Parse(code));
    }

    [Fact]
    public void Uri_RoundTrips()
    {
        InviteCode original = Sample();

        string uri = original.ToUri();

        Assert.StartsWith("tak://invite?v=2&", uri);
        Assert.Contains("seed_commit=" + Commit, uri);
        Assert.Contains("komi=5", uri);
        AssertSame(original, InviteCode.Parse(uri));
    }

    [Fact]
    public void WebUrl_RoundTrips()
    {
        InviteCode original = Sample(nick: null);

        string url = original.ToWebUrl("https://tak.game/play/");

        Assert.StartsWith("https://tak.game/play/?invite=TAK2_", url);
        AssertSame(original, InviteCode.Parse(url));
    }

    [Fact]
    public void LocalToolRelays_AreAllowed()
    {
        InviteCode original = Sample() with { Relays = ["ws://127.0.0.1:7777", "ws://localhost:8080/"] };

        AssertSame(original, InviteCode.Parse(original.ToCompactCode()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_IsRefused(string text) => AssertRefused(text, "empty");

    [Fact]
    public void OldVersionCode_IsRefusedAsOlder()
    {
        string json = $"{{\"gid\":\"{Game}\",\"host\":\"{HostKey.PublicKey.ToHex()}\",\"size\":5,\"relays\":[],\"seed\":\"ab\"}}";
        string v1 = "TAK1_" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        AssertRefused(v1, "older version");
        AssertRefused($"tak://invite?gid={Game}&host={HostKey.PublicKey.ToHex()}&size=5&relays=", "older version");
        AssertRefused("https://tak.game/?invite=" + v1, "older version");
    }

    [Theory]
    [InlineData("hello there")]
    [InlineData("TAK3_abc")]
    [InlineData("https://tak.game/?other=1")]
    public void NotAnInvite_IsRefused(string text) => AssertRefused(text, "not a Tak invite");

    [Theory]
    [InlineData("TAK2_!!!")]
    [InlineData("TAK2_bm90IGpzb24")] // "not json"
    [InlineData("TAK2_WzFd")]        // "[1]"
    public void DamagedCode_IsRefused(string text) => AssertRefused(text, "damaged");

    [Fact]
    public void MissingSeedCommitment_IsRefused()
    {
        AssertRefused(Sample().ToUri().Replace("&seed_commit=" + Commit, ""), "no seed commitment");
        AssertRefused(Recode(Sample(), "\"seed_commit\":\"" + Commit + "\",", ""), "no seed commitment");
    }

    [Theory]
    [InlineData("abcd")]
    [InlineData("zz00000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("00000000000000000000000000000000000000000000000000000000000000000")]
    public void SeedCommitmentThatIsNot64Hex_IsRefused(string commit) =>
        AssertRefused(Sample().ToUri().Replace(Commit, commit), "seed commitment is not 64 hex characters");

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(0)]
    public void SizeOutOfRange_IsRefused(int size)
    {
        AssertRefused(Sample().ToUri().Replace("size=6", $"size={size}"), $"board size {size} is not supported");
        AssertRefused(Recode(Sample(), "\"size\":6", $"\"size\":{size}"), $"board size {size} is not supported");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void KomiOutOfRange_IsRefused(int komi)
    {
        AssertRefused(Sample().ToUri().Replace("komi=5", $"komi={komi}"), $"komi {komi} is out of range");
        AssertRefused(Recode(Sample(), "\"komi\":5", $"\"komi\":{komi}"), $"komi {komi} is out of range");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcd")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")] // not on the curve
    [InlineData("79be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f8179800")] // 33 bytes
    public void BadHostKey_IsRefused(string host) =>
        AssertRefused(Sample().ToUri().Replace(HostKey.PublicKey.ToHex(), host), "host key");

    [Fact]
    public void MissingGameId_IsRefused() =>
        AssertRefused(Sample().ToUri().Replace(Game.ToString(), "nope"), "game id");

    [Theory]
    [InlineData("https://nos.lol")]
    [InlineData("ws://nos.lol")]
    [InlineData("wss://")]
    [InlineData("nos.lol")]
    public void NonWssRelay_IsRefused(string relay)
    {
        InviteCode bad = Sample() with { Relays = ["wss://relay.damus.io", relay] };
        AssertRefused(bad.ToCompactCode(), "is not a wss:// address");
    }

    [Fact]
    public void SelfInvite_IsRefusedByTheGuest()
    {
        InviteCode invite = InviteCode.Parse(Sample().ToCompactCode());

        Assert.Contains("your own invite", invite.CheckJoinableBy(HostKey.PublicKey));
        Assert.Null(invite.CheckJoinableBy(GuestKey.PublicKey));
    }

    [Fact]
    public void Validate_ChecksAnInviteBuiltInCode()
    {
        var ex = Assert.Throws<InviteFormatException>(() => Sample(komi: 21).Validate());
        Assert.Contains("komi 21", ex.Message);
        Sample().Validate();
    }

    private static string Recode(InviteCode invite, string find, string replace)
    {
        string code = invite.ToCompactCode()["TAK2_".Length..].Replace('-', '+').Replace('_', '/');
        code = code.PadRight(code.Length + (4 - code.Length % 4) % 4, '=');
        string json = Encoding.UTF8.GetString(Convert.FromBase64String(code));
        Assert.Contains(find, json);
        json = json.Replace(find, replace);
        return "TAK2_" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

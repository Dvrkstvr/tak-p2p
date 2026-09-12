using System;
using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Transport.Matchmaking;
using Xunit;

namespace TakEngine.Transport.Tests;

public class InviteCodeTests
{
    [Fact]
    public void InviteCode_ToUriAndParse_RoundTripsAccurately()
    {
        var gameId = Guid.NewGuid();
        string hostPub = "3bf0c63fcb93463407af97b5e0918838e64edd9d071293ad011663f1d7b6af94";
        var relays = new[] { "wss://relay.damus.io", "wss://nos.lol" };
        string seed = "aabbcc112233";

        var original = new InviteCode(gameId, hostPub, BoardSize.Five, relays, seed);

        string uri = original.ToUri();
        Assert.StartsWith("tak://invite?", uri);
        Assert.Contains(gameId.ToString(), uri);
        Assert.Contains(hostPub, uri);

        var parsed = InviteCode.Parse(uri);

        Assert.Equal(original.GameId, parsed.GameId);
        Assert.Equal(original.HostPubKey, parsed.HostPubKey);
        Assert.Equal(original.BoardSize, parsed.BoardSize);
        Assert.Equal(original.Relays, parsed.Relays);
        Assert.Equal(original.SeedHex, parsed.SeedHex);
    }

    [Fact]
    public void InviteCode_ToCompactCodeAndParse_RoundTripsAccurately()
    {
        var gameId = Guid.NewGuid();
        string hostPub = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        var relays = new[] { "wss://relay.primal.net" };

        var original = new InviteCode(gameId, hostPub, BoardSize.Six, relays);

        string compact = original.ToCompactCode();
        Assert.StartsWith("TAK1_", compact);

        var parsed = InviteCode.Parse(compact);

        Assert.Equal(original.GameId, parsed.GameId);
        Assert.Equal(original.HostPubKey, parsed.HostPubKey);
        Assert.Equal(original.BoardSize, parsed.BoardSize);
        Assert.Equal(original.Relays, parsed.Relays);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void InviteCode_ParseEmpty_ThrowsArgumentException(string input)
    {
        Assert.Throws<ArgumentException>(() => InviteCode.Parse(input));
    }

    [Fact]
    public void InviteCode_ToWebUrlAndParse_RoundTripsAccurately()
    {
        var gameId = Guid.NewGuid();
        string hostPub = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
        var relays = new[] { "wss://relay.damus.io" };

        var original = new InviteCode(gameId, hostPub, BoardSize.Four, relays);
        string url = original.ToWebUrl("https://tak.game/play/");

        Assert.StartsWith("https://tak.game/play/?invite=TAK1_", url);

        var parsed = InviteCode.Parse(url);
        Assert.Equal(original.GameId, parsed.GameId);
        Assert.Equal(original.HostPubKey, parsed.HostPubKey);
        Assert.Equal(original.BoardSize, parsed.BoardSize);
    }

    [Fact]
    public void InviteCode_ParseMalformedUri_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => InviteCode.Parse("tak://invite?invalid=123"));
    }
}

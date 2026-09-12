using System;
using System.Text.Json;
using TakEngine.Abstractions;
using TakEngine.Transport.Matchmaking;
using TakEngine.Transport.Nostr;
using Xunit;

namespace TakEngine.Transport.Tests;

public class NostrProfileTests
{
    [Fact]
    public void NostrProfile_CreateMetadataEvent_BuildsValidKind0Event()
    {
        string pubKey = "3bf0c63fcb93463407af97b5e0918838e64edd9d071293ad011663f1d7b6af94";
        var profile = new NostrProfile(
            Name: "kvothe",
            DisplayName: "Kvothe the Bloodless",
            About: "Edema Ruh to my bones",
            Picture: "https://example.com/avatar.png");

        var evt = NostrProfile.CreateMetadataEvent(pubKey, profile);

        Assert.Equal(0, evt.Kind);
        Assert.Equal(pubKey, evt.Pubkey);
        Assert.NotEmpty(evt.Id);
        Assert.NotEmpty(evt.Content);

        // Parse content back
        var parsed = NostrProfile.Parse(evt.Content);
        Assert.Equal("kvothe", parsed.Name);
        Assert.Equal("Kvothe the Bloodless", parsed.DisplayName);
        Assert.Equal("Kvothe the Bloodless", parsed.BestDisplayName());
        Assert.Equal("Edema Ruh to my bones", parsed.About);
        Assert.Equal("https://example.com/avatar.png", parsed.Picture);
    }

    [Fact]
    public void NostrProfile_BestDisplayName_FallsBackToNameOrEmpty()
    {
        var withDisplay = new NostrProfile(Name: "kvothe", DisplayName: "Reshi");
        Assert.Equal("Reshi", withDisplay.BestDisplayName());

        var withoutDisplay = new NostrProfile(Name: "kvothe");
        Assert.Equal("kvothe", withoutDisplay.BestDisplayName());

        var empty = new NostrProfile();
        Assert.Equal("", empty.BestDisplayName());
    }

    [Fact]
    public void InviteCode_WithNickname_RoundTripsAccurately()
    {
        var gameId = Guid.NewGuid();
        string hostPub = "3bf0c63fcb93463407af97b5e0918838e64edd9d071293ad011663f1d7b6af94";
        var relays = new[] { "wss://relay.damus.io" };

        var invite = new InviteCode(
            GameId: gameId,
            HostPubKey: hostPub,
            BoardSize: BoardSize.Five,
            Relays: relays,
            SeedHex: "1234abcd",
            HostNickname: "Kvothe");

        // URI Roundtrip
        string uri = invite.ToUri();
        Assert.Contains("nick=Kvothe", uri);

        var parsedUri = InviteCode.Parse(uri);
        Assert.Equal("Kvothe", parsedUri.HostNickname);

        // Compact code roundtrip
        string compact = invite.ToCompactCode();
        var parsedCompact = InviteCode.Parse(compact);
        Assert.Equal("Kvothe", parsedCompact.HostNickname);
    }
}

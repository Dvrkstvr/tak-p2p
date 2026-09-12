using System;
using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Transport.Matchmaking;
using Xunit;

namespace TakEngine.Transport.Tests;

public class MatchmakingHandshakeTests
{
    [Fact]
    public void CreateBroadcastEvent_HasCorrectKindTagsAndTtl()
    {
        string pubkey = "3bf0c63fcb93463407af97b5e0918838e64edd9d071293ad011663f1d7b6af94";
        var relays = new[] { "wss://relay.damus.io" };

        var evt = QuickPlayMatchmaker.CreateBroadcastEvent(pubkey, BoardSize.Five, relays);

        Assert.Equal(QuickPlayMatchmaker.EphemeralQuickPlayKind, evt.Kind);
        Assert.Equal(pubkey, evt.Pubkey);
        Assert.Contains(evt.Tags, t => t.Count >= 2 && t[0] == "t" && t[1] == QuickPlayMatchmaker.QuickPlayTag);
        Assert.Contains(evt.Tags, t => t.Count >= 2 && t[0] == "board_size" && t[1] == "5");
        Assert.Contains(evt.Tags, t => t.Count >= 2 && t[0] == "expiration");

        bool parsed = QuickPlayMatchmaker.TryParseBroadcastEvent(evt, out var size, out var payload);
        Assert.True(parsed);
        Assert.Equal(BoardSize.Five, size);
        Assert.NotNull(payload);
        Assert.Equal(pubkey, payload!.EphemeralPubKey);
        Assert.Equal(relays, payload.Relays);
    }

    [Fact]
    public void ColorResolver_IsDeterministic_ForBothPeers()
    {
        string alicePub = "aaaa1111222233334444555566667777888899990000aaaabbbbccccddddeeee";
        string bobPub = "bbbb1111222233334444555566667777888899990000aaaabbbbccccddddeeee";
        string seed = "deadbeefcafebabe1234567890abcdef";

        // Alice computes colors
        (PlayerColor aliceLocal, PlayerColor aliceOpponent) = ColorResolver.ResolveColors(seed, alicePub, bobPub);

        // Bob computes colors independently
        (PlayerColor bobLocal, PlayerColor bobOpponent) = ColorResolver.ResolveColors(seed, bobPub, alicePub);

        // Alice's assigned color must equal Bob's perception of Alice
        Assert.Equal(aliceLocal, bobOpponent);
        Assert.Equal(bobLocal, aliceOpponent);

        // One player is White, other is Black
        Assert.NotEqual(aliceLocal, bobLocal);
        Assert.True((aliceLocal == PlayerColor.White && bobLocal == PlayerColor.Black) ||
                    (aliceLocal == PlayerColor.Black && bobLocal == PlayerColor.White));
    }

    [Fact]
    public void ChallengeHandshake_EstablishesSymmetricMatchSessions()
    {
        string alicePub = "alice_pub_key_1234";
        string bobPub = "bob_pub_key_5678";
        var relays = new[] { "wss://relay.damus.io" };

        // Alice creates challenge proposal
        var proposal = QuickPlayMatchmaker.CreateChallenge(BoardSize.Five, alicePub);

        // Bob accepts challenge proposal
        var bobSession = QuickPlayMatchmaker.AcceptChallenge(proposal, bobPub, relays);

        // Alice finalizes challenge upon receipt of acceptance
        var aliceSession = QuickPlayMatchmaker.FinalizeChallengeOnProposer(proposal, bobPub, relays);

        // Both sessions agree on GameId, BoardSize, Relays, and Colors
        Assert.Equal(proposal.GameId, aliceSession.GameId);
        Assert.Equal(proposal.GameId, bobSession.GameId);
        Assert.Equal(BoardSize.Five, aliceSession.BoardSize);
        Assert.Equal(BoardSize.Five, bobSession.BoardSize);

        Assert.Equal(bobPub, aliceSession.OpponentPubKey);
        Assert.Equal(alicePub, bobSession.OpponentPubKey);

        Assert.NotEqual(aliceSession.LocalColor, bobSession.LocalColor);
        Assert.Equal(relays, aliceSession.Relays);
        Assert.Equal(relays, bobSession.Relays);
    }
}

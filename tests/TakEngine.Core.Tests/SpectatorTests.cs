using System;
using System.Collections.Generic;
using TakEngine.Abstractions;
using TakEngine.Core.Cryptography;
using TakEngine.Core.Serialization;
using TakEngine.Core.Session;
using Xunit;

namespace TakEngine.Core.Tests;

public class SpectatorTests
{
    [Fact]
    public void DelayedBroadcastQueue_BuffersAndReleasesInOrder()
    {
        var queue = new DelayedBroadcastQueue();
        var gameId = Guid.NewGuid();
        var now = new DateTime(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);

        var env1 = new BroadcastEnvelope(gameId, 1, "pub1", "hash0", now, "a1", "sig1");
        var env2 = new BroadcastEnvelope(gameId, 2, "pub2", "hash1", now.AddSeconds(10), "e5", "sig2");

        // Enqueue with 120s and 60s delays
        queue.Enqueue(env1, TimeSpan.FromSeconds(120), now);
        queue.Enqueue(env2, TimeSpan.FromSeconds(60), now);

        Assert.Equal(2, queue.Count);

        // At now + 30s, nothing is ready
        var ready30 = queue.DequeueReady(now.AddSeconds(30));
        Assert.Empty(ready30);
        Assert.Equal(2, queue.Count);

        // At now + 70s, env2 (due at +60s) is released
        var ready70 = queue.DequeueReady(now.AddSeconds(70));
        Assert.Single(ready70);
        Assert.Equal(2, ready70[0].TurnIndex);
        Assert.Equal(1, queue.Count);

        // At now + 130s, env1 (due at +120s) is released
        var ready130 = queue.DequeueReady(now.AddSeconds(130));
        Assert.Single(ready130);
        Assert.Equal(1, ready130[0].TurnIndex);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void DelayedBroadcastQueue_Cancel_RemovesTargetGameOnly()
    {
        var queue = new DelayedBroadcastQueue();
        var game1 = Guid.NewGuid();
        var game2 = Guid.NewGuid();
        var now = DateTime.UtcNow;

        queue.Enqueue(new BroadcastEnvelope(game1, 1, "p1", "h0", now, "a1", "s1"), TimeSpan.FromMinutes(1));
        queue.Enqueue(new BroadcastEnvelope(game1, 2, "p2", "h1", now, "b2", "s2"), TimeSpan.FromMinutes(2));
        queue.Enqueue(new BroadcastEnvelope(game2, 1, "p3", "h0", now, "c3", "s3"), TimeSpan.FromMinutes(1));

        Assert.Equal(3, queue.Count);

        int cancelled = queue.Cancel(game1);
        Assert.Equal(2, cancelled);
        Assert.Equal(1, queue.Count);

        var ready = queue.DequeueReady(now.AddMinutes(5));
        Assert.Single(ready);
        Assert.Equal(game2, ready[0].GameId);
    }

    [Fact]
    public void SpectatorGameSession_IngestsValidMoves_AndAdvancesBoard()
    {
        var whiteKeys = CryptoSigner.GenerateKeyPair();
        var blackKeys = CryptoSigner.GenerateKeyPair();
        var gameId = Guid.NewGuid();

        using var session = new SpectatorGameSession(
            gameId,
            BoardSize.Five,
            whiteKeys.PublicKeyHex,
            blackKeys.PublicKeyHex,
            tournamentId: "swiss_round_1",
            whitePlayerElo: 1750,
            blackPlayerElo: 1810);

        var moveEvents = new List<TakMove>();
        session.OnMoveReceived += (_, move) => moveEvents.Add(move);

        // Turn 1: White places flat (in first turn, places opponent color)
        string genesisHash = session.LastStateHash;
        var env1 = CreateSignedEnvelope(gameId, 1, whiteKeys, genesisHash, "a1", DateTime.UtcNow);

        bool ok1 = session.IngestEnvelope(env1);
        Assert.True(ok1);
        Assert.Single(moveEvents);
        Assert.Equal(PlayerColor.Black, session.CurrentTurnColor);

        // Turn 1: Black places flat
        var env2 = CreateSignedEnvelope(gameId, 1, blackKeys, session.LastStateHash, "e5", DateTime.UtcNow);
        bool ok2 = session.IngestEnvelope(env2);
        Assert.True(ok2);
        Assert.Equal(2, moveEvents.Count);
        Assert.Equal(2, session.CurrentTurnIndex);
        Assert.Equal(PlayerColor.White, session.CurrentTurnColor);

        // Historical snapshots check
        var snapshot0 = session.GetHistoricalSnapshot(0);
        var snapshot1 = session.GetHistoricalSnapshot(1);
        var snapshot2 = session.GetHistoricalSnapshot(2);
        Assert.Empty(snapshot0.Stacks);
        Assert.Single(snapshot1.Stacks);
        Assert.Equal(2, snapshot2.Stacks.Count);
    }

    [Fact]
    public void SpectatorGameSession_DetectsTamperedSignature_AndFiresDesync()
    {
        var whiteKeys = CryptoSigner.GenerateKeyPair();
        var blackKeys = CryptoSigner.GenerateKeyPair();
        var gameId = Guid.NewGuid();

        using var session = new SpectatorGameSession(
            gameId,
            BoardSize.Five,
            whiteKeys.PublicKeyHex,
            blackKeys.PublicKeyHex);

        ProtocolViolationException? caughtViolation = null;
        session.OnStateDesyncDetected += ex => caughtViolation = ex;

        // Attacker creates envelope with invalid signature
        var env = new BroadcastEnvelope(
            gameId,
            1,
            whiteKeys.PublicKeyHex,
            session.LastStateHash,
            DateTime.UtcNow,
            "a1",
            "deadbeefbadsignature00112233");

        bool ok = session.IngestEnvelope(env);
        Assert.False(ok);
        Assert.NotNull(caughtViolation);
        Assert.Contains("Invalid cryptographic signature", caughtViolation.Message);
    }

    [Fact]
    public void SpectatorGameSession_DetectsBrokenHashChain_AndFiresDesync()
    {
        var whiteKeys = CryptoSigner.GenerateKeyPair();
        var blackKeys = CryptoSigner.GenerateKeyPair();
        var gameId = Guid.NewGuid();

        using var session = new SpectatorGameSession(
            gameId,
            BoardSize.Five,
            whiteKeys.PublicKeyHex,
            blackKeys.PublicKeyHex);

        ProtocolViolationException? caughtViolation = null;
        session.OnStateDesyncDetected += ex => caughtViolation = ex;

        // Envelope with wrong prev state hash
        var env = CreateSignedEnvelope(gameId, 1, whiteKeys, "wrong_prev_hash", "a1", DateTime.UtcNow);

        bool ok = session.IngestEnvelope(env);
        Assert.False(ok);
        Assert.NotNull(caughtViolation);
        Assert.Contains("State hash chain broken", caughtViolation.Message);
    }

    [Fact]
    public void SpectatorGameSession_DetectsWrongPlayerPubKey_AndFiresDesync()
    {
        var whiteKeys = CryptoSigner.GenerateKeyPair();
        var blackKeys = CryptoSigner.GenerateKeyPair();
        var imposterKeys = CryptoSigner.GenerateKeyPair();
        var gameId = Guid.NewGuid();

        using var session = new SpectatorGameSession(
            gameId,
            BoardSize.Five,
            whiteKeys.PublicKeyHex,
            blackKeys.PublicKeyHex);

        ProtocolViolationException? caughtViolation = null;
        session.OnStateDesyncDetected += ex => caughtViolation = ex;

        // Imposter attempts to play White's turn
        var env = CreateSignedEnvelope(gameId, 1, imposterKeys, session.LastStateHash, "a1", DateTime.UtcNow);

        bool ok = session.IngestEnvelope(env);
        Assert.False(ok);
        Assert.NotNull(caughtViolation);
        Assert.Contains("Player pubkey mismatch", caughtViolation.Message);
    }

    [Fact]
    public void SpectatorGameSession_DetectsIllegalMove_AndFiresDesync()
    {
        var whiteKeys = CryptoSigner.GenerateKeyPair();
        var blackKeys = CryptoSigner.GenerateKeyPair();
        var gameId = Guid.NewGuid();

        using var session = new SpectatorGameSession(
            gameId,
            BoardSize.Four,
            whiteKeys.PublicKeyHex,
            blackKeys.PublicKeyHex);

        ProtocolViolationException? caughtViolation = null;
        session.OnStateDesyncDetected += ex => caughtViolation = ex;

        // In turn 1 (first turn placement), placing a Capstone is illegal in Tak rules
        var env = CreateSignedEnvelope(gameId, 1, whiteKeys, session.LastStateHash, "Ca1", DateTime.UtcNow);

        bool ok = session.IngestEnvelope(env);
        Assert.False(ok);
        Assert.NotNull(caughtViolation);
        Assert.Contains("Illegal move attempted", caughtViolation.Message);
    }

    private static BroadcastEnvelope CreateSignedEnvelope(
        Guid gameId,
        int turnIndex,
        KeyPair keys,
        string prevStateHash,
        string ptnMove,
        DateTime timestamp)
    {
        var dummy = new BroadcastEnvelope(
            gameId,
            turnIndex,
            keys.PublicKeyHex,
            prevStateHash,
            timestamp,
            ptnMove,
            "");

        string payload = dummy.GetSigningPayload();
        string signature = CryptoSigner.Sign(keys.PrivateKeyHex, payload);

        return dummy with { Signature = signature };
    }
}

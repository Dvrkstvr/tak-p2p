using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using TakEngine.Transport;
using TakEngine.Transport.Nostr;
using Xunit;

namespace TakEngine.Transport.Tests;

public class TransportBenchmarkTests
{
    [Fact]
    public void RoundTripPayloadProcessing_CompletesWellUnder300ms()
    {
        var gameId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        string senderPriv = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        string senderPub = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        string recipientPub = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        var envelope = new TransportEnvelope(
            GameId: gameId,
            Turn: 24,
            PlayerPubkey: senderPub,
            PrevStateHash: "abc123statehash",
            TimestampUtc: now,
            ActionType: "MOVE",
            ActionData: new ActionData("3c3+12", new ActionDataDetails("c3", "+", 3, [1, 2])),
            Signature: "signature_hex_123");

        var stopwatch = Stopwatch.StartNew();

        // 1. Serialize envelope
        string json = JsonSerializer.Serialize(envelope);

        // 2. Derive shared secret and Encrypt with NIP-44
        byte[] senderSecret = Nip44Encryption.DeriveSharedSecret(senderPriv, recipientPub);
        string encryptedContent = Nip44Encryption.Encrypt(json, senderSecret);

        // 3. Wrap in NostrEvent
        var evt = new NostrEvent
        {
            Pubkey = senderPub,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Kind = 4,
            Tags = [["p", recipientPub], ["g", gameId.ToString()]],
            Content = encryptedContent
        };
        evt.Id = evt.ComputeId();

        // 4. Simulate receiver: decrypt and parse
        byte[] receiverSecret = Nip44Encryption.DeriveSharedSecret(senderPriv, recipientPub);
        string decryptedJson = Nip44Encryption.Decrypt(evt.Content, receiverSecret);
        var unpackedEnvelope = JsonSerializer.Deserialize<TransportEnvelope>(decryptedJson);

        stopwatch.Stop();

        Assert.NotNull(unpackedEnvelope);
        Assert.Equal(envelope.GameId, unpackedEnvelope!.GameId);
        Assert.Equal(envelope.Turn, unpackedEnvelope.Turn);
        Assert.Equal(envelope.ActionData.Ptn, unpackedEnvelope.ActionData.Ptn);

        // Milestone M1.4 acceptance criterion: round-trip verified under 300 ms
        Assert.True(stopwatch.ElapsedMilliseconds < 300,
            $"Processing time was {stopwatch.ElapsedMilliseconds} ms, expected < 300 ms.");
    }
}

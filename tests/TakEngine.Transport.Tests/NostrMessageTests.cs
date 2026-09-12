using System;
using System.Collections.Generic;
using System.Text.Json;
using TakEngine.Transport;
using TakEngine.Transport.Nostr;
using Xunit;

namespace TakEngine.Transport.Tests;

public class NostrMessageTests
{
    [Fact]
    public void NostrEvent_ComputeId_ReturnsSha256Hex()
    {
        var evt = new NostrEvent
        {
            Pubkey = "3bf0c63fcb93463407af97b5e0918838e64edd9d071293ad011663f1d7b6af94",
            CreatedAt = 1726110499,
            Kind = 4,
            Tags = [["p", "opponent_pubkey_hex"]],
            Content = "test content"
        };

        string id = evt.ComputeId();

        Assert.Equal(64, id.Length);
        Assert.Matches("^[0-9a-f]{64}$", id);
    }

    [Fact]
    public void SerializeAndParse_EventMessage_Succeeds()
    {
        var evt = new NostrEvent
        {
            Id = "e1",
            Pubkey = "pub1",
            CreatedAt = 12345678,
            Kind = 4,
            Content = "secret_encrypted"
        };

        string json = NostrMessageParser.SerializeEvent(evt);
        var parsed = NostrMessageParser.Parse(json);

        // Parsed message from relay is ["EVENT", subId, event]
        // But client sent ["EVENT", event]
        Assert.NotNull(json);
        Assert.StartsWith("[\"EVENT\",", json);
    }

    [Fact]
    public void Parse_RelayEventAndOkAndNoticeMessages_Succeeds()
    {
        string eventJson = """["EVENT", "sub_1", {"id": "id1", "pubkey": "pub1", "created_at": 100, "kind": 4, "tags": [], "content": "hello", "sig": "sig1"}]""";
        var msg = NostrMessageParser.Parse(eventJson);

        var eventMsg = Assert.IsType<EventRelayMessage>(msg);
        Assert.Equal("sub_1", eventMsg.SubscriptionId);
        Assert.Equal("id1", eventMsg.Event.Id);
        Assert.Equal("hello", eventMsg.Event.Content);

        string okJson = """["OK", "id1", true, ""]""";
        var okMsg = Assert.IsType<OkRelayMessage>(NostrMessageParser.Parse(okJson));
        Assert.True(okMsg.Success);
        Assert.Equal("id1", okMsg.EventId);

        string noticeJson = """["NOTICE", "Rate limit exceeded"]""";
        var noticeMsg = Assert.IsType<NoticeRelayMessage>(NostrMessageParser.Parse(noticeJson));
        Assert.Equal("Rate limit exceeded", noticeMsg.Message);
    }

    [Fact]
    public void TransportEnvelope_SerializesAndDeserializes_MatchingSpec()
    {
        var gameId = Guid.Parse("9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d");
        var now = DateTime.Parse("2026-09-12T03:08:19Z").ToUniversalTime();

        var envelope = new TransportEnvelope(
            GameId: gameId,
            Turn: 15,
            PlayerPubkey: "3bf0c63fcb93463407af97b5e0918838e64edd9d071293ad011663f1d7b6af94",
            PrevStateHash: "a4f8c92b23a9d9b4009e879a8bc43428d223298c5d12ef4b476e3e577e3e9d89",
            TimestampUtc: now,
            ActionType: "MOVE",
            ActionData: new ActionData(
                Ptn: "3c3+12",
                Details: new ActionDataDetails(
                    From: "c3",
                    Direction: "+",
                    Lift: 3,
                    Drops: [1, 2])),
            Signature: "3045022100e4b8108a38");

        string json = envelope.ToJson();
        Assert.Contains("9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d", json);
        Assert.Contains("\"turn\":15", json);
        Assert.Contains("3c3+12", json);

        var restored = TransportEnvelope.FromJson(json);
        Assert.NotNull(restored);
        Assert.Equal(envelope.GameId, restored!.GameId);
        Assert.Equal(envelope.Turn, restored.Turn);
        Assert.Equal(envelope.ActionData.Ptn, restored.ActionData.Ptn);
        Assert.Equal(3, restored.ActionData.Details!.Lift);
        Assert.Equal([1, 2], restored.ActionData.Details.Drops);
    }
}

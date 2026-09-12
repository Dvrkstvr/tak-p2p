using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TakEngine.Transport;

public sealed record ActionDataDetails(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("lift")] int Lift,
    [property: JsonPropertyName("drops")] IReadOnlyList<int> Drops);

public sealed record ActionData(
    [property: JsonPropertyName("ptn")] string Ptn,
    [property: JsonPropertyName("details")] ActionDataDetails? Details = null);

public sealed record TransportEnvelope(
    [property: JsonPropertyName("game_id")] Guid GameId,
    [property: JsonPropertyName("turn")] int Turn,
    [property: JsonPropertyName("player_pubkey")] string PlayerPubkey,
    [property: JsonPropertyName("prev_state_hash")] string PrevStateHash,
    [property: JsonPropertyName("timestamp_utc")] DateTime TimestampUtc,
    [property: JsonPropertyName("action_type")] string ActionType,
    [property: JsonPropertyName("action_data")] ActionData ActionData,
    [property: JsonPropertyName("signature")] string Signature)
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);
    public static TransportEnvelope? FromJson(string json) => JsonSerializer.Deserialize<TransportEnvelope>(json, SerializerOptions);
}

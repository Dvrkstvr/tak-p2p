using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TakEngine.Transport.Nostr;

public sealed class NostrEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("pubkey")]
    public string Pubkey { get; set; } = "";

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("kind")]
    public int Kind { get; set; }

    [JsonPropertyName("tags")]
    public List<List<string>> Tags { get; set; } = new();

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("sig")]
    public string Sig { get; set; } = "";

    public string ComputeId()
    {
        // NIP-01 serialized event: [0, pubkey, created_at, kind, tags, content]
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartArray();
        writer.WriteNumberValue(0);
        writer.WriteStringValue(Pubkey);
        writer.WriteNumberValue(CreatedAt);
        writer.WriteNumberValue(Kind);

        writer.WriteStartArray();
        foreach (var tag in Tags)
        {
            writer.WriteStartArray();
            foreach (var item in tag)
            {
                writer.WriteStringValue(item);
            }
            writer.WriteEndArray();
        }
        writer.WriteEndArray();

        writer.WriteStringValue(Content);
        writer.WriteEndArray();
        writer.Flush();

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexStringLower(hash);
    }
}

public sealed class NostrFilter
{
    [JsonPropertyName("ids")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Ids { get; set; }

    [JsonPropertyName("authors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Authors { get; set; }

    [JsonPropertyName("kinds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? Kinds { get; set; }

    [JsonPropertyName("#p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? TagP { get; set; }

    [JsonPropertyName("#e")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? TagE { get; set; }

    [JsonPropertyName("#t")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? TagT { get; set; }

    [JsonPropertyName("since")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Since { get; set; }

    [JsonPropertyName("until")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Until { get; set; }

    [JsonPropertyName("limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Limit { get; set; }
}

public abstract record NostrRelayMessage;

public sealed record EventRelayMessage(string SubscriptionId, NostrEvent Event) : NostrRelayMessage;
public sealed record OkRelayMessage(string EventId, bool Success, string Message) : NostrRelayMessage;
public sealed record EoseRelayMessage(string SubscriptionId) : NostrRelayMessage;
public sealed record NoticeRelayMessage(string Message) : NostrRelayMessage;

public static class NostrMessageParser
{
    public static string SerializeEvent(NostrEvent evt)
    {
        return JsonSerializer.Serialize(new object[] { "EVENT", evt });
    }

    public static string SerializeReq(string subId, params NostrFilter[] filters)
    {
        var elements = new List<object> { "REQ", subId };
        elements.AddRange(filters);
        return JsonSerializer.Serialize(elements);
    }

    public static string SerializeClose(string subId)
    {
        return JsonSerializer.Serialize(new object[] { "CLOSE", subId });
    }

    public static NostrRelayMessage? Parse(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return null;

        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return null;

        string verb = root[0].GetString() ?? "";

        return verb switch
        {
            "EVENT" when root.GetArrayLength() >= 3 =>
                new EventRelayMessage(
                    root[1].GetString() ?? "",
                    JsonSerializer.Deserialize<NostrEvent>(root[2].GetRawText())!),

            "OK" when root.GetArrayLength() >= 4 =>
                new OkRelayMessage(
                    root[1].GetString() ?? "",
                    root[2].GetBoolean(),
                    root[3].GetString() ?? ""),

            "EOSE" when root.GetArrayLength() >= 2 =>
                new EoseRelayMessage(root[1].GetString() ?? ""),

            "NOTICE" when root.GetArrayLength() >= 2 =>
                new NoticeRelayMessage(root[1].GetString() ?? ""),

            _ => null
        };
    }
}

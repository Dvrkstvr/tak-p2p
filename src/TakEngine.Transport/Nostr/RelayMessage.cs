using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TakEngine.Transport.Nostr;

/// <summary>
/// One NIP-01 frame in either direction. <see cref="Parse"/> never throws: anything it can't read becomes <see cref="Unknown"/>.
/// Client frames: EVENT (<see cref="Publish"/>), REQ, CLOSE, AUTH. Relay frames: EVENT (<see cref="Event"/>), OK, EOSE, NOTICE,
/// CLOSED, AUTH (<see cref="AuthChallenge"/>).
/// </summary>
public abstract record RelayMessage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Client -&gt; relay <c>["EVENT", event]</c>.</summary>
    public sealed record Publish(NostrEvent NostrEvent) : RelayMessage;

    /// <summary>Relay -&gt; client <c>["EVENT", sub, event]</c>.</summary>
    public sealed record Event(string SubscriptionId, NostrEvent NostrEvent) : RelayMessage;

    public sealed record Req(string SubscriptionId, IReadOnlyList<NostrFilter> Filters) : RelayMessage;

    public sealed record Close(string SubscriptionId) : RelayMessage;

    public sealed record Ok(string EventId, bool Accepted, string Message) : RelayMessage
    {
        /// <summary>The machine-readable prefix before ':' (NIP-01: duplicate, pow, blocked, rate-limited, invalid, restricted, auth-required, error).</summary>
        public string Prefix => ReasonPrefix(Message);
    }

    public sealed record Eose(string SubscriptionId) : RelayMessage;

    public sealed record Notice(string Message) : RelayMessage;

    public sealed record Closed(string SubscriptionId, string Message) : RelayMessage
    {
        public string Prefix => ReasonPrefix(Message);
    }

    /// <summary>Relay -&gt; client <c>["AUTH", challenge]</c> (NIP-42).</summary>
    public sealed record AuthChallenge(string Challenge) : RelayMessage;

    /// <summary>Client -&gt; relay <c>["AUTH", signed event]</c> (NIP-42).</summary>
    public sealed record AuthResponse(NostrEvent NostrEvent) : RelayMessage;

    public sealed record Unknown(string Raw) : RelayMessage;

    public static string ReasonPrefix(string message)
    {
        int colon = message.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? "" : message[..colon];
    }

    public string Format()
    {
        JsonArray array = this switch
        {
            Publish p => ["EVENT", Node(p.NostrEvent)],
            Event e => ["EVENT", e.SubscriptionId, Node(e.NostrEvent)],
            Req r => [.. new JsonNode?[] { "REQ", r.SubscriptionId }.Concat(r.Filters.Select(f => Node(f)))],
            Close c => ["CLOSE", c.SubscriptionId],
            Ok o => ["OK", o.EventId, o.Accepted, o.Message],
            Eose e => ["EOSE", e.SubscriptionId],
            Notice n => ["NOTICE", n.Message],
            Closed c => ["CLOSED", c.SubscriptionId, c.Message],
            AuthChallenge a => ["AUTH", a.Challenge],
            AuthResponse a => ["AUTH", Node(a.NostrEvent)],
            Unknown u => throw new InvalidOperationException("An unknown frame cannot be formatted."),
            _ => throw new InvalidOperationException(),
        };
        return array.ToJsonString(Options);
    }

    public static RelayMessage Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new Unknown(raw ?? "");
        try
        {
            using JsonDocument doc = JsonDocument.Parse(raw);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 || root[0].ValueKind != JsonValueKind.String)
                return new Unknown(raw);
            int n = root.GetArrayLength();
            return root[0].GetString() switch
            {
                "EVENT" when n == 2 && root[1].ValueKind == JsonValueKind.Object => new Publish(EventFrom(root[1])),
                "EVENT" when n == 3 && IsString(root[1]) && root[2].ValueKind == JsonValueKind.Object => new Event(root[1].GetString()!, EventFrom(root[2])),
                "REQ" when n >= 2 && IsString(root[1]) && Enumerable.Range(2, n - 2).All(i => root[i].ValueKind == JsonValueKind.Object) =>
                    new Req(root[1].GetString()!, Enumerable.Range(2, n - 2).Select(i => root[i].Deserialize<NostrFilter>(Options)!).ToList()),
                "CLOSE" when n == 2 && IsString(root[1]) => new Close(root[1].GetString()!),
                "OK" when n >= 3 && IsString(root[1]) && root[2].ValueKind is JsonValueKind.True or JsonValueKind.False =>
                    new Ok(root[1].GetString()!, root[2].GetBoolean(), n >= 4 && IsString(root[3]) ? root[3].GetString()! : ""),
                "EOSE" when n == 2 && IsString(root[1]) => new Eose(root[1].GetString()!),
                "NOTICE" when n >= 2 && IsString(root[1]) => new Notice(root[1].GetString()!),
                "CLOSED" when n >= 2 && IsString(root[1]) => new Closed(root[1].GetString()!, n >= 3 && IsString(root[2]) ? root[2].GetString()! : ""),
                "AUTH" when n == 2 && IsString(root[1]) => new AuthChallenge(root[1].GetString()!),
                "AUTH" when n == 2 && root[1].ValueKind == JsonValueKind.Object => new AuthResponse(EventFrom(root[1])),
                _ => new Unknown(raw),
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return new Unknown(raw);
        }
    }

    private static bool IsString(JsonElement e) => e.ValueKind == JsonValueKind.String;

    /// <summary>Reads an event object; fields of the wrong JSON type throw (caught by <see cref="Parse"/>), missing ones stay empty.</summary>
    private static NostrEvent EventFrom(JsonElement e) => e.Deserialize<NostrEvent>(Options) ?? throw new FormatException("null event");

    private static JsonNode? Node<T>(T value) => JsonSerializer.SerializeToNode(value, Options);
}

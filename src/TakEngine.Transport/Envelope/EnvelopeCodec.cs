using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TakEngine.Abstractions;
using TakEngine.Crypto;
using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Envelope;

/// <summary>
/// The one envelope codec (F-033): <see cref="SignedGameAction"/> &lt;-&gt; JSON plaintext &lt;-&gt; NIP-44 v2 &lt;-&gt; kind-3825 event
/// with tags exactly <c>["p", recipient]</c> and <c>["g", game id]</c> (pipeline/architecture.md "Wire format"). Pure: created_at,
/// the NIP-44 nonce and the BIP-340 aux bytes are arguments. <see cref="Decode"/> never throws on peer input and never decrypts
/// an event whose id, signature or author has not been checked first.
/// </summary>
public static class EnvelopeCodec
{
    public const int Kind = 3825;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    private static readonly HashSet<string> PlaintextKeys = new(StringComparer.Ordinal)
    {
        "pv", "action_type", "game_id", "turn", "player_pubkey", "prev_state_hash", "action_data", "signature",
    };

    public static NostrEvent Encode(
        SecretKey sender,
        PublicKey recipient,
        SignedGameAction action,
        long createdAt,
        byte[] nonce32,
        byte[] aux32)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(action);
        if (!string.Equals(action.PlayerPubKey, sender.PublicKey.ToHex(), StringComparison.Ordinal))
            throw new ArgumentException("The action's player_pubkey must be the sender's key (D-011).", nameof(action));

        string plaintext = ToPlaintext(action);
        string content = Nip44.Encrypt(plaintext, Nip44.ConversationKey(sender, recipient), nonce32);
        var tags = new List<IReadOnlyList<string>>
        {
            new[] { "p", recipient.ToHex() },
            new[] { "g", action.GameId.ToString("D") },
        };
        return NostrEvents.Sign(sender, createdAt, Kind, tags, content, aux32);
    }

    /// <summary>The inner JSON (relaxed escaping, fixed key order). Exposed for tests and the 64 KiB check.</summary>
    public static string ToPlaintext(SignedGameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteNumber("pv", action.Pv);
            w.WriteString("action_type", action.ActionType.ToWire());
            w.WriteString("game_id", action.GameId.ToString("D"));
            w.WriteNumber("turn", action.Turn);
            w.WriteString("player_pubkey", action.PlayerPubKey);
            w.WriteString("prev_state_hash", action.PrevStateHash);
            w.WritePropertyName("action_data");
            w.WriteStartObject();
            foreach (var key in action.ActionData.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                ActionValue value = action.ActionData[key];
                if (value.Text is not null)
                    w.WriteString(key, value.Text);
                else
                    w.WriteNumber(key, value.Integer ?? 0);
            }
            w.WriteEndObject();
            w.WriteString("signature", action.Signature);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static DecodeResult Decode(NostrEvent? evt, DecodeContext ctx) => Decode(evt, ctx, Nip44.Decrypt);

    /// <summary>Same as <see cref="Decode(NostrEvent?, DecodeContext)"/> with the NIP-44 decrypt call injectable (tests spy on it).</summary>
    internal static DecodeResult Decode(NostrEvent? evt, DecodeContext ctx, Func<string, byte[], string> decrypt)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // Row 1: id + sig. Nothing else about the event is trusted before this.
        if (!NostrEvents.Verify(evt))
            return new DecodeResult(DecodeVerdict.Row1BadIdOrSig);

        string me = ctx.Me.PublicKey.ToHex();
        string gameId = ctx.GameId.ToString("D");
        if (evt!.Kind != Kind || evt.Tags.Count != 2
            || evt.Tags[0].Count != 2 || evt.Tags[0][0] != "p"
            || evt.Tags[1].Count != 2 || evt.Tags[1][0] != "g" || evt.Tags[1][1] != gameId)
            return new DecodeResult(DecodeVerdict.NotForUs, Detail: "kind or tags");

        string recipientHex = evt.Tags[0][1];
        bool isOwn = evt.Pubkey == me;

        // Row 2: author.
        PublicKey counterparty;
        if (isOwn)
        {
            // Our own event echoed back: decrypt with the conversation key of its recipient.
            if (!PublicKey.TryFromHex(recipientHex, out PublicKey? recipient) || recipient.ToHex() != recipientHex)
                return new DecodeResult(DecodeVerdict.NotForUs, Detail: "own event with a bad p tag");
            if (ctx.Opponent is not null && recipient != ctx.Opponent)
                return new DecodeResult(DecodeVerdict.NotForUs, Detail: "own event to someone else");
            counterparty = recipient;
        }
        else
        {
            if (recipientHex != me)
                return new DecodeResult(DecodeVerdict.NotForUs, Detail: "addressed to someone else");
            if (ctx.Opponent is not null && evt.Pubkey != ctx.Opponent.ToHex())
                return new DecodeResult(DecodeVerdict.Row2WrongAuthor);
            counterparty = PublicKey.FromHex(evt.Pubkey); // Row 1 proved it is a valid key
        }

        // Row 3: decrypt.
        string plaintext;
        try
        {
            plaintext = decrypt(evt.Content, Nip44.ConversationKey(ctx.Me, counterparty));
        }
        catch (Exception ex) when (ex is Nip44Exception or ArgumentException or FormatException or DecoderFallbackException)
        {
            return new DecodeResult(DecodeVerdict.Row3Undecryptable, IsOwn: isOwn, Detail: ex.Message);
        }

        // Row 4: JSON, pv (judged first), game id, inner signature, player_pubkey == author.
        if (!TryParsePlaintext(plaintext, out SignedGameAction? action, out string? error, out bool incompatible))
            return new DecodeResult(incompatible ? DecodeVerdict.IncompatibleVersion : DecodeVerdict.Row4BadPayload, IsOwn: isOwn, Detail: error);
        if (action!.GameId != ctx.GameId)
            return Row4("game id mismatch", isOwn);
        if (action.PlayerPubKey != evt.Pubkey)
            return Row4("player_pubkey is not the event author", isOwn);
        if (!ActionDigest.Verify(action))
            return Row4("bad inner signature", isOwn);
        if (!isOwn && ctx.Opponent is null && action.ActionType != GameActionType.Join)
            return Row4("only a JOIN may come from an unknown author", isOwn);

        return new DecodeResult(DecodeVerdict.Accepted, action, isOwn);
    }

    private static DecodeResult Row4(string detail, bool isOwn) => new(DecodeVerdict.Row4BadPayload, IsOwn: isOwn, Detail: detail);

    private static bool TryParsePlaintext(string plaintext, out SignedGameAction? action, out string? error, out bool incompatible)
    {
        action = null;
        incompatible = false;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(plaintext);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Fail("plaintext is not a JSON object", out error);

            if (!root.TryGetProperty("pv", out JsonElement pv) || pv.ValueKind != JsonValueKind.Number || !pv.TryGetInt32(out int pvValue))
                return Fail("missing or non-integer pv", out error);
            if (pvValue != SignedGameAction.CurrentPv)
            {
                incompatible = true;
                return Fail($"pv {pvValue}", out error);
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!PlaintextKeys.Contains(property.Name) || !names.Add(property.Name))
                    return Fail($"unexpected or repeated key '{property.Name}'", out error);
            }
            if (names.Count != PlaintextKeys.Count)
                return Fail("missing keys", out error);

            if (!GameActionTypes.TryParseWire(String(root, "action_type"), out GameActionType type))
                return Fail("unknown action_type", out error);
            string? gameIdText = String(root, "game_id");
            if (gameIdText is null || !Guid.TryParseExact(gameIdText, "D", out Guid gameId) || gameId.ToString("D") != gameIdText)
                return Fail("game_id is not a lowercase uuid", out error);
            JsonElement turnElement = root.GetProperty("turn");
            if (turnElement.ValueKind != JsonValueKind.Number || !turnElement.TryGetInt32(out int turn) || turn < 0)
                return Fail("turn is not a non-negative integer", out error);
            string? pubkey = String(root, "player_pubkey");
            string? prev = String(root, "prev_state_hash");
            string? signature = String(root, "signature");
            if (pubkey is null || prev is null || signature is null)
                return Fail("player_pubkey, prev_state_hash and signature must be strings", out error);

            JsonElement data = root.GetProperty("action_data");
            if (data.ValueKind != JsonValueKind.Object)
                return Fail("action_data is not an object", out error);
            var actionData = new SortedDictionary<string, ActionValue>(StringComparer.Ordinal);
            foreach (JsonProperty property in data.EnumerateObject())
            {
                ActionValue value;
                if (property.Value.ValueKind == JsonValueKind.String)
                    value = ActionValue.Of(property.Value.GetString()!);
                else if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out long integer))
                    value = ActionValue.Of(integer);
                else
                    return Fail($"action_data '{property.Name}' is not a string or an integer", out error);
                if (!actionData.TryAdd(property.Name, value))
                    return Fail($"repeated action_data key '{property.Name}'", out error);
            }

            action = new SignedGameAction(pvValue, type, gameId, turn, pubkey, prev, actionData, signature);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            return Fail($"bad JSON: {ex.Message}", out error);
        }

        static string? String(JsonElement root, string name) =>
            root.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

        static bool Fail(string message, out string? error)
        {
            error = message;
            return false;
        }
    }
}

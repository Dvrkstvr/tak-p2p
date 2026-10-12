using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TakEngine.Abstractions;
using TakEngine.Crypto;

namespace TakEngine.Transport.Matchmaking;

/// <summary>An invite that is malformed, from an older version, or out of range. The message says which, for the user.</summary>
public sealed class InviteFormatException(string message) : FormatException(message);

/// <summary>
/// A direct invite, version 2 (F-017, D-037, D-038): game id, the host's x-only key, board size 3..8, komi in half flats 0..20,
/// relay hints, and the commitment to the host's colour nonce (never the seed itself). Three spellings: the compact
/// <c>TAK2_</c> code, a web URL carrying it as <c>?invite=</c>, and a <c>tak://invite?v=2&amp;…</c> URI. Parsing is strict and
/// never touches the network; every refusal carries a message the user can act on.
/// </summary>
public sealed record InviteCode(
    Guid GameId,
    string HostPubKey,
    BoardSize BoardSize,
    int KomiHalves,
    IReadOnlyList<string> Relays,
    string SeedCommit,
    string? HostNickname = null)
{
    public const int MaxKomiHalves = 20;

    private const string CompactPrefix = "TAK2_";
    private const string OldCompactPrefix = "TAK1_";
    private const string UriPrefix = "tak://invite";

    private const string Older = "This invite is from an older version of Tak P2P. Ask the host for a new invite.";
    private const string NotAnInvite = "This is not a Tak invite. Paste the whole TAK2_ code or link the host sent.";
    private const string Damaged = "This invite is damaged and could not be read. Ask the host to send it again.";

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public string ToCompactCode()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("gid", GameId.ToString("D"));
            w.WriteString("host", HostPubKey);
            w.WriteNumber("size", (int)BoardSize);
            w.WriteNumber("komi", KomiHalves);
            w.WriteStartArray("relays");
            foreach (string relay in Relays)
                w.WriteStringValue(relay);
            w.WriteEndArray();
            w.WriteString("seed_commit", SeedCommit);
            if (HostNickname is not null)
                w.WriteString("nick", HostNickname);
            w.WriteEndObject();
        }
        return CompactPrefix + Base64Url(buffer.WrittenSpan);
    }

    public string ToWebUrl(string baseUrl) => $"{baseUrl.TrimEnd('/')}/?invite={ToCompactCode()}";

    public string ToUri()
    {
        var sb = new StringBuilder(UriPrefix)
            .Append("?v=2")
            .Append("&gid=").Append(GameId.ToString("D"))
            .Append("&host=").Append(Uri.EscapeDataString(HostPubKey))
            .Append("&size=").Append((int)BoardSize)
            .Append("&komi=").Append(KomiHalves)
            .Append("&relays=").Append(Uri.EscapeDataString(string.Join(';', Relays)))
            .Append("&seed_commit=").Append(Uri.EscapeDataString(SeedCommit));
        if (HostNickname is not null)
            sb.Append("&nick=").Append(Uri.EscapeDataString(HostNickname));
        return sb.ToString();
    }

    /// <summary>Parses and validates any of the three spellings. Throws <see cref="InviteFormatException"/> with a user-facing message.</summary>
    public static InviteCode Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InviteFormatException("The invite is empty. Paste the TAK2_ code or link the host sent.");
        text = text.Trim();

        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            string? code = Uri.TryCreate(text, UriKind.Absolute, out Uri? url) ? QueryValue(url.Query, "invite") : null;
            if (code is null)
                throw new InviteFormatException(NotAnInvite);
            return Parse(code);
        }

        InviteCode invite;
        if (text.StartsWith(CompactPrefix, StringComparison.Ordinal))
            invite = ParseCompact(text[CompactPrefix.Length..]);
        else if (text.StartsWith(OldCompactPrefix, StringComparison.Ordinal))
            throw new InviteFormatException(Older);
        else if (text.StartsWith(UriPrefix + "?", StringComparison.OrdinalIgnoreCase))
            invite = ParseUri(text[(UriPrefix.Length + 1)..]);
        else
            throw new InviteFormatException(NotAnInvite);

        invite.Validate();
        return invite;
    }

    public static bool TryParse(string? text, out InviteCode? invite, out string? error)
    {
        try
        {
            invite = Parse(text);
            error = null;
            return true;
        }
        catch (InviteFormatException ex)
        {
            invite = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Throws <see cref="InviteFormatException"/> if a field is out of range (also for an invite built in code).</summary>
    public void Validate()
    {
        if (GameId == Guid.Empty)
            throw new InviteFormatException("The invite has no game id.");
        if (string.IsNullOrEmpty(HostPubKey))
            throw new InviteFormatException("The invite has no host key.");
        if (HostPubKey.Length != 2 * PublicKey.Length || !PublicKey.TryFromHex(HostPubKey, out _))
            throw new InviteFormatException("The invite's host key is not a valid public key.");
        if ((int)BoardSize is < 3 or > 8)
            throw new InviteFormatException($"The invite's board size {(int)BoardSize} is not supported (3 to 8).");
        if (KomiHalves is < 0 or > MaxKomiHalves)
            throw new InviteFormatException($"The invite's komi {KomiHalves} is out of range (0 to {MaxKomiHalves} half flats).");
        if (string.IsNullOrEmpty(SeedCommit))
            throw new InviteFormatException("The invite has no seed commitment, so colours could not be drawn fairly. Ask the host for a new invite.");
        if (SeedCommit.Length != 64 || !SeedCommit.All(char.IsAsciiHexDigit))
            throw new InviteFormatException("The invite's seed commitment is not 64 hex characters.");
        if (Relays is null)
            throw new InviteFormatException(Damaged);
        foreach (string relay in Relays)
        {
            if (!IsAllowedRelay(relay))
                throw new InviteFormatException($"The invite's relay '{relay}' is not a wss:// address.");
        }
    }

    /// <summary>The guest's own check: null if this key may join, else the message to show (no network traffic either way).</summary>
    public string? CheckJoinableBy(PublicKey me)
    {
        ArgumentNullException.ThrowIfNull(me);
        return string.Equals(HostPubKey, me.ToHex(), StringComparison.OrdinalIgnoreCase)
            ? "This is your own invite. Send it to your opponent, or open it with another profile or device."
            : null;
    }

    private static bool IsAllowedRelay(string? relay)
    {
        if (relay is null || !Uri.TryCreate(relay, UriKind.Absolute, out Uri? uri) || string.IsNullOrEmpty(uri.Host))
            return false;
        if (uri.Scheme == "wss")
            return true;
        // Local tools (tools/TakRelay.Local) serve plain ws on loopback.
        return uri.Scheme == "ws" && (uri.Host == "127.0.0.1" || uri.Host == "localhost");
    }

    private static InviteCode ParseCompact(string base64Url)
    {
        byte[] bytes;
        try
        {
            string b64 = base64Url.Replace('-', '+').Replace('_', '/');
            bytes = Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
        }
        catch (FormatException)
        {
            throw new InviteFormatException(Damaged);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(bytes);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InviteFormatException(Damaged);

            Guid gid = root.TryGetProperty("gid", out JsonElement g) && g.ValueKind == JsonValueKind.String && Guid.TryParse(g.GetString(), out Guid parsed)
                ? parsed
                : Guid.Empty;
            string host = OptionalString(root, "host") ?? "";
            int size = root.TryGetProperty("size", out JsonElement s) ? s.GetInt32() : throw new InviteFormatException("The invite has no board size.");
            int komi = root.TryGetProperty("komi", out JsonElement k) ? k.GetInt32() : throw new InviteFormatException("The invite has no komi.");
            var relays = new List<string>();
            if (root.TryGetProperty("relays", out JsonElement r))
            {
                foreach (JsonElement relay in r.EnumerateArray())
                    relays.Add(relay.GetString() ?? "");
            }
            return new InviteCode(gid, host, (BoardSize)size, komi, relays, OptionalString(root, "seed_commit") ?? "", OptionalString(root, "nick"));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException and not InviteFormatException)
        {
            throw new InviteFormatException(Damaged);
        }
    }

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement e) ? e.GetString() : null;

    private static InviteCode ParseUri(string query)
    {
        if (QueryValue(query, "v") != "2")
            throw new InviteFormatException(Older);

        Guid gid = Guid.TryParse(QueryValue(query, "gid"), out Guid parsed) ? parsed : Guid.Empty;
        int size = ParseInt(QueryValue(query, "size"), "The invite has no board size.");
        int komi = ParseInt(QueryValue(query, "komi"), "The invite has no komi.");
        string relays = QueryValue(query, "relays") ?? "";
        return new InviteCode(
            gid,
            QueryValue(query, "host") ?? "",
            (BoardSize)size,
            komi,
            relays.Split(';', StringSplitOptions.RemoveEmptyEntries),
            QueryValue(query, "seed_commit") ?? "",
            QueryValue(query, "nick"));
    }

    private static int ParseInt(string? value, string missing) =>
        value is null ? throw new InviteFormatException(missing)
        : int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) ? n
        : throw new InviteFormatException(Damaged);

    private static string? QueryValue(string query, string key)
    {
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string name = eq < 0 ? pair : pair[..eq];
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                return eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return null;
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TakEngine.Abstractions;

namespace TakEngine.Transport.Matchmaking;

public sealed record InviteCode(
    [property: JsonPropertyName("gid")] Guid GameId,
    [property: JsonPropertyName("host")] string HostPubKey,
    [property: JsonPropertyName("size")] BoardSize BoardSize,
    [property: JsonPropertyName("relays")] IReadOnlyList<string> Relays,
    [property: JsonPropertyName("seed")] string? SeedHex = null)
{
    private const string UriScheme = "tak";
    private const string UriHost = "invite";
    private const string CompactPrefix = "TAK1_";

    public string ToUri()
    {
        string relaysJoined = Uri.EscapeDataString(string.Join(';', Relays));
        string seedParam = !string.IsNullOrEmpty(SeedHex) ? $"&seed={SeedHex}" : "";
        return $"{UriScheme}://{UriHost}?gid={GameId}&host={HostPubKey}&size={(int)BoardSize}&relays={relaysJoined}{seedParam}";
    }

    public string ToCompactCode()
    {
        string json = JsonSerializer.Serialize(this);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        string base64 = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{CompactPrefix}{base64}";
    }

    public static InviteCode Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Invite code text cannot be empty.", nameof(text));

        text = text.Trim();

        if (text.StartsWith(CompactPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ParseCompactCode(text[CompactPrefix.Length..]);
        }

        if (text.StartsWith($"{UriScheme}://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseUri(text);
        }

        // Try raw base64 or JSON as fallback
        try
        {
            return ParseCompactCode(text);
        }
        catch
        {
            return JsonSerializer.Deserialize<InviteCode>(text)
                ?? throw new FormatException("Could not parse invite code from text.");
        }
    }

    private static InviteCode ParseCompactCode(string base64Url)
    {
        string incoming = base64Url.Replace('-', '+').Replace('_', '/');
        switch (incoming.Length % 4)
        {
            case 2: incoming += "=="; break;
            case 3: incoming += "="; break;
        }

        byte[] bytes = Convert.FromBase64String(incoming);
        string json = Encoding.UTF8.GetString(bytes);

        return JsonSerializer.Deserialize<InviteCode>(json)
            ?? throw new FormatException("Invalid invite code JSON payload.");
    }

    private static InviteCode ParseUri(string uriStr)
    {
        var uri = new Uri(uriStr);
        string query = uri.Query.TrimStart('?');
        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

        Guid gid = Guid.Empty;
        string host = "";
        BoardSize size = BoardSize.Five;
        var relays = new List<string>();
        string? seed = null;

        foreach (string pair in pairs)
        {
            int eq = pair.IndexOf('=');
            if (eq < 0) continue;

            string key = pair[..eq].ToLowerInvariant();
            string val = Uri.UnescapeDataString(pair[(eq + 1)..]);

            switch (key)
            {
                case "gid":
                case "game_id":
                    gid = Guid.Parse(val);
                    break;
                case "host":
                    host = val;
                    break;
                case "size":
                    size = (BoardSize)int.Parse(val);
                    break;
                case "relays":
                    relays.AddRange(val.Split(';', StringSplitOptions.RemoveEmptyEntries));
                    break;
                case "seed":
                    seed = val;
                    break;
            }
        }

        if (gid == Guid.Empty || string.IsNullOrEmpty(host))
            throw new FormatException("Invite URI missing required parameters (gid, host).");

        return new InviteCode(gid, host, size, relays, seed);
    }
}

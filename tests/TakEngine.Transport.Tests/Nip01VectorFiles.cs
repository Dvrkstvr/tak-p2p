using System.Text.Json;
using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Tests;

/// <summary>One known-good event from an independent implementation. Fields are exactly as the generator wrote them.</summary>
public sealed record Nip01Vector(
    string Name,
    long CreatedAt,
    int Kind,
    List<List<string>> Tags,
    string Content,
    string Serialized,
    string Aux,
    string Id,
    string Sig);

internal static class Nip01VectorFiles
{
    public static string ReadText(string fileName)
    {
        using Stream stream = typeof(Nip01VectorFiles).Assembly.GetManifestResourceStream("Vectors/" + fileName)
            ?? throw new InvalidOperationException($"Embedded vector file 'Vectors/{fileName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>nip01-vectors.json: (secret key hex, pubkey hex, events).</summary>
    public static (string SecretKeyHex, string PubkeyHex, IReadOnlyList<Nip01Vector> Events) Generated()
    {
        using JsonDocument doc = JsonDocument.Parse(ReadText("nip01-vectors.json"));
        JsonElement root = doc.RootElement;
        var events = root.GetProperty("events").EnumerateArray().Select(e => new Nip01Vector(
            e.GetProperty("name").GetString()!,
            e.GetProperty("created_at").GetInt64(),
            e.GetProperty("kind").GetInt32(),
            e.GetProperty("tags").EnumerateArray().Select(t => t.EnumerateArray().Select(v => v.GetString()!).ToList()).ToList(),
            e.GetProperty("content").GetString()!,
            e.GetProperty("serialized").GetString()!,
            e.GetProperty("aux").GetString()!,
            e.GetProperty("id").GetString()!,
            e.GetProperty("sig").GetString()!)).ToList();
        return (root.GetProperty("secret_key").GetString()!, root.GetProperty("pubkey").GetString()!, events);
    }

    /// <summary>A JSONL file of complete signed events (the spike's C#-signed/Python-verified and Python-signed sets).</summary>
    public static IReadOnlyList<NostrEvent> SignedEvents(string fileName) =>
        ReadText(fileName)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<NostrEvent>(line.TrimEnd('\r'))!)
            .ToList();
}

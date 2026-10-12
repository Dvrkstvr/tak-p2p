using System.Text.Json;

namespace TakEngine.Crypto.Tests;

/// <summary>Reads the official vector files embedded in this assembly (see the csproj).</summary>
internal static class VectorFiles
{
    public static string ReadText(string fileName)
    {
        using Stream stream = typeof(VectorFiles).Assembly.GetManifestResourceStream("Vectors/" + fileName)
            ?? throw new InvalidOperationException($"Embedded vector file 'Vectors/{fileName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The "v2" object of nip44.vectors.json.</summary>
    public static JsonElement Nip44V2()
    {
        using JsonDocument doc = JsonDocument.Parse(ReadText("nip44.vectors.json"));
        return doc.RootElement.GetProperty("v2").Clone();
    }

    /// <summary>Rows of bip340-test-vectors.csv without the header: index, secret key, public key, aux_rand, message, signature, result, comment.</summary>
    public static IReadOnlyList<string[]> Bip340Rows() =>
        ReadText("bip340-test-vectors.csv")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.TrimEnd('\r').Split(','))
            .ToList();

    public static byte[] Hex(string hex) => Convert.FromHexString(hex);
}

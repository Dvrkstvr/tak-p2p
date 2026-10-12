using System.Text.Json;

namespace TakEngine.Crypto;

/// <summary>The stored identity could not be read. The message names the reason; callers never overwrite the stored data.</summary>
public sealed class IdentityFormatException(string message, Exception? innerException = null)
    : FormatException(message, innerException);

/// <summary>
/// The versioned identity blob (pipeline/architecture.md "Data", docs/decisions/0010): <c>{"v":1,"nsec":"nsec1…"}</c>.
/// Same format for the CLI/desktop key file and the browser's <c>tak.identity.v1</c>. Forward versions only:
/// a higher <c>v</c> than this build knows is an error, never rewritten.
/// </summary>
public static class IdentityDocument
{
    public const int CurrentVersion = 1;

    public static string Serialize(SecretKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return $"{{\"v\":{CurrentVersion},\"nsec\":\"{key.ToNsec()}\"}}";
    }

    public static SecretKey Parse(string document)
    {
        ArgumentNullException.ThrowIfNull(document);
        JsonElement root = TryParseJson(document) ?? throw new IdentityFormatException("Identity is not valid JSON.");
        if (root.ValueKind != JsonValueKind.Object)
            throw new IdentityFormatException("Identity is not a JSON object.");

        int version = ReadVersion(root);
        if (version > CurrentVersion)
            throw new IdentityFormatException(
                $"Identity was written by a newer version (v={version}); this build reads v={CurrentVersion}.");
        if (version < CurrentVersion)
            throw new IdentityFormatException($"Unknown identity version v={version}.");

        if (!root.TryGetProperty("nsec", out JsonElement nsec) || nsec.ValueKind != JsonValueKind.String)
            throw new IdentityFormatException("Identity has no \"nsec\".");
        return KeyFromNsec(nsec.GetString()!);
    }

    private static JsonElement? TryParseJson(string document)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(document);
            return json.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ReadVersion(JsonElement root)
    {
        if (root.TryGetProperty("v", out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int version))
            return version;
        throw new IdentityFormatException("Identity has no integer version \"v\".");
    }

    private static SecretKey KeyFromNsec(string nsec)
    {
        try
        {
            return SecretKey.FromNsec(nsec);
        }
        catch (InvalidKeyException ex)
        {
            throw new IdentityFormatException($"Identity key is invalid: {ex.Message}", ex);
        }
    }
}

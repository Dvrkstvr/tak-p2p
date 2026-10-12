using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using TakEngine.Crypto;

namespace TakEngine.Transport.Nostr;

public sealed record NostrProfile(
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("display_name")] string? DisplayName = null,
    [property: JsonPropertyName("about")] string? About = null,
    [property: JsonPropertyName("picture")] string? Picture = null,
    [property: JsonPropertyName("nip05")] string? Nip05 = null)
{
    public string BestDisplayName() =>
        !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : (!string.IsNullOrWhiteSpace(Name) ? Name : "");

    public static NostrProfile Parse(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
            return new NostrProfile();

        try
        {
            return JsonSerializer.Deserialize<NostrProfile>(jsonContent) ?? new NostrProfile();
        }
        catch
        {
            return new NostrProfile();
        }
    }

    /// <summary>A signed kind-0 metadata event (NIP-01), built with <see cref="NostrEvents.Sign"/>. Pure: time and aux are arguments.</summary>
    public static NostrEvent CreateMetadataEvent(SecretKey key, NostrProfile profile, long createdAt, ReadOnlySpan<byte> aux32)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string json = JsonSerializer.Serialize(profile);
        return NostrEvents.Sign(key, createdAt, 0, [], json, aux32);
    }
}

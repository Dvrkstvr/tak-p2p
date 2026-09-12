using System;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public static NostrEvent CreateMetadataEvent(string pubKey, NostrProfile profile)
    {
        string json = JsonSerializer.Serialize(profile);
        var evt = new NostrEvent
        {
            Pubkey = pubKey,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Kind = 0,
            Tags = new(),
            Content = json
        };
        evt.Id = evt.ComputeId();
        return evt;
    }
}

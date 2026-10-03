using System;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using TakEngine.Crypto;

namespace TakApp.Blazor.Services;

public sealed class BrowserStorage
{
    // The secp256k1 identity (D-011) lives under a new name, in the same {"v":1,"nsec":...} format as the CLI key file.
    // The old Ed25519 entries (tak_p2p_privkey / tak_p2p_pubkey) are never read: an Ed25519 secret is also a valid
    // secp256k1 scalar and would silently become a different npub (docs/decisions/0010). Only ClearIdentityAsync removes them.
    private const string IdentityKey = "tak.identity.v1";

    private readonly IJSRuntime _js;

    public BrowserStorage(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<string?> GetItemAsync(string key)
    {
        try
        {
            return await _js.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetItemAsync(string key, string value)
    {
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", key, value);
        }
        catch
        {
            // Ignore in restricted environments
        }
    }

    /// <summary>
    /// Loads the identity, or creates and stores one on first use. A stored identity that cannot be read throws
    /// <see cref="IdentityFormatException"/> and is left untouched (never silently replaced).
    /// </summary>
    public async Task<(string PrivKeyHex, string PubKeyHex)> GetOrCreateKeypairAsync()
    {
        string? document = await ReadIdentityAsync();
        if (!string.IsNullOrEmpty(document))
        {
            SecretKey stored = IdentityDocument.Parse(document);
            return (stored.ToHex(), stored.PublicKey.ToHex());
        }

        SecretKey created = SecretKey.Generate(() => RandomNumberGenerator.GetBytes(SecretKey.Length));
        await SetItemAsync(IdentityKey, IdentityDocument.Serialize(created));
        return (created.ToHex(), created.PublicKey.ToHex());
    }

    // Unlike GetItemAsync, a failed read must not look like "no identity": that would generate a key and overwrite the stored one.
    private async Task<string?> ReadIdentityAsync()
    {
        return await _js.InvokeAsync<string?>("localStorage.getItem", IdentityKey);
    }

    /// <summary>Replaces the identity with an imported nsec or 64-char hex secret; throws <see cref="InvalidKeyException"/> if invalid.</summary>
    public async Task<(string PrivKeyHex, string PubKeyHex)> ImportPrivateKeyAsync(string privateKeyOrNsec)
    {
        string input = privateKeyOrNsec.Trim();
        SecretKey key = input.StartsWith("nsec1", StringComparison.OrdinalIgnoreCase)
            ? SecretKey.FromNsec(input)
            : SecretKey.FromHex(input);

        await SetItemAsync(IdentityKey, IdentityDocument.Serialize(key));
        return (key.ToHex(), key.PublicKey.ToHex());
    }

    public async Task<string?> GetNicknameAsync()
    {
        return await GetItemAsync("tak_p2p_nickname");
    }

    public async Task SetNicknameAsync(string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            try
            {
                await _js.InvokeVoidAsync("localStorage.removeItem", "tak_p2p_nickname");
            }
            catch { }
        }
        else
        {
            await SetItemAsync("tak_p2p_nickname", nickname.Trim());
        }
    }

    public async Task<TakEngine.Transport.Nostr.NostrProfile> GetProfileAsync()
    {
        string? json = await GetItemAsync("tak_p2p_profile");
        if (!string.IsNullOrEmpty(json))
        {
            return TakEngine.Transport.Nostr.NostrProfile.Parse(json);
        }

        string? nick = await GetNicknameAsync();
        return new TakEngine.Transport.Nostr.NostrProfile(Name: nick, DisplayName: nick);
    }

    public async Task SetProfileAsync(TakEngine.Transport.Nostr.NostrProfile profile)
    {
        string json = JsonSerializer.Serialize(profile);
        await SetItemAsync("tak_p2p_profile", json);
        string bestName = profile.BestDisplayName();
        if (!string.IsNullOrWhiteSpace(bestName))
        {
            await SetItemAsync("tak_p2p_nickname", bestName);
        }
    }

    public async Task ClearIdentityAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", IdentityKey);
            await _js.InvokeVoidAsync("localStorage.removeItem", "tak_p2p_privkey");
            await _js.InvokeVoidAsync("localStorage.removeItem", "tak_p2p_pubkey");
            await _js.InvokeVoidAsync("localStorage.removeItem", "tak_p2p_nickname");
            await _js.InvokeVoidAsync("localStorage.removeItem", "tak_p2p_profile");
        }
        catch
        {
            // Ignore in restricted environments
        }
    }
}

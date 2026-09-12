using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using TakEngine.Core.Cryptography;

namespace TakApp.Blazor.Services;

public sealed class BrowserStorage
{
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

    public async Task<(string PrivKeyHex, string PubKeyHex)> GetOrCreateKeypairAsync()
    {
        string? privKey = await GetItemAsync("tak_p2p_privkey");
        string? pubKey = await GetItemAsync("tak_p2p_pubkey");

        if (!string.IsNullOrEmpty(privKey) && !string.IsNullOrEmpty(pubKey))
        {
            return (privKey, pubKey);
        }

        var (generatedPriv, generatedPub) = CryptoSigner.GenerateKeyPair();
        await SetItemAsync("tak_p2p_privkey", generatedPriv);
        await SetItemAsync("tak_p2p_pubkey", generatedPub);

        return (generatedPriv, generatedPub);
    }

    public async Task SetKeypairAsync(string privKeyHex, string pubKeyHex)
    {
        await SetItemAsync("tak_p2p_privkey", privKeyHex);
        await SetItemAsync("tak_p2p_pubkey", pubKeyHex);
    }

    public async Task<(string PrivKeyHex, string PubKeyHex)> ImportPrivateKeyAsync(string privateKeyOrNsec)
    {
        string privHex;
        if (privateKeyOrNsec.StartsWith("nsec1", StringComparison.OrdinalIgnoreCase))
        {
            var (_, hex) = Nip19.Decode(privateKeyOrNsec);
            privHex = hex;
        }
        else
        {
            privHex = privateKeyOrNsec.Trim().ToLowerInvariant();
        }

        string pubHex = CryptoSigner.GetPublicKeyHex(privHex);
        await SetKeypairAsync(privHex, pubHex);
        return (privHex, pubHex);
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

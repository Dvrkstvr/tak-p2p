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
}

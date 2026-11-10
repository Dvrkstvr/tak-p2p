namespace TakApp.Blazor.Services;

/// <summary>The board view this browser last chose (2D or 3D), kept in localStorage. 3D unless the player picked 2D.</summary>
public sealed class ViewPreference(BrowserStorage storage)
{
    private const string Key = "tak.view";
    private bool _loaded;

    public bool Is3D { get; private set; } = true;

    public async Task LoadAsync()
    {
        if (_loaded)
            return;
        _loaded = true;
        Is3D = await storage.GetItemAsync(Key) != "2d";
    }

    public async Task SetAsync(bool is3D)
    {
        Is3D = is3D;
        await storage.SetItemAsync(Key, is3D ? "3d" : "2d");
    }
}

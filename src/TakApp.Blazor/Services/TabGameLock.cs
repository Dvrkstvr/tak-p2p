using Microsoft.JSInterop;

namespace TakApp.Blazor.Services;

/// <summary>
/// One tab per game, across the tabs of this origin (M0 code review, finding 6). Every tab reads the same key, so two tabs on one
/// game would sign two different moves for one turn; the CLI's profile lock stops the same thing. Uses the Web Locks API through
/// <c>wwwroot/js/game-lock.js</c>. Where that API is missing (an http:// page off localhost) the lock is allowed and the console
/// says so, rather than locking the player out.
/// </summary>
public sealed class TabGameLock(IJSRuntime js)
{
    public enum Outcome
    {
        Held,
        Busy,
        Unsupported,
    }

    public async Task<Outcome> AcquireAsync(Guid gameId)
    {
        try
        {
            string result = await js.InvokeAsync<string>("takGameLock.acquire", Name(gameId));
            return result switch
            {
                "held" => Outcome.Held,
                "busy" => Outcome.Busy,
                _ => Outcome.Unsupported,
            };
        }
        catch (JSException ex)
        {
            await WarnAsync($"Tak: the tab lock for game {gameId} is unavailable ({ex.Message}); playing without it.");
            return Outcome.Unsupported;
        }
    }

    public async Task ReleaseAsync(Guid gameId)
    {
        try
        {
            await js.InvokeVoidAsync("takGameLock.release", Name(gameId));
        }
        catch (JSException)
        {
            // Nothing held; the browser frees any Web Lock when the tab closes.
        }
    }

    private async Task WarnAsync(string text)
    {
        try
        {
            await js.InvokeVoidAsync("console.warn", text);
        }
        catch (JSException)
        {
        }
    }

    private static string Name(Guid gameId) => $"tak.game.{gameId:N}";
}

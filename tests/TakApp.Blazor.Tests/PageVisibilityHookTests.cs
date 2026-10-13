using System.Reflection;
using Microsoft.JSInterop;
using TakApp.Blazor.Services;

namespace TakApp.Blazor.Tests;

/// <summary>
/// D-051's wake hook: <c>visibilitychange</c>/<c>online</c> in wwwroot/js/page-visibility.js calls
/// <see cref="OnlineGameService.OnPageVisible"/>, which probes the relays at once (MultiplayerGame.CheckConnections, tested in
/// GameCoordinatorTests). A missing script tag or a renamed function fails quietly in the browser (the service catches
/// JSException), so the wiring is pinned here; tests/e2e/background.mjs --silent drives it in a real page (M1 code review 3).
/// </summary>
public class PageVisibilityHookTests
{
    private static string Web(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TakGame.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "TakApp.Blazor", "wwwroot", relative));
    }

    [Fact]
    public void IndexHtml_LoadsTheHookBeforeBlazor()
    {
        string html = Web("index.html");

        int hook = html.IndexOf("<script src=\"js/page-visibility.js\"></script>", StringComparison.Ordinal);
        int blazor = html.IndexOf("_framework/blazor.webassembly", StringComparison.Ordinal);
        Assert.True(hook >= 0, "index.html does not load js/page-visibility.js");
        Assert.True(hook < blazor, "the hook must be defined before Blazor starts");
    }

    [Fact]
    public void TheScript_DefinesTheWatchTheServiceCalls_AndCallsOnPageVisibleOnShowAndOnline()
    {
        string js = Web(Path.Combine("js", "page-visibility.js"));
        string[] watch = OnlineGameService.VisibilityWatch.Split('.');

        Assert.Contains($"window.{watch[0]} =", js, StringComparison.Ordinal);
        Assert.Contains($"{watch[1]}(dotnetRef)", js, StringComparison.Ordinal);
        Assert.Contains($"invokeMethodAsync(\"{nameof(OnlineGameService.OnPageVisible)}\")", js, StringComparison.Ordinal);
        Assert.Contains("addEventListener(\"visibilitychange\"", js, StringComparison.Ordinal);
        Assert.Contains("addEventListener(\"online\"", js, StringComparison.Ordinal);
        Assert.Contains("document.visibilityState === \"visible\"", js, StringComparison.Ordinal);
    }

    [Fact]
    public void OnPageVisible_IsInvokableFromJs()
    {
        MethodInfo method = typeof(OnlineGameService).GetMethod(nameof(OnlineGameService.OnPageVisible))!;

        var attribute = method.GetCustomAttribute<JSInvokableAttribute>();
        Assert.NotNull(attribute);
        Assert.True(attribute!.Identifier is null || attribute.Identifier == nameof(OnlineGameService.OnPageVisible));
        Assert.Empty(method.GetParameters());
    }
}

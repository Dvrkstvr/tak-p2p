using TakEngine.Testing;

namespace TakRelay.Local;

/// <summary>Options from the command line: <c>[--port N] [--relays r1,r2] [--rate-limit-like-damus] [--auth-for-reads r2]</c>.</summary>
public sealed record LocalRelayOptions(int Port = 7447, IReadOnlyList<string>? Names = null, bool RateLimitLikeDamus = false,
    IReadOnlyList<string>? AuthForReads = null)
{
    public IReadOnlyList<string> RelayNames => Names is { Count: > 0 } ? Names : ["r1", "r2"];

    /// <summary>Null (with <paramref name="error"/>) when the arguments don't parse.</summary>
    public static LocalRelayOptions? Parse(string[] args, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new LocalRelayOptions();
        error = null;
        for (int i = 0; i < args.Length; i++)
        {
            string? next = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--port" when int.TryParse(next, out int port) && port is >= 0 and <= 65535:
                    o = o with { Port = port };
                    i++;
                    break;
                case "--relays" when next is not null:
                    o = o with { Names = Split(next) };
                    i++;
                    break;
                case "--auth-for-reads" when next is not null:
                    o = o with { AuthForReads = Split(next) };
                    i++;
                    break;
                case "--rate-limit-like-damus":
                    o = o with { RateLimitLikeDamus = true };
                    break;
                default:
                    error = "usage: TakRelay.Local [--port N] [--relays r1,r2] [--rate-limit-like-damus] [--auth-for-reads r2]";
                    return null;
            }
        }
        return o;
    }

    private static string[] Split(string csv) => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// One <see cref="InMemoryRelay"/> per name, each at <c>ws://127.0.0.1:port/name</c>, loopback only. Port 0 picks a free port
/// (tests); <see cref="Urls"/> then holds the real ones.
/// </summary>
public sealed class LocalRelayHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LocalRelayHost(WebApplication app, IReadOnlyDictionary<string, InMemoryRelay> relays)
    {
        _app = app;
        Relays = relays;
    }

    /// <summary>The relays by name, so a test can turn knobs while clients are connected.</summary>
    public IReadOnlyDictionary<string, InMemoryRelay> Relays { get; }

    public IReadOnlyList<Uri> Urls { get; private set; } = [];

    /// <summary>Fires when the host is told to stop (SIGTERM, Ctrl+C).</summary>
    public CancellationToken Stopping => _app.Lifetime.ApplicationStopping;

    public static async Task<LocalRelayHost> StartAsync(LocalRelayOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var relays = new Dictionary<string, InMemoryRelay>(StringComparer.Ordinal);
        foreach (string name in options.RelayNames)
        {
            var relay = new InMemoryRelay($"ws://127.0.0.1/{name}");
            if (options.RateLimitLikeDamus)
                relay.RateLimitLikeDamus();
            if (options.AuthForReads?.Contains(name) == true)
                relay.AuthRequiredForReads();
            relays[name] = relay;
        }

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
        WebApplication app = builder.Build();
        app.UseWebSockets();
        app.Map("/{name}", async (string name, HttpContext context) =>
        {
            if (!relays.TryGetValue(name, out InMemoryRelay? relay))
                return Results.NotFound();
            if (!context.WebSockets.IsWebSocketRequest)
                return Results.Text($"TakRelay.Local: connect a WebSocket to ws://{context.Request.Host}/{name}");
            using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            await relay.ServeAsync(socket, context.RequestAborted).ConfigureAwait(false);
            return Results.Empty;
        });

        var host = new LocalRelayHost(app, relays);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        var bound = new Uri(app.Urls.First());
        host.Urls = [.. options.RelayNames.Select(n => new Uri($"ws://127.0.0.1:{bound.Port}/{n}"))];
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}

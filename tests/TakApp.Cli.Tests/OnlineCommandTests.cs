using TakApp.Cli.Online;
using TakEngine.Storage.Local;
using TakEngine.Testing;

namespace TakApp.Cli.Tests;

/// <summary>F-036 in-process over the fake relays: profiles, the failure exits, and a taken invite.</summary>
public sealed class OnlineCommandTests : IDisposable
{
    private readonly string _appData = Directory.CreateTempSubdirectory("tak-cli-").FullName;
    private readonly InMemoryRelay _relay = new("wss://r1.test");
    private readonly FakeRelaySocketFactory _sockets;

    public OnlineCommandTests() => _sockets = new FakeRelaySocketFactory(_relay);

    public void Dispose() => Directory.Delete(_appData, recursive: true);

    private string[] Args(string profile, params string[] args) => [.. args, "--profile", profile, "--relays", "wss://r1.test", "--plain"];

    [Theory]
    [InlineData(null, null, "default")]
    [InlineData(null, "alice", "alice")]
    public void Profile_ResolvesUnderLocalAppData(string? dataDir, string? profile, string expected) =>
        Assert.Equal(Path.Combine("/home/x/.local/share", "tak-p2p", expected), Profile.Directory(dataDir, profile, "/home/x/.local/share"));

    [Fact]
    public void DataDir_WinsOverProfile() =>
        Assert.Equal(Path.GetFullPath("/tmp/tak-b"), Profile.Directory("/tmp/tak-b", "alice", "/home/x/.local/share"));

    [Fact]
    public async Task FirstRun_CreatesTheKey_LaterRunsReuseIt()
    {
        string identity = Path.Combine(_appData, "tak-p2p", "alice", FileKeyStore.FileName);

        await using (CliRun first = CliRun.Start(_appData, _sockets, Args("alice", "resume")))
            Assert.Equal(2, await first.Exit);
        string created = await File.ReadAllTextAsync(identity);
        await using (CliRun second = CliRun.Start(_appData, _sockets, Args("alice", "resume")))
            Assert.Equal(2, await second.Exit);

        Assert.Equal(created, await File.ReadAllTextAsync(identity));
    }

    [Fact]
    public async Task ACorruptKeyFile_PrintsItsError_Exits2_AndIsLeftAlone()
    {
        string dir = Path.Combine(_appData, "tak-p2p", "broken");
        Directory.CreateDirectory(dir);
        string identity = Path.Combine(dir, FileKeyStore.FileName);
        await File.WriteAllTextAsync(identity, "{\"v\":1,\"nsec\":\"nsec1nope\"}");

        await using CliRun run = CliRun.Start(_appData, _sockets, Args("broken", "host"));

        Assert.Equal(2, await run.Exit);
        Assert.Contains("Cannot read the identity", run.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal("{\"v\":1,\"nsec\":\"nsec1nope\"}", await File.ReadAllTextAsync(identity));
        Assert.Empty(_sockets.Created);
    }

    [Fact]
    public async Task ABadInvite_PrintsTheInviteError_Exits2_WithNoRelayTraffic()
    {
        await using CliRun run = CliRun.Start(_appData, _sockets, Args("bob", "join", "TAK2_not-an-invite"));

        Assert.Equal(2, await run.Exit);
        Assert.NotEmpty(run.Error.ToString());
        Assert.Empty(_sockets.Created);
        Assert.Empty(_relay.ReceivedFrames);
    }

    [Fact]
    public async Task UnreachableRelays_PrintBackoff_AndKeepRetrying_UntilStopped()
    {
        await using CliRun run = CliRun.Start(_appData, _sockets, ["host", "--profile", "alice", "--relays", "wss://nowhere.test", "--plain"]);

        await run.Line("INVITE ");
        await run.Line("RELAY wss://nowhere.test backoff");
        await Task.Delay(1500);
        Assert.False(run.Exit.IsCompleted);
        Assert.True(_sockets.Created.Count(s => s.Url.Host == "nowhere.test") >= 2, "it retried");
        Assert.Equal(0, await run.Stop());
    }

    [Fact]
    public async Task OneProfile_InTwoProcesses_TheSecondIsRefused()
    {
        await using CliRun first = CliRun.Start(_appData, _sockets, Args("alice", "host"));
        await first.Line("INVITE ");

        await using CliRun second = CliRun.Start(_appData, _sockets, Args("alice", "resume"));

        Assert.Equal(2, await second.Exit);
        Assert.Contains("in use by another tak process", second.Error.ToString(), StringComparison.Ordinal);
        Assert.False(first.Exit.IsCompleted);
    }

    [Fact]
    public async Task AGamesFileFromANewerVersion_IsAnErrorAndExit2_NotACrash()
    {
        string dir = Path.Combine(_appData, "tak-p2p", "newer");
        Directory.CreateDirectory(dir);
        string db = Path.Combine(dir, SqliteGameStore.FileName);
        await new SqliteGameStore(db).ListGamesAsync();
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA user_version = 3;";
            cmd.ExecuteNonQuery();
        }

        await using CliRun run = CliRun.Start(_appData, _sockets, Args("newer", "host"));

        Assert.Equal(2, await run.Exit.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("newer version", run.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATakenInvite_TheHostSaysSo_TheSecondGuestKeepsWaiting()
    {
        await using CliRun host = CliRun.Start(_appData, _sockets, Args("alice", "host"));
        string invite = (await host.Line("INVITE ")).Split(' ')[1];
        await using CliRun bob = CliRun.Start(_appData, _sockets, Args("bob", "join", invite));
        await Table.Until(() => new[] { host, bob }.All(r => r.Out.Lines.Any(l => l.StartsWith("WAITING", StringComparison.Ordinal)
            && l.EndsWith("turn 1", StringComparison.Ordinal))), "both have started");

        await using CliRun carol = CliRun.Start(_appData, _sockets, Args("carol", "join", invite));

        string notice = await host.Line("NOTICE ignored join from npub1", 15_000);
        Assert.EndsWith("game already taken", notice, StringComparison.Ordinal);
        Assert.Equal("WAITING for the host to answer", await carol.Line("WAITING "));
        await Task.Delay(300);
        Assert.False(carol.Exit.IsCompleted);
    }
}

internal static class Table
{
    public static async Task Until(Func<bool> condition, string what, int timeoutMs = 15_000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(20);
        Assert.True(condition(), "timed out waiting for: " + what);
    }
}

/// <summary>Code review M0 finding 3: <c>--relays</c> follows the invite's own relay rule, so a bad relay is exit 2, not a crash.</summary>
public sealed class RelayArgumentTests : IDisposable
{
    private readonly string _appData = Directory.CreateTempSubdirectory("tak-cli-").FullName;
    private readonly FakeRelaySocketFactory _sockets = new(new InMemoryRelay("wss://r1.test"));

    public void Dispose() => Directory.Delete(_appData, recursive: true);

    [Theory]
    [InlineData("ws://example.com")]
    [InlineData("ws://10.0.0.5:7447")]
    [InlineData("http://example.com")]
    [InlineData("wss://nos.lol,ws://example.com")]
    public void ARelayTheInviteWouldRefuse_IsAnArgumentError(string relays)
    {
        Assert.Null(OnlineArgs.Parse(["host", "--relays", relays], out string? error));
        Assert.Contains("wss://", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wss://nos.lol,wss://relay.damus.io")]
    [InlineData("ws://127.0.0.1:7447/r1,ws://127.0.0.1:7447/r2")]
    [InlineData("ws://localhost:7447")]
    public void RelaysTheInviteAccepts_Parse(string relays) =>
        Assert.NotNull(OnlineArgs.Parse(["host", "--relays", relays], out _));

    [Fact]
    public async Task HostWithABadRelay_Exits2_WithTheReason_AndNoRelayTraffic()
    {
        var args = new OnlineArgs("host", Profile: "alice", Relays: ["ws://example.com"], Plain: true);
        var output = new StringWriter();
        var error = new StringWriter();
        var env = new CliEnvironment(_sockets, TimeProvider.System, _appData, () => System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        int exit = await OnlineCommand.RunAsync(args, new StringReader(""), new PlainOutput(output), error, env, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, exit);
        Assert.Contains("ws://example.com", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
        Assert.Empty(_sockets.Created);
    }
}

using System.Diagnostics;
using TakEngine.Testing;
using TakRelay.Local;

namespace TakApp.Cli.Tests;

/// <summary>One CLI process, driven through stdin and read through its <c>--plain</c> stdout.</summary>
internal sealed class CliProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly LineWriter _out = new();

    private CliProcess(Process process) => _process = process;

    public IReadOnlyCollection<string> Lines => _out.Lines;

    public static CliProcess Start(params string[] args)
    {
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var info = new ProcessStartInfo(dotnet)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "TakApp.Cli.dll"));
        foreach (string arg in args)
            info.ArgumentList.Add(arg);
        var cli = new CliProcess(new Process { StartInfo = info });
        cli._process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                cli._out.WriteLine(e.Data);
        };
        cli._process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                cli._out.WriteLine("STDERR " + e.Data);
        };
        cli._process.Start();
        cli._process.BeginOutputReadLine();
        cli._process.BeginErrorReadLine();
        return cli;
    }

    public void Send(string line)
    {
        _process.StandardInput.WriteLine(line);
        _process.StandardInput.Flush();
    }

    public bool Has(string line) => _out.Lines.Contains(line);

    public async Task<string> Line(string prefix, int timeoutMs = 20_000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (_out.Lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal)) is { } line)
                return line;
            await Task.Delay(25);
        }
        throw new Xunit.Sdk.XunitException($"no line '{prefix}…' in:\n{_out}");
    }

    public void Kill()
    {
        _process.Kill(entireProcessTree: true);
        _process.WaitForExit(10_000);
    }

    public async Task<int> WaitForExit(int timeoutMs = 20_000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        await _process.WaitForExitAsync(cts.Token);
        return _process.ExitCode;
    }

    public override string ToString() => _out.ToString();

    public ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
            Kill();
        _process.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// F-036/F-037 with real processes over loopback (pipeline/playbook.md "Verify method" step 2): TakRelay.Local, then
/// <c>host --plain</c> and <c>join &lt;invite&gt; --plain</c> as two CLI processes with their own profiles. Runs in CI (loopback only).
/// </summary>
public sealed class TwoProcessTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("tak-2p-").FullName;
    private LocalRelayHost _relays = null!;

    public async Task InitializeAsync() => _relays = await LocalRelayHost.StartAsync(new LocalRelayOptions(Port: 0));

    public async Task DisposeAsync()
    {
        await _relays.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    private string Relays => string.Join(',', _relays.Urls);

    private string[] Host => ["host", "--data-dir", Path.Combine(_root, "a"), "--relays", Relays, "--plain"];

    private string[] Join(string invite) => ["join", invite, "--data-dir", Path.Combine(_root, "b"), "--relays", Relays, "--plain"];

    /// <summary>Waits for whoever says it is their move at <paramref name="ply"/>.</summary>
    private static async Task<CliProcess> Mover(int ply, params CliProcess[] players)
    {
        string waiting = $"WAITING your move turn {ply}";
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20_000)
        {
            if (players.FirstOrDefault(p => p.Has(waiting)) is { } mover)
                return mover;
            await Task.Delay(25);
        }
        throw new Xunit.Sdk.XunitException($"nobody is to move at {ply}:\n--- A\n{players[0]}\n--- B\n{players[1]}");
    }

    [Fact]
    public async Task HostAndJoin_PlayTheScriptedRoadWin_TheJoinerIsKilledAndResumes_BothPrintTheSameFinal()
    {
        IReadOnlyList<string> script = ScriptedGames.FiveByFiveRoadWin;
        await using CliProcess a = CliProcess.Start(Host);
        string invite = (await a.Line("INVITE ")).Split(' ')[1];
        CliProcess b = CliProcess.Start(Join(invite));
        try
        {
            bool restarted = false;
            for (int ply = 1; ply <= script.Count; ply++)
            {
                CliProcess mover = await Mover(ply, a, b);
                mover.Send(script[ply - 1]);
                await b.Line($"APPLIED turn {ply} ");

                // Crash the joiner once the next move is the host's: the host plays it while the joiner is gone.
                if (!restarted && ply >= 4 && ply < script.Count && mover == b)
                {
                    await b.Line($"SENT turn {ply} "); // a relay has the joiner's move, so the host can answer it
                    b.Kill();
                    CliProcess hostMover = await Mover(ply + 1, a, a);
                    hostMover.Send(script[ply]);
                    await a.Line($"APPLIED turn {ply + 1} ");
                    await b.DisposeAsync();

                    b = CliProcess.Start(Join(invite)); // the same command again
                    restarted = true;
                    await b.Line($"APPLIED turn {ply + 1} ");
                    ply++;
                }
            }

            string finalA = await a.Line("FINAL ");
            string finalB = await b.Line("FINAL ");
            Assert.Equal(finalA, finalB);
            Assert.StartsWith("FINAL white-road ", finalA, StringComparison.Ordinal);
            Assert.True(restarted, "the joiner was killed and resumed");
            Assert.Equal(0, await a.WaitForExit());
            Assert.Equal(0, await b.WaitForExit());
            Assert.Contains(a.Lines, l => l.StartsWith("SENT turn ", StringComparison.Ordinal) && l.Contains(":ok", StringComparison.Ordinal));
        }
        finally
        {
            await b.DisposeAsync();
        }
    }
}

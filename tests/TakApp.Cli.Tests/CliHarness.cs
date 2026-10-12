using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using TakApp.Cli.Online;
using TakEngine.Transport.Relay;

namespace TakApp.Cli.Tests;

/// <summary>Collects written lines (thread-safe) so a test can wait for one.</summary>
internal sealed class LineWriter : TextWriter
{
    private readonly StringBuilder _partial = new();

    public ConcurrentQueue<string> Lines { get; } = new();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (_partial)
        {
            if (value == '\n')
            {
                Lines.Enqueue(_partial.ToString().TrimEnd('\r'));
                _partial.Clear();
            }
            else
            {
                _partial.Append(value);
            }
        }
    }

    public override string ToString() => string.Join('\n', Lines);
}

/// <summary>Stdin that stays open until the test writes a line or completes it.</summary>
internal sealed class InputLines : TextReader
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

    public void Send(string line) => _lines.Writer.TryWrite(line);

    public void End() => _lines.Writer.TryComplete();

    public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _lines.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }
}

/// <summary>One CLI command run in-process with plain output.</summary>
internal sealed class CliRun : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();

    private CliRun(string[] args, string appData, IRelaySocketFactory sockets)
    {
        OnlineArgs parsed = OnlineArgs.Parse(args, out string? error) ?? throw new ArgumentException(error);
        var env = new CliEnvironment(sockets, TimeProvider.System, appData, () => RandomNumberGenerator.GetBytes(32));
        Exit = Task.Run(() => OnlineCommand.RunAsync(parsed, Input, new PlainOutput(Out), Error, env, _stop.Token));
    }

    public InputLines Input { get; } = new();

    public LineWriter Out { get; } = new();

    public LineWriter Error { get; } = new();

    public Task<int> Exit { get; }

    public static CliRun Start(string appData, IRelaySocketFactory sockets, params string[] args) => new(args, appData, sockets);

    /// <summary>The first line starting with <paramref name="prefix"/>, waiting for it.</summary>
    public async Task<string> Line(string prefix, int timeoutMs = 10_000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (Out.Lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal)) is { } line)
                return line;
            if (Exit.IsCompleted)
                break;
            await Task.Delay(20);
        }
        return Out.Lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal))
            ?? throw new Xunit.Sdk.XunitException($"no line '{prefix}…' in:\n{Out}\nstderr:\n{Error}");
    }

    public async Task<int> Stop()
    {
        await _stop.CancelAsync();
        return await Exit.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public async ValueTask DisposeAsync()
    {
        if (!Exit.IsCompleted)
            await Stop();
        _stop.Dispose();
    }
}

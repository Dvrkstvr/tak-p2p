using System.Security.Cryptography;
using TakEngine.Abstractions;
using TakEngine.Crypto;
using TakEngine.Multiplayer;
using TakEngine.Testing;
using TakEngine.Transport.Matchmaking;
using TakEngine.Transport.Nostr;
using TakEngine.Transport.Relay;

namespace TakTestPeer;

/// <summary>
/// <c>TakTestPeer forge --relays &lt;csv&gt; --invite &lt;code&gt; --as-key &lt;key file&gt; --ptn &lt;move&gt; [--turn &lt;n&gt;]
/// [--kind illegal|fork|equivocate|undecryptable]</c>: rebuilds the game from the relays as the player whose key it holds, forges
/// one tampered event from that player to the opponent (<see cref="TamperForge"/>), publishes it once and prints its id. The key
/// file holds an nsec or 64 hex characters. Exit codes: 0 sent, 1 usage, 2 the game can't be read, 3 no relay took the event.
/// </summary>
public static class TestPeer
{
    private const string Usage =
        "usage: TakTestPeer forge --relays <csv> --invite <code> --as-key <key file> --ptn <move> [--turn <n>] "
        + "[--kind illegal|fork|equivocate|undecryptable]";

    public static readonly TimeSpan HistoryWait = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan PublishWait = TimeSpan.FromSeconds(30);

    public static async Task<int> RunAsync(string[] args, IRelaySocketFactory sockets, TimeProvider time, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Length == 0 || args[0] != "forge" || Options(args.Skip(1).ToArray()) is not { } o)
        {
            await error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 1;
        }
        if (!o.TryGetValue("relays", out string? relayCsv) || !o.TryGetValue("invite", out string? inviteText)
            || !o.TryGetValue("as-key", out string? keyFile) || !o.TryGetValue("ptn", out string? ptn))
        {
            await error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 1;
        }
        ForgeKind kind = o.GetValueOrDefault("kind", "illegal") switch
        {
            "illegal" => ForgeKind.Illegal,
            "fork" => ForgeKind.Fork,
            "equivocate" => ForgeKind.Equivocate,
            "undecryptable" => ForgeKind.Undecryptable,
            _ => (ForgeKind)(-1),
        };
        int? turnArg = o.TryGetValue("turn", out string? turnText) && int.TryParse(turnText, out int parsed) ? parsed : null;
        if (!Enum.IsDefined(kind) || (o.ContainsKey("turn") && turnArg is null) || (kind == ForgeKind.Equivocate && turnArg is null))
        {
            await error.WriteLineAsync(Usage + " (--kind equivocate needs --turn of a move already played by this key)").ConfigureAwait(false);
            return 1;
        }
        if (!InviteCode.TryParse(inviteText, out InviteCode? invite, out string? inviteError))
        {
            await error.WriteLineAsync("invite: " + inviteError).ConfigureAwait(false);
            return 1;
        }
        SecretKey key;
        try
        {
            key = ReadKey(await File.ReadAllTextAsync(keyFile).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            await error.WriteLineAsync("key: " + ex.Message).ConfigureAwait(false);
            return 1;
        }

        string[] relays = relayCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var options = new MultiplayerOptions(key, new InMemoryGameStore(), sockets, time, relays, () => RandomNumberGenerator.GetBytes(32));
        IReadOnlyList<HistoryEntry> history = await RelayHistory.FetchAsync(options, invite!, HistoryWait).ConfigureAwait(false);
        RebuildResult game = RebuildPlan.Build(invite!, key.PublicKey, history);
        if (game.Error is not null)
        {
            await error.WriteLineAsync("game: " + game.Error).ConfigureAwait(false);
            return 2;
        }

        int turn = turnArg ?? game.Progress!.AppliedTurn + 1;
        string prev = turn <= game.Applied.Count ? game.Applied[turn - 1].Action.PrevStateHash : game.Progress!.StateHash;
        PublicKey opponent = PublicKey.FromHex(game.Record!.OpponentPubKey!);
        NostrEvent evt = TamperForge.Forge(kind, key, opponent, invite!.GameId, turn, prev, ptn, time.GetUtcNow().ToUnixTimeSeconds(),
            options.Random32);

        PublishOutcome outcome = await PublishOnceAsync(options, relays, evt).ConfigureAwait(false);
        await output.WriteLineAsync($"event {evt.Id} {kind.ToString().ToLowerInvariant()} turn {turn} {outcome.ToString().ToLowerInvariant()}")
            .ConfigureAwait(false);
        return outcome == PublishOutcome.Sent ? 0 : 3;
    }

    private static async Task<PublishOutcome> PublishOnceAsync(MultiplayerOptions options, string[] relays, NostrEvent evt)
    {
        var done = new TaskCompletionSource<PublishOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pool = new RelayPool(relays.Select(r => new Uri(r)), options.Sockets, options.Time, budget: options.Budget);
        pool.PublishStatusChanged += (id, outcome) =>
        {
            if (id == evt.Id && outcome != PublishOutcome.Pending)
                done.TrySetResult(outcome);
        };
        pool.Start(() => new RelayMessage.Req("tak-peer", [new NostrFilter { Ids = [evt.Id] }]));
        pool.Publish(evt);
        DateTimeOffset deadline = options.Time.GetUtcNow() + PublishWait;
        while (!done.Task.IsCompleted && options.Time.GetUtcNow() < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        return done.Task.IsCompleted ? await done.Task.ConfigureAwait(false) : PublishOutcome.Pending;
    }

    private static SecretKey ReadKey(string text)
    {
        text = text.Trim();
        return text.StartsWith("nsec1", StringComparison.Ordinal) ? SecretKey.FromNsec(text) : SecretKey.FromHex(text);
    }

    private static Dictionary<string, string>? Options(string[] args)
    {
        var o = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                return null;
            o[args[i][2..]] = args[i + 1];
        }
        return o;
    }
}

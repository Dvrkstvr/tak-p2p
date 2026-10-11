using TakEngine.Abstractions;
using TakEngine.Multiplayer;
using TakEngine.Storage.Local;
using TakEngine.Transport.Matchmaking;
using TakEngine.Transport.Relay;

namespace TakApp.Cli.Online;

/// <summary>What the commands need from the machine; tests pass loopback sockets and a temp folder.</summary>
public sealed record CliEnvironment(IRelaySocketFactory Sockets, TimeProvider Time, string LocalAppData, Func<byte[]> Random32);

/// <summary>
/// <c>host</c>, <c>join</c> and <c>resume</c> (F-036): a view over <see cref="MultiplayerGame"/>. Moves come in one per input line
/// (PTN, or <c>resign</c>, or <c>quit</c> to leave a game that carries on later); everything the game reports goes to
/// <see cref="IGameOutput"/>. Exit codes: 0 the game ended or the player quit, 2 it could not start (the reason is on the error
/// writer and nothing was sent), 3 the handshake failed.
/// </summary>
public sealed class OnlineCommand
{
    public static readonly IReadOnlyList<string> DefaultRelays = ["wss://nos.lol", "wss://relay.damus.io"];

    private readonly IGameOutput _out;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _ownEvents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MultiplayerGame _game = null!;
    private int _printedTurn;
    private string? _lastWaiting;

    private OnlineCommand(IGameOutput output) => _out = output;

    public static async Task<int> RunAsync(OnlineArgs args, TextReader input, IGameOutput output, TextWriter error, CliEnvironment env,
        CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(env);

        string dir = Profile.Directory(args.DataDir, args.Profile, env.LocalAppData);
        MultiplayerOptions options;
        try
        {
            Directory.CreateDirectory(dir);
            var key = await IdentityBootstrap.LoadOrCreateAsync(new FileKeyStore(dir), env.Random32, stop).ConfigureAwait(false);
            var store = new SqliteGameStore(Path.Combine(dir, SqliteGameStore.FileName));
            options = new MultiplayerOptions(key, store, env.Sockets, env.Time, args.Relays ?? DefaultRelays, env.Random32);
        }
        catch (Exception ex) when (ex is KeyStoreException or GameStoreException or IOException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 2;
        }

        var command = new OnlineCommand(output);
        return await command.PlayAsync(args, options, input, error, stop).ConfigureAwait(false);
    }

    private async Task<int> PlayAsync(OnlineArgs args, MultiplayerOptions options, TextReader input, TextWriter error, CancellationToken stop)
    {
        (MultiplayerGame? game, string? failure) = await OpenAsync(args, options).ConfigureAwait(false);
        if (game is null)
        {
            await error.WriteLineAsync(failure).ConfigureAwait(false);
            return 2;
        }
        _game = game;
        await using (game.ConfigureAwait(false))
        {
            Attach();
            if (args.Command == "host" && game.Record.State == HandshakeState.Inviting)
                _out.Invite(game.Invite.ToCompactCode());
            await ReportStoredAsync(options.Store).ConfigureAwait(false);

            using var watch = CancellationTokenSource.CreateLinkedTokenSource(stop);
            Task relays = WatchRelaysAsync(watch.Token);
            Task reading = ReadMovesAsync(input, watch.Token);
            Task stopped = Task.Delay(Timeout.Infinite, stop);
            await Task.WhenAny(_done.Task, stopped).ConfigureAwait(false);
            if (_game.Result is not null && !stop.IsCancellationRequested)
                await WaitUntilSentAsync(options.Store, stop).ConfigureAwait(false);
            await watch.CancelAsync().ConfigureAwait(false);
            await relays.ConfigureAwait(false);
            _ = reading; // a console read can't be cancelled; it ends with the process
            return _done.Task.IsCompleted ? await _done.Task.ConfigureAwait(false) : 0;
        }
    }

    /// <summary>How long a finished game waits for its last events (the winning move, a resign) to reach a relay before exiting.</summary>
    public static readonly TimeSpan FinalSendWait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The game is over, but the opponent may still need our last event: stay until a relay has taken everything stored for this
    /// game, or say how to send it later.
    /// </summary>
    private async Task WaitUntilSentAsync(IGameStore store, CancellationToken stop)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < FinalSendWait && !stop.IsCancellationRequested)
        {
            StoredGame? stored = await store.LoadAsync(_game.GameId, stop).ConfigureAwait(false);
            if (stored is null || stored.Outbox.All(o => o.Sent))
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100), stop).ConfigureAwait(false);
        }
        _out.Notice("your last move has not reached a relay yet; run resume to send it");
    }

    private static async Task<(MultiplayerGame? Game, string? Error)> OpenAsync(OnlineArgs args, MultiplayerOptions options)
    {
        switch (args.Command)
        {
            case "host":
                return (await MultiplayerGame.HostAsync(options, args.Size, args.KomiHalves).ConfigureAwait(false), null);
            case "join":
                {
                    if (!InviteCode.TryParse(args.Target, out InviteCode? invite, out string? error))
                        return (null, error);
                    if (await options.Store.LoadAsync(invite!.GameId).ConfigureAwait(false) is not null)
                        return (await MultiplayerGame.ResumeAsync(options, invite.GameId).ConfigureAwait(false), null);
                    JoinOutcome joined = await MultiplayerGame.JoinAsync(options, args.Target!).ConfigureAwait(false);
                    return (joined.Game, joined.Error);
                }
            default:
                return await ResumeAsync(args, options).ConfigureAwait(false);
        }
    }

    private static async Task<(MultiplayerGame? Game, string? Error)> ResumeAsync(OnlineArgs args, MultiplayerOptions options)
    {
        if (args.Invite is { } inviteText)
        {
            if (!InviteCode.TryParse(inviteText, out InviteCode? invite, out string? error))
                return (null, error);
            if (await options.Store.LoadAsync(invite!.GameId).ConfigureAwait(false) is not null)
                return (await MultiplayerGame.ResumeAsync(options, invite.GameId).ConfigureAwait(false), null);
            JoinOutcome rebuilt = await MultiplayerGame.RebuildAsync(options, inviteText).ConfigureAwait(false);
            return (rebuilt.Game, rebuilt.Error);
        }

        Guid gameId;
        if (args.Target is { } target)
        {
            if (!Guid.TryParse(target, out gameId))
                return (null, $"'{target}' is not a game id.");
        }
        else
        {
            var open = (await options.Store.ListGamesAsync().ConfigureAwait(false))
                .Where(g => g.State is HandshakeState.Inviting or HandshakeState.Joining or HandshakeState.InGame).ToList();
            if (open.Count != 1)
            {
                return (null, open.Count == 0
                    ? "No unfinished game on this profile. Start one with host or join, or rebuild one with resume --invite <code>."
                    : "Several unfinished games on this profile; name one: " + string.Join(", ", open.Select(g => g.GameId)));
            }
            gameId = open[0].GameId;
        }
        MultiplayerGame? game = await MultiplayerGame.ResumeAsync(options, gameId).ConfigureAwait(false);
        return game is null ? (null, $"No game {gameId} on this profile.") : (game, null);
    }

    private void Attach()
    {
        _game.Started += _ => Prompt();
        _game.ActionApplied += (applied, mine) =>
        {
            if (mine)
            {
                lock (_gate) _ownEvents[applied.EventId] = applied.Action.Turn;
            }
            Report(applied, mine);
            Prompt();
        };
        _game.PublishStatus += (eventId, outcome) =>
        {
            int turn;
            lock (_gate)
            {
                if (outcome == PublishOutcome.Pending || !_ownEvents.TryGetValue(eventId, out turn) || !_reported.Add(eventId))
                    return;
            }
            _out.Sent(turn, _game.PublishDetail(eventId));
        };
        _game.Flagged += flag => _out.Flag(flag.Turn, flag.Reason);
        _game.Notice += _out.Notice;
        _game.Failed += message =>
        {
            _out.Notice(message);
            _done.TrySetResult(3);
        };
        _game.Ended += result =>
        {
            _out.Final(result, _game.StateHash);
            _done.TrySetResult(0);
        };
    }

    /// <summary>On resume, replays what is stored so the transcript is complete; then says whose turn it is.</summary>
    private async Task ReportStoredAsync(IGameStore store)
    {
        StoredGame? stored = await store.LoadAsync(_game.GameId).ConfigureAwait(false);
        string me = _game.Record.MyPubKey;
        foreach (AppliedAction applied in stored?.Applied ?? [])
        {
            bool mine = applied.Action.PlayerPubKey == me;
            if (mine)
            {
                lock (_gate) _ownEvents[applied.EventId] = applied.Action.Turn;
            }
            Report(applied, mine);
        }
        if (_game.Result is { } result)
        {
            _out.Final(result, _game.StateHash);
            _done.TrySetResult(0);
            return;
        }
        if (_game.Record.State == HandshakeState.Inviting)
            Waiting("for a guest to join");
        else if (_game.Record.State == HandshakeState.Joining)
            Waiting("for the host to answer");
        else
            Prompt();
    }

    private void Report(AppliedAction applied, bool mine)
    {
        lock (_gate)
        {
            if (applied.Action.Turn <= _printedTurn)
                return;
            _printedTurn = applied.Action.Turn;
        }
        _out.Applied(applied.Action.Turn, applied.Action.Ptn ?? "", applied.StateHashAfter, applied.TpsAfter, _game.Record.KomiHalves, mine);
    }

    private void Prompt()
    {
        if (_game.Result is not null || _game.MyColor is not { } color)
            return;
        int turn = _game.AppliedTurn + 1;
        Waiting(TurnExchange.TurnOwner(turn) == color ? $"your move turn {turn}" : $"opponent move turn {turn}");
    }

    private void Waiting(string text)
    {
        lock (_gate)
        {
            if (text == _lastWaiting)
                return;
            _lastWaiting = text;
        }
        _out.Waiting(text);
    }

    private async Task ReadMovesAsync(TextReader input, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            string? line = await input.ReadLineAsync(stop).ConfigureAwait(false);
            if (line is null)
                return; // end of input: the game goes on until it ends (moves from the opponent still arrive)
            line = line.Trim();
            if (line.Length == 0)
                continue;
            if (line is "quit" or "exit")
            {
                _out.Notice("left the game; it is saved, and resume picks it up");
                _done.TrySetResult(0);
                return;
            }
            CommandResult result = line == "resign" ? await _game.ResignAsync().ConfigureAwait(false) : await _game.SubmitAsync(line).ConfigureAwait(false);
            if (!result.IsSuccess)
                _out.Rejected(_game.AppliedTurn + 1, result.ErrorMessage ?? "refused");
        }
    }

    private async Task WatchRelaysAsync(CancellationToken stop)
    {
        var last = new Dictionary<Uri, RelayLinkState>();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (RelayLink link in _game.RelayLinks)
                {
                    if (last.TryGetValue(link.Url, out RelayLinkState seen) && seen == link.State)
                        continue;
                    last[link.Url] = link.State;
                    _out.Relay(link.Url, link.State);
                }
                await Task.Delay(TimeSpan.FromMilliseconds(100), stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}

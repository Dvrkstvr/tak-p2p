using System.Security.Cryptography;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Serialization;
using TakEngine.Crypto;
using TakEngine.Multiplayer;
using TakEngine.Transport.Relay;

namespace TakApp.Blazor.Services;

/// <summary>
/// The browser's view over one <see cref="MultiplayerGame"/> (D-046): host, join or rebuild by invite, submit moves, and keep a
/// transcript in the CLI's <c>--plain</c> line format so two devices' runs can be compared. No rules or protocol here; the board shown
/// is the TPS the game committed after each applied move.
/// </summary>
public sealed class OnlineGameService(BrowserStorage storage) : IAsyncDisposable
{
    public static readonly IReadOnlyList<string> DefaultRelays = ["wss://nos.lol", "wss://relay.damus.io"];

    private readonly TabGameStore _store = new();
    private readonly PublishBudget _budget = new();
    private readonly object _gate = new();
    private readonly List<string> _transcript = [];
    private readonly List<string> _moves = [];
    private readonly Dictionary<string, int> _ownEvents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedEvents = new(StringComparer.Ordinal);
    private readonly Dictionary<Uri, RelayLinkState> _links = [];
    private CancellationTokenSource? _watch;

    public MultiplayerGame? Game { get; private set; }

    /// <summary>The board after the last applied move (or the empty board before the first).</summary>
    public GameBoard? Board { get; private set; }

    public string? InviteCode { get; private set; }

    public IReadOnlyList<string> Transcript
    {
        get { lock (_gate) return [.. _transcript]; }
    }

    public IReadOnlyList<string> Moves
    {
        get { lock (_gate) return [.. _moves]; }
    }

    public IReadOnlyDictionary<Uri, RelayLinkState> Links
    {
        get { lock (_gate) return new Dictionary<Uri, RelayLinkState>(_links); }
    }

    /// <summary>Raised after anything shown changed; may come from the game's worker.</summary>
    public event Action? Changed;

    public bool IsMyTurn =>
        Game is { Result: null, MyColor: { } color } g && TurnExchange.TurnOwner(g.AppliedTurn + 1) == color;

    /// <summary>What the player is waiting for, in the CLI's words.</summary>
    public string Waiting
    {
        get
        {
            if (Game is not { } g)
                return "";
            if (g.Result is not null)
                return "game over";
            return g.Record.State switch
            {
                HandshakeState.Inviting => "for a guest to join",
                HandshakeState.Joining => "for the host to answer",
                HandshakeState.InGame => IsMyTurn ? $"your move turn {g.AppliedTurn + 1}" : $"opponent move turn {g.AppliedTurn + 1}",
                _ => g.Record.Note ?? g.Record.State.ToString().ToLowerInvariant(),
            };
        }
    }

    public async Task<string?> HostAsync(BoardSize size, int komiHalves)
    {
        MultiplayerOptions options = await OptionsAsync();
        try
        {
            MultiplayerGame game = await MultiplayerGame.HostAsync(options, size, komiHalves);
            InviteCode = game.Invite.ToCompactCode();
            Line($"INVITE {InviteCode}");
            await BeginAsync(game);
            return null;
        }
        catch (Exception ex) when (ex is TakEngine.Transport.Matchmaking.InviteFormatException or GameStoreException)
        {
            return ex.Message;
        }
    }

    public async Task<string?> JoinAsync(string inviteText) =>
        await OpenAsync(inviteText.Trim(), rebuild: false);

    /// <summary>Rebuilds a game this browser's key is in from the relays (after a reload lost the tab's memory).</summary>
    public async Task<string?> RebuildAsync(string inviteText) =>
        await OpenAsync(inviteText.Trim(), rebuild: true);

    public async Task<CommandResult> SubmitAsync(TakMove move)
    {
        if (Game is not { } g)
            return new CommandResult(false, "No game.");
        CommandResult result = await g.SubmitAsync(move);
        if (!result.IsSuccess)
            Line($"REJECTED turn {g.AppliedTurn + 1} {OneLine(result.ErrorMessage ?? "refused")}");
        return result;
    }

    public async Task<CommandResult> SubmitAsync(string ptn)
    {
        if (Game is not { } g)
            return new CommandResult(false, "No game.");
        CommandResult result = await g.SubmitAsync(ptn.Trim());
        if (!result.IsSuccess)
            Line($"REJECTED turn {g.AppliedTurn + 1} {OneLine(result.ErrorMessage ?? "refused")}");
        return result;
    }

    public async Task ResignAsync()
    {
        if (Game is { } g)
            await g.ResignAsync();
    }

    /// <summary>Leaves the game view; a game still in progress stays on the relays and can be rebuilt with its invite.</summary>
    public async Task LeaveAsync()
    {
        if (_watch is not null)
            await _watch.CancelAsync();
        _watch = null;
        if (Game is { } g)
            await g.DisposeAsync();
        Game = null;
        Board = null;
        InviteCode = null;
        lock (_gate)
        {
            _transcript.Clear();
            _moves.Clear();
            _ownEvents.Clear();
            _reportedEvents.Clear();
            _links.Clear();
        }
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync() => await LeaveAsync();

    private async Task<string?> OpenAsync(string inviteText, bool rebuild)
    {
        if (!TakEngine.Transport.Matchmaking.InviteCode.TryParse(inviteText, out _, out string? error))
            return error;
        MultiplayerOptions options = await OptionsAsync();
        JoinOutcome outcome = rebuild
            ? await MultiplayerGame.RebuildAsync(options, inviteText)
            : await MultiplayerGame.JoinAsync(options, inviteText);
        if (outcome.Game is not { } game)
            return outcome.Error;
        InviteCode = inviteText;
        await BeginAsync(game);
        return null;
    }

    private async Task<MultiplayerOptions> OptionsAsync()
    {
        if (Game is not null)
            await LeaveAsync();
        var (privHex, _) = await storage.GetOrCreateKeypairAsync();
        return new MultiplayerOptions(SecretKey.FromHex(privHex), _store, new ClientWebSocketRelaySocketFactory(), TimeProvider.System,
            DefaultRelays, () => RandomNumberGenerator.GetBytes(32))
        {
            Budget = _budget,
            Observe = Attach,
        };
    }

    private void Attach(MultiplayerGame game)
    {
        game.Started += _ => Changed?.Invoke();
        game.ActionApplied += (applied, mine) =>
        {
            lock (_gate)
            {
                if (mine)
                    _ownEvents[applied.EventId] = applied.Action.Turn;
                if (applied.Action.Ptn is { } ptn)
                    _moves.Add(ptn);
            }
            Board = TpsSerializer.Deserialize(applied.TpsAfter, game.Record.KomiHalves);
            Line($"APPLIED turn {applied.Action.Turn} {applied.Action.Ptn} {applied.StateHashAfter}");
        };
        game.PublishStatus += (eventId, outcome) =>
        {
            int turn;
            lock (_gate)
            {
                if (outcome == PublishOutcome.Pending || !_reportedEvents.Add(eventId))
                    return;
                if (!_ownEvents.Remove(eventId, out turn))
                    turn = -1;
            }
            if (turn >= 0)
            {
                Line($"SENT turn {turn} " + string.Join(' ', game.PublishDetail(eventId).OrderBy(p => p.Key.ToString(), StringComparer.Ordinal)
                    .Select(p => $"{p.Key.ToString().TrimEnd('/')}:{Status(p.Value)}")));
            }
            _ = ReportEventAsync(game.GameId, eventId);
        };
        game.Flagged += flag => Line($"FLAG turn {flag.Turn} {OneLine(flag.Reason)}");
        game.Notice += text => Line($"NOTICE {OneLine(text)}");
        game.Failed += text => Line($"NOTICE {OneLine(text)}");
        game.Ended += result => Line($"FINAL {Result(result)} {game.StateHash}");
        game.LinkChanged += (_, _) => Changed?.Invoke();
    }

    private async Task BeginAsync(MultiplayerGame game)
    {
        Game = game;
        // A rebuilt game arrives with its history already stored; show it as stored.
        StoredGame? stored = await _store.LoadAsync(game.GameId);
        if (stored is { Progress.AppliedTurn: > 0 } && Moves.Count == 0)
        {
            Board = TpsSerializer.Deserialize(stored.Progress.Tps, game.Record.KomiHalves);
            lock (_gate)
            {
                _moves.Clear();
                _moves.AddRange(stored.Applied.Select(a => a.Action.Ptn).OfType<string>());
            }
            foreach (AppliedAction applied in stored.Applied)
                Line($"APPLIED turn {applied.Action.Turn} {applied.Action.Ptn} {applied.StateHashAfter} (rebuilt)");
            if (game.Result is { } result)
                Line($"FINAL {Result(result)} {game.StateHash}");
        }
        Board ??= new GameBoard(game.Record.Size, game.Record.KomiHalves);
        _watch = new CancellationTokenSource();
        _ = WatchRelaysAsync(game, _watch.Token);
        Changed?.Invoke();
    }

    private async Task WatchRelaysAsync(MultiplayerGame game, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (RelayLink link in game.RelayLinks)
                {
                    lock (_gate)
                    {
                        if (_links.TryGetValue(link.Url, out RelayLinkState seen) && seen == link.State)
                            continue;
                        _links[link.Url] = link.State;
                    }
                    Line($"RELAY {link.Url.ToString().TrimEnd('/')} {(link.State == RelayLinkState.CaughtUp ? "caught-up" : link.State.ToString().ToLowerInvariant())}");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(250), stop);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Every event this browser published, by id, so a live run can log it (pipeline/evidence/live-relay-log.md).</summary>
    private async Task ReportEventAsync(Guid gameId, string eventId)
    {
        StoredGame? stored = await _store.LoadAsync(gameId);
        if (stored?.Outbox.FirstOrDefault(o => o.EventId == eventId) is { } entry)
            Line($"EVENT {entry.ActionType.ToString().ToUpperInvariant()} turn {entry.Turn} {eventId}");
    }

    private void Line(string line)
    {
        lock (_gate) _transcript.Add(line);
        Changed?.Invoke();
    }

    private static string Status(RelayPublishState state) => state.Status switch
    {
        RelayPublishStatus.Sent => "ok",
        RelayPublishStatus.Refused => $"refused({string.Join('_', (state.Reason ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))})",
        _ => "pending",
    };

    /// <summary>The CLI's FINAL wording: <c>white-road</c>, <c>black-flats</c>, <c>draw-flats</c>, <c>white-resign</c>.</summary>
    public static string Result(GameResult result)
    {
        string who = result.IsDraw || result.Winner is null ? "draw" : result.Winner.Value.ToString().ToLowerInvariant();
        string why = result.Reason switch
        {
            GameEndReason.Road => "road",
            GameEndReason.FlatCount => "flats",
            GameEndReason.Resignation => "resign",
            GameEndReason.TimeoutDraw => "timeout",
            GameEndReason.Agreement => "agreement",
            _ => result.Reason.ToString().ToLowerInvariant(),
        };
        return $"{who}-{why}";
    }

    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

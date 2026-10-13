using System.Security.Cryptography;
using Microsoft.JSInterop;
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
public sealed class OnlineGameService(BrowserStorage storage, TabGameLock tabLock, IJSRuntime js) : IAsyncDisposable
{
    /// <summary>The refusal when another tab of this browser holds the game (M0 code review, finding 6).</summary>
    public const string OpenInAnotherTab =
        "This game is open in another tab of this browser. Play it there, or press Leave in that tab (or close it) and try again here.";

    public static readonly IReadOnlyList<string> DefaultRelays = ["wss://nos.lol", "wss://relay.damus.io"];

    /// <summary>The JS function (wwwroot/js/page-visibility.js) that calls <see cref="OnPageVisible"/> when the tab is shown again.</summary>
    public const string VisibilityWatch = "takVisibility.watch";

    private readonly TabGameStore _store = new();
    private readonly PublishBudget _budget = new();
    private readonly object _gate = new();
    private readonly List<string> _transcript = [];
    private readonly List<string> _moves = [];
    private readonly Dictionary<string, int> _ownEvents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedEvents = new(StringComparer.Ordinal);
    private readonly Dictionary<Uri, RelayLinkState> _links = [];
    private CancellationTokenSource? _watch;
    private DotNetObjectReference<OnlineGameService>? _visibility;
    private Guid? _locked;

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
            // A fresh game id, so no other tab holds it; holding it stops a second tab from rebuilding this game.
            await ClaimAsync(game.GameId);
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

    /// <summary>The games this tab stored since it loaded, finished or not; any of them can be resumed.</summary>
    public Task<IReadOnlyList<NetGameRecord>> StoredGamesAsync() => _store.ListGamesAsync();

    /// <summary>Joins by invite; a game this tab already stored (one it left) is resumed instead.</summary>
    public async Task<string?> JoinAsync(string inviteText) =>
        await OpenAsync(inviteText.Trim(), rebuild: false);

    /// <summary>
    /// Rebuilds a game this browser's key is in from the relays (after a reload lost the tab's memory); a game this tab still has
    /// stored is resumed instead.
    /// </summary>
    public async Task<string?> RebuildAsync(string inviteText) =>
        await OpenAsync(inviteText.Trim(), rebuild: true);

    /// <summary>Reopens a game this tab stored (after Leave), as the CLI's <c>resume</c> does.</summary>
    public async Task<string?> ResumeAsync(Guid gameId)
    {
        MultiplayerOptions options = await OptionsAsync();
        if (await ClaimAsync(gameId) is { } refused)
            return refused;
        if (await MultiplayerGame.ResumeAsync(options, gameId) is not { } game)
        {
            await ReleaseAsync();
            return "This game is not stored in this tab any more. Paste its invite and use Rebuild from relays.";
        }
        InviteCode = game.Invite.ToCompactCode();
        await BeginAsync(game, "stored");
        return null;
    }

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
        await ReleaseAsync();
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

    public async ValueTask DisposeAsync()
    {
        await LeaveAsync();
        _visibility?.Dispose();
        _visibility = null;
    }

    private async Task<string?> OpenAsync(string inviteText, bool rebuild)
    {
        if (!TakEngine.Transport.Matchmaking.InviteCode.TryParse(inviteText, out var invite, out string? error))
            return error;
        // Left earlier in this tab: the store still has it, so carry on from there (as the CLI's join does).
        if (await _store.LoadAsync(invite!.GameId) is not null)
            return await ResumeAsync(invite.GameId);
        MultiplayerOptions options = await OptionsAsync();
        if (await ClaimAsync(invite.GameId) is { } refused)
            return refused;
        try
        {
            JoinOutcome outcome = rebuild
                ? await MultiplayerGame.RebuildAsync(options, inviteText)
                : await MultiplayerGame.JoinAsync(options, inviteText);
            if (outcome.Game is not { } game)
            {
                await ReleaseAsync();
                return outcome.Error;
            }
            InviteCode = inviteText;
            await BeginAsync(game, "rebuilt");
            return null;
        }
        catch
        {
            await ReleaseAsync();
            throw;
        }
    }

    /// <summary>Takes the cross-tab lock on <paramref name="gameId"/>, or returns the refusal when another tab holds it.</summary>
    private async Task<string?> ClaimAsync(Guid gameId)
    {
        if (await tabLock.AcquireAsync(gameId) == TabGameLock.Outcome.Busy)
            return OpenInAnotherTab;
        _locked = gameId;
        return null;
    }

    private async Task ReleaseAsync()
    {
        if (_locked is { } id)
        {
            _locked = null;
            await tabLock.ReleaseAsync(id);
        }
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

    private async Task BeginAsync(MultiplayerGame game, string historyFrom = "stored")
    {
        Game = game;
        // A rebuilt or resumed game arrives with its history already stored; show it as stored.
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
                Line($"APPLIED turn {applied.Action.Turn} {applied.Action.Ptn} {applied.StateHashAfter} ({historyFrom})");
            if (game.Result is { } result)
                Line($"FINAL {Result(result)} {game.StateHash}");
        }
        Board ??= new GameBoard(game.Record.Size, game.Record.KomiHalves);
        await WatchVisibilityAsync();
        _watch = new CancellationTokenSource();
        _ = WatchRelaysAsync(game, _watch.Token);
        Changed?.Invoke();
    }

    /// <summary>
    /// A tab that was hidden (a phone locked, a laptop slept) may hold relay sockets that died without a close. When it becomes
    /// visible again, or the browser says it is back online, the game probes its relays at once instead of after the idle window
    /// (D-051). The hook lives in <c>wwwroot/js/page-visibility.js</c>; without it the idle-window probe still finds the drop.
    /// </summary>
    [JSInvokable]
    public void OnPageVisible() => Game?.CheckConnections();

    private async Task WatchVisibilityAsync()
    {
        if (_visibility is not null)
            return;
        _visibility = DotNetObjectReference.Create(this);
        try
        {
            await js.InvokeVoidAsync(VisibilityWatch, _visibility);
        }
        catch (JSException)
        {
            // No hook: the idle-window probe still catches a silent drop, only later.
        }
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

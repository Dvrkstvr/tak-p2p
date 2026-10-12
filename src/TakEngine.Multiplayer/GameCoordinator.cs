using System.Threading.Channels;
using TakEngine.Abstractions;
using TakEngine.Core.Cryptography;
using TakEngine.Core.Serialization;
using TakEngine.Core.Board;
using TakEngine.Core.Session;
using TakEngine.Crypto;
using TakEngine.Multiplayer.Handshake;
using TakEngine.Transport.Envelope;
using TakEngine.Transport.Matchmaking;
using TakEngine.Transport.Nostr;
using TakEngine.Transport.Relay;

namespace TakEngine.Multiplayer;

/// <summary>
/// The one shell of the multiplayer module: runs the pure decisions (<see cref="HostHandshake"/>, <see cref="GuestHandshake"/>,
/// <see cref="TurnExchange"/>, <see cref="IncomingClassifier"/>, <see cref="LinkStatus"/>) against <see cref="RelayPool"/>,
/// <see cref="IGameStore"/> and <see cref="TimeProvider"/>. Everything runs on one worker loop, one item at a time: received
/// events, a 1 s tick (JOIN republish, catch-up, link status), publish acknowledgements and the head's commands.
/// Order for a local move: validate, sign, save with its outbox entry, then publish. For a remote action: classify, apply, save.
/// </summary>
internal sealed class GameCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    /// <summary>How far ahead of our clock an opponent's <c>created_at</c> may move <c>since</c>.</summary>
    private static readonly TimeSpan MaxClockAhead = TimeSpan.FromMinutes(1);

    private readonly MultiplayerOptions _o;
    private readonly MultiplayerGame _owner;
    private readonly IGameStore _store;
    private readonly TimeProvider _time;
    private readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly List<(SignedGameAction Action, string EventId)> _early = [];

    private NetGameRecord _record = null!;
    private InviteCode _invite = null!;
    private HostHandshake? _host;
    private GuestHandshake? _guest;
    private TakGameSession? _session;
    private TurnExchange? _exchange;
    private ExchangeProgress? _final;
    private SignedGameAction? _lastSigned;
    private RelayPool? _pool;
    private Task? _loop;
    private Task? _reader;
    private ITimer? _timer;
    private NostrEvent? _join;
    private DateTimeOffset _joinFirstSent;
    private int _joinTimesSent;
    private int _publishedTurn;
    private long _maxCreatedAt;
    private DateTimeOffset? _lastHeardFromOpponent;
    private (OwnLink, OpponentLink)? _lastLinks;
    private int _dropped;
    private readonly Dictionary<string, int> _flagsBy = new(StringComparer.Ordinal);
    private bool _broken;

    public GameCoordinator(MultiplayerOptions options, MultiplayerGame owner)
    {
        ArgumentNullException.ThrowIfNull(options);
        _o = options;
        _owner = owner;
        _store = options.Store;
        _time = options.Time;
    }

    public NetGameRecord Record => _record;

    public InviteCode Invite => _invite;

    public int AppliedTurn => _session?.AppliedTurn ?? _final?.AppliedTurn ?? 0;

    public string StateHash => _session?.CurrentStateHash ?? _final?.StateHash ?? StateHasher.ComputeGenesisHash(_record.Size);

    /// <summary>Rows 1-2: events whose id or signature the pool refused, plus valid events from someone who isn't the opponent.</summary>
    public int DroppedEvents =>
        Volatile.Read(ref _dropped) + (_pool is { } pool ? pool.Links.Sum(l => pool.InvalidEventsDropped(l.Url)) : 0);

    public IReadOnlyList<RelayLink> RelayLinks => _pool?.Links ?? [];

    public IReadOnlyDictionary<Uri, RelayPublishState> PublishDetail(string eventId) =>
        _pool?.PerRelay(eventId) ?? new Dictionary<Uri, RelayPublishState>();

    public OwnLink OwnLink => _pool is null ? OwnLink.Offline : LinkStatus.Own(_pool.Links.Select(l => l.State));

    public OpponentLink OpponentLink => LinkStatus.Opponent(_lastHeardFromOpponent, _time.GetUtcNow());

    private PublicKey Me => _o.Key.PublicKey;

    private PublicKey? Opponent => _record.OpponentPubKey is { } hex ? PublicKey.FromHex(hex) : null;

    // ---- start ----

    public async Task HostAsync(BoardSize size, int komiHalves, string? nickname)
    {
        _host = HostHandshake.Create(Guid.NewGuid(), Me, size, komiHalves, _o.Random32(), _o.DefaultRelays, nickname);
        _record = _host.Record;
        _invite = _host.Invite;
        await _store.SaveNewGameAsync(_record, Genesis()).ConfigureAwait(false);
        StartNetwork();
    }

    public async Task<string?> JoinAsync(string inviteText)
    {
        GuestStart start = GuestHandshake.Start(inviteText, Me, _o.Random32(), _o.DefaultRelays);
        if (start.Handshake is null)
            return start.Error;
        _guest = start.Handshake;
        _record = _guest.Record;
        _invite = _guest.Invite;
        await _store.SaveNewGameAsync(_record, Genesis()).ConfigureAwait(false);
        _join = Encode(Opponent!, Sign(_guest.UnsignedJoin));
        await _store.AddOutboxAsync(_record.GameId, Outbox(_join, GameActionType.Join, 0)).ConfigureAwait(false);
        StartNetwork();
        _pool!.Publish(_join);
        _joinFirstSent = _time.GetUtcNow();
        _joinTimesSent = 1;
        return null;
    }

    public async Task<bool> ResumeAsync(Guid gameId)
    {
        StoredGame? stored = await _store.LoadAsync(gameId).ConfigureAwait(false);
        if (stored is null)
            return false;
        _record = stored.Record;
        _publishedTurn = stored.Progress.PublishedTurn;
        _maxCreatedAt = stored.Progress.MaxCreatedAt;
        foreach (FlagEntry flag in stored.Flags)
            _flagsBy[flag.Author] = _flagsBy.GetValueOrDefault(flag.Author) + 1;
        if (_record.Role == NetRole.Host)
        {
            _host = HostHandshake.Restore(_record);
            _invite = _host.Invite;
        }
        else
        {
            _guest = GuestHandshake.Restore(_record);
            _invite = _guest.Invite;
        }
        if (_record.State is HandshakeState.Cancelled or HandshakeState.Failed or HandshakeState.Completed)
        {
            _final = stored.Progress;
            // A finished game may still owe the opponent its last word (a RESIGN or the winning move that no relay took yet).
            // A cancelled or failed one publishes nothing more.
            var owed = _record.State == HandshakeState.Completed ? stored.Outbox.Where(o => !o.Sent).ToList() : [];
            if (owed.Count > 0)
            {
                StartNetwork();
                foreach (OutboxEntry entry in owed)
                    _pool!.Publish(ParseEvent(entry.EventJson));
            }
            return true;
        }

        if (_record.State == HandshakeState.InGame)
        {
            ExchangeProgress p = stored.Progress;
            AttachSession(TakGameSession.Restore(new GameId(gameId), _record.MyColor!.Value, _o.Key, Opponent!, _o.Random32,
                p.Tps, p.StateHash, p.AppliedTurn, _record.KomiHalves), stored.Applied.Select(a => a.Action));
        }

        StartNetwork();
        // Republish what may not have arrived: every unsent entry, and our last move once (receivers ignore duplicates).
        OutboxEntry? lastOwnMove = stored.Outbox.Where(o => o.ActionType == GameActionType.Move).MaxBy(o => o.Turn);
        foreach (OutboxEntry entry in stored.Outbox.Where(o => !o.Sent || o == lastOwnMove))
            _pool!.Publish(ParseEvent(entry.EventJson));
        if (_record.State == HandshakeState.Joining && stored.Outbox.FirstOrDefault(o => o.ActionType == GameActionType.Join) is { } join)
        {
            _join = ParseEvent(join.EventJson);
            _pool!.Publish(_join);
            _joinFirstSent = _time.GetUtcNow();
            _joinTimesSent = 1;
        }
        return true;
    }

    /// <summary>After this many flags against the opponent, a notice suggests resigning (scope.md; nothing auto-forfeits).</summary>
    private const int FlagsBeforeResignHint = 3;

    /// <summary>How long a cold rebuild waits for relay history before working with what arrived.</summary>
    private static readonly TimeSpan RebuildWait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Cold rebuild (F-037, D-045): our key and the invite, no stored state. Reads this game's whole history from the relays (no
    /// <c>since</c>), rebuilds it with <see cref="RebuildPlan"/>, stores it, then carries on as a resume. Null when it worked,
    /// else why not (nothing is stored then).
    /// </summary>
    public async Task<string?> RebuildAsync(string inviteText)
    {
        if (!InviteCode.TryParse(inviteText, out InviteCode? invite, out string? error))
            return error;
        if (await _store.LoadAsync(invite!.GameId).ConfigureAwait(false) is not null)
            return "This game is already stored on this device: resume it instead.";
        RelayHistoryResult fetched = await RelayHistory.FetchAsync(_o, invite, RebuildWait).ConfigureAwait(false);
        if (fetched.Unanswered.Count > 0)
        {
            // Rebuilding from part of the history could stop before a move we already signed, and we would then sign that turn again.
            return "Not every relay sent its history in time (" + string.Join(", ", fetched.Unanswered)
                + "), so the game can't be rebuilt safely yet. Try again when they are reachable.";
        }

        RebuildResult rebuilt = RebuildPlan.Build(invite, Me, fetched.History);
        if (rebuilt.Error is not null)
            return rebuilt.Error;

        NetGameRecord record = rebuilt.Record!;
        try
        {
            await _store.SaveRebuiltGameAsync(record, rebuilt.Applied, rebuilt.Outbox, rebuilt.Progress!).ConfigureAwait(false);
        }
        catch (GameStoreException ex)
        {
            return "The rebuilt game could not be saved: " + ex.Message;
        }
        _record = record;
        await ResumeAsync(record.GameId).ConfigureAwait(false);
        return null;
    }

    private void StartNetwork()
    {
        _pool = new RelayPool(_record.Relays.Select(r => new Uri(r)), _o.Sockets, _time, Me.ToHex(), budget: _o.Budget);
        _pool.PublishStatusChanged += (id, outcome) => Post(() => OnPublishStatusAsync(id, outcome));
        _loop = Task.Run(RunAsync);
        _reader = Task.Run(ReadAsync);
        _pool.Start(Subscription);
        _timer = _time.CreateTimer(_ => Post(TickAsync), null, TickInterval, TickInterval);
    }

    private RelayMessage.Req Subscription()
    {
        // Only the opponent's accepted events move `since` (NoteCreatedAt): a stranger's or our own clock must not hide their moves.
        long max = Interlocked.Read(ref _maxCreatedAt);
        // A host never narrows the first filter to its guest (D-042): a second guest's JOIN must still reach it to be surfaced.
        // Decoding still judges authors (row 2), so a stranger's event is only counted.
        return SubscriptionPlan.Build(_record.GameId, Me, _host is null ? Opponent : null, max > 0 ? max : null);
    }

    // ---- worker ----

    private void Post(Func<Task> work) => _work.Writer.TryWrite(work);

    private Task<T> Enqueue<T>(Func<Task<T>> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration onStop = default;
        if (!_work.Writer.TryWrite(async () =>
            {
                try
                {
                    done.TrySetResult(await work().ConfigureAwait(false));
                }
                catch (GameStoreException ex)
                {
                    MarkBroken(ex);
                    done.TrySetException(ex);
                }
                catch (Exception ex)
                {
                    done.TrySetException(ex);
                }
                finally
                {
                    onStop.Dispose();
                }
            }))
        {
            done.TrySetException(new ObjectDisposedException(nameof(MultiplayerGame)));
            return done.Task;
        }
        try
        {
            // Disposing stops the worker; a command still queued then fails instead of hanging.
            onStop = _stop.Token.Register(() => done.TrySetException(new ObjectDisposedException(nameof(MultiplayerGame))));
        }
        catch (ObjectDisposedException ex)
        {
            done.TrySetException(ex);
        }
        return done.Task;
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (Func<Task> work in _work.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                try
                {
                    await work().ConfigureAwait(false);
                }
                catch (GameStoreException ex)
                {
                    MarkBroken(ex);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A bug, not peer input (peer input never throws). Stop acting on this game rather than guess.
                    _broken = true;
                    _owner.RaiseNotice("Internal error, this game is paused: " + ex.Message + " Reopen the game to continue.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void MarkBroken(GameStoreException ex)
    {
        _broken = true;
        _owner.RaiseNotice("Could not save the game: " + ex.Message + " Reopen the game to continue.");
    }

    private async Task ReadAsync()
    {
        try
        {
            await foreach (ReceivedEvent received in _pool!.Events.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                Post(() => OnReceivedAsync(received));
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ---- incoming ----

    private async Task OnReceivedAsync(ReceivedEvent received)
    {
        if (_broken)
            return;
        NostrEvent evt = received.Event;

        PublicKey? opponent = Opponent;
        var context = new DecodeContext(_o.Key, _record.GameId, opponent);
        DecodeResult result = EnvelopeCodec.Decode(evt, context);

        if (result.Verdict == DecodeVerdict.Row2WrongAuthor && _host is not null && opponent is not null)
        {
            // A second guest's JOIN after the game is locked: the user should hear about it, but it changes nothing.
            DecodeResult asJoin = EnvelopeCodec.Decode(evt, context with { Opponent = null });
            if (asJoin.IsAccepted && asJoin.Action!.ActionType == GameActionType.Join && _host.OnJoin(asJoin.Action).Message is { } note)
                _owner.RaiseNotice(note);
        }

        Classification c = IncomingClassifier.Classify(result, opponent is not null);
        if (c.Decision == IncomingDecision.Drop)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }
        if (c.Decision == IncomingDecision.DropAndFlag)
        {
            await FlagAsync(result.Action?.Turn ?? 0, c.Reason!, evt.Id, evt.Pubkey).ConfigureAwait(false);
            return;
        }
        if (result.IsOwn)
            return;

        SignedGameAction action = result.Action!;
        if (opponent is not null && action.PlayerPubKey == opponent.ToHex())
        {
            _lastHeardFromOpponent = _time.GetUtcNow();
            NoteCreatedAt(evt.CreatedAt);
        }

        switch (action.ActionType)
        {
            case GameActionType.Join when _host is not null:
                await OnJoinAsync(action).ConfigureAwait(false);
                break;
            case GameActionType.Accept when _guest is not null:
                await OnAcceptAsync(action, evt).ConfigureAwait(false);
                break;
            case GameActionType.Move or GameActionType.Resign when _exchange is null:
                if (_early.Count < TurnExchange.BufferCapacity)
                    _early.Add((action, evt.Id)); // the ACCEPT may arrive after the first move
                break;
            case GameActionType.Move or GameActionType.Resign:
                await ApplyStepAsync(_exchange!.Receive(action, evt.Id, _time.GetUtcNow())).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>Moves <c>since</c> forward from an accepted opponent event, never past our clock plus <see cref="MaxClockAhead"/>.</summary>
    private void NoteCreatedAt(long createdAt)
    {
        long cap = (_time.GetUtcNow() + MaxClockAhead).ToUnixTimeSeconds();
        long value = Math.Min(createdAt, cap);
        if (value > Interlocked.Read(ref _maxCreatedAt))
            Interlocked.Exchange(ref _maxCreatedAt, value);
    }

    private async Task OnJoinAsync(SignedGameAction join)
    {
        HostDecision d = _host!.OnJoin(join);
        switch (d.Kind)
        {
            case HostDecisionKind.Accept:
                NostrEvent accept = Encode(PublicKey.FromHex(_host.Record.OpponentPubKey!), Sign(d.Accept!));
                // The lock and its ACCEPT land together: a restart never finds one without the other.
                _record = _host.Record;
                await _store.UpdateRecordAsync(_record, Outbox(accept, GameActionType.Accept, 0)).ConfigureAwait(false);
                StartGame();
                _pool!.Publish(accept);
                break;
            case HostDecisionKind.ResendAccept:
                StoredGame? stored = await _store.LoadAsync(_record.GameId).ConfigureAwait(false);
                if (stored?.Outbox.FirstOrDefault(o => o.ActionType == GameActionType.Accept) is { } entry)
                    _pool!.Republish(ParseEvent(entry.EventJson));
                break;
            case HostDecisionKind.Ignore when d.Message is { } message:
                _owner.RaiseNotice(message);
                break;
        }
    }

    private async Task OnAcceptAsync(SignedGameAction accept, NostrEvent evt)
    {
        GuestDecision d = _guest!.OnAccept(accept);
        switch (d.Kind)
        {
            case GuestDecisionKind.Start:
                _record = _guest.Record;
                await _store.UpdateRecordAsync(_record).ConfigureAwait(false);
                StartGame();
                break;
            case GuestDecisionKind.Fail:
                _record = _guest.Record;
                await _store.UpdateRecordAsync(_record).ConfigureAwait(false);
                _early.Clear();
                _owner.RaiseFailed(d.Message!);
                break;
            case GuestDecisionKind.DropAndFlag:
                await FlagAsync(0, d.Message!, evt.Id, evt.Pubkey).ConfigureAwait(false);
                break;
        }
    }

    private void StartGame()
    {
        AttachSession(TakGameSession.CreateRemote(new GameId(_record.GameId), _record.Size, _record.MyColor!.Value, _o.Key, Opponent!,
            _o.Random32, _record.KomiHalves), []);
        _owner.RaiseStarted(_record.MyColor!.Value);
        var early = _early.ToList();
        _early.Clear();
        foreach (var (action, eventId) in early)
            Post(() => ApplyStepAsync(_exchange!.Receive(action, eventId, _time.GetUtcNow())));
    }

    private void AttachSession(TakGameSession session, IEnumerable<SignedGameAction> history)
    {
        _session = session;
        _session.OnLocalActionSigned += a => _lastSigned = a;
        _exchange = new TurnExchange(session, Me, history);
    }

    /// <summary>Saves and announces what one exchange step did. A local move's own entry is saved by the caller.</summary>
    private async Task ApplyStepAsync(ExchangeStep step, int skip = 0)
    {
        foreach (AppliedAction applied in step.Applied.Skip(skip))
        {
            await _store.CommitAppliedAsync(_record.GameId, applied, Progress(applied)).ConfigureAwait(false);
            _owner.RaiseApplied(applied, mine: false);
        }
        foreach (ExchangeFlag flag in step.Flags)
            await FlagAsync(flag.Turn, flag.Reason, flag.EventId, _record.OpponentPubKey ?? "").ConfigureAwait(false);
        if (step.Ended is { } result)
            await EndAsync(result).ConfigureAwait(false);
    }

    private async Task EndAsync(GameResult result)
    {
        _record = _record with { State = HandshakeState.Completed, Result = result };
        await _store.UpdateRecordAsync(_record).ConfigureAwait(false);
        _owner.RaiseEnded(result);
    }

    private async Task FlagAsync(int turn, string reason, string? eventId, string author)
    {
        var flag = new FlagEntry(turn, reason, eventId, author, _time.GetUtcNow());
        await _store.AddFlagAsync(_record.GameId, flag).ConfigureAwait(false);
        _owner.RaiseFlagged(flag);
        int count = _flagsBy[author] = _flagsBy.GetValueOrDefault(author) + 1;
        if (count == FlagsBeforeResignHint && author == _record.OpponentPubKey)
        {
            _owner.RaiseNotice($"Your opponent has sent {count} rejected messages in this game. Your game is unchanged; if you no longer "
                + "trust it, you can resign. Nothing is forfeited automatically.");
        }
    }

    // ---- commands ----

    public Task<CommandResult> SubmitAsync(string ptn) => Enqueue(() => SubmitCoreAsync(session => LocalMoves.Submit(session, ptn)));

    public Task<CommandResult> SubmitAsync(TakMove move) => Enqueue(() => SubmitCoreAsync(session => LocalMoves.Submit(session, move)));

    private async Task<CommandResult> SubmitCoreAsync(Func<TakGameSession, CommandResult> submit)
    {
        if (Refusal() is { } refusal)
            return refusal;
        _lastSigned = null;
        CommandResult result = submit(_session!);
        if (!result.IsSuccess)
            return result; // refused locally: nothing is signed, saved or published

        SignedGameAction own = _lastSigned!;
        NostrEvent evt = Encode(Opponent!, own);
        ExchangeStep step = _exchange!.RecordLocal(own, evt.Id);
        _publishedTurn = own.Turn;
        AppliedAction mine = step.Applied[0];
        try
        {
            await _store.CommitAppliedAsync(_record.GameId, mine, Progress(mine), Outbox(evt, GameActionType.Move, own.Turn)).ConfigureAwait(false);
        }
        catch (GameStoreException ex)
        {
            _broken = true;
            return CommandResult.Fail("Your move could not be saved, so it was not sent. Reopen the game to continue. " + ex.Message);
        }
        _pool!.Publish(evt);
        _owner.RaiseApplied(mine, mine: true);
        try
        {
            await ApplyStepAsync(step, skip: 1).ConfigureAwait(false);
        }
        catch (GameStoreException ex)
        {
            MarkBroken(ex); // the move itself is saved and on its way
        }
        return CommandResult.Success();
    }

    public Task<CommandResult> ResignAsync() => Enqueue(async () =>
    {
        if (Refusal() is { } refusal)
            return refusal;
        _lastSigned = null;
        CommandResult result = _session!.Resign();
        if (!result.IsSuccess)
            return result;
        NostrEvent evt = Encode(Opponent!, _lastSigned!);
        GameResult ended = _session.Result!;
        NetGameRecord completed = _record with { State = HandshakeState.Completed, Result = ended };
        try
        {
            await _store.UpdateRecordAsync(completed, Outbox(evt, GameActionType.Resign, _lastSigned!.Turn)).ConfigureAwait(false);
        }
        catch (GameStoreException ex)
        {
            _broken = true;
            return CommandResult.Fail("Your resignation could not be saved, so it was not sent. Reopen the game to continue. " + ex.Message);
        }
        _record = completed;
        _pool!.Publish(evt);
        _owner.RaiseEnded(ended);
        return CommandResult.Success();
    });

    public Task<bool> CancelAsync() => Enqueue(async () =>
    {
        bool cancelled = _host?.Cancel() ?? _guest!.Cancel();
        if (!cancelled)
            return false;
        _record = _host?.Record ?? _guest!.Record;
        if (_join is not null)
            _pool?.Withdraw(_join.Id); // nothing more is published, even if the JOIN is still waiting for a relay
        _join = null;
        await _store.UpdateRecordAsync(_record).ConfigureAwait(false);
        return true;
    });

    private CommandResult? Refusal()
    {
        if (_broken)
            return CommandResult.Fail("This game could not be saved earlier. Reopen it to continue.");
        if (_session is null || _exchange is null)
            return CommandResult.Fail("The game has not started yet.");
        if (_exchange.IsCompleted)
            return CommandResult.Fail("The game is over.");
        return null;
    }

    // ---- timers and acknowledgements ----

    private async Task TickAsync()
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (_join is not null && _record.State == HandshakeState.Joining && now >= GuestHandshake.NextJoinAt(_joinFirstSent, _joinTimesSent))
        {
            _pool!.Republish(_join);
            _joinTimesSent++;
        }
        if (_exchange is not null && !_exchange.IsCompleted && _exchange.Tick(now))
            await _pool!.ResubscribeAsync().ConfigureAwait(false);

        var links = (OwnLink, OpponentLink);
        if (links != _lastLinks)
        {
            _lastLinks = links;
            _owner.RaiseLinkChanged(links.Item1, links.Item2);
        }
    }

    private async Task OnPublishStatusAsync(string eventId, PublishOutcome outcome)
    {
        if (outcome == PublishOutcome.Sent)
            await _store.MarkOutboxSentAsync(_record.GameId, eventId).ConfigureAwait(false);
        _owner.RaisePublishStatus(eventId, outcome);
    }

    // ---- helpers ----

    private SignedGameAction Sign(SignedGameAction unsigned) => ActionDigest.Sign(_o.Key, unsigned, _o.Random32());

    private NostrEvent Encode(PublicKey recipient, SignedGameAction action) =>
        EnvelopeCodec.Encode(_o.Key, recipient, action, _time.GetUtcNow().ToUnixTimeSeconds(), _o.Random32(), _o.Random32());

    private static OutboxEntry Outbox(NostrEvent evt, GameActionType type, int turn) =>
        new(evt.Id, type, turn, new RelayMessage.Publish(evt).Format());

    private static NostrEvent ParseEvent(string json) =>
        RelayMessage.Parse(json) is RelayMessage.Publish p ? p.NostrEvent : throw new GameStoreException("A stored event could not be read.");

    private ExchangeProgress Genesis() =>
        new(0, StateHasher.ComputeGenesisHash(_record.Size), TpsSerializer.Serialize(new GameBoard(_record.Size, _record.KomiHalves)), 0, 0);

    private ExchangeProgress Progress(AppliedAction applied) =>
        new(applied.Action.Turn, applied.StateHashAfter, applied.TpsAfter, _publishedTurn, Interlocked.Read(ref _maxCreatedAt));

    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested)
            return;
        _timer?.Dispose();
        _work.Writer.TryComplete();
        _stop.Cancel();
        foreach (Task? task in new[] { _loop, _reader })
        {
            if (task is not null)
                await task.ConfigureAwait(false);
        }
        if (_pool is not null)
            await _pool.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
    }
}

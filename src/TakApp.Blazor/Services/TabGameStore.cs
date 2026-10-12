using TakEngine.Abstractions;

namespace TakApp.Blazor.Services;

/// <summary>
/// <see cref="IGameStore"/> for one browser tab, kept in memory (D-046): a reload loses the game, and a later run can rebuild it from
/// the relays with the invite (D-045). A persisted browser store is M3. Each call lands whole or throws and changes nothing.
/// </summary>
/// <remarks>
/// Same semantics as <c>tests/TakEngine.Testing/InMemoryGameStore</c>, minus its crash knobs (<c>FailNextWrite</c>, <c>HoldWrites</c>).
/// It is a copy on purpose: the shipped app can't reference a test project, and this class goes away when M3 brings the persisted
/// browser store. Until then a change to the in-memory semantics (turn-follows check, outbox) goes into both copies
/// (M0 code review, nit 7).
/// </remarks>
public sealed class TabGameStore : IGameStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _games = [];

    public Task SaveNewGameAsync(NetGameRecord record, ExchangeProgress progress, CancellationToken cancellationToken = default) =>
        Write(() =>
        {
            if (_games.ContainsKey(record.GameId))
                throw new GameStoreException($"Game {record.GameId} is already stored.");
            _games[record.GameId] = new Entry(record, progress);
        });

    public Task UpdateRecordAsync(NetGameRecord record, OutboxEntry? outbox = null, CancellationToken cancellationToken = default) =>
        Write(() =>
        {
            Entry e = Get(record.GameId);
            e.Record = record;
            if (outbox is not null)
                e.Outbox.Add(outbox);
        });

    public Task SaveRebuiltGameAsync(NetGameRecord record, IReadOnlyList<AppliedAction> applied, IReadOnlyList<OutboxEntry> outbox,
        ExchangeProgress progress, CancellationToken cancellationToken = default) =>
        Write(() =>
        {
            if (_games.ContainsKey(record.GameId))
                throw new GameStoreException($"Game {record.GameId} is already stored.");
            var e = new Entry(record, progress);
            e.Applied.AddRange(applied);
            e.Outbox.AddRange(outbox);
            _games[record.GameId] = e;
        });

    public Task CommitAppliedAsync(Guid gameId, AppliedAction applied, ExchangeProgress progress, OutboxEntry? outbox = null,
        CancellationToken cancellationToken = default) =>
        Write(() =>
        {
            Entry e = Get(gameId);
            if (applied.Action.ActionType == GameActionType.Move && e.Progress.AppliedTurn != applied.Action.Turn - 1)
                throw new GameStoreException($"Turn {applied.Action.Turn} can't follow turn {e.Progress.AppliedTurn}.");
            e.Applied.Add(applied);
            e.Progress = progress;
            if (outbox is not null)
                e.Outbox.Add(outbox);
        });

    public Task UpdateProgressAsync(Guid gameId, ExchangeProgress progress, CancellationToken cancellationToken = default) =>
        Write(() => Get(gameId).Progress = progress);

    public Task AddOutboxAsync(Guid gameId, OutboxEntry entry, CancellationToken cancellationToken = default) =>
        Write(() => Get(gameId).Outbox.Add(entry));

    public Task MarkOutboxSentAsync(Guid gameId, string eventId, CancellationToken cancellationToken = default) =>
        Write(() =>
        {
            List<OutboxEntry> outbox = Get(gameId).Outbox;
            int i = outbox.FindIndex(o => o.EventId == eventId);
            if (i >= 0)
                outbox[i] = outbox[i] with { Sent = true };
        });

    public Task AddFlagAsync(Guid gameId, FlagEntry flag, CancellationToken cancellationToken = default) =>
        Write(() => Get(gameId).Flags.Add(flag));

    public Task<StoredGame?> LoadAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_games.TryGetValue(gameId, out Entry? e)
                ? new StoredGame(e.Record, e.Progress, [.. e.Applied], [.. e.Outbox], [.. e.Flags])
                : null);
        }
    }

    public Task<IReadOnlyList<NetGameRecord>> ListGamesAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<NetGameRecord>>(_games.Values.Select(e => e.Record).ToList());
    }

    private Task Write(Action apply)
    {
        try
        {
            lock (_gate) apply();
            return Task.CompletedTask;
        }
        catch (GameStoreException ex)
        {
            return Task.FromException(ex);
        }
    }

    private Entry Get(Guid gameId) =>
        _games.TryGetValue(gameId, out Entry? e) ? e : throw new GameStoreException($"Game {gameId} is not stored.");

    private sealed class Entry(NetGameRecord record, ExchangeProgress progress)
    {
        public NetGameRecord Record { get; set; } = record;
        public ExchangeProgress Progress { get; set; } = progress;
        public List<AppliedAction> Applied { get; } = [];
        public List<OutboxEntry> Outbox { get; } = [];
        public List<FlagEntry> Flags { get; } = [];
    }
}

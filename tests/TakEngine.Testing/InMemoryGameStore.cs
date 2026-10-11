using TakEngine.Abstractions;

namespace TakEngine.Testing;

/// <summary>
/// <see cref="IGameStore"/> in memory (architecture.md "Seams"). Outlives the coordinator that used it, so disposing a coordinator
/// and opening a new one on the same store is a restart. <see cref="FailNextWrite"/> makes the next write throw with nothing kept,
/// which simulates a crash at that point.
/// </summary>
public sealed class InMemoryGameStore : IGameStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _games = [];
    private Func<string, bool>? _failNext;
    private Task? _hold;

    /// <summary>Number of writes that landed.</summary>
    public int Writes { get; private set; }

    /// <summary>The next write (or the next write whose operation name matches) throws and changes nothing.</summary>
    public void FailNextWrite(Func<string, bool>? which = null)
    {
        lock (_gate) _failNext = which ?? (_ => true);
    }

    /// <summary>Writes wait for <paramref name="until"/> before they land (a slow disk); null lets them through again.</summary>
    public void HoldWrites(Task? until)
    {
        lock (_gate) _hold = until;
    }

    public Task SaveNewGameAsync(NetGameRecord record, ExchangeProgress progress, CancellationToken cancellationToken = default) =>
        Write(nameof(SaveNewGameAsync), () =>
        {
            if (_games.ContainsKey(record.GameId))
                throw new GameStoreException($"Game {record.GameId} is already stored.");
            _games[record.GameId] = new Entry(record, progress);
        });

    public Task UpdateRecordAsync(NetGameRecord record, OutboxEntry? outbox = null, CancellationToken cancellationToken = default) =>
        Write(nameof(UpdateRecordAsync), () =>
        {
            Entry e = Get(record.GameId);
            e.Record = record;
            if (outbox is not null)
                e.Outbox.Add(outbox);
        });

    public Task CommitAppliedAsync(Guid gameId, AppliedAction applied, ExchangeProgress progress, OutboxEntry? outbox = null, CancellationToken cancellationToken = default) =>
        Write(nameof(CommitAppliedAsync), () =>
        {
            Entry e = Get(gameId);
            e.Applied.Add(applied);
            e.Progress = progress;
            if (outbox is not null)
                e.Outbox.Add(outbox);
        });

    public Task UpdateProgressAsync(Guid gameId, ExchangeProgress progress, CancellationToken cancellationToken = default) =>
        Write(nameof(UpdateProgressAsync), () => Get(gameId).Progress = progress);

    public Task AddOutboxAsync(Guid gameId, OutboxEntry entry, CancellationToken cancellationToken = default) =>
        Write(nameof(AddOutboxAsync), () => Get(gameId).Outbox.Add(entry));

    public Task MarkOutboxSentAsync(Guid gameId, string eventId, CancellationToken cancellationToken = default) =>
        Write(nameof(MarkOutboxSentAsync), () =>
        {
            List<OutboxEntry> outbox = Get(gameId).Outbox;
            int i = outbox.FindIndex(o => o.EventId == eventId);
            if (i >= 0)
                outbox[i] = outbox[i] with { Sent = true };
        });

    public Task AddFlagAsync(Guid gameId, FlagEntry flag, CancellationToken cancellationToken = default) =>
        Write(nameof(AddFlagAsync), () => Get(gameId).Flags.Add(flag));

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

    private async Task Write(string operation, Action apply)
    {
        Task? hold;
        lock (_gate) hold = _hold;
        if (hold is not null)
            await hold.ConfigureAwait(false);
        lock (_gate)
        {
            if (_failNext is { } fail && fail(operation))
            {
                _failNext = null;
                throw new GameStoreException($"Simulated crash: {operation} was not saved.");
            }
            apply();
            Writes++;
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

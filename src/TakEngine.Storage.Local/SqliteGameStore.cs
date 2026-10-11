using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TakEngine.Abstractions;
using TakEngine.Crypto;

namespace TakEngine.Storage.Local;

/// <summary>
/// <see cref="IGameStore"/> on a SQLite file (F-037; pipeline/architecture.md "Data"), one per profile at
/// <c>&lt;data-dir&gt;/games.db</c>. Every call is one transaction (<c>BEGIN IMMEDIATE</c>, so two processes on one file
/// serialise). The file is migrated to <see cref="Schema.Current"/> on first use; a file written by a newer version is only read,
/// never written. Every failure surfaces as <see cref="GameStoreException"/> with nothing of that call kept.
/// </summary>
public sealed class SqliteGameStore : IGameStore
{
    public const string FileName = "games.db";

    private readonly string _connectionString;
    private readonly SemaphoreSlim _open = new(1, 1);
    private bool _ready;
    private string? _readOnlyReason;

    public SqliteGameStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
    }

    public string DatabasePath { get; }

    public Task SaveNewGameAsync(NetGameRecord record, ExchangeProgress progress, CancellationToken cancellationToken = default) =>
        WriteAsync(cancellationToken, (c, tx) =>
        {
            if (Scalar(c, tx, "SELECT count(*) FROM NetGames WHERE GameId = $id;", ("$id", Id(record.GameId))) > 0)
                throw new GameStoreException($"Game {record.GameId} is already stored.");
            Execute(c, tx, """
                INSERT INTO NetGames (GameId, Role, MyPubKey, HostPubKey, OpponentPubKey, Size, KomiHalves, SeedCommit, HostNonce, GuestNonce,
                    Relays, State, MyColor, ResultIsDraw, ResultWinner, ResultReason, Note, AppliedTurn, StateHash, Tps, PublishedTurn, MaxCreatedAt)
                VALUES ($id, '', '', '', NULL, 0, 0, '', NULL, NULL, '', '', NULL, NULL, NULL, NULL, NULL, 0, '', '', 0, 0);
                """, ("$id", Id(record.GameId)));
            WriteRecord(c, tx, record);
            WriteProgress(c, tx, record.GameId, progress);
        });

    public Task UpdateRecordAsync(NetGameRecord record, OutboxEntry? outbox = null, CancellationToken cancellationToken = default) =>
        WriteAsync(cancellationToken, (c, tx) =>
        {
            WriteRecord(c, tx, record);
            if (outbox is not null)
                InsertOutbox(c, tx, record.GameId, outbox);
        });

    public Task SaveRebuiltGameAsync(NetGameRecord record, IReadOnlyList<AppliedAction> applied, IReadOnlyList<OutboxEntry> outbox,
        ExchangeProgress progress, CancellationToken cancellationToken = default) =>
        WriteAsync(cancellationToken, (c, tx) =>
        {
            if (Scalar(c, tx, "SELECT count(*) FROM NetGames WHERE GameId = $id;", ("$id", Id(record.GameId))) > 0)
                throw new GameStoreException($"Game {record.GameId} is already stored.");
            Execute(c, tx, """
                INSERT INTO NetGames (GameId, Role, MyPubKey, HostPubKey, OpponentPubKey, Size, KomiHalves, SeedCommit, HostNonce, GuestNonce,
                    Relays, State, MyColor, ResultIsDraw, ResultWinner, ResultReason, Note, AppliedTurn, StateHash, Tps, PublishedTurn, MaxCreatedAt)
                VALUES ($id, '', '', '', NULL, 0, 0, '', NULL, NULL, '', '', NULL, NULL, NULL, NULL, NULL, 0, '', '', 0, 0);
                """, ("$id", Id(record.GameId)));
            WriteRecord(c, tx, record);
            foreach (AppliedAction a in applied)
                InsertMove(c, tx, record.GameId, a);
            foreach (OutboxEntry entry in outbox)
                InsertOutbox(c, tx, record.GameId, entry);
            WriteProgress(c, tx, record.GameId, progress);
        });

    public Task CommitAppliedAsync(Guid gameId, AppliedAction applied, ExchangeProgress progress, OutboxEntry? outbox = null, CancellationToken cancellationToken = default) =>
        WriteAsync(cancellationToken, (c, tx) =>
        {
            RequireGame(c, tx, gameId);
            long stored = Scalar(c, tx, "SELECT AppliedTurn FROM NetGames WHERE GameId = $id;", ("$id", Id(gameId)));
            if (applied.Action.ActionType == GameActionType.Move && stored != applied.Action.Turn - 1)
                throw new GameStoreException($"Turn {applied.Action.Turn} can't follow turn {stored}: another process may be playing this game.");
            InsertMove(c, tx, gameId, applied);
            WriteProgress(c, tx, gameId, progress);
            if (outbox is not null)
                InsertOutbox(c, tx, gameId, outbox);
        });

    private static void InsertMove(SqliteConnection c, SqliteTransaction tx, Guid gameId, AppliedAction applied)
    {
        SignedGameAction a = applied.Action;
        Execute(c, tx, """
                INSERT INTO NetMoves (GameId, Pv, ActionType, Turn, PlayerPubKey, PrevStateHash, ActionData, Signature, EventId, TpsAfter, StateHashAfter)
                VALUES ($game, $pv, $type, $turn, $player, $prev, $data, $sig, $event, $tps, $hash);
                """,
            ("$game", Id(gameId)), ("$pv", a.Pv), ("$type", a.ActionType.ToWire()), ("$turn", a.Turn), ("$player", a.PlayerPubKey),
            ("$prev", a.PrevStateHash), ("$data", ActionDigest.CanonicalActionData(a.ActionData)), ("$sig", a.Signature),
            ("$event", applied.EventId), ("$tps", applied.TpsAfter), ("$hash", applied.StateHashAfter));
    }

    public Task UpdateProgressAsync(Guid gameId, ExchangeProgress progress, CancellationToken cancellationToken = default) =>
        WriteAsync(cancellationToken, (c, tx) => WriteProgress(c, tx, gameId, progress));

    public Task AddOutboxAsync(Guid gameId, OutboxEntry entry, CancellationToken cancellationToken = default) =>
        WriteAsync(cancellationToken, (c, tx) =>
        {
            RequireGame(c, tx, gameId);
            InsertOutbox(c, tx, gameId, entry);
        });

    public Task MarkOutboxSentAsync(Guid gameId, string eventId, CancellationToken cancellationToken = default) =>
        WriteAsync(cancellationToken, (c, tx) =>
            Execute(c, tx, "UPDATE Outbox SET Sent = 1 WHERE GameId = $game AND EventId = $event;", ("$game", Id(gameId)), ("$event", eventId)));

    public Task AddFlagAsync(Guid gameId, FlagEntry flag, CancellationToken cancellationToken = default) =>
        WriteAsync(cancellationToken, (c, tx) =>
        {
            RequireGame(c, tx, gameId);
            Execute(c, tx, "INSERT INTO Flags (GameId, Turn, Reason, EventId, Author, SeenAt) VALUES ($game, $turn, $reason, $event, $author, $at);",
                ("$game", Id(gameId)), ("$turn", flag.Turn), ("$reason", flag.Reason), ("$event", flag.EventId), ("$author", flag.Author),
                ("$at", flag.SeenAt.ToString("O", CultureInfo.InvariantCulture)));
        });

    public Task<StoredGame?> LoadAsync(Guid gameId, CancellationToken cancellationToken = default) =>
        ReadAsync(cancellationToken, c =>
        {
            (NetGameRecord Record, ExchangeProgress Progress)? game = ReadGames(c, gameId)
                .Select(g => ((NetGameRecord, ExchangeProgress)?)g).FirstOrDefault();
            if (game is null)
                return null;
            var applied = new List<AppliedAction>();
            using (var cmd = Command(c, null, """
                SELECT Pv, ActionType, Turn, PlayerPubKey, PrevStateHash, ActionData, Signature, EventId, TpsAfter, StateHashAfter
                FROM NetMoves WHERE GameId = $game ORDER BY Seq;
                """, ("$game", Id(gameId))))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    if (!GameActionTypes.TryParseWire(r.GetString(1), out GameActionType type))
                        throw new GameStoreException($"A stored action of game {gameId} has an unknown type '{r.GetString(1)}'.");
                    var action = new SignedGameAction(r.GetInt32(0), type, gameId, r.GetInt32(2), r.GetString(3), r.GetString(4),
                        ParseActionData(r.GetString(5)), r.GetString(6));
                    applied.Add(new AppliedAction(action, r.GetString(7), r.GetString(8), r.GetString(9)));
                }
            }
            var outbox = new List<OutboxEntry>();
            using (var cmd = Command(c, null, "SELECT EventId, ActionType, Turn, EventJson, Sent FROM Outbox WHERE GameId = $game ORDER BY Seq;", ("$game", Id(gameId))))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    GameActionTypes.TryParseWire(r.GetString(1), out GameActionType type);
                    outbox.Add(new OutboxEntry(r.GetString(0), type, r.GetInt32(2), r.GetString(3), r.GetInt64(4) != 0));
                }
            }
            var flags = new List<FlagEntry>();
            using (var cmd = Command(c, null, "SELECT Turn, Reason, EventId, Author, SeenAt FROM Flags WHERE GameId = $game ORDER BY Seq;", ("$game", Id(gameId))))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    flags.Add(new FlagEntry(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3),
                        DateTimeOffset.Parse(r.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
                }
            }
            return new StoredGame(game.Value.Record, game.Value.Progress, applied, outbox, flags);
        });

    public Task<IReadOnlyList<NetGameRecord>> ListGamesAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<NetGameRecord>>(cancellationToken, c => ReadGames(c, null).Select(g => g.Record).ToList());

    // ---- opening ----

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await _open.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_ready)
            {
                string? dir = Path.GetDirectoryName(Path.GetFullPath(DatabasePath));
                if (dir is not null)
                    Directory.CreateDirectory(dir);
                // Look at the version before anything can write: a newer file is opened read-only from then on.
                if (File.Exists(DatabasePath))
                {
                    using var probe = new SqliteConnection(new SqliteConnectionStringBuilder(_connectionString) { Mode = SqliteOpenMode.ReadOnly }.ToString());
                    probe.Open();
                    long version = Schema.Version(probe);
                    if (version > Schema.Current)
                    {
                        _readOnlyReason = $"The game database {DatabasePath} was written by a newer version (schema {version}; this build knows "
                            + $"{Schema.Current}). It was opened read-only and left unchanged.";
                    }
                }
                if (_readOnlyReason is null)
                {
                    using var migrate = new SqliteConnection(_connectionString);
                    migrate.Open();
                    Schema.Migrate(migrate);
                }
                _ready = true;
            }
        }
        catch (SqliteException ex)
        {
            throw new GameStoreException($"The game database {DatabasePath} could not be opened: {ex.Message}", ex);
        }
        finally
        {
            _open.Release();
        }

        var builder = new SqliteConnectionStringBuilder(_connectionString) { ForeignKeys = true };
        if (_readOnlyReason is not null)
            builder.Mode = SqliteOpenMode.ReadOnly;
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            connection.Open();
        }
        catch (SqliteException ex)
        {
            connection.Dispose();
            throw new GameStoreException($"The game database {DatabasePath} could not be opened: {ex.Message}", ex);
        }
        return connection;
    }

    private async Task WriteAsync(CancellationToken cancellationToken, Action<SqliteConnection, SqliteTransaction> write)
    {
        using SqliteConnection c = await OpenAsync(cancellationToken).ConfigureAwait(false);
        if (_readOnlyReason is not null)
            throw new GameStoreException(_readOnlyReason);
        try
        {
            using SqliteTransaction tx = c.BeginTransaction(deferred: false);
            write(c, tx);
            tx.Commit();
        }
        catch (SqliteException ex)
        {
            throw new GameStoreException($"Saving the game failed: {ex.Message}", ex);
        }
    }

    private async Task<T> ReadAsync<T>(CancellationToken cancellationToken, Func<SqliteConnection, T> read)
    {
        using SqliteConnection c = await OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return read(c);
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            throw new GameStoreException($"Reading the game database failed: {ex.Message}", ex);
        }
    }

    // ---- rows ----

    private static void RequireGame(SqliteConnection c, SqliteTransaction tx, Guid gameId)
    {
        if (Scalar(c, tx, "SELECT count(*) FROM NetGames WHERE GameId = $id;", ("$id", Id(gameId))) == 0)
            throw new GameStoreException($"Game {gameId} is not stored.");
    }

    private static void WriteRecord(SqliteConnection c, SqliteTransaction tx, NetGameRecord r)
    {
        int changed = Execute(c, tx, """
            UPDATE NetGames SET Role = $role, MyPubKey = $me, HostPubKey = $host, OpponentPubKey = $opp, Size = $size, KomiHalves = $komi,
                SeedCommit = $commit, HostNonce = $hn, GuestNonce = $gn, Relays = $relays, State = $state, MyColor = $color,
                ResultIsDraw = $draw, ResultWinner = $winner, ResultReason = $reason, Note = $note
            WHERE GameId = $id;
            """,
            ("$id", Id(r.GameId)), ("$role", r.Role.ToString()), ("$me", r.MyPubKey), ("$host", r.HostPubKey), ("$opp", r.OpponentPubKey),
            ("$size", (int)r.Size), ("$komi", r.KomiHalves), ("$commit", r.SeedCommit), ("$hn", r.HostNonce), ("$gn", r.GuestNonce),
            ("$relays", JsonSerializer.Serialize(new RelayList(1, r.Relays))), ("$state", r.State.ToString()), ("$color", r.MyColor?.ToString()),
            ("$draw", r.Result is null ? null : r.Result.IsDraw ? 1 : 0), ("$winner", r.Result?.Winner?.ToString()),
            ("$reason", r.Result?.Reason.ToString()), ("$note", r.Note));
        if (changed == 0)
            throw new GameStoreException($"Game {r.GameId} is not stored.");
    }

    private static void WriteProgress(SqliteConnection c, SqliteTransaction tx, Guid gameId, ExchangeProgress p)
    {
        int changed = Execute(c, tx, """
            UPDATE NetGames SET AppliedTurn = $turn, StateHash = $hash, Tps = $tps, PublishedTurn = $published, MaxCreatedAt = $max
            WHERE GameId = $id;
            """,
            ("$id", Id(gameId)), ("$turn", p.AppliedTurn), ("$hash", p.StateHash), ("$tps", p.Tps), ("$published", p.PublishedTurn),
            ("$max", p.MaxCreatedAt));
        if (changed == 0)
            throw new GameStoreException($"Game {gameId} is not stored.");
    }

    private static void InsertOutbox(SqliteConnection c, SqliteTransaction tx, Guid gameId, OutboxEntry e) =>
        Execute(c, tx, "INSERT INTO Outbox (GameId, EventId, ActionType, Turn, EventJson, Sent) VALUES ($game, $event, $type, $turn, $json, $sent);",
            ("$game", Id(gameId)), ("$event", e.EventId), ("$type", e.ActionType.ToWire()), ("$turn", e.Turn), ("$json", e.EventJson),
            ("$sent", e.Sent ? 1 : 0));

    private static IEnumerable<(NetGameRecord Record, ExchangeProgress Progress)> ReadGames(SqliteConnection c, Guid? gameId)
    {
        var list = new List<(NetGameRecord, ExchangeProgress)>();
        using var cmd = Command(c, null, """
            SELECT GameId, Role, MyPubKey, HostPubKey, OpponentPubKey, Size, KomiHalves, SeedCommit, HostNonce, GuestNonce, Relays, State,
                MyColor, ResultIsDraw, ResultWinner, ResultReason, Note, AppliedTurn, StateHash, Tps, PublishedTurn, MaxCreatedAt
            FROM NetGames WHERE $id IS NULL OR GameId = $id ORDER BY rowid;
            """, ("$id", gameId is { } g ? Id(g) : null));
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            string? Text(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            RelayList relays = JsonSerializer.Deserialize<RelayList>(r.GetString(10)) ?? throw new FormatException("relay list is empty");
            if (relays.V != 1)
                throw new FormatException($"relay list version {relays.V} is not known");
            GameResult? result = r.IsDBNull(13) ? null : new GameResult(r.GetInt64(13) != 0,
                Text(14) is { } w ? Enum.Parse<PlayerColor>(w) : null, Enum.Parse<GameEndReason>(r.GetString(15)));
            var record = new NetGameRecord(
                Guid.Parse(r.GetString(0)), Enum.Parse<NetRole>(r.GetString(1)), r.GetString(2), r.GetString(3), Text(4),
                (BoardSize)r.GetInt32(5), r.GetInt32(6), r.GetString(7), Text(8), Text(9), relays.Relays, Enum.Parse<HandshakeState>(r.GetString(11)),
                Text(12) is { } color ? Enum.Parse<PlayerColor>(color) : null, result, Text(16));
            list.Add((record, new ExchangeProgress(r.GetInt32(17), r.GetString(18), r.GetString(19), r.GetInt32(20), r.GetInt64(21))));
        }
        return list;
    }

    private static IReadOnlyDictionary<string, ActionValue> ParseActionData(string json)
    {
        var data = new Dictionary<string, ActionValue>(StringComparer.Ordinal);
        using JsonDocument doc = JsonDocument.Parse(json);
        foreach (JsonProperty p in doc.RootElement.EnumerateObject())
        {
            data[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => ActionValue.Of(p.Value.GetString()!),
                JsonValueKind.Number => ActionValue.Of(p.Value.GetInt64()),
                _ => throw new FormatException($"action data value '{p.Name}' is neither a string nor an integer"),
            };
        }
        return data;
    }

    // ---- plumbing ----

    private static string Id(Guid id) => id.ToString("D");

    private static SqliteCommand Command(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        SqliteCommand cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static int Execute(SqliteConnection c, SqliteTransaction tx, string sql, params (string Name, object? Value)[] args)
    {
        using SqliteCommand cmd = Command(c, tx, sql, args);
        return cmd.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection c, SqliteTransaction tx, string sql, params (string Name, object? Value)[] args)
    {
        using SqliteCommand cmd = Command(c, tx, sql, args);
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>The relay list column: versioned JSON (docs/decisions/0010).</summary>
    private sealed record RelayList(
        [property: System.Text.Json.Serialization.JsonPropertyName("v")] int V,
        [property: System.Text.Json.Serialization.JsonPropertyName("relays")] IReadOnlyList<string> Relays);
}

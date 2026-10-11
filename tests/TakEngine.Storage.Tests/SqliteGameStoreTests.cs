using Microsoft.Data.Sqlite;
using TakEngine.Abstractions;
using TakEngine.Storage.Local;

namespace TakEngine.Storage.Tests;

/// <summary>F-037 storage: SQLite-only behaviour (migration, versions, transactions). The shared promises are in GameStoreContractTests.</summary>
public sealed class SqliteGameStoreTests : IDisposable
{
    private static readonly Guid Game = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private readonly string _dir = Directory.CreateTempSubdirectory("tak-sqlite-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Db => Path.Combine(_dir, SqliteGameStore.FileName);

    private string CopyFixture()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "v0.db"), Db);
        return Db;
    }

    private static NetGameRecord Record() => new(
        Game, NetRole.Guest, "me", "host", "host", BoardSize.Three, 0, new string('c', 64), null, "gn", ["ws://127.0.0.1:7447"], HandshakeState.InGame);

    private static ExchangeProgress Progress(int turn) => new(turn, $"hash{turn}", $"tps{turn}", 0, 0);

    private static AppliedAction Applied(int turn) => new(
        SignedGameAction.Unsigned(GameActionType.Move, Game, turn, "me", $"hash{turn - 1}", [new("ptn", ActionValue.Of("a1"))]) with { Signature = "s" },
        $"event{turn}", $"tps{turn}", $"hash{turn}");

    private List<object?[]> Rows(string sql)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var rows = new List<object?[]>();
        while (r.Read())
        {
            var row = new object?[r.FieldCount];
            for (int i = 0; i < r.FieldCount; i++)
                row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private void Exec(string sql)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task V0Fixture_MigratesToV2_KeepingLegacyRowsAndMarkingThemEd25519()
    {
        CopyFixture();
        var before = Rows("SELECT * FROM Moves ORDER BY TurnIndex");

        await new SqliteGameStore(Db).ListGamesAsync();

        Assert.Equal(2L, Rows("PRAGMA user_version")[0][0]);
        var after = Rows("SELECT * FROM Moves ORDER BY TurnIndex");
        Assert.Equal(before.Select(r => r.Concat(new object?[] { "legacy-ed25519", null })), after); // old bytes kept, two new columns
        Assert.Single(Rows("SELECT * FROM Games"));
        Assert.Equal(["Flags", "Games", "Moves", "NetGames", "NetMoves", "Outbox"],
            Rows("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name").Select(r => (string)r[0]!));
    }

    [Fact]
    public async Task FreshFile_GetsOnlyTheMultiplayerTables_AndReopeningIsANoOp()
    {
        await new SqliteGameStore(Db).SaveNewGameAsync(Record(), Progress(0));
        string schema = string.Join("\n", Rows("SELECT sql FROM sqlite_master ORDER BY name").Select(r => r[0]));

        await new SqliteGameStore(Db).ListGamesAsync();

        Assert.Equal(schema, string.Join("\n", Rows("SELECT sql FROM sqlite_master ORDER BY name").Select(r => r[0])));
        Assert.Equal(2L, Rows("PRAGMA user_version")[0][0]);
        Assert.Empty(Rows("SELECT name FROM sqlite_master WHERE name IN ('Games', 'Moves')"));
    }

    [Fact]
    public async Task NewerVersion_IsReadOnly_AndTheFileIsUnchanged()
    {
        await new SqliteGameStore(Db).SaveNewGameAsync(Record(), Progress(0));
        Exec("PRAGMA user_version = 3;");
        byte[] bytes = await File.ReadAllBytesAsync(Db);

        var store = new SqliteGameStore(Db);
        StoredGame? loaded = await store.LoadAsync(Game);
        var ex = await Assert.ThrowsAsync<GameStoreException>(() => store.UpdateProgressAsync(Game, Progress(1)));

        Assert.NotNull(loaded);
        Assert.Contains("written by a newer version", ex.Message);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Db));
    }

    [Fact]
    public async Task CommitApplied_FailingHalfWay_KeepsNothing()
    {
        var store = new SqliteGameStore(Db);
        await store.SaveNewGameAsync(Record(), Progress(0));
        Exec("CREATE TRIGGER boom BEFORE INSERT ON Outbox BEGIN SELECT RAISE(ABORT, 'disk full'); END;");

        await Assert.ThrowsAsync<GameStoreException>(() =>
            store.CommitAppliedAsync(Game, Applied(1), Progress(1), new OutboxEntry("event1", GameActionType.Move, 1, "{}")));

        StoredGame loaded = (await store.LoadAsync(Game))!;
        Assert.Empty(loaded.Applied);
        Assert.Equal(Progress(0), loaded.Progress);
        Assert.Empty(loaded.Outbox);
    }

    [Fact]
    public async Task RebuiltGame_FailingHalfWay_KeepsNothing()
    {
        var store = new SqliteGameStore(Db);
        await store.ListGamesAsync(); // creates the schema
        Exec("CREATE TRIGGER boom BEFORE INSERT ON Outbox BEGIN SELECT RAISE(ABORT, 'disk full'); END;");

        await Assert.ThrowsAsync<GameStoreException>(() => store.SaveRebuiltGameAsync(Record(), [Applied(1), Applied(2)],
            [new OutboxEntry("m2", GameActionType.Move, 2, "{}", Sent: true)], Progress(2)));

        Assert.Null(await store.LoadAsync(Game));
        Assert.Empty(Rows("SELECT * FROM NetMoves"));
    }

    [Fact]
    public async Task RecordWithOutbox_FailingHalfWay_KeepsNothing()
    {
        var store = new SqliteGameStore(Db);
        await store.SaveNewGameAsync(Record(), Progress(0));
        Exec("CREATE TRIGGER boom BEFORE INSERT ON Outbox BEGIN SELECT RAISE(ABORT, 'disk full'); END;");

        await Assert.ThrowsAsync<GameStoreException>(() =>
            store.UpdateRecordAsync(Record() with { State = HandshakeState.Completed }, new OutboxEntry("resign", GameActionType.Resign, 0, "{}")));

        Assert.Equal(HandshakeState.InGame, (await store.LoadAsync(Game))!.Record.State);
    }

    [Fact]
    public async Task UnreadableFile_IsAStoreError()
    {
        await File.WriteAllTextAsync(Db, "this is not a database, but it is long enough to have a header page of sorts...");

        await Assert.ThrowsAsync<GameStoreException>(() => new SqliteGameStore(Db).ListGamesAsync());
    }
}

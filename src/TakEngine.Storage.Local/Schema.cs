using Microsoft.Data.Sqlite;

namespace TakEngine.Storage.Local;

/// <summary>
/// The local database's versions (pipeline/architecture.md "Data", docs/decisions/0010). <c>user_version</c> 0 is the pre-M0 schema
/// (Games, Moves) or an empty file; 2 is M0. Steps only go forward, each in one transaction; a file newer than <see cref="Current"/>
/// is never written.
/// </summary>
internal static class Schema
{
    public const int Current = 2;

    /// <summary>v0 -> v2 (additive): legacy moves are marked Ed25519 and never verified (D-011); multiplayer tables are new
    /// (applied actions in NetMoves, not Moves: D-044).</summary>
    private const string V2 = """
        CREATE TABLE NetGames (
            GameId TEXT PRIMARY KEY NOT NULL,
            Role TEXT NOT NULL,
            MyPubKey TEXT NOT NULL,
            HostPubKey TEXT NOT NULL,
            OpponentPubKey TEXT NULL,
            Size INTEGER NOT NULL,
            KomiHalves INTEGER NOT NULL,
            SeedCommit TEXT NOT NULL,
            HostNonce TEXT NULL,
            GuestNonce TEXT NULL,
            Relays TEXT NOT NULL,
            State TEXT NOT NULL,
            MyColor TEXT NULL,
            ResultIsDraw INTEGER NULL,
            ResultWinner TEXT NULL,
            ResultReason TEXT NULL,
            Note TEXT NULL,
            AppliedTurn INTEGER NOT NULL,
            StateHash TEXT NOT NULL,
            Tps TEXT NOT NULL,
            PublishedTurn INTEGER NOT NULL,
            MaxCreatedAt INTEGER NOT NULL
        );
        CREATE TABLE NetMoves (
            Seq INTEGER PRIMARY KEY AUTOINCREMENT,
            GameId TEXT NOT NULL REFERENCES NetGames(GameId) ON DELETE CASCADE,
            Pv INTEGER NOT NULL,
            ActionType TEXT NOT NULL,
            Turn INTEGER NOT NULL,
            PlayerPubKey TEXT NOT NULL,
            PrevStateHash TEXT NOT NULL,
            ActionData TEXT NOT NULL,
            Signature TEXT NOT NULL,
            EventId TEXT NOT NULL,
            TpsAfter TEXT NOT NULL,
            StateHashAfter TEXT NOT NULL
        );
        CREATE UNIQUE INDEX NetMovesOneMovePerTurn ON NetMoves (GameId, Turn) WHERE ActionType = 'MOVE';
        CREATE TABLE Outbox (
            Seq INTEGER PRIMARY KEY AUTOINCREMENT,
            GameId TEXT NOT NULL REFERENCES NetGames(GameId) ON DELETE CASCADE,
            EventId TEXT NOT NULL,
            ActionType TEXT NOT NULL,
            Turn INTEGER NOT NULL,
            EventJson TEXT NOT NULL,
            Sent INTEGER NOT NULL DEFAULT 0,
            UNIQUE (GameId, EventId)
        );
        CREATE TABLE Flags (
            Seq INTEGER PRIMARY KEY AUTOINCREMENT,
            GameId TEXT NOT NULL REFERENCES NetGames(GameId) ON DELETE CASCADE,
            Turn INTEGER NOT NULL,
            Reason TEXT NOT NULL,
            EventId TEXT NULL,
            Author TEXT NOT NULL,
            SeenAt TEXT NOT NULL
        );
        CREATE INDEX idx_netmoves_game ON NetMoves(GameId, Seq);
        CREATE INDEX idx_outbox_game ON Outbox(GameId, Seq);
        CREATE INDEX idx_flags_game ON Flags(GameId, Seq);
        """;

    private const string LegacyMoveColumns = """
        ALTER TABLE Moves ADD COLUMN SigScheme TEXT NOT NULL DEFAULT 'legacy-ed25519';
        ALTER TABLE Moves ADD COLUMN EventId TEXT NULL;
        """;

    public static long Version(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Brings a v0 file to <see cref="Current"/> in one transaction. Callers have checked the version is not newer.</summary>
    public static void Migrate(SqliteConnection connection)
    {
        using SqliteTransaction tx = connection.BeginTransaction();
        long version = Version(connection);
        if (version == 0)
        {
            if (TableExists(connection, tx, "Moves"))
                Execute(connection, tx, LegacyMoveColumns);
            Execute(connection, tx, V2);
            Execute(connection, tx, $"PRAGMA user_version = {Current};");
        }
        tx.Commit();
    }

    private static bool TableExists(SqliteConnection connection, SqliteTransaction tx, string name)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        cmd.Parameters.AddWithValue("$name", name);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction tx, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

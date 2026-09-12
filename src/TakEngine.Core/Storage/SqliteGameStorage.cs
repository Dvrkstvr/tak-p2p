using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TakEngine.Abstractions;

namespace TakEngine.Core.Storage;

public sealed class SqliteGameStorage : IAsyncDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public SqliteGameStorage(string connectionString)
    {
        _connectionString = connectionString;
    }

    public static SqliteGameStorage CreateInMemory()
    {
        return new SqliteGameStorage("Data Source=:memory:");
    }

    public async Task InitializeAsync()
    {
        var conn = await GetOpenConnectionAsync();

        const string createTablesSql = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS Games (
                Id TEXT PRIMARY KEY NOT NULL,
                BoardSize INTEGER NOT NULL CHECK(BoardSize IN (4, 5, 6)),
                LocalPlayerColor INTEGER NOT NULL,
                OpponentPubKey TEXT NOT NULL,
                Status INTEGER NOT NULL,
                WinnerPubKey TEXT NULL,
                StartedAt TEXT NOT NULL,
                LastUpdatedAt TEXT NOT NULL,
                TournamentId TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS Moves (
                GameId TEXT NOT NULL,
                TurnIndex INTEGER NOT NULL,
                PlayerPubKey TEXT NOT NULL,
                PtnMove TEXT NOT NULL,
                TpsSnapshot TEXT NOT NULL,
                StateHash TEXT NOT NULL,
                PrevStateHash TEXT NOT NULL,
                TimestampUtc TEXT NOT NULL,
                Signature TEXT NOT NULL,
                PRIMARY KEY (GameId, TurnIndex),
                FOREIGN KEY(GameId) REFERENCES Games(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_moves_game_turn ON Moves(GameId, TurnIndex);
            """;

        using var cmd = new SqliteCommand(createTablesSql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task CreateGameAsync(GameEntity game)
    {
        var conn = await GetOpenConnectionAsync();

        const string sql = """
            INSERT INTO Games (Id, BoardSize, LocalPlayerColor, OpponentPubKey, Status, WinnerPubKey, StartedAt, LastUpdatedAt, TournamentId)
            VALUES (@Id, @BoardSize, @LocalPlayerColor, @OpponentPubKey, @Status, @WinnerPubKey, @StartedAt, @LastUpdatedAt, @TournamentId);
            """;

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", game.Id.ToString());
        cmd.Parameters.AddWithValue("@BoardSize", (int)game.BoardSize);
        cmd.Parameters.AddWithValue("@LocalPlayerColor", (int)game.LocalPlayerColor);
        cmd.Parameters.AddWithValue("@OpponentPubKey", game.OpponentPubKey);
        cmd.Parameters.AddWithValue("@Status", (int)game.Status);
        cmd.Parameters.AddWithValue("@WinnerPubKey", (object?)game.WinnerPubKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StartedAt", game.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@LastUpdatedAt", game.LastUpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@TournamentId", (object?)game.TournamentId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AppendMoveAsync(MoveEntity move)
    {
        var conn = await GetOpenConnectionAsync();

        const string sql = """
            INSERT INTO Moves (GameId, TurnIndex, PlayerPubKey, PtnMove, TpsSnapshot, StateHash, PrevStateHash, TimestampUtc, Signature)
            VALUES (@GameId, @TurnIndex, @PlayerPubKey, @PtnMove, @TpsSnapshot, @StateHash, @PrevStateHash, @TimestampUtc, @Signature);

            UPDATE Games SET LastUpdatedAt = @TimestampUtc WHERE Id = @GameId;
            """;

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@GameId", move.GameId.ToString());
        cmd.Parameters.AddWithValue("@TurnIndex", move.TurnIndex);
        cmd.Parameters.AddWithValue("@PlayerPubKey", move.PlayerPubKey);
        cmd.Parameters.AddWithValue("@PtnMove", move.PtnMove);
        cmd.Parameters.AddWithValue("@TpsSnapshot", move.TpsSnapshot);
        cmd.Parameters.AddWithValue("@StateHash", move.StateHash);
        cmd.Parameters.AddWithValue("@PrevStateHash", move.PrevStateHash);
        cmd.Parameters.AddWithValue("@TimestampUtc", move.TimestampUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@Signature", move.Signature);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateGameStatusAsync(Guid gameId, GameStatus status, string? winnerPubKey, DateTime lastUpdatedAt)
    {
        var conn = await GetOpenConnectionAsync();

        const string sql = """
            UPDATE Games 
            SET Status = @Status, WinnerPubKey = @WinnerPubKey, LastUpdatedAt = @LastUpdatedAt
            WHERE Id = @Id;
            """;

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", gameId.ToString());
        cmd.Parameters.AddWithValue("@Status", (int)status);
        cmd.Parameters.AddWithValue("@WinnerPubKey", (object?)winnerPubKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LastUpdatedAt", lastUpdatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<GameEntity?> GetGameAsync(Guid gameId)
    {
        var conn = await GetOpenConnectionAsync();

        const string sql = "SELECT * FROM Games WHERE Id = @Id;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", gameId.ToString());

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapGameEntity(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<MoveEntity>> GetMovesAsync(Guid gameId)
    {
        var conn = await GetOpenConnectionAsync();
        var moves = new List<MoveEntity>();

        const string sql = "SELECT * FROM Moves WHERE GameId = @GameId ORDER BY TurnIndex ASC;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@GameId", gameId.ToString());

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            moves.Add(MapMoveEntity(reader));
        }

        return moves;
    }

    public async Task<IReadOnlyList<GameEntity>> GetActiveGamesAsync()
    {
        var conn = await GetOpenConnectionAsync();
        var games = new List<GameEntity>();

        const string sql = "SELECT * FROM Games WHERE Status IN (0, 1) ORDER BY LastUpdatedAt DESC;";
        using var cmd = new SqliteCommand(sql, conn);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            games.Add(MapGameEntity(reader));
        }

        return games;
    }

    public async Task DeleteGameAsync(Guid gameId)
    {
        var conn = await GetOpenConnectionAsync();

        const string sql = "DELETE FROM Games WHERE Id = @Id;";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", gameId.ToString());

        await cmd.ExecuteNonQueryAsync();
    }

    private static GameEntity MapGameEntity(SqliteDataReader reader)
    {
        return new GameEntity(
            Id: Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
            BoardSize: (BoardSize)reader.GetInt32(reader.GetOrdinal("BoardSize")),
            LocalPlayerColor: (PlayerColor)reader.GetInt32(reader.GetOrdinal("LocalPlayerColor")),
            OpponentPubKey: reader.GetString(reader.GetOrdinal("OpponentPubKey")),
            Status: (GameStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            WinnerPubKey: reader.IsDBNull(reader.GetOrdinal("WinnerPubKey")) ? null : reader.GetString(reader.GetOrdinal("WinnerPubKey")),
            StartedAt: DateTime.Parse(reader.GetString(reader.GetOrdinal("StartedAt"))),
            LastUpdatedAt: DateTime.Parse(reader.GetString(reader.GetOrdinal("LastUpdatedAt"))),
            TournamentId: reader.IsDBNull(reader.GetOrdinal("TournamentId")) ? null : reader.GetString(reader.GetOrdinal("TournamentId")));
    }

    private static MoveEntity MapMoveEntity(SqliteDataReader reader)
    {
        return new MoveEntity(
            GameId: Guid.Parse(reader.GetString(reader.GetOrdinal("GameId"))),
            TurnIndex: reader.GetInt32(reader.GetOrdinal("TurnIndex")),
            PlayerPubKey: reader.GetString(reader.GetOrdinal("PlayerPubKey")),
            PtnMove: reader.GetString(reader.GetOrdinal("PtnMove")),
            TpsSnapshot: reader.GetString(reader.GetOrdinal("TpsSnapshot")),
            StateHash: reader.GetString(reader.GetOrdinal("StateHash")),
            PrevStateHash: reader.GetString(reader.GetOrdinal("PrevStateHash")),
            TimestampUtc: DateTime.Parse(reader.GetString(reader.GetOrdinal("TimestampUtc"))),
            Signature: reader.GetString(reader.GetOrdinal("Signature")));
    }

    private async Task<SqliteConnection> GetOpenConnectionAsync()
    {
        if (_connection == null)
        {
            _connection = new SqliteConnection(_connectionString);
            await _connection.OpenAsync();
        }
        else if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync();
        }

        return _connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}

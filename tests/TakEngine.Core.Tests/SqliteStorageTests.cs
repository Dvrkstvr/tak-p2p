using System;
using System.IO;
using System.Threading.Tasks;
using TakEngine.Abstractions;
using TakEngine.Core.Storage;
using Xunit;

namespace TakEngine.Core.Tests;

public class SqliteStorageTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly SqliteGameStorage _storage;

    public SqliteStorageTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tak_test_{Guid.NewGuid():N}.db");
        _storage = new SqliteGameStorage($"Data Source={_dbPath}");
    }

    public async Task InitializeAsync()
    {
        await _storage.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _storage.DisposeAsync();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { /* Ignore */ }
        }
    }

    [Fact]
    public async Task CreateGame_AndRetrieve_Succeeds()
    {
        var gameId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var game = new GameEntity(
            Id: gameId,
            BoardSize: BoardSize.Five,
            LocalPlayerColor: PlayerColor.White,
            OpponentPubKey: "opponent_pubkey_hex_123",
            Status: GameStatus.Active,
            WinnerPubKey: null,
            StartedAt: now,
            LastUpdatedAt: now);

        await _storage.CreateGameAsync(game);

        var retrieved = await _storage.GetGameAsync(gameId);
        Assert.NotNull(retrieved);
        Assert.Equal(gameId, retrieved!.Id);
        Assert.Equal(BoardSize.Five, retrieved.BoardSize);
        Assert.Equal(PlayerColor.White, retrieved.LocalPlayerColor);
        Assert.Equal("opponent_pubkey_hex_123", retrieved.OpponentPubKey);
        Assert.Equal(GameStatus.Active, retrieved.Status);
    }

    [Fact]
    public async Task AppendMoves_AndRetrieveInOrder_Succeeds()
    {
        var gameId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var game = new GameEntity(
            Id: gameId,
            BoardSize: BoardSize.Five,
            LocalPlayerColor: PlayerColor.White,
            OpponentPubKey: "opponent_pubkey_hex_123",
            Status: GameStatus.Active,
            WinnerPubKey: null,
            StartedAt: now,
            LastUpdatedAt: now);

        await _storage.CreateGameAsync(game);

        var move1 = new MoveEntity(
            GameId: gameId,
            TurnIndex: 1,
            PlayerPubKey: "pubkey_white",
            PtnMove: "a1",
            TpsSnapshot: "x5/x5/x5/x5/2,x4 2 1",
            StateHash: "state_hash_1",
            PrevStateHash: "genesis_hash",
            TimestampUtc: now,
            Signature: "sig_1");

        var move2 = new MoveEntity(
            GameId: gameId,
            TurnIndex: 2,
            PlayerPubKey: "pubkey_black",
            PtnMove: "e5",
            TpsSnapshot: "x4,1/x5/x5/x5/2,x4 1 2",
            StateHash: "state_hash_2",
            PrevStateHash: "state_hash_1",
            TimestampUtc: now.AddSeconds(10),
            Signature: "sig_2");

        await _storage.AppendMoveAsync(move1);
        await _storage.AppendMoveAsync(move2);

        var moves = await _storage.GetMovesAsync(gameId);
        Assert.Equal(2, moves.Count);
        Assert.Equal(1, moves[0].TurnIndex);
        Assert.Equal("a1", moves[0].PtnMove);
        Assert.Equal(2, moves[1].TurnIndex);
        Assert.Equal("e5", moves[1].PtnMove);
    }

    [Fact]
    public async Task DeleteGame_Cascades_AndDeletesMoves()
    {
        var gameId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var game = new GameEntity(
            Id: gameId,
            BoardSize: BoardSize.Four,
            LocalPlayerColor: PlayerColor.Black,
            OpponentPubKey: "opp_1",
            Status: GameStatus.Active,
            WinnerPubKey: null,
            StartedAt: now,
            LastUpdatedAt: now);

        await _storage.CreateGameAsync(game);

        var move = new MoveEntity(
            GameId: gameId,
            TurnIndex: 1,
            PlayerPubKey: "pub_1",
            PtnMove: "a1",
            TpsSnapshot: "x4/x4/x4/2,x3 2 1",
            StateHash: "hash_1",
            PrevStateHash: "gen",
            TimestampUtc: now,
            Signature: "sig");

        await _storage.AppendMoveAsync(move);

        // Delete game
        await _storage.DeleteGameAsync(gameId);

        var retrievedGame = await _storage.GetGameAsync(gameId);
        Assert.Null(retrievedGame);

        var retrievedMoves = await _storage.GetMovesAsync(gameId);
        Assert.Empty(retrievedMoves);
    }

    [Fact]
    public async Task ReplayProvider_ScrubsToTargetTurn_Instantly()
    {
        var gameId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var game = new GameEntity(
            Id: gameId,
            BoardSize: BoardSize.Five,
            LocalPlayerColor: PlayerColor.White,
            OpponentPubKey: "opp",
            Status: GameStatus.Active,
            WinnerPubKey: null,
            StartedAt: now,
            LastUpdatedAt: now);

        await _storage.CreateGameAsync(game);

        // Move 1 TPS: Black flat at a1
        var move1 = new MoveEntity(
            GameId: gameId,
            TurnIndex: 1,
            PlayerPubKey: "p1",
            PtnMove: "a1",
            TpsSnapshot: "x5/x5/x5/x5/2,x4 2 1",
            StateHash: "h1",
            PrevStateHash: "gen",
            TimestampUtc: now,
            Signature: "s1");

        // Move 2 TPS: White flat at e5 added
        var move2 = new MoveEntity(
            GameId: gameId,
            TurnIndex: 2,
            PlayerPubKey: "p2",
            PtnMove: "e5",
            TpsSnapshot: "x4,1/x5/x5/x5/2,x4 1 2",
            StateHash: "h2",
            PrevStateHash: "h1",
            TimestampUtc: now.AddSeconds(5),
            Signature: "s2");

        await _storage.AppendMoveAsync(move1);
        await _storage.AppendMoveAsync(move2);

        var replay = new ReplayProvider(_storage);

        // Scrub to turn 1
        var boardTurn1 = await replay.ScrubToTurnAsync(gameId, 1);
        Assert.Equal(PlayerColor.Black, boardTurn1.GetStack(new Coord(0, 0)).Owner);
        Assert.True(boardTurn1.GetStack(new Coord(4, 4)).IsEmpty);

        // Scrub to turn 2
        var boardTurn2 = await replay.ScrubToTurnAsync(gameId, 2);
        Assert.Equal(PlayerColor.Black, boardTurn2.GetStack(new Coord(0, 0)).Owner);
        Assert.Equal(PlayerColor.White, boardTurn2.GetStack(new Coord(4, 4)).Owner);

        // Get all replay frames
        var frames = await replay.GetReplayFramesAsync(gameId);
        Assert.Equal(2, frames.Count);
        Assert.Equal(1, frames[0].TurnIndex);
        Assert.Equal(2, frames[1].TurnIndex);
    }
}

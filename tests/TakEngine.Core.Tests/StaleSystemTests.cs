using System;
using System.IO;
using System.Threading.Tasks;
using TakEngine.Abstractions;
using TakEngine.Core.Session;
using TakEngine.Core.Storage;
using Xunit;

namespace TakEngine.Core.Tests;

public class StaleSystemTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly SqliteGameStorage _storage;

    public StaleSystemTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tak_stale_{Guid.NewGuid():N}.db");
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

    [Theory]
    [InlineData(1, GameStatus.Active, false, false)]
    [InlineData(2, GameStatus.Active, false, false)]
    [InlineData(3, GameStatus.Stale, true, false)]
    [InlineData(5, GameStatus.Stale, true, false)]
    [InlineData(7, GameStatus.DrawTimeout, false, true)]
    [InlineData(10, GameStatus.DrawTimeout, false, true)]
    public void EvaluateGame_CalculatesCorrectThresholds(
        int elapsedDays,
        GameStatus expectedStatus,
        bool expectedWarning,
        bool expectedTimedOut)
    {
        var now = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
        var lastUpdated = now.AddDays(-elapsedDays);

        var game = new GameEntity(
            Id: Guid.NewGuid(),
            BoardSize: BoardSize.Five,
            LocalPlayerColor: PlayerColor.White,
            OpponentPubKey: "opp",
            Status: GameStatus.Active,
            WinnerPubKey: null,
            StartedAt: lastUpdated.AddDays(-1),
            LastUpdatedAt: lastUpdated);

        var result = StaleMatchMonitor.EvaluateGame(game, now);

        Assert.Equal(expectedStatus, result.NewStatus);
        Assert.Equal(expectedWarning, result.IsWarning);
        Assert.Equal(expectedTimedOut, result.IsTimedOut);
    }

    [Fact]
    public async Task CheckAndUpdateActiveGames_UpdatesDatabaseStatuses()
    {
        var now = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
        var mockTime = new MockNtpTimeService(now);

        // 1. Active game (1 day old)
        var gameActiveId = Guid.NewGuid();
        var gameActive = new GameEntity(
            Id: gameActiveId,
            BoardSize: BoardSize.Five,
            LocalPlayerColor: PlayerColor.White,
            OpponentPubKey: "opp1",
            Status: GameStatus.Active,
            WinnerPubKey: null,
            StartedAt: now.AddDays(-2),
            LastUpdatedAt: now.AddDays(-1));

        // 2. Stale game (4 days old)
        var gameStaleId = Guid.NewGuid();
        var gameStale = new GameEntity(
            Id: gameStaleId,
            BoardSize: BoardSize.Five,
            LocalPlayerColor: PlayerColor.Black,
            OpponentPubKey: "opp2",
            Status: GameStatus.Active,
            WinnerPubKey: null,
            StartedAt: now.AddDays(-5),
            LastUpdatedAt: now.AddDays(-4));

        // 3. Timed out game (8 days old)
        var gameTimeoutId = Guid.NewGuid();
        var gameTimeout = new GameEntity(
            Id: gameTimeoutId,
            BoardSize: BoardSize.Four,
            LocalPlayerColor: PlayerColor.White,
            OpponentPubKey: "opp3",
            Status: GameStatus.Active,
            WinnerPubKey: null,
            StartedAt: now.AddDays(-10),
            LastUpdatedAt: now.AddDays(-8));

        await _storage.CreateGameAsync(gameActive);
        await _storage.CreateGameAsync(gameStale);
        await _storage.CreateGameAsync(gameTimeout);

        var evaluations = await StaleMatchMonitor.CheckAndUpdateActiveGamesAsync(_storage, mockTime);
        Assert.Equal(3, evaluations.Count);

        // Verify updated database records
        var updatedActive = await _storage.GetGameAsync(gameActiveId);
        Assert.Equal(GameStatus.Active, updatedActive!.Status);

        var updatedStale = await _storage.GetGameAsync(gameStaleId);
        Assert.Equal(GameStatus.Stale, updatedStale!.Status);

        var updatedTimeout = await _storage.GetGameAsync(gameTimeoutId);
        Assert.Equal(GameStatus.DrawTimeout, updatedTimeout!.Status);
    }
}

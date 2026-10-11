using System;
using System.IO;
using System.Threading.Tasks;
using TakEngine.Abstractions;
using TakEngine.Core.Session;
using TakEngine.Core.Storage;
using Xunit;

namespace TakEngine.Core.Tests;

public class StaleSystemTests
{
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
}

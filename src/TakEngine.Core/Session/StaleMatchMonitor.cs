using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TakEngine.Core.Storage;

namespace TakEngine.Core.Session;

public sealed record StaleEvaluationResult(
    Guid GameId,
    GameStatus OriginalStatus,
    GameStatus NewStatus,
    TimeSpan InactiveDuration,
    TimeSpan RemainingUntilTimeout,
    bool IsWarning,
    bool IsTimedOut);

public sealed class StaleMatchMonitor
{
    public static readonly TimeSpan StaleWarningThreshold = TimeSpan.FromDays(3);
    public static readonly TimeSpan AutoDrawTimeoutThreshold = TimeSpan.FromDays(7);

    public static StaleEvaluationResult EvaluateGame(GameEntity game, DateTime currentUtc)
    {
        TimeSpan inactiveDuration = currentUtc - game.LastUpdatedAt;
        if (inactiveDuration < TimeSpan.Zero)
        {
            inactiveDuration = TimeSpan.Zero;
        }

        bool isTimedOut = inactiveDuration >= AutoDrawTimeoutThreshold;
        bool isWarning = !isTimedOut && inactiveDuration >= StaleWarningThreshold;

        GameStatus newStatus;
        if (isTimedOut)
        {
            newStatus = GameStatus.DrawTimeout;
        }
        else if (isWarning)
        {
            newStatus = GameStatus.Stale;
        }
        else
        {
            newStatus = game.Status;
        }

        TimeSpan remaining = isTimedOut ? TimeSpan.Zero : (AutoDrawTimeoutThreshold - inactiveDuration);

        return new StaleEvaluationResult(
            GameId: game.Id,
            OriginalStatus: game.Status,
            NewStatus: newStatus,
            InactiveDuration: inactiveDuration,
            RemainingUntilTimeout: remaining,
            IsWarning: isWarning,
            IsTimedOut: isTimedOut);
    }

    public static async Task<IReadOnlyList<StaleEvaluationResult>> CheckAndUpdateActiveGamesAsync(
        SqliteGameStorage storage,
        INtpTimeService timeService,
        CancellationToken cancellationToken = default)
    {
        DateTime currentUtc = await timeService.GetUtcNowAsync(cancellationToken);
        var activeGames = await storage.GetActiveGamesAsync();
        var results = new List<StaleEvaluationResult>(activeGames.Count);

        foreach (var game in activeGames)
        {
            var result = EvaluateGame(game, currentUtc);
            results.Add(result);

            if (result.NewStatus != game.Status)
            {
                await storage.UpdateGameStatusAsync(
                    game.Id,
                    result.NewStatus,
                    winnerPubKey: null,
                    lastUpdatedAt: game.LastUpdatedAt);
            }
        }

        return results;
    }
}

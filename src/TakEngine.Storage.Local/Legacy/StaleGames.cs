using TakEngine.Core.Session;
using TakEngine.Core.Storage;

namespace TakEngine.Storage.Local.Legacy;

/// <summary>Applies <see cref="StaleMatchMonitor.EvaluateGame"/> to the legacy local games (moved out of Core with the SQLite store).</summary>
public static class StaleGames
{
    public static async Task<IReadOnlyList<StaleEvaluationResult>> CheckAndUpdateActiveGamesAsync(
        SqliteGameStorage storage,
        INtpTimeService timeService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(timeService);
        DateTime currentUtc = await timeService.GetUtcNowAsync(cancellationToken).ConfigureAwait(false);
        var activeGames = await storage.GetActiveGamesAsync().ConfigureAwait(false);
        var results = new List<StaleEvaluationResult>(activeGames.Count);

        foreach (var game in activeGames)
        {
            var result = StaleMatchMonitor.EvaluateGame(game, currentUtc);
            results.Add(result);

            if (result.NewStatus != game.Status)
            {
                await storage.UpdateGameStatusAsync(
                    game.Id,
                    result.NewStatus,
                    winnerPubKey: null,
                    lastUpdatedAt: game.LastUpdatedAt).ConfigureAwait(false);
            }
        }

        return results;
    }
}

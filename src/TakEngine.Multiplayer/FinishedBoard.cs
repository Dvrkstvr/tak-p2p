using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Rules;
using TakEngine.Core.Serialization;

namespace TakEngine.Multiplayer;

/// <summary>
/// Whether a stored position is already over, so a reopen never plays on after the end (review M0-code finding 1). TPS carries no
/// result, so this repeats the engine's end check after a ply (<c>GameBoard.CheckGameEndAfterMove</c>) on the restored board: a road
/// (both roads: the player who made the last ply wins), else a full board or the last mover out of pieces ends it on flats. Pure.
/// </summary>
public static class FinishedBoard
{
    /// <summary>The result the last applied ply produced, or null if the game goes on (or no ply was applied yet).</summary>
    public static GameResult? Result(ExchangeProgress progress, int komiHalves)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (progress.AppliedTurn < 1)
            return null;

        GameBoard board = TpsSerializer.Deserialize(progress.Tps, komiHalves);
        PlayerColor mover = TurnExchange.TurnOwner(progress.AppliedTurn);
        int size = (int)board.Size;
        var grid = new PieceStack[size, size];
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
                grid[x, y] = board.GetStack(new Coord(x, y));
        }

        bool whiteRoad = RoadFinder.HasRoad(grid, size, PlayerColor.White);
        bool blackRoad = RoadFinder.HasRoad(grid, size, PlayerColor.Black);
        if (whiteRoad || blackRoad)
            return new GameResult(false, whiteRoad && blackRoad ? mover : whiteRoad ? PlayerColor.White : PlayerColor.Black, GameEndReason.Road);

        PlayerReserves left = mover == PlayerColor.White ? board.WhiteReserves : board.BlackReserves;
        if (board.IsBoardFull() || (left.Stones == 0 && left.Capstones == 0))
            return board.DetermineFlatCountResult();
        return null;
    }
}

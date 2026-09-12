using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TakEngine.Abstractions;
using TakEngine.Core.AI;
using TakEngine.Core.Board;
using TakEngine.Core.Cryptography;
using TakEngine.Core.Rules;
using TakEngine.Core.Serialization;

namespace TakApp.Blazor.Services;

public sealed class WebGameSessionManager
{
    private static readonly (int dx, int dy)[] OrthogonalNeighbors =
    [
        (1, 0),
        (-1, 0),
        (0, 1),
        (0, -1)
    ];

    public GameBoard? Board { get; private set; }
    public Guid GameId { get; private set; }
    public BoardSize CurrentSize { get; private set; } = BoardSize.Five;
    public PlayerColor LocalPlayerColor { get; private set; } = PlayerColor.White;
    public bool IsLocalOnly { get; private set; } = true;
    public bool IsBotMatch { get; private set; }
    public BotDifficulty? BotDifficulty { get; private set; }
    public MinimaxTakBot? Bot { get; private set; }
    public bool IsBotThinking { get; private set; }
    public string? OpponentPubKey { get; private set; }

    public List<string> MoveHistoryPtn { get; } = new();
    public string? LastMovePtn { get; private set; }
    public string? GenesisHash { get; private set; }
    public string? PrevStateHash { get; private set; }
    public HashSet<Coord> WinningRoadCoords { get; private set; } = new();

    public event Action? OnStateChanged;

    public void StartLocalMatch(BoardSize size)
    {
        CurrentSize = size;
        Board = new GameBoard(size);
        GameId = Guid.NewGuid();
        LocalPlayerColor = PlayerColor.White;
        IsLocalOnly = true;
        IsBotMatch = false;
        BotDifficulty = null;
        Bot = null;
        IsBotThinking = false;
        OpponentPubKey = null;

        MoveHistoryPtn.Clear();
        LastMovePtn = null;
        GenesisHash = StateHasher.ComputeGenesisHash(size);
        PrevStateHash = GenesisHash;
        WinningRoadCoords.Clear();

        NotifyStateChanged();
    }

    public void StartBotMatch(BoardSize size, BotDifficulty difficulty, PlayerColor humanColor = PlayerColor.White)
    {
        CurrentSize = size;
        Board = new GameBoard(size);
        GameId = Guid.NewGuid();
        LocalPlayerColor = humanColor;
        IsLocalOnly = false;
        IsBotMatch = true;
        BotDifficulty = difficulty;
        Bot = new MinimaxTakBot(difficulty);
        IsBotThinking = false;
        OpponentPubKey = $"BOT_{difficulty.ToString().ToUpperInvariant()}";

        MoveHistoryPtn.Clear();
        LastMovePtn = null;
        GenesisHash = StateHasher.ComputeGenesisHash(size);
        PrevStateHash = GenesisHash;
        WinningRoadCoords.Clear();

        NotifyStateChanged();

        if (Board.ActivePlayer != LocalPlayerColor)
        {
            _ = TriggerBotMoveAsync();
        }
    }

    public CommandResult ExecuteMove(TakMove move)
    {
        if (Board == null)
            return CommandResult.Fail("No active game.");

        if (Board.Phase == GamePhase.Completed)
            return CommandResult.Fail("Game is already completed.");

        string ptnMove = move.ToPtn();
        var result = Board.Execute(move);
        if (!result.IsSuccess)
            return result;

        LastMovePtn = ptnMove;
        MoveHistoryPtn.Add(ptnMove);

        // Compute state hash link
        if (PrevStateHash != null)
        {
            string tpsSnapshot = TpsSerializer.Serialize(Board);
            PrevStateHash = StateHasher.ComputeStateHash(
                PrevStateHash,
                MoveHistoryPtn.Count,
                "local",
                ptnMove,
                tpsSnapshot);
        }

        // Check winning road
        if (Board.Result != null && Board.Result.Winner.HasValue && Board.Result.Reason == GameEndReason.Road)
        {
            WinningRoadCoords = CalculateRoadCoordinates(Board, Board.Result.Winner.Value);
        }

        NotifyStateChanged();

        if (IsBotMatch && Board.Phase != GamePhase.Completed && Board.ActivePlayer != LocalPlayerColor)
        {
            _ = TriggerBotMoveAsync();
        }

        return CommandResult.Success();
    }

    private async Task TriggerBotMoveAsync()
    {
        if (Board == null || Bot == null || Board.Phase == GamePhase.Completed)
            return;

        IsBotThinking = true;
        NotifyStateChanged();

        // Brief delay (350ms) for human ergonomics and UI transition
        await Task.Delay(350);

        try
        {
            var move = Bot.SelectMove(Board);
            IsBotThinking = false;
            ExecuteMove(move);
        }
        catch
        {
            IsBotThinking = false;
            NotifyStateChanged();
        }
    }

    public CommandResult Resign(PlayerColor player)
    {
        if (Board == null || Board.Phase == GamePhase.Completed)
            return CommandResult.Fail("Game is already completed.");

        var res = Board.Resign(player);
        NotifyStateChanged();
        return res;
    }

    public void NotifyStateChanged() => OnStateChanged?.Invoke();

    private static HashSet<Coord> CalculateRoadCoordinates(GameBoard board, PlayerColor player)
    {
        int size = (int)board.Size;
        var resultCoords = new HashSet<Coord>();

        // Check North-South road coordinates
        CheckPathNorthSouth(board, size, player, resultCoords);

        // Check East-West road coordinates
        CheckPathEastWest(board, size, player, resultCoords);

        return resultCoords;
    }

    private static void CheckPathNorthSouth(GameBoard board, int size, PlayerColor player, HashSet<Coord> output)
    {
        var visited = new bool[size, size];
        var parent = new Dictionary<Coord, Coord>();
        var queue = new Queue<Coord>();

        for (int x = 0; x < size; x++)
        {
            var c = new Coord(x, 0);
            if (RoadFinder.CanCarryRoad(board.GetStack(c), player))
            {
                visited[x, 0] = true;
                queue.Enqueue(c);
            }
        }

        Coord? endCoord = null;
        while (queue.Count > 0)
        {
            var curr = queue.Dequeue();
            if (curr.Y == size - 1)
            {
                endCoord = curr;
                break;
            }

            foreach (var (dx, dy) in OrthogonalNeighbors)
            {
                int nx = curr.X + dx;
                int ny = curr.Y + dy;
                if (nx >= 0 && nx < size && ny >= 0 && ny < size && !visited[nx, ny])
                {
                    var neighbor = new Coord(nx, ny);
                    if (RoadFinder.CanCarryRoad(board.GetStack(neighbor), player))
                    {
                        visited[nx, ny] = true;
                        parent[neighbor] = curr;
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        if (endCoord.HasValue)
        {
            var p = endCoord.Value;
            output.Add(p);
            while (parent.TryGetValue(p, out var prev))
            {
                output.Add(prev);
                p = prev;
            }
        }
    }

    private static void CheckPathEastWest(GameBoard board, int size, PlayerColor player, HashSet<Coord> output)
    {
        var visited = new bool[size, size];
        var parent = new Dictionary<Coord, Coord>();
        var queue = new Queue<Coord>();

        for (int y = 0; y < size; y++)
        {
            var c = new Coord(0, y);
            if (RoadFinder.CanCarryRoad(board.GetStack(c), player))
            {
                visited[0, y] = true;
                queue.Enqueue(c);
            }
        }

        Coord? endCoord = null;
        while (queue.Count > 0)
        {
            var curr = queue.Dequeue();
            if (curr.X == size - 1)
            {
                endCoord = curr;
                break;
            }

            foreach (var (dx, dy) in OrthogonalNeighbors)
            {
                int nx = curr.X + dx;
                int ny = curr.Y + dy;
                if (nx >= 0 && nx < size && ny >= 0 && ny < size && !visited[nx, ny])
                {
                    var neighbor = new Coord(nx, ny);
                    if (RoadFinder.CanCarryRoad(board.GetStack(neighbor), player))
                    {
                        visited[nx, ny] = true;
                        parent[neighbor] = curr;
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        if (endCoord.HasValue)
        {
            var p = endCoord.Value;
            output.Add(p);
            while (parent.TryGetValue(p, out var prev))
            {
                output.Add(prev);
                p = prev;
            }
        }
    }
}

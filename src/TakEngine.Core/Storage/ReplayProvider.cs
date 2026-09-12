using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Serialization;

namespace TakEngine.Core.Storage;

public sealed record ReplayFrame(
    int TurnIndex,
    string PlayerPubKey,
    string PtnMove,
    string StateHash,
    DateTime TimestampUtc,
    TakBoardSnapshot BoardSnapshot);

public sealed class ReplayProvider
{
    private readonly SqliteGameStorage _storage;

    public ReplayProvider(SqliteGameStorage storage)
    {
        _storage = storage;
    }

    public async Task<GameBoard> ScrubToTurnAsync(Guid gameId, int targetTurnIndex)
    {
        var game = await _storage.GetGameAsync(gameId)
            ?? throw new KeyNotFoundException($"Game {gameId} not found.");

        if (targetTurnIndex <= 0)
        {
            return new GameBoard(game.BoardSize);
        }

        var moves = await _storage.GetMovesAsync(gameId);
        if (moves.Count == 0)
        {
            return new GameBoard(game.BoardSize);
        }

        // Find move matching targetTurnIndex or closest earlier turn
        MoveEntity? targetMove = null;
        foreach (var m in moves)
        {
            if (m.TurnIndex == targetTurnIndex)
            {
                targetMove = m;
                break;
            }
            if (m.TurnIndex < targetTurnIndex)
            {
                targetMove = m;
            }
        }

        if (targetMove == null)
        {
            return new GameBoard(game.BoardSize);
        }

        // Instant O(1) state reconstruction from cached TPS snapshot
        return TpsSerializer.Deserialize(targetMove.TpsSnapshot);
    }

    public async Task<IReadOnlyList<ReplayFrame>> GetReplayFramesAsync(Guid gameId)
    {
        var moves = await _storage.GetMovesAsync(gameId);
        var frames = new List<ReplayFrame>(moves.Count);

        foreach (var m in moves)
        {
            var board = TpsSerializer.Deserialize(m.TpsSnapshot);
            frames.Add(new ReplayFrame(
                m.TurnIndex,
                m.PlayerPubKey,
                m.PtnMove,
                m.StateHash,
                m.TimestampUtc,
                board.ToSnapshot()));
        }

        return frames;
    }
}

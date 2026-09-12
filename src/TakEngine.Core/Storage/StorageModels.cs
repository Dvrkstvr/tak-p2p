using System;
using TakEngine.Abstractions;

namespace TakEngine.Core.Storage;

public enum GameStatus
{
    Active = 0,
    Stale = 1,
    Completed = 2,
    DrawTimeout = 3,
    Resigned = 4
}

public sealed record GameEntity(
    Guid Id,
    BoardSize BoardSize,
    PlayerColor LocalPlayerColor,
    string OpponentPubKey,
    GameStatus Status,
    string? WinnerPubKey,
    DateTime StartedAt,
    DateTime LastUpdatedAt,
    string? TournamentId = null);

public sealed record MoveEntity(
    Guid GameId,
    int TurnIndex,
    string PlayerPubKey,
    string PtnMove,
    string TpsSnapshot,
    string StateHash,
    string PrevStateHash,
    DateTime TimestampUtc,
    string Signature);

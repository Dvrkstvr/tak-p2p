using System;
using System.Collections.Generic;

namespace TakEngine.Abstractions;

public interface ISpectatorGameSession : IDisposable
{
    Guid GameId { get; }
    string? TournamentId { get; }
    BoardSize Size { get; }
    PlayerColor CurrentTurnColor { get; }
    int CurrentTurnIndex { get; }

    string WhitePlayerPubKey { get; }
    string BlackPlayerPubKey { get; }
    int? WhitePlayerElo { get; }
    int? BlackPlayerElo { get; }

    TakBoardSnapshot CurrentBoard { get; }
    IReadOnlyList<TakMove> MoveHistory { get; }

    TakBoardSnapshot GetHistoricalSnapshot(int turnIndex);

    event Action<TakBoardSnapshot, TakMove> OnMoveReceived;
    event Action<TakBoardSnapshot, GameResult> OnGameCompleted;
    event Action<string> OnSpectatorStatusChanged;
    event Action<ProtocolViolationException> OnStateDesyncDetected;
}

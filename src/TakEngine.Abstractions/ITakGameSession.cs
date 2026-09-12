using System;
using System.Collections.Generic;

namespace TakEngine.Abstractions;

public interface ITakGameSession
{
    GameId Id { get; }
    BoardSize Size { get; }
    PlayerColor LocalColor { get; }
    GamePhase CurrentPhase { get; }
    
    // Read-only snapshots
    TakBoardSnapshot CurrentBoard { get; }
    IReadOnlyList<TakMove> GetLegalMovesForSquare(Coord coord);

    // Command dispatch
    CommandResult SubmitPlacement(Coord target, PieceType piece);
    CommandResult SubmitMove(Coord origin, Direction direction, IReadOnlyList<int> drops);
    CommandResult Resign();

    // Reactive streams
    event Action<TakBoardSnapshot, TakMove> OnMoveExecuted;
    event Action<TakBoardSnapshot, GameResult> OnGameEnded;
    event Action<TimeSpan> OnStaleWarning;
    event Action<string> OnTransportStatusChanged;
    event Action<ProtocolViolationException> OnProtocolViolationDetected;
}

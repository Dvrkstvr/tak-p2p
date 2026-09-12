namespace TakEngine.Abstractions;

public enum PieceType
{
    Flat = 0,
    Standing = 1,
    Capstone = 2
}

public enum PlayerColor
{
    White = 0,
    Black = 1
}

public enum Direction
{
    North = 0, // '+'
    South = 1, // '-'
    East = 2,  // '>'
    West = 3   // '<'
}

public enum GamePhase
{
    NotStarted = 0,
    FirstTurnPlacement = 1,
    Playing = 2,
    Completed = 3
}

public enum BoardSize
{
    Four = 4,
    Five = 5,
    Six = 6
}

public enum GameEndReason
{
    Road = 0,
    FlatCount = 1,
    Resignation = 2,
    TimeoutDraw = 3,
    Agreement = 4
}

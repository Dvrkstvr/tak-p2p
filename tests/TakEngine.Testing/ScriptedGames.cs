namespace TakEngine.Testing;

/// <summary>Fixed games both peers replay (F-016).</summary>
public static class ScriptedGames
{
    /// <summary>5x5 to a White road on row 1 at ply 9; plies 1 and 2 are the first-turn swap.</summary>
    public static readonly IReadOnlyList<string> FiveByFiveRoadWin = ["e5", "a1", "b1", "e4", "c1", "e3", "d1", "e2", "e1"];
}

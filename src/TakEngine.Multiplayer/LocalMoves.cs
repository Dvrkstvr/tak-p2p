using TakEngine.Abstractions;
using TakEngine.Core.Serialization;
using TakEngine.Core.Session;

namespace TakEngine.Multiplayer;

/// <summary>Submits a move given as PTN or a <see cref="TakMove"/> to a session (heads and tests type PTN).</summary>
public static class LocalMoves
{
    public static CommandResult Submit(TakGameSession session, TakMove move)
    {
        ArgumentNullException.ThrowIfNull(session);
        return move switch
        {
            PlaceMove p => session.SubmitPlacement(p.Target, p.PieceType),
            SlideMove s when s.Drops.Sum() == s.LiftCount => session.SubmitMove(s.Origin, s.Direction, s.Drops),
            SlideMove => CommandResult.Fail("The lift count does not match the drops."),
            _ => CommandResult.Fail("Unknown move."),
        };
    }

    public static CommandResult Submit(TakGameSession session, string ptn)
    {
        TakMove move;
        try
        {
            move = PtnParser.ParseMove(ptn);
        }
        catch (FormatException ex)
        {
            return CommandResult.Fail($"'{ptn}' is not a move: {ex.Message}");
        }
        return Submit(session, move);
    }
}

using TakEngine.Transport.Envelope;

namespace TakEngine.Multiplayer;

public enum IncomingDecision
{
    /// <summary>Passed rows 1-4: hand it to the handshake or the turn exchange.</summary>
    Accept,

    /// <summary>Never applied, counted only: noise, relay junk or a stranger (cannot be attributed).</summary>
    Drop,

    /// <summary>Never applied, and a visible, persisted flag against the opponent.</summary>
    DropAndFlag,
}

/// <param name="Reason">The flag text (scope.md "A tampered, forged or illegal move"), set for <see cref="IncomingDecision.DropAndFlag"/>.</param>
public sealed record Classification(IncomingDecision Decision, string? Reason = null);

/// <summary>
/// The ordered tamper classification (scope.md, rows 1-7). Rows 1-4 come from <see cref="EnvelopeCodec"/>'s verdict; rows 5-7 need
/// the game state and are decided by <see cref="TurnExchange"/> with the reason texts here. A flag never changes the game. Pure.
/// </summary>
public static class IncomingClassifier
{
    public const string Undecryptable = "undecryptable message from opponent";
    public const string IncompatibleVersion = "opponent runs an incompatible version";

    /// <param name="opponentKnown">False while a host is still inviting: an undecryptable or bad event then comes from someone
    /// who is not (yet) the opponent, so it is only counted.</param>
    public static Classification Classify(DecodeResult result, bool opponentKnown)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Verdict switch
        {
            DecodeVerdict.Accepted => new Classification(IncomingDecision.Accept),
            DecodeVerdict.NotForUs or DecodeVerdict.Row1BadIdOrSig or DecodeVerdict.Row2WrongAuthor => new Classification(IncomingDecision.Drop),
            _ when !opponentKnown => new Classification(IncomingDecision.Drop),
            DecodeVerdict.Row3Undecryptable => new Classification(IncomingDecision.DropAndFlag, Undecryptable),
            DecodeVerdict.IncompatibleVersion => new Classification(IncomingDecision.DropAndFlag, IncompatibleVersion),
            _ => new Classification(IncomingDecision.DropAndFlag, "bad message from opponent: " + (result.Detail ?? "unreadable payload")),
        };
    }

    /// <summary>Row 5: the action's previous hash is not this side's hash for the turn it claims (a fork).</summary>
    public static string HashChainMismatch(int turn) => $"hash chain mismatch at turn {turn}";

    /// <summary>Row 6: wrong turn owner, malformed PTN or a move the engine refuses.</summary>
    public static string IllegalMove(string engineReason) => $"illegal move from opponent: {engineReason}";

    /// <summary>Row 7: a different action for a turn that already has one (equivocation); the first one seen stands.</summary>
    public static string ConflictingMove(int turn) => $"conflicting move for turn {turn}";
}

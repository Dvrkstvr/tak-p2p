using TakEngine.Abstractions;
using TakEngine.Crypto;

namespace TakEngine.Transport.Envelope;

/// <summary>
/// What <see cref="EnvelopeCodec.Decode"/> concluded about one event, in tamper-row order (pipeline/architecture.md "Wire format").
/// Rows 5-7 (prev hash, turn owner and legality, equivocation) are judged later by the turn exchange and the session.
/// </summary>
public enum DecodeVerdict
{
    Accepted,

    /// <summary>Not kind 3825, not exactly the <c>p</c> + <c>g</c> tags, or tagged for another recipient or game: noise, not tampering.</summary>
    NotForUs,

    /// <summary>Row 1: the NIP-01 id or the BIP-340 event signature is wrong.</summary>
    Row1BadIdOrSig,

    /// <summary>Row 2: the author is not the opponent (or, while a host is inviting, is ourselves posing as a stranger).</summary>
    Row2WrongAuthor,

    /// <summary>Row 3: the NIP-44 payload does not decrypt (bad MAC, bad padding, wrong conversation key, junk).</summary>
    Row3Undecryptable,

    /// <summary>Row 4: bad JSON, wrong game id, bad inner signature, <c>player_pubkey</c> not the event author, or a non-JOIN from a stranger.</summary>
    Row4BadPayload,

    /// <summary>The plaintext's <c>pv</c> is not one we speak: an incompatible client, kept distinct from tampering.</summary>
    IncompatibleVersion,
}

/// <summary>Who we are and whom we expect, for one game.</summary>
/// <param name="Me">Our key: decrypts events addressed to us and recognises our own echoes.</param>
/// <param name="GameId">The game this decoder is for (the <c>g</c> tag and the inner <c>game_id</c>).</param>
/// <param name="Opponent">The opponent's key, or null while a host is still inviting (then any non-self author may JOIN).</param>
public sealed record DecodeContext(SecretKey Me, Guid GameId, PublicKey? Opponent);

/// <param name="IsOwn">The event is our own, echoed back by a relay (decrypted with the conversation key of its <c>p</c> recipient).</param>
public sealed record DecodeResult(DecodeVerdict Verdict, SignedGameAction? Action = null, bool IsOwn = false, string? Detail = null)
{
    public bool IsAccepted => Verdict == DecodeVerdict.Accepted;
}

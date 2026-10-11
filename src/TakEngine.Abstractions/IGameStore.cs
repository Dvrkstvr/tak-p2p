namespace TakEngine.Abstractions;

/// <summary>
/// Seam for multiplayer game state (pipeline/architecture.md "Data"). Real: <c>SqliteGameStore</c> (F-037, CLI/desktop), a browser
/// store at M3. Each call is one transaction: it either lands whole or throws <see cref="GameStoreException"/> and changes nothing.
/// </summary>
public interface IGameStore
{
    /// <summary>Saves a game that doesn't exist yet, with its starting progress. Throws if the game id is already stored.</summary>
    Task SaveNewGameAsync(NetGameRecord record, ExchangeProgress progress, CancellationToken cancellationToken = default);

    /// <summary>Replaces the game record (handshake state, opponent, nonces, colour, result). The id must exist.</summary>
    Task UpdateRecordAsync(NetGameRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends one applied action and moves the progress to <paramref name="progress"/>, in one transaction. For a local action the
    /// signed event goes into the outbox in the same transaction (persist before publishing).
    /// </summary>
    Task CommitAppliedAsync(Guid gameId, AppliedAction applied, ExchangeProgress progress, OutboxEntry? outbox = null, CancellationToken cancellationToken = default);

    /// <summary>Moves the progress without an applied action (a newer <c>maxCreatedAt</c>).</summary>
    Task UpdateProgressAsync(Guid gameId, ExchangeProgress progress, CancellationToken cancellationToken = default);

    /// <summary>Adds a signed event to publish (JOIN, ACCEPT, RESIGN; moves go through <see cref="CommitAppliedAsync"/>).</summary>
    Task AddOutboxAsync(Guid gameId, OutboxEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Records that at least one relay accepted the event. Unknown ids are ignored.</summary>
    Task MarkOutboxSentAsync(Guid gameId, string eventId, CancellationToken cancellationToken = default);

    Task AddFlagAsync(Guid gameId, FlagEntry flag, CancellationToken cancellationToken = default);

    /// <summary>Everything stored for one game, or null if it isn't stored.</summary>
    Task<StoredGame?> LoadAsync(Guid gameId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NetGameRecord>> ListGamesAsync(CancellationToken cancellationToken = default);
}

/// <summary>A store write failed. Nothing of that write was kept.</summary>
public sealed class GameStoreException(string message, Exception? innerException = null) : Exception(message, innerException);

public enum NetRole
{
    Host,
    Guest,
}

/// <summary>Host: Inviting -> InGame | Cancelled. Guest: Joining -> InGame | Failed. Both end Completed (scope.md "Invite").</summary>
public enum HandshakeState
{
    Inviting,
    Joining,
    InGame,
    Completed,
    Cancelled,
    Failed,
}

/// <summary>
/// One multiplayer game as this device sees it. Holds public keys only; the secret key lives in <see cref="IKeyStore"/>.
/// </summary>
/// <param name="HostNonce">The host's colour nonce (hex). Kept secret by the host until its ACCEPT reveals it; null on a guest until then.</param>
/// <param name="GuestNonce">The guest's nonce (hex), from its JOIN; null on a host until a JOIN locks the game.</param>
/// <param name="MyColor">Null until the handshake resolves colours.</param>
/// <param name="Note">Why the game failed or how it ended, for the user.</param>
public sealed record NetGameRecord(
    Guid GameId,
    NetRole Role,
    string MyPubKey,
    string HostPubKey,
    string? OpponentPubKey,
    BoardSize Size,
    int KomiHalves,
    string SeedCommit,
    string? HostNonce,
    string? GuestNonce,
    IReadOnlyList<string> Relays,
    HandshakeState State,
    PlayerColor? MyColor = null,
    GameResult? Result = null,
    string? Note = null);

/// <summary>Where the turn exchange is: committed together with each applied action (scope.md "Turn exchange").</summary>
/// <param name="Tps">The board after <paramref name="AppliedTurn"/> plies, so a restart restores in O(1).</param>
/// <param name="PublishedTurn">The newest own turn put in the outbox.</param>
/// <param name="MaxCreatedAt">Newest relay <c>created_at</c> seen; only for the subscription's <c>since</c>, never for ordering.</param>
public sealed record ExchangeProgress(int AppliedTurn, string StateHash, string Tps, int PublishedTurn, long MaxCreatedAt);

/// <summary>An action applied to the board, with the event it arrived in (or was sent in) and the state after it.</summary>
public sealed record AppliedAction(SignedGameAction Action, string EventId, string TpsAfter, string StateHashAfter);

/// <param name="EventJson">The signed Nostr event, republished verbatim (same id), never re-signed.</param>
/// <param name="Sent">At least one relay answered OK true.</param>
public sealed record OutboxEntry(string EventId, GameActionType ActionType, int Turn, string EventJson, bool Sent = false);

/// <summary>A rejected action from the opponent (tamper rows 3-7). Flags survive restarts.</summary>
public sealed record FlagEntry(int Turn, string Reason, string? EventId, string Author, DateTimeOffset SeenAt);

public sealed record StoredGame(
    NetGameRecord Record,
    ExchangeProgress Progress,
    IReadOnlyList<AppliedAction> Applied,
    IReadOnlyList<OutboxEntry> Outbox,
    IReadOnlyList<FlagEntry> Flags);

using System;

namespace TakEngine.Abstractions;

public enum BroadcastMode
{
    Disabled = 0,
    AdminOnly = 1,       // Zero-delay direct stream to Tournament Admins
    PublicDelayed = 2,   // Delayed broadcast for community (anti-cheat)
    PublicRealtime = 3   // Casual unrated games only
}

public sealed record BroadcastConfiguration(
    BroadcastMode Mode = BroadcastMode.Disabled,
    TimeSpan Delay = default,
    string? AdminPubKey = null,
    string? TournamentId = null,
    bool AllowSpectatorChat = false)
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(120);

    public static BroadcastConfiguration CreatePublicDelayed(string? tournamentId = null, TimeSpan? delay = null) =>
        new(BroadcastMode.PublicDelayed, delay ?? DefaultDelay, null, tournamentId);

    public static BroadcastConfiguration CreateAdminOnly(string adminPubKey, string tournamentId) =>
        new(BroadcastMode.AdminOnly, TimeSpan.Zero, adminPubKey, tournamentId);
}

public sealed record LiveMatchSummary(
    Guid GameId,
    string? TournamentId,
    string BroadcasterPubKey,
    string WhitePlayerPubKey,
    int? WhitePlayerElo,
    string BlackPlayerPubKey,
    int? BlackPlayerElo,
    BoardSize BoardSize,
    int CurrentTurn,
    DateTime StartedAtUtc,
    DateTime LastActiveUtc,
    int DelaySeconds);

/// <summary>
/// A player's signed action relayed to spectators, with the time it was broadcast. The spectator verifies the action's own
/// signature (<c>ActionDigest</c>), so the broadcaster is not trusted. <see cref="StateHash"/> is the broadcaster's claimed hash
/// after the action and is checked when present.
/// </summary>
public sealed record BroadcastEnvelope(
    SignedGameAction Action,
    DateTime TimestampUtc,
    string? StateHash = null,
    string? TournamentId = null)
{
    public Guid GameId => Action.GameId;

    public int TurnIndex => Action.Turn;
}

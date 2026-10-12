using TakEngine.Transport.Relay;

namespace TakEngine.Multiplayer;

/// <summary>What this side last heard from the opponent. Derived, never authoritative (scope.md "Opponent offline").</summary>
public enum OpponentLink
{
    /// <summary>No verified event from the opponent yet.</summary>
    Unknown,

    /// <summary>A verified event within the last 10 minutes.</summary>
    Active,

    /// <summary>Heard from before, but not within 10 minutes. (Stale, over 3 days, is M5.)</summary>
    Quiet,
}

/// <summary>This side's own connection to the relay set.</summary>
public enum OwnLink
{
    /// <summary>Every relay is subscribed.</summary>
    Online,

    /// <summary>Some relays are subscribed, some are down.</summary>
    Degraded,

    /// <summary>No relay is subscribed: moves wait in the outbox until one is.</summary>
    Offline,
}

/// <summary>Link states for the status line. Pure: times and relay states are passed in.</summary>
public static class LinkStatus
{
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(10);

    /// <param name="lastVerifiedAt">When this side received the opponent's last verified event (local clock), or null.</param>
    public static OpponentLink Opponent(DateTimeOffset? lastVerifiedAt, DateTimeOffset now) => lastVerifiedAt switch
    {
        null => OpponentLink.Unknown,
        { } at when now - at < ActiveWindow => OpponentLink.Active,
        _ => OpponentLink.Quiet,
    };

    public static OwnLink Own(IEnumerable<RelayLinkState> relays)
    {
        ArgumentNullException.ThrowIfNull(relays);
        int up = 0, total = 0;
        foreach (RelayLinkState state in relays)
        {
            total++;
            if (state is RelayLinkState.Subscribed or RelayLinkState.CaughtUp)
                up++;
        }
        return up == 0 ? OwnLink.Offline : up == total ? OwnLink.Online : OwnLink.Degraded;
    }
}

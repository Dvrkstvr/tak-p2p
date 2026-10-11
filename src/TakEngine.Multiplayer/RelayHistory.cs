using TakEngine.Crypto;
using TakEngine.Transport.Envelope;
using TakEngine.Transport.Matchmaking;
using TakEngine.Transport.Nostr;
using TakEngine.Transport.Relay;

namespace TakEngine.Multiplayer;

/// <summary>
/// Reads one game's whole history from its relays (no <c>since</c>) as the holder of <see cref="MultiplayerOptions.Key"/> sees it:
/// every event that passes tamper rows 1, 3 and 4. Who played which colour is not known yet, so the author check (row 2) is left to
/// <see cref="RebuildPlan"/>. A shell: it opens sockets and waits.
/// </summary>
public static class RelayHistory
{
    /// <param name="wait">How long to wait (on <see cref="MultiplayerOptions.Time"/>) for every relay's stored history.</param>
    public static async Task<IReadOnlyList<HistoryEntry>> FetchAsync(MultiplayerOptions options, InviteCode invite, TimeSpan wait)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(invite);
        PublicKey me = options.Key.PublicKey;
        bool host = string.Equals(invite.HostPubKey, me.ToHex(), StringComparison.OrdinalIgnoreCase);
        PublicKey? opponent = host ? null : PublicKey.FromHex(invite.HostPubKey.ToLowerInvariant());
        var relays = invite.Relays.Concat(options.DefaultRelays).Distinct(StringComparer.Ordinal).Select(r => new Uri(r)).ToList();

        var history = new List<HistoryEntry>();
        await using var pool = new RelayPool(relays, options.Sockets, options.Time, me.ToHex(), budget: options.Budget);
        pool.Start(() => SubscriptionPlan.Build(invite.GameId, me, opponent, null));
        DateTimeOffset deadline = options.Time.GetUtcNow() + wait;
        while (options.Time.GetUtcNow() < deadline && !Complete(pool.Links))
            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        while (pool.Events.TryRead(out ReceivedEvent? received))
        {
            NostrEvent evt = received.Event;
            PublicKey? author = received.IsOwn || !PublicKey.TryFromHex(evt.Pubkey, out PublicKey? key) ? null : key;
            DecodeResult decoded = EnvelopeCodec.Decode(evt, new DecodeContext(options.Key, invite.GameId, author));
            if (decoded.IsAccepted)
                history.Add(new HistoryEntry(decoded.Action!, evt.Id, new RelayMessage.Publish(evt).Format()));
        }
        return history;
    }

    /// <summary>Every relay has sent its stored history (EOSE), refused reads, or failed to connect at least once; and one answered.</summary>
    private static bool Complete(IReadOnlyList<RelayLink> links) =>
        links.Any(l => l.State == RelayLinkState.CaughtUp)
        && links.All(l => l.State == RelayLinkState.CaughtUp || l.ReadImpossible || l.Failures > 0);
}

using TakEngine.Crypto;
using TakEngine.Transport.Envelope;
using TakEngine.Transport.Matchmaking;
using TakEngine.Transport.Nostr;
using TakEngine.Transport.Relay;

namespace TakEngine.Multiplayer;

/// <param name="Unanswered">Relays that neither finished sending their history (EOSE) nor refused reads before the wait ran out.</param>
public sealed record RelayHistoryResult(IReadOnlyList<HistoryEntry> History, IReadOnlyList<Uri> Unanswered);

/// <summary>
/// Reads one game's whole history from its relays (no <c>since</c>) as the holder of <see cref="MultiplayerOptions.Key"/> sees it:
/// every event that passes tamper rows 1, 3 and 4. Who played which colour is not known yet, so the author check (row 2) is left to
/// <see cref="RebuildPlan"/>. A shell: it opens sockets and waits.
/// </summary>
public static class RelayHistory
{
    /// <param name="wait">How long to wait (on <see cref="MultiplayerOptions.Time"/>) for every relay's stored history.</param>
    public static async Task<RelayHistoryResult> FetchAsync(MultiplayerOptions options, InviteCode invite, TimeSpan wait)
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
        // The deadline is protocol time (TimeProvider); the poll between checks is only a real-time pause.
        while (options.Time.GetUtcNow() < deadline && !Complete(pool.Links))
            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        var unanswered = pool.Links.Where(l => !Answered(l)).Select(l => l.Url).ToList();
        while (pool.Events.TryRead(out ReceivedEvent? received))
        {
            NostrEvent evt = received.Event;
            PublicKey? author = received.IsOwn || !PublicKey.TryFromHex(evt.Pubkey, out PublicKey? key) ? null : key;
            DecodeResult decoded = EnvelopeCodec.Decode(evt, new DecodeContext(options.Key, invite.GameId, author));
            if (decoded.IsAccepted)
                history.Add(new HistoryEntry(decoded.Action!, evt.Id, new RelayMessage.Publish(evt).Format()));
        }
        if (!pool.Links.Any(l => l.State == RelayLinkState.CaughtUp))
            unanswered = [.. pool.Links.Select(l => l.Url)];
        return new RelayHistoryResult(history, unanswered);
    }

    /// <summary>
    /// Every relay has sent its whole stored history (EOSE) or refused reads, and at least one sent it. A relay that dropped or never
    /// connected is not done: its history may hold the one move that decides where the game stands.
    /// </summary>
    private static bool Complete(IReadOnlyList<RelayLink> links) =>
        links.Any(l => l.State == RelayLinkState.CaughtUp) && links.All(Answered);

    private static bool Answered(RelayLink link) => link.State == RelayLinkState.CaughtUp || link.ReadImpossible;
}

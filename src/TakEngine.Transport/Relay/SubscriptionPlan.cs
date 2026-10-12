using TakEngine.Crypto;
using TakEngine.Transport.Envelope;
using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Relay;

/// <summary>
/// The one REQ a client sends per relay per (re)connect (pipeline/architecture.md "Wire format"): the opponent's events addressed to
/// me for this game, and my own events for this game. Pure.
/// </summary>
public static class SubscriptionPlan
{
    public const int Limit = 500;
    public const int IdsChunk = 5;
    public static readonly TimeSpan SinceSlack = TimeSpan.FromSeconds(30);

    public static string SubscriptionId(Guid gameId) => "tak-" + gameId.ToString("N")[..8];

    /// <param name="opponent">Null while a host is still inviting: then any author may write to me (a JOIN).</param>
    /// <param name="maxCreatedAt">The newest <c>created_at</c> seen for this game; null on a cold rebuild (no <c>since</c>).</param>
    public static RelayMessage.Req Build(Guid gameId, PublicKey me, PublicKey? opponent, long? maxCreatedAt)
    {
        ArgumentNullException.ThrowIfNull(me);
        string gid = gameId.ToString("D");
        long? since = maxCreatedAt is long max ? Math.Max(0, max - (long)SinceSlack.TotalSeconds) : null;
        var fromOpponent = new NostrFilter
        {
            Kinds = [EnvelopeCodec.Kind],
            Authors = opponent is null ? null : [opponent.ToHex()],
            TagP = [me.ToHex()],
            TagG = [gid],
            Since = since,
            Limit = Limit,
        };
        var mine = new NostrFilter
        {
            Kinds = [EnvelopeCodec.Kind],
            Authors = [me.ToHex()],
            TagG = [gid],
            Since = since,
            Limit = Limit,
        };
        return new RelayMessage.Req(SubscriptionId(gameId), [fromOpponent, mine]);
    }

    /// <summary><c>ids</c> lookups in filters of at most 5 ids (nos.lol silently returns nothing for 20).</summary>
    public static IReadOnlyList<NostrFilter> IdLookups(IEnumerable<string> eventIds) =>
        eventIds.Distinct(StringComparer.Ordinal).Chunk(IdsChunk).Select(chunk => new NostrFilter { Ids = [.. chunk] }).ToList();
}

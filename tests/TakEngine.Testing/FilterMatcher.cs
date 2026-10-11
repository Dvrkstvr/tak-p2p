using TakEngine.Transport.Nostr;

namespace TakEngine.Testing;

/// <summary>NIP-01 filter semantics for the fake relay: every present field must match; lists match any element.</summary>
public static class FilterMatcher
{
    public static bool Matches(NostrFilter filter, NostrEvent evt)
    {
        if (filter.Ids is { } ids && !ids.Contains(evt.Id))
            return false;
        if (filter.Authors is { } authors && !authors.Contains(evt.Pubkey))
            return false;
        if (filter.Kinds is { } kinds && !kinds.Contains(evt.Kind))
            return false;
        if (filter.Since is long since && evt.CreatedAt < since)
            return false;
        if (filter.Until is long until && evt.CreatedAt > until)
            return false;
        return TagMatches(filter.TagP, "p", evt) && TagMatches(filter.TagG, "g", evt)
            && TagMatches(filter.TagE, "e", evt) && TagMatches(filter.TagT, "t", evt);
    }

    private static bool TagMatches(List<string>? wanted, string name, NostrEvent evt) =>
        wanted is null || evt.Tags.Any(t => t.Count >= 2 && t[0] == name && wanted.Contains(t[1]));
}

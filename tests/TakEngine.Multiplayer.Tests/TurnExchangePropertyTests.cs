using TakEngine.Abstractions;
using TakEngine.Testing;

namespace TakEngine.Multiplayer.Tests;

/// <summary>F-035: the same game delivered in any order, with every event twice, ends at the same hash.</summary>
public class TurnExchangePropertyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static IEnumerable<IReadOnlyList<T>> Permutations<T>(IReadOnlyList<T> items)
    {
        if (items.Count <= 1)
        {
            yield return items;
            yield break;
        }
        for (int i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, j) => j != i).ToList();
            foreach (var tail in Permutations(rest))
                yield return [items[i], .. tail];
        }
    }

    [Fact]
    public void EveryOrderOfASixPlyGame_WithDuplicates_EndsAtTheSameHash()
    {
        IReadOnlyList<string> script = ScriptedGames.FiveByFiveRoadWin.Take(6).ToList();
        var (actions, finalHash, _) = SignedScript.Play(script);
        var white = actions.Where(a => a.Turn % 2 == 1).ToList();
        var events = white.Concat(white).ToList(); // each opponent event delivered twice

        int orders = 0;
        foreach (var order in Permutations(events))
        {
            var r = new TurnExchangeTests.Receiver(script);
            foreach (var a in order)
                r.Deliver(a, T0);
            Assert.Equal(finalHash, r.Session.CurrentStateHash);
            Assert.Equal(6, r.Exchange.AppliedTurn);
            Assert.Empty(r.Flags);
            orders++;
        }
        Assert.Equal(720, orders);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2026)]
    [InlineData(90210)]
    public void RandomOrdersOfTheScriptedRoadWin_WithDuplicates_EndAtTheSameHash(int seed)
    {
        IReadOnlyList<string> script = ScriptedGames.FiveByFiveRoadWin;
        var (actions, finalHash, result) = SignedScript.Play(script);
        var white = actions.Where(a => a.Turn % 2 == 1).ToList();
        var rng = new Random(seed);

        for (int run = 0; run < 200; run++)
        {
            var events = white.Concat(white).Concat(white.Where(_ => rng.Next(2) == 0)).OrderBy(_ => rng.Next()).ToList();
            var r = new TurnExchangeTests.Receiver(script);
            foreach (var a in events)
                r.Deliver(a, T0);

            string context = $"seed {seed}, run {run}, order {string.Join(",", events.Select(e => e.Turn))}";
            Assert.True(finalHash == r.Session.CurrentStateHash, "final hash differs: " + context);
            Assert.True(result == r.Session.Result, "result differs: " + context);
            Assert.True(r.Flags.Count == 0, "unexpected flag: " + context);
        }
    }
}

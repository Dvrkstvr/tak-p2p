using TakEngine.Abstractions;
using TakEngine.Core.Cryptography;
using TakEngine.Crypto;
using TakEngine.Testing;
using TakEngine.Transport.Nostr;

namespace TakEngine.Multiplayer.Tests;

/// <summary>
/// F-037, scope.md "Reconnect and catch-up": a player who crashes or loses their device's game state carries on from the store, or
/// rebuilds the game from relay history, and ends at the same hash as the opponent.
/// </summary>
public class RestartTests
{
    private static readonly IReadOnlyList<string> Script = ScriptedGames.FiveByFiveRoadWin;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlackCrashesAfterTurn4_WhitePlays5_BlackRestartsAndCatchesUp(bool sqlite)
    {
        await using var t = new Table(seed: 7, sqlite);
        var (host, guest) = await t.Connect();
        await t.Play(host, guest, Script.Take(4));
        bool hostIsWhite = host.MyColor == PlayerColor.White;
        MultiplayerGame white = hostIsWhite ? host : guest;
        MultiplayerGame black = hostIsWhite ? guest : host;
        MultiplayerOptions blackOptions = hostIsWhite ? t.GuestOptions : t.HostOptions;
        await black.DisposeAsync(); // the crash

        Assert.True((await white.SubmitAsync(Script[4])).IsSuccess);
        await t.Advance(TimeSpan.FromSeconds(2));
        MultiplayerGame again = t.Track((await MultiplayerGame.ResumeAsync(blackOptions, black.GameId))!);

        Assert.NotNull(again);
        // White's move may still wait on its publish budget (4 events per 10 s): let protocol time run while waiting.
        int calls = 0;
        await Table.Eventually(() => again.AppliedTurn == 5, "the move played while black was away arrives",
            nudge: () => { if (++calls % 10 == 0) t.Time.Advance(TimeSpan.FromSeconds(1)); });
        Assert.Equal(white.StateHash, again.StateHash);
        await t.Play(white, again, Script.Skip(5), from: 6);
        await Table.Eventually(() => again.Result is not null && white.Result is not null, "both games end");
        Assert.Equal(white.Result, again.Result);
        Assert.Equal(white.StateHash, again.StateHash);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LostState_IsRebuiltFromTheRelays_AndTheGameGoesOn(bool rebuildHost, bool sqlite)
    {
        await using var t = new Table(seed: 8, sqlite);
        var (host, guest) = await t.Connect();
        await t.Play(host, guest, Script.Take(4));
        MultiplayerGame lost = rebuildHost ? host : guest;
        MultiplayerGame other = rebuildHost ? guest : host;
        MultiplayerOptions original = rebuildHost ? t.HostOptions : t.GuestOptions;
        await lost.DisposeAsync();

        var fresh = new InMemoryGameStore(); // a new device, or a wiped one: only the key and the invite survive
        MultiplayerOptions options = t.Options(original.Key, fresh);
        Task<JoinOutcome> rebuilding = MultiplayerGame.RebuildAsync(options, host.Invite.ToCompactCode());
        await Table.Eventually(() => rebuilding.IsCompleted, "rebuild finishes", nudge: () => t.Time.Advance(TimeSpan.FromMilliseconds(100)));
        JoinOutcome outcome = await rebuilding;

        Assert.True(outcome.Game is not null, outcome.Error);
        MultiplayerGame rebuilt = t.Track(outcome.Game);
        Assert.Equal(4, rebuilt.AppliedTurn);
        Assert.Equal(other.StateHash, rebuilt.StateHash);
        Assert.Equal(lost.MyColor, rebuilt.MyColor);
        Assert.Equal("rebuilt from relays", rebuilt.Record.Note);
        StoredGame stored = (await fresh.LoadAsync(rebuilt.GameId))!;
        Assert.Equal(Enumerable.Range(1, 4), stored.Applied.Select(a => a.Action.Turn));
        Assert.All(stored.Outbox, o => Assert.True(o.Sent));

        await t.Play(rebuilt, other, Script.Skip(4), from: 5);
        await Table.Eventually(() => rebuilt.Result is not null && other.Result is not null, "both games end");
        Assert.Equal(other.Result, rebuilt.Result);
        Assert.Equal(other.StateHash, rebuilt.StateHash);
    }

    [Fact]
    public async Task Rebuild_WithAnEquivocationOnARelay_StoresTheFlag()
    {
        await using var t = new Table(seed: 12);
        var (host, guest) = await t.Connect();
        await t.Play(host, guest, Script.Take(4));
        bool hostIsWhite = host.MyColor == PlayerColor.White;
        MultiplayerGame black = hostIsWhite ? guest : host;
        MultiplayerOptions blackOptions = hostIsWhite ? t.GuestOptions : t.HostOptions;
        SecretKey white = (hostIsWhite ? t.HostOptions : t.GuestOptions).Key;
        // White's second move for turn 1, validly signed: black's turn 2 chains from the real one, so this one is dropped.
        NostrEvent forged = TamperForge.Forge(ForgeKind.Equivocate, white, blackOptions.Key.PublicKey, black.GameId, 1,
            StateHasher.ComputeGenesisHash(BoardSize.Five), "c3", t.Time.GetUtcNow().ToUnixTimeSeconds(), t.Random32);
        Assert.True(t.R1.Seed(forged));
        await black.DisposeAsync();

        var fresh = new InMemoryGameStore();
        Task<JoinOutcome> rebuilding = MultiplayerGame.RebuildAsync(t.Options(blackOptions.Key, fresh), host.Invite.ToCompactCode());
        await Table.Eventually(() => rebuilding.IsCompleted, "rebuild finishes", nudge: () => t.Time.Advance(TimeSpan.FromMilliseconds(100)));
        JoinOutcome outcome = await rebuilding;

        Assert.True(outcome.Game is not null, outcome.Error);
        t.Track(outcome.Game);
        FlagEntry flag = Assert.Single((await fresh.LoadAsync(black.GameId))!.Flags);
        Assert.Equal(1, flag.Turn);
        Assert.Equal(forged.Id, flag.EventId);
        Assert.Equal(white.PublicKey.ToHex(), flag.Author);
        Assert.Contains("conflicting move for turn 1", flag.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rebuild_WithAMoveMissingFromEveryRelay_FailsAndStoresNothing()
    {
        await using var t = new Table(seed: 9);
        var (host, guest) = await t.Connect();
        await t.Play(host, guest, Script.Take(4));
        string turn2 = (await t.GuestStore.LoadAsync(guest.GameId))!.Applied.Single(a => a.Action.Turn == 2).EventId;
        t.R1.Withhold(turn2);
        t.R2.Withhold(turn2);
        await guest.DisposeAsync();

        var fresh = new InMemoryGameStore();
        Task<JoinOutcome> rebuilding = MultiplayerGame.RebuildAsync(t.Options(t.GuestOptions.Key, fresh), host.Invite.ToCompactCode());
        await Table.Eventually(() => rebuilding.IsCompleted, "rebuild finishes", nudge: () => t.Time.Advance(TimeSpan.FromMilliseconds(100)));
        JoinOutcome outcome = await rebuilding;

        Assert.Null(outcome.Game);
        Assert.Contains("move 2 not found on any relay", outcome.Error, StringComparison.Ordinal);
        Assert.Null(await fresh.LoadAsync(guest.GameId));
    }

    [Fact]
    public async Task Rebuild_WithARelayThatNeverSentItsHistory_IsRefused_AndStoresNothing()
    {
        await using var t = new Table(seed: 11);
        var (host, guest) = await t.Connect();
        await t.Play(host, guest, Script.Take(4));
        await guest.DisposeAsync();
        t.R2.Partition(); // the move that decides where the game stands could be on this one

        var fresh = new InMemoryGameStore();
        Task<JoinOutcome> rebuilding = MultiplayerGame.RebuildAsync(t.Options(t.GuestOptions.Key, fresh), host.Invite.ToCompactCode());
        await Table.Eventually(() => rebuilding.IsCompleted, "rebuild gives up", nudge: () => t.Time.Advance(TimeSpan.FromMilliseconds(100)));
        JoinOutcome outcome = await rebuilding;

        Assert.Null(outcome.Game);
        Assert.Contains("Not every relay sent its history", outcome.Error, StringComparison.Ordinal);
        Assert.Contains(t.R2.Url.ToString().TrimEnd('/'), outcome.Error, StringComparison.Ordinal);
        Assert.Null(await fresh.LoadAsync(guest.GameId));
    }

    [Fact]
    public async Task Rebuild_OfAGameStillStored_IsRefused()
    {
        await using var t = new Table(seed: 10);
        var (host, _) = await t.Connect();

        JoinOutcome outcome = await MultiplayerGame.RebuildAsync(t.GuestOptions, host.Invite.ToCompactCode());

        Assert.Null(outcome.Game);
        Assert.Contains("already stored", outcome.Error, StringComparison.Ordinal);
    }
}

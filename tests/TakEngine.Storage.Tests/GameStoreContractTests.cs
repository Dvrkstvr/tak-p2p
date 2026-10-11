using TakEngine.Abstractions;
using TakEngine.Storage.Local;
using TakEngine.Testing;

namespace TakEngine.Storage.Tests;

/// <summary>
/// What every <see cref="IGameStore"/> promises (architecture.md "Data"), run against the in-memory fake and SQLite, so the fake
/// the multiplayer tests use can't drift from the real store.
/// </summary>
public abstract class GameStoreContractTests
{
    protected static readonly Guid Game = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");

    protected abstract IGameStore NewStore();

    /// <summary>The same storage, opened again (a restart).</summary>
    protected abstract IGameStore Reopen(IGameStore store);

    protected static NetGameRecord Record() => new(
        Game, NetRole.Host, "me", "me", null, BoardSize.Seven, 5, new string('a', 64), "nonce", null, ["wss://nos.lol", "wss://relay.damus.io"],
        HandshakeState.Inviting);

    protected static ExchangeProgress Progress(int turn) => new(turn, $"hash{turn}", $"tps{turn}", turn / 2, 1_760_000_000 + turn);

    protected static AppliedAction Applied(int turn, GameActionType type = GameActionType.Move) => new(
        new SignedGameAction(1, type, Game, turn, "author", $"hash{turn - 1}",
            type == GameActionType.Move
                ? new Dictionary<string, ActionValue> { ["ptn"] = ActionValue.Of("a1") }
                : new Dictionary<string, ActionValue> { ["size"] = ActionValue.Of(7), ["white"] = ActionValue.Of("w\"<>+") },
            new string('5', 128)),
        $"event{turn}", $"tps{turn}", $"hash{turn}");

    [Fact]
    public async Task EverythingSurvivesARestart()
    {
        IGameStore store = NewStore();
        await store.SaveNewGameAsync(Record(), Progress(0));
        await store.CommitAppliedAsync(Game, Applied(0, GameActionType.Accept), Progress(0));
        await store.CommitAppliedAsync(Game, Applied(1), Progress(1), new OutboxEntry("event1", GameActionType.Move, 1, "[\"EVENT\",{}]"));
        await store.AddFlagAsync(Game, new FlagEntry(2, "hash chain mismatch at turn 2", "bad", "opp", new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero)));
        await store.MarkOutboxSentAsync(Game, "event1");
        NetGameRecord done = Record() with
        {
            State = HandshakeState.Completed,
            OpponentPubKey = "opp",
            GuestNonce = "g",
            MyColor = PlayerColor.Black,
            Result = new GameResult(false, PlayerColor.White, GameEndReason.Resignation),
            Note = "resigned",
        };
        await store.UpdateRecordAsync(done, new OutboxEntry("resign", GameActionType.Resign, 1, "{}"));

        StoredGame loaded = (await Reopen(store).LoadAsync(Game))!;

        Assert.Equal(done with { Relays = loaded.Record.Relays }, loaded.Record);
        Assert.Equal(done.Relays, loaded.Record.Relays);
        Assert.Equal(Progress(1), loaded.Progress);
        Assert.Equal(2, loaded.Applied.Count);
        Assert.Equal(Applied(1).Action.ActionData, loaded.Applied[1].Action.ActionData);
        Assert.Equal(Applied(0, GameActionType.Accept).Action.ActionData, loaded.Applied[0].Action.ActionData);
        Assert.Equal(Applied(1) with { Action = loaded.Applied[1].Action }, loaded.Applied[1]);
        Assert.Equal(Applied(1).Action with { ActionData = loaded.Applied[1].Action.ActionData }, loaded.Applied[1].Action);
        Assert.Equal(["event1", "resign"], loaded.Outbox.Select(o => o.EventId));
        Assert.True(loaded.Outbox[0].Sent);
        Assert.False(loaded.Outbox[1].Sent);
        Assert.Equal(new FlagEntry(2, "hash chain mismatch at turn 2", "bad", "opp", new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero)), Assert.Single(loaded.Flags));
        Assert.Single(await Reopen(store).ListGamesAsync());
    }

    [Fact]
    public async Task SavingAGameTwice_OrWritingAnUnknownGame_Fails_AndUnknownLoadsAreNull()
    {
        IGameStore store = NewStore();
        await store.SaveNewGameAsync(Record(), Progress(0));

        await Assert.ThrowsAsync<GameStoreException>(() => store.SaveNewGameAsync(Record(), Progress(0)));
        await Assert.ThrowsAsync<GameStoreException>(() => store.UpdateProgressAsync(Guid.NewGuid(), Progress(1)));
        await Assert.ThrowsAsync<GameStoreException>(() => store.UpdateRecordAsync(Record() with { GameId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<GameStoreException>(() => store.CommitAppliedAsync(Guid.NewGuid(), Applied(1), Progress(1)));
        await Assert.ThrowsAsync<GameStoreException>(() => store.AddFlagAsync(Guid.NewGuid(), new FlagEntry(1, "x", null, "a", DateTimeOffset.UnixEpoch)));
        Assert.Null(await store.LoadAsync(Guid.NewGuid()));
        await store.MarkOutboxSentAsync(Game, "unknown event"); // ignored
    }
}

public sealed class InMemoryGameStoreContractTests : GameStoreContractTests
{
    protected override IGameStore NewStore() => new InMemoryGameStore();

    protected override IGameStore Reopen(IGameStore store) => store;
}

public sealed class SqliteGameStoreContractTests : GameStoreContractTests, IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tak-store-").FullName;

    protected override IGameStore NewStore() => new SqliteGameStore(Path.Combine(_dir, SqliteGameStore.FileName));

    protected override IGameStore Reopen(IGameStore store) => new SqliteGameStore(((SqliteGameStore)store).DatabasePath);

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}

using TakEngine.Abstractions;
using TakEngine.Testing;

namespace TakEngine.Multiplayer.Tests;

/// <summary>Smoke tests for the storage fakes the coordinator tests rely on (restart = a new coordinator on the same store).</summary>
public class FakeStoreTests
{
    private static readonly Guid Game = Guid.Parse("0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e");

    private static NetGameRecord Record() => new(
        Game, NetRole.Host, "me", "me", null, BoardSize.Five, 0, new string('a', 64), "nonce", null, ["wss://nos.lol"], HandshakeState.Inviting);

    private static ExchangeProgress Progress(int turn) => new(turn, $"hash{turn}", $"tps{turn}", 0, 100 + turn);

    private static AppliedAction Applied(int turn) =>
        new(SignedGameAction.UnsignedMove(Game, turn, "me", $"hash{turn - 1}", "a1"), $"event{turn}", $"tps{turn}", $"hash{turn}");

    [Fact]
    public async Task Data_SurvivesTheUserGoingAway()
    {
        var store = new InMemoryGameStore();
        await store.SaveNewGameAsync(Record(), Progress(0));
        await store.CommitAppliedAsync(Game, Applied(1), Progress(1), new OutboxEntry("event1", GameActionType.Move, 1, "{}"));
        await store.AddFlagAsync(Game, new FlagEntry(2, "hash chain mismatch at turn 2", "bad", "opp", DateTimeOffset.UnixEpoch));
        await store.MarkOutboxSentAsync(Game, "event1");
        await store.UpdateRecordAsync(Record() with { State = HandshakeState.InGame, OpponentPubKey = "opp" });

        StoredGame? loaded = await store.LoadAsync(Game);

        Assert.NotNull(loaded);
        Assert.Equal(HandshakeState.InGame, loaded.Record.State);
        Assert.Equal(Progress(1), loaded.Progress);
        Assert.Equal("event1", Assert.Single(loaded.Applied).EventId);
        Assert.True(Assert.Single(loaded.Outbox).Sent);
        Assert.Equal(2, Assert.Single(loaded.Flags).Turn);
        Assert.Single(await store.ListGamesAsync());
        Assert.Null(await store.LoadAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task FailNextWrite_ThrowsAndKeepsNothing_ThenWritesWorkAgain()
    {
        var store = new InMemoryGameStore();
        await store.SaveNewGameAsync(Record(), Progress(0));
        store.FailNextWrite();

        await Assert.ThrowsAsync<GameStoreException>(() =>
            store.CommitAppliedAsync(Game, Applied(1), Progress(1), new OutboxEntry("event1", GameActionType.Move, 1, "{}")));

        StoredGame loaded = (await store.LoadAsync(Game))!;
        Assert.Equal(Progress(0), loaded.Progress);
        Assert.Empty(loaded.Applied);
        Assert.Empty(loaded.Outbox);

        await store.CommitAppliedAsync(Game, Applied(1), Progress(1));
        Assert.Equal(1, (await store.LoadAsync(Game))!.Progress.AppliedTurn);
    }

    [Fact]
    public async Task FailNextWrite_CanTargetOneOperation()
    {
        var store = new InMemoryGameStore();
        await store.SaveNewGameAsync(Record(), Progress(0));
        store.FailNextWrite(op => op == nameof(IGameStore.MarkOutboxSentAsync));

        await store.AddOutboxAsync(Game, new OutboxEntry("e", GameActionType.Join, 0, "{}"));
        await Assert.ThrowsAsync<GameStoreException>(() => store.MarkOutboxSentAsync(Game, "e"));

        Assert.False(Assert.Single((await store.LoadAsync(Game))!.Outbox).Sent);
    }

    [Fact]
    public async Task SavingAGameTwice_OrWritingAnUnknownGame_Fails()
    {
        var store = new InMemoryGameStore();
        await store.SaveNewGameAsync(Record(), Progress(0));

        await Assert.ThrowsAsync<GameStoreException>(() => store.SaveNewGameAsync(Record(), Progress(0)));
        await Assert.ThrowsAsync<GameStoreException>(() => store.UpdateProgressAsync(Guid.NewGuid(), Progress(1)));
    }

    [Fact]
    public async Task KeyStore_LoadsSavesAndRefusesToOverwrite()
    {
        var empty = new InMemoryKeyStore();
        Assert.Null(await empty.LoadSecretAsync());
        await empty.SaveNewSecretAsync(new byte[32]);
        Assert.Equal(new byte[32], await empty.LoadSecretAsync());
        await Assert.ThrowsAsync<KeyStoreException>(() => empty.SaveNewSecretAsync(new byte[32]));

        await Assert.ThrowsAsync<KeyStoreException>(() => InMemoryKeyStore.Corrupt().LoadSecretAsync());
    }

    [Fact]
    public async Task RecordUpdate_WithItsOutboxEntry_LandsWhole_OrNotAtAll()
    {
        var store = new InMemoryGameStore();
        await store.SaveNewGameAsync(Record(), Progress(0));
        var accept = new OutboxEntry("accept", GameActionType.Accept, 0, "{}");

        store.FailNextWrite();
        await Assert.ThrowsAsync<GameStoreException>(() => store.UpdateRecordAsync(Record() with { State = HandshakeState.InGame }, accept));
        StoredGame before = (await store.LoadAsync(Game))!;
        Assert.Equal(HandshakeState.Inviting, before.Record.State);
        Assert.Empty(before.Outbox);

        await store.UpdateRecordAsync(Record() with { State = HandshakeState.InGame }, accept);
        StoredGame after = (await store.LoadAsync(Game))!;
        Assert.Equal(HandshakeState.InGame, after.Record.State);
        Assert.Equal("accept", Assert.Single(after.Outbox).EventId);
    }
}

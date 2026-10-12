using TakEngine.Abstractions;
using TakEngine.Crypto;
using TakEngine.Testing;
using TakEngine.Transport.Envelope;
using TakEngine.Transport.Matchmaking;
using TakEngine.Transport.Nostr;
using TakEngine.Transport.Relay;

namespace TakEngine.Multiplayer.Tests;

/// <summary>F-035 / F-017 through the shell: MultiplayerGame over InMemoryRelay, InMemoryGameStore and a fake clock.</summary>
public sealed class GameCoordinatorTests : IAsyncDisposable
{
    private readonly Table _t = new(seed: 7);

    public ValueTask DisposeAsync() => _t.DisposeAsync();

    private int EventFrames(InMemoryRelay relay) => relay.ReceivedFrames.Count(f => f.StartsWith("[\"EVENT\"", StringComparison.Ordinal));

    [Fact]
    public async Task HostAndGuest_LandInTheSameGame_WithOppositeColours()
    {
        MultiplayerGame host = await _t.Host(BoardSize.Six, komi: 5);
        PlayerColor? hostStarted = null, guestStarted = null;
        host.Started += c => hostStarted = c;

        MultiplayerGame guest = await _t.Join(host);
        guest.Started += c => guestStarted = c;
        await Table.Eventually(() => hostStarted is not null && guest.Record.State == HandshakeState.InGame, "both started");

        Assert.NotEqual(host.MyColor, guest.MyColor);
        Assert.Equal(hostStarted, host.MyColor);
        Assert.Equal((BoardSize.Six, 5), (guest.Record.Size, guest.Record.KomiHalves));
        Assert.Equal(host.StateHash, guest.StateHash);
        Assert.Equal(HandshakeState.InGame, (await _t.HostStore.LoadAsync(host.GameId))!.Record.State);
        Assert.Equal(HandshakeState.InGame, (await _t.GuestStore.LoadAsync(guest.GameId))!.Record.State);
    }

    [Fact]
    public async Task WhitesFirstMove_ShowsOnBoth()
    {
        var (host, guest) = await _t.Connect();
        MultiplayerGame white = host.MyColor == PlayerColor.White ? host : guest;
        MultiplayerGame black = white == host ? guest : host;
        var seen = new List<string>();
        black.ActionApplied += (a, mine) => seen.Add($"{a.Action.Ptn}:{mine}");

        Assert.True((await white.SubmitAsync("e5")).IsSuccess);

        // The board moves before the event is raised (apply, save, then announce): wait for both.
        await Table.Eventually(() => black.AppliedTurn == 1 && seen.Count == 1, "black sees ply 1");
        Assert.Equal(white.StateHash, black.StateHash);
        Assert.Equal(["e5:False"], seen);
    }

    [Fact]
    public async Task IllegalLocalMove_PublishesNothing()
    {
        var (host, guest) = await _t.Connect();
        MultiplayerGame white = host.MyColor == PlayerColor.White ? host : guest;
        MultiplayerGame black = white == host ? guest : host;
        await Task.Delay(50);
        int before = EventFrames(_t.R1) + EventFrames(_t.R2);

        CommandResult capstone = await white.SubmitAsync("Ca1"); // no capstone on the first turn
        CommandResult notYours = await black.SubmitAsync("a1");
        CommandResult garbage = await white.SubmitAsync("zz9");

        Assert.False(capstone.IsSuccess);
        Assert.False(notYours.IsSuccess);
        Assert.Contains("not a move", garbage.ErrorMessage);
        await Task.Delay(50);
        Assert.Equal(before, EventFrames(_t.R1) + EventFrames(_t.R2));
        Assert.Equal(0, white.AppliedTurn);
        Assert.Empty((await _t.HostStore.LoadAsync(host.GameId))!.Applied);
    }

    [Fact]
    public async Task MovesEnteredOffline_AreSentOnReconnect()
    {
        var (host, guest) = await _t.Connect();
        MultiplayerGame white = host.MyColor == PlayerColor.White ? host : guest;
        MultiplayerGame black = white == host ? guest : host;
        _t.R1.Partition();
        _t.R2.Partition();
        await Table.Eventually(() => white.OwnLink == OwnLink.Offline, "white offline");

        Assert.True((await white.SubmitAsync("e5")).IsSuccess); // accepted locally, queued
        Assert.Equal(1, white.AppliedTurn);
        await Task.Delay(50);
        Assert.Equal(0, black.AppliedTurn);

        _t.R1.Heal();
        _t.R2.Heal();
        await _t.Advance(TimeSpan.FromSeconds(31));

        await Table.Eventually(() => black.AppliedTurn == 1, "the queued move arrives");
        Assert.Equal(white.StateHash, black.StateHash);
    }

    [Fact]
    public async Task PublishThenCrashBeforeSave_RepublishesTheSameTurn_PeerIgnoresTheDuplicate()
    {
        var (host, guest) = await _t.Connect();
        bool hostIsWhite = host.MyColor == PlayerColor.White;
        MultiplayerGame white = hostIsWhite ? host : guest;
        MultiplayerGame black = hostIsWhite ? guest : host;
        InMemoryGameStore whiteStore = hostIsWhite ? _t.HostStore : _t.GuestStore;
        MultiplayerOptions whiteOptions = hostIsWhite ? _t.HostOptions : _t.GuestOptions;
        var flags = new List<FlagEntry>();
        black.Flagged += flags.Add;
        whiteStore.FailNextWrite(op => op == nameof(IGameStore.MarkOutboxSentAsync)); // the crash: "sent" is never saved

        Assert.True((await white.SubmitAsync("e5")).IsSuccess);
        await Table.Eventually(() => black.AppliedTurn == 1, "black has ply 1");
        string moveId = (await whiteStore.LoadAsync(white.GameId))!.Outbox.Single(o => o.Turn == 1).EventId;
        await Table.Eventually(() => _t.R1.ReceivedFrames.Count(f => f.Contains(moveId)) == 1, "first publish");
        await white.DisposeAsync();

        MultiplayerGame? again = _t.Track((await MultiplayerGame.ResumeAsync(whiteOptions, white.GameId))!);

        Assert.NotNull(again);
        Assert.Equal(1, again.AppliedTurn);
        await Table.Eventually(() => _t.R1.ReceivedFrames.Count(f => f.Contains(moveId) && f.StartsWith("[\"EVENT\"")) == 2, "republished verbatim");
        await Task.Delay(50);
        Assert.Equal(1, black.AppliedTurn);
        Assert.Equal(again.StateHash, black.StateHash);
        Assert.Empty(flags);
        await Table.Eventually(() => whiteStore.LoadAsync(white.GameId).Result!.Outbox.Single(o => o.Turn == 1).Sent, "now saved as sent");
    }

    [Fact]
    public async Task MoveNoRelayAccepted_IsSentAfterARestart()
    {
        var (host, guest) = await _t.Connect();
        bool hostIsWhite = host.MyColor == PlayerColor.White;
        MultiplayerGame white = hostIsWhite ? host : guest;
        MultiplayerGame black = hostIsWhite ? guest : host;
        MultiplayerOptions whiteOptions = hostIsWhite ? _t.HostOptions : _t.GuestOptions;
        _t.R1.Refuse(_ => true, "blocked: maintenance");
        _t.R2.Refuse(_ => true, "blocked: maintenance");
        var outcomes = new List<PublishOutcome>();
        white.PublishStatus += (_, o) => outcomes.Add(o);

        Assert.True((await white.SubmitAsync("e5")).IsSuccess);
        await Table.Eventually(() => outcomes.Contains(PublishOutcome.Failed), "both relays refused");
        await white.DisposeAsync(); // the app is closed with the move unsent
        _t.R1.ClearRefusals();
        _t.R2.ClearRefusals();

        _t.Track((await MultiplayerGame.ResumeAsync(whiteOptions, white.GameId))!);

        await Table.Eventually(() => black.AppliedTurn == 1, "the stored move is delivered after the restart");
    }

    [Fact]
    public async Task BadInvite_FailsWithNoStoreWriteAndNoTraffic()
    {
        JoinOutcome outcome = await MultiplayerGame.JoinAsync(_t.GuestOptions, "TAK2_!!!");

        Assert.Null(outcome.Game);
        Assert.Contains("damaged", outcome.Error);
        Assert.Equal(0, _t.GuestStore.Writes);
        Assert.Equal(0, _t.R1.ConnectAttempts + _t.R2.ConnectAttempts);
    }

    [Fact]
    public async Task OwnInvite_Fails()
    {
        MultiplayerGame host = await _t.Host();
        await Table.Eventually(() => host.OwnLink == OwnLink.Online, "host online");
        int connects = _t.R1.ConnectAttempts;

        JoinOutcome outcome = await MultiplayerGame.JoinAsync(_t.HostOptions, host.Invite.ToCompactCode());

        Assert.Contains("your own invite", outcome.Error);
        Assert.Equal(connects, _t.R1.ConnectAttempts);
    }

    [Fact]
    public async Task HostRestartedBeforeTheGuestJoins_StillAcceptsTheJoin()
    {
        MultiplayerGame host = await _t.Host();
        await host.DisposeAsync();

        MultiplayerGame restored = _t.Track((await MultiplayerGame.ResumeAsync(_t.HostOptions, host.GameId))!);
        MultiplayerGame guest = await _t.Join(restored);

        await Table.Eventually(() => restored.Record.State == HandshakeState.InGame && guest.Record.State == HandshakeState.InGame, "both in game");
        Assert.NotEqual(restored.MyColor, guest.MyColor);
    }

    [Fact]
    public async Task SecondGuest_IsIgnoredAndSurfaced()
    {
        var (host, guest) = await _t.Connect();
        var notices = new List<string>();
        host.Notice += notices.Add;

        JoinOutcome second = await MultiplayerGame.JoinAsync(_t.Options(TestKeys.Create("late guest"), new InMemoryGameStore()), host.Invite.ToCompactCode());
        _t.Track(second.Game!);

        await Table.Eventually(() => notices.Any(n => n.Contains("game already taken")), "the host hears about it");
        await _t.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(HandshakeState.Joining, second.Game!.Record.State);
        Assert.Equal(guest.Record.MyPubKey, host.Record.OpponentPubKey);
    }

    [Fact]
    public async Task GuestJoin_IsRepublishedOnSchedule_UntilTheHostAnswers()
    {
        MultiplayerGame host = await _t.Host();
        await host.DisposeAsync(); // the host is away
        MultiplayerGame guest = await _t.Join(host);
        string joinId = (await _t.GuestStore.LoadAsync(guest.GameId))!.Outbox.Single().EventId;
        int Sends() => _t.R1.ReceivedFrames.Count(f => f.Contains(joinId) && f.StartsWith("[\"EVENT\""));
        await Table.Eventually(() => Sends() == 1, "the first JOIN");

        await _t.Advance(TimeSpan.FromSeconds(5));
        await Table.Eventually(() => Sends() == 2, "republished at 5 s");
        await _t.Advance(TimeSpan.FromSeconds(10));
        await Table.Eventually(() => Sends() == 3, "republished at 15 s");

        _t.Track((await MultiplayerGame.ResumeAsync(_t.HostOptions, host.GameId))!);
        await Table.Eventually(() => guest.Record.State == HandshakeState.InGame, "the host comes back and accepts");
        await _t.Advance(TimeSpan.FromSeconds(40));
        Assert.Equal(3, Sends());
    }

    [Fact]
    public async Task CancelBeforeAGuest_PublishesNothing_AndTheJoinNeverStarts()
    {
        MultiplayerGame host = await _t.Host();
        Assert.True(await host.CancelAsync());
        Assert.Equal(HandshakeState.Cancelled, (await _t.HostStore.LoadAsync(host.GameId))!.Record.State);

        MultiplayerGame guest = await _t.Join(host);
        await _t.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal(HandshakeState.Joining, guest.Record.State);
        Assert.Equal(0, _t.R1.StoredEvents.Count(e => e.Pubkey == host.Record.MyPubKey));
        Assert.False(await guest.CancelAsync() is false);
    }

    [Fact]
    public async Task Resign_EndsBothSides()
    {
        var (host, guest) = await _t.Connect();
        await _t.Play(host, guest, ["e5", "a1"]);
        GameResult? guestSaw = null;
        guest.Ended += r => guestSaw = r;

        Assert.True((await host.ResignAsync()).IsSuccess);

        Assert.Equal(GameEndReason.Resignation, host.Result!.Reason);
        await Table.Eventually(() => guestSaw is not null, "the guest sees the resignation");
        Assert.Equal(guest.MyColor, guestSaw!.Winner);
        Assert.False((await host.SubmitAsync("b1")).IsSuccess);
        Assert.False(await host.CancelAsync());
    }

    [Fact]
    public async Task SubmitBeforeTheGameStarts_IsRefused()
    {
        MultiplayerGame host = await _t.Host();

        CommandResult result = await host.SubmitAsync("a1");

        Assert.Equal("The game has not started yet.", result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptArrivingAfterTheFirstMove_StillPlaysThatMove()
    {
        // The guest's relays deliver the host's first move before its ACCEPT: the move waits for the handshake. That needs a
        // White host; the colour draw depends on the seeded nonces, so take the first seed that gives one.
        for (int seed = 100; seed < 140; seed++)
        {
            await using var t = new Table(seed);
            MultiplayerGame host = await t.Host();
            await Table.Eventually(() => host.OwnLink == OwnLink.Online, "host online");
            string hostKey = host.Record.MyPubKey;
            t.R1.WithholdWhere(e => e.Pubkey == hostKey);
            t.R2.WithholdWhere(e => e.Pubkey == hostKey);
            MultiplayerGame guest = await t.Join(host);
            await Table.Eventually(() => host.Record.State == HandshakeState.InGame, "host locked");
            if (host.MyColor != PlayerColor.White)
                continue;

            Assert.True((await host.SubmitAsync("e5")).IsSuccess);
            var outbox = (await t.HostStore.LoadAsync(host.GameId))!.Outbox;
            string acceptId = outbox.Single(o => o.ActionType == GameActionType.Accept).EventId;
            string moveId = outbox.Single(o => o.ActionType == GameActionType.Move).EventId;
            await Table.Eventually(() => t.R1.StoredEvents.Any(e => e.Id == moveId), "the move is on the relay");

            t.R1.Release(moveId);
            await Task.Delay(50);
            Assert.Equal(HandshakeState.Joining, guest.Record.State);
            t.R1.Release(acceptId);

            await Table.Eventually(() => guest.AppliedTurn == 1, $"the guest has the move (seed {seed})");
            Assert.Equal(host.StateHash, guest.StateHash);
            return;
        }
        Assert.Fail("no seed in 100..139 made the host White");
    }

    [Fact]
    public async Task ResignNoRelayAccepted_IsSentAfterARestart_AndTheFinalStateSurvives()
    {
        var (host, guest) = await _t.Connect();
        await _t.Play(host, guest, ["e5", "a1"]);
        string hash = host.StateHash;
        _t.R1.Refuse(_ => true, "blocked: maintenance");
        _t.R2.Refuse(_ => true, "blocked: maintenance");
        var outcomes = new List<PublishOutcome>();
        host.PublishStatus += (_, o) => outcomes.Add(o);

        Assert.True((await host.ResignAsync()).IsSuccess);
        await Table.Eventually(() => outcomes.Contains(PublishOutcome.Failed), "both relays refused the resign");
        await host.DisposeAsync(); // closed with the resign unsent
        _t.R1.ClearRefusals();
        _t.R2.ClearRefusals();

        MultiplayerGame again = _t.Track((await MultiplayerGame.ResumeAsync(_t.HostOptions, host.GameId))!);

        Assert.Equal(HandshakeState.Completed, again.Record.State);
        Assert.Equal(2, again.AppliedTurn);
        Assert.Equal(hash, again.StateHash);
        await Table.Eventually(() => guest.Result is not null, "the guest sees the resignation after the host's restart");
        Assert.Equal(GameEndReason.Resignation, guest.Result!.Reason);
    }

    [Fact]
    public async Task CancelledGame_PublishesNothingOnResume()
    {
        MultiplayerGame host = await _t.Host();
        await host.DisposeAsync();
        _t.R1.Partition();
        _t.R2.Partition();
        MultiplayerGame guest = await _t.Join(host);
        Assert.True(await guest.CancelAsync());
        await guest.DisposeAsync();
        _t.R1.Heal();
        _t.R2.Heal();

        _t.Track((await MultiplayerGame.ResumeAsync(_t.GuestOptions, guest.GameId))!);
        await _t.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal(0, EventFrames(_t.R1) + EventFrames(_t.R2));
    }

    [Fact]
    public async Task GuestCancelWhileOffline_TheJoinNeverGoesOut()
    {
        MultiplayerGame host = await _t.Host();
        await Table.Eventually(() => host.OwnLink == OwnLink.Online, "host online");
        _t.R1.Partition();
        _t.R2.Partition();
        MultiplayerGame guest = await _t.Join(host);

        Assert.True(await guest.CancelAsync());
        _t.R1.Heal();
        _t.R2.Heal();
        await _t.Advance(TimeSpan.FromSeconds(35));
        await Table.Eventually(() => guest.OwnLink == OwnLink.Online, "guest back online");
        await Task.Delay(50);

        Assert.DoesNotContain(_t.R1.StoredEvents.Concat(_t.R2.StoredEvents), e => e.Pubkey == guest.Record.MyPubKey);
        Assert.Equal(HandshakeState.Inviting, host.Record.State);
    }

    [Fact]
    public async Task StrangersFutureEvent_DoesNotHideTheOpponentsMoves()
    {
        // D-042 lets anyone write to the host's game tag; a far-future created_at must not push the host's `since` past real moves.
        var (host, guest) = await _t.Connect();
        SecretKey stranger = TestKeys.Create("stranger");
        long future = _t.Time.GetUtcNow().AddMinutes(14).ToUnixTimeSeconds();
        NostrEvent junk = NostrEvents.Sign(stranger, future, EnvelopeCodec.Kind,
            [new[] { "p", host.Record.MyPubKey }, new[] { "g", host.GameId.ToString("D") }], "junk", new byte[32]);
        string frame = new RelayMessage.Event(SubscriptionPlan.SubscriptionId(host.GameId), junk).Format();
        _t.R1.Inject(frame);
        _t.R2.Inject(frame);
        await Table.Eventually(() => host.DroppedEvents > 0, "the stranger's event is dropped");

        _t.R1.DropConnections();
        _t.R2.DropConnections();
        await _t.Advance(TimeSpan.FromSeconds(3));
        await Table.Eventually(() => host.OwnLink == OwnLink.Online && guest.OwnLink == OwnLink.Online, "reconnected");

        await _t.Play(host, guest, ["e5", "a1"]);
        Assert.Equal(host.StateHash, guest.StateHash);
    }

    [Fact]
    public async Task ObserverCopyingTheJoinTags_CannotTakeTheGame()
    {
        // D-043: while the host is away, someone who sees the guest's JOIN on a relay (game id, host key) joins too, later.
        MultiplayerGame host = await _t.Host();
        await host.DisposeAsync();
        MultiplayerGame guest = await _t.Join(host);
        await Table.Eventually(() => _t.R1.StoredEvents.Any(e => e.Pubkey == guest.Record.MyPubKey), "the JOIN is on the relay");
        NostrEvent seen = _t.R1.StoredEvents.First(e => e.Pubkey == guest.Record.MyPubKey);
        var guessed = new InviteCode(Guid.Parse(seen.Tags[1][1]), seen.Tags[0][1], BoardSize.Five, 0, [_t.R1.Url.ToString()],
            ColorSeed.Commit(new byte[32]));
        await _t.Advance(TimeSpan.FromSeconds(2));
        JoinOutcome observer = await MultiplayerGame.JoinAsync(_t.Options(TestKeys.Create("observer"), new InMemoryGameStore()), guessed.ToCompactCode());
        _t.Track(observer.Game!);
        await Table.Eventually(() => _t.R1.StoredEvents.Any(e => e.Pubkey == observer.Game!.Record.MyPubKey), "the observer's JOIN is on the relay");

        MultiplayerGame back = _t.Track((await MultiplayerGame.ResumeAsync(_t.HostOptions, host.GameId))!);

        await Table.Eventually(() => guest.Record.State == HandshakeState.InGame, "the real guest gets in");
        Assert.Equal(guest.Record.MyPubKey, back.Record.OpponentPubKey);
        Assert.Equal(HandshakeState.Joining, observer.Game!.Record.State);
    }

    [Fact]
    public async Task HostLockNotSaved_LeavesNoAcceptBehind()
    {
        MultiplayerGame host = await _t.Host();
        var notices = new List<string>();
        host.Notice += notices.Add;
        _t.HostStore.FailNextWrite(op => op == nameof(IGameStore.UpdateRecordAsync));

        await _t.Join(host);

        await Table.Eventually(() => notices.Any(n => n.StartsWith("Could not save the game", StringComparison.Ordinal)), "the host is told");
        StoredGame stored = (await _t.HostStore.LoadAsync(host.GameId))!;
        Assert.Equal(HandshakeState.Inviting, stored.Record.State);
        Assert.Empty(stored.Outbox);
        Assert.DoesNotContain(_t.R1.StoredEvents, e => e.Pubkey == host.Record.MyPubKey);
    }

    [Fact]
    public async Task ResignNotSaved_IsRefused_AndNothingIsSent()
    {
        var (host, guest) = await _t.Connect();
        await _t.Play(host, guest, ["e5", "a1"]);
        _t.HostStore.FailNextWrite(op => op == nameof(IGameStore.UpdateRecordAsync));
        int frames = EventFrames(_t.R1);

        CommandResult result = await host.ResignAsync();

        Assert.False(result.IsSuccess);
        Assert.StartsWith("Your resignation could not be saved", result.ErrorMessage);
        Assert.Equal(HandshakeState.InGame, (await _t.HostStore.LoadAsync(host.GameId))!.Record.State);
        Assert.Equal("This game could not be saved earlier. Reopen it to continue.", (await host.SubmitAsync("b1")).ErrorMessage);
        await Task.Delay(50);
        Assert.Equal(frames, EventFrames(_t.R1));
        Assert.Null(guest.Result);
    }

    [Fact]
    public async Task Dispose_FailsCommandsStillQueued()
    {
        var (host, guest) = await _t.Connect();
        MultiplayerGame white = host.MyColor == PlayerColor.White ? host : guest;
        InMemoryGameStore store = white == host ? _t.HostStore : _t.GuestStore;
        var disk = new TaskCompletionSource();
        store.HoldWrites(disk.Task);
        Task<CommandResult> first = white.SubmitAsync("e5"); // the worker waits on the slow save
        var queued = Enumerable.Range(0, 5).Select(_ => white.SubmitAsync("a1")).ToList();

        ValueTask disposing = white.DisposeAsync();

        await Task.WhenAny(Task.WhenAll(queued).ContinueWith(_ => { }, TaskScheduler.Default), Task.Delay(5000));
        Assert.True(queued.All(q => q.IsCompleted), "a queued command hangs after dispose");
        Assert.All(queued, q => Assert.IsType<ObjectDisposedException>(q.Exception!.InnerException));
        disk.SetResult();
        await disposing;
        Assert.True(first.IsCompleted);
    }
}

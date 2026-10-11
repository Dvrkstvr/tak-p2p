using TakEngine.Abstractions;
using TakEngine.Core.Session;
using TakEngine.Crypto;

namespace TakEngine.Core.Tests;

/// <summary>F-033: remote sessions sign every action as a <see cref="SignedGameAction"/> and verify the opponent's.</summary>
public class RemoteSessionTests
{
    [Fact]
    public void RemoteGame_TwoIndependentKeys_PlaysToRoadWin_WithEqualFinalHash()
    {
        var pair = new RemoteSessionPair();

        pair.Play(RemoteSessionPair.FiveByFiveRoadWin);

        Assert.Equal(GamePhase.Completed, pair.Alice.CurrentPhase);
        Assert.Equal(GamePhase.Completed, pair.Bob.CurrentPhase);
        Assert.Equal(PlayerColor.White, pair.Alice.Result!.Winner);
        Assert.Equal(pair.Alice.Result, pair.Bob.Result);
        Assert.Equal(pair.Alice.CurrentStateHash, pair.Bob.CurrentStateHash);
        Assert.Equal(9, pair.Bob.AppliedTurn);

        // Every emitted action is a MOVE for this game, numbered by ply (D-040), signed by its mover.
        Assert.Equal(Enumerable.Range(1, 9), pair.Emitted.Select(a => a.Turn));
        Assert.All(pair.Emitted, a => Assert.True(ActionDigest.Verify(a)));
        Assert.All(pair.Emitted, a => Assert.Equal(GameActionType.Move, a.ActionType));
        Assert.Equal(RemoteSessionPair.FiveByFiveRoadWin, pair.Emitted.Select(a => a.Ptn));
    }

    [Fact]
    public void FirstTurnSwap_RoundTrips()
    {
        var pair = new RemoteSessionPair();

        pair.Play(["e5", "a1"]);

        // White's first placement puts a Black flat on e5, Black's puts a White flat on a1, on both devices.
        foreach (var session in new[] { pair.Alice, pair.Bob })
        {
            var stacks = session.CurrentBoard.Stacks;
            Assert.Equal(PlayerColor.Black, stacks[Coord.FromAlgebraic("e5")].TopPiece!.Value.Color);
            Assert.Equal(PlayerColor.White, stacks[Coord.FromAlgebraic("a1")].TopPiece!.Value.Color);
            Assert.Equal(GamePhase.Playing, session.CurrentPhase);
            Assert.Equal(PlayerColor.White, session.CurrentBoard.ActivePlayer);
        }
        Assert.Equal(pair.Alice.CurrentStateHash, pair.Bob.CurrentStateHash);
    }

    public static TheoryData<string> FailureRows() =>
    [
        "wrong author", "bad signature", "wrong prev hash", "wrong turn number", "wrong game id",
        "wrong pv", "malformed ptn", "non-canonical ptn", "illegal move", "extra action data", "join action",
    ];

    [Theory]
    [MemberData(nameof(FailureRows))]
    public void RemoteAction_FailureRow_IsAViolation_AndChangesNothing(string row)
    {
        var pair = new RemoteSessionPair();
        SecretKey alice = pair.Keys.Alice;
        SecretKey stranger = TestKeys.Create(3);
        TakGameSession bob = pair.Bob;
        string genesis = bob.CurrentStateHash;
        byte[] aux = new byte[32];

        SignedGameAction Signed(SecretKey key, string ptn, int turn = 1, string? prev = null, Guid? game = null) =>
            ActionDigest.Sign(key, SignedGameAction.UnsignedMove(game ?? RemoteSessionPair.Game.Value, turn, key.PublicKey.ToHex(), prev ?? genesis, ptn), aux);

        SignedGameAction action = row switch
        {
            "wrong author" => Signed(stranger, "e5"),
            "bad signature" => Signed(alice, "e5") with { Signature = Signed(alice, "e4").Signature },
            "wrong prev hash" => Signed(alice, "e5", prev: new string('0', 64)),
            "wrong turn number" => Signed(alice, "e5", turn: 2),
            "wrong game id" => Signed(alice, "e5", game: Guid.Parse("00000000-0000-0000-0000-000000000001")),
            "wrong pv" => ActionDigest.Sign(alice, Signed(alice, "e5") with { Pv = 2 }, aux),
            "malformed ptn" => Signed(alice, "zz9"),
            "non-canonical ptn" => Signed(alice, "Fe5"),
            "illegal move" => Signed(alice, "Ce5"), // capstone on the first turn
            "extra action data" => ActionDigest.Sign(alice, SignedGameAction.Unsigned(GameActionType.Move, RemoteSessionPair.Game.Value, 1, alice.PublicKey.ToHex(), genesis,
                [new("ptn", ActionValue.Of("e5")), new("x", ActionValue.Of(1))]), aux),
            "join action" => ActionDigest.Sign(alice, SignedGameAction.Unsigned(GameActionType.Join, RemoteSessionPair.Game.Value, 0, alice.PublicKey.ToHex(), genesis), aux),
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        };

        AssertRejectedWithoutChange(bob, action);
    }

    [Fact]
    public void RemoteAction_OnLocalPlayersTurn_IsAViolation_AndChangesNothing()
    {
        var pair = new RemoteSessionPair();
        pair.Play(["e5"]); // now Black (Bob) to move

        // Alice signs a correctly chained, correctly numbered ply 2, but ply 2 belongs to Bob.
        var action = ActionDigest.Sign(pair.Keys.Alice,
            SignedGameAction.UnsignedMove(RemoteSessionPair.Game.Value, 2, pair.Keys.Alice.PublicKey.ToHex(), pair.Bob.CurrentStateHash, "a1"), new byte[32]);

        AssertRejectedWithoutChange(pair.Bob, action);
    }

    [Fact]
    public void Resign_IsSignedAtTheAppliedTurn_AndEndsBothGames()
    {
        var pair = new RemoteSessionPair();
        pair.Play(["e5", "a1", "b1"]);

        Assert.True(pair.Bob.Resign().IsSuccess);
        SignedGameAction resign = pair.Emitted[^1];
        Assert.Equal(GameActionType.Resign, resign.ActionType);
        Assert.Equal(3, resign.Turn);
        Assert.Empty(resign.ActionData);

        Assert.True(pair.Alice.ProcessRemoteAction(resign).IsSuccess);
        Assert.Equal(PlayerColor.White, pair.Alice.Result!.Winner);
        Assert.Equal(pair.Alice.Result, pair.Bob.Result);
    }

    [Fact]
    public void Restore_ContinuesTheSameChain()
    {
        var pair = new RemoteSessionPair();
        pair.Play(RemoteSessionPair.FiveByFiveRoadWin.Take(4));

        TakGameSession restoredBob = TakGameSession.Restore(
            RemoteSessionPair.Game, PlayerColor.Black, pair.Keys.Bob, pair.Keys.Alice.PublicKey, RemoteSessionPair.SeededAux("bob again"),
            pair.Bob.CurrentTps, pair.Bob.CurrentStateHash, pair.Bob.AppliedTurn);

        Assert.Equal(pair.Bob.CurrentStateHash, restoredBob.CurrentStateHash);
        Assert.Equal(4, restoredBob.AppliedTurn);
        Assert.Equal(PlayerColor.White, restoredBob.CurrentBoard.ActivePlayer);

        foreach (string ptn in RemoteSessionPair.FiveByFiveRoadWin.Skip(4))
        {
            bool aliceMoves = pair.Alice.CurrentBoard.ActivePlayer == PlayerColor.White;
            TakGameSession mover = aliceMoves ? pair.Alice : restoredBob;
            TakGameSession receiver = aliceMoves ? restoredBob : pair.Alice;
            Assert.True(receiver.ProcessRemoteAction(RemoteSessionPair.Submit(mover, ptn)).IsSuccess);
        }

        Assert.Equal(GamePhase.Completed, restoredBob.CurrentPhase);
        Assert.Equal(pair.Alice.CurrentStateHash, restoredBob.CurrentStateHash);
    }

    [Fact]
    public void Spectator_VerifiesAPlayersChain()
    {
        var pair = new RemoteSessionPair();
        List<string> hashes = pair.Play(RemoteSessionPair.FiveByFiveRoadWin);
        using var spectator = new SpectatorGameSession(RemoteSessionPair.Game.Value, BoardSize.Five,
            pair.Keys.Alice.PublicKey.ToHex(), pair.Keys.Bob.PublicKey.ToHex());

        for (int i = 0; i < pair.Emitted.Count; i++)
        {
            Assert.True(spectator.IngestEnvelope(new BroadcastEnvelope(pair.Emitted[i], DateTime.UnixEpoch, hashes[i])));
            Assert.Equal(hashes[i], spectator.LastStateHash);
        }
        Assert.Equal(pair.Alice.Result, spectator.CurrentBoard.Result);
    }

    private static void AssertRejectedWithoutChange(TakGameSession session, SignedGameAction action)
    {
        string hashBefore = session.CurrentStateHash;
        string tpsBefore = session.CurrentTps;
        int turnBefore = session.AppliedTurn;
        ProtocolViolationException? violation = null;
        session.OnProtocolViolationDetected += ex => violation = ex;

        CommandResult result = session.ProcessRemoteAction(action);

        Assert.False(result.IsSuccess);
        Assert.NotNull(violation);
        Assert.Equal(hashBefore, session.CurrentStateHash);
        Assert.Equal(tpsBefore, session.CurrentTps);
        Assert.Equal(turnBefore, session.AppliedTurn);
    }
}

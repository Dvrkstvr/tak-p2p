using TakEngine.Abstractions;
using TakEngine.Core.Session;
using TakEngine.Crypto;
using TakEngine.Testing;

namespace TakEngine.Multiplayer.Tests;

/// <summary>F-035: scope.md "Turn exchange", row by row. The receiver plays Black; White is the opponent.</summary>
public class TurnExchangeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<string> Script = ScriptedGames.FiveByFiveRoadWin;

    /// <summary>Black's side of the scripted game: plays its own scripted move whenever it is Black's turn.</summary>
    internal sealed class Receiver
    {
        private readonly IReadOnlyList<string> _script;

        public Receiver(IReadOnlyList<string> script, TakGameSession? session = null, IEnumerable<SignedGameAction>? history = null)
        {
            _script = script;
            Session = session ?? SignedScript.Session(PlayerColor.Black);
            Session.OnLocalActionSigned += a => LastOwn = a;
            Exchange = new TurnExchange(Session, TestKeys.Black.PublicKey, history);
        }

        public TakGameSession Session { get; }

        public TurnExchange Exchange { get; }

        public SignedGameAction? LastOwn { get; private set; }

        public List<AppliedAction> Applied { get; } = [];

        public List<ExchangeFlag> Flags { get; } = [];

        public ExchangeStep Deliver(SignedGameAction action, DateTimeOffset? now = null)
        {
            ExchangeStep step = Exchange.Receive(action, SignedScript.EventId(action), now ?? T0);
            Record(step);
            PlayOwnMoves();
            return step;
        }

        public void PlayOwnMoves()
        {
            while (!Exchange.IsCompleted && Session.CurrentBoard.ActivePlayer == PlayerColor.Black && Exchange.AppliedTurn < _script.Count)
            {
                Assert.True(LocalMoves.Submit(Session, _script[Exchange.AppliedTurn]).IsSuccess);
                Record(Exchange.RecordLocal(LastOwn!, SignedScript.EventId(LastOwn!)));
            }
        }

        private void Record(ExchangeStep step)
        {
            Applied.AddRange(step.Applied);
            Flags.AddRange(step.Flags);
        }
    }

    private static (IReadOnlyList<SignedGameAction> Actions, string FinalHash) Game() =>
        SignedScript.Play(Script) is var g ? (g.Actions, g.FinalHash) : default;

    private static SignedGameAction WhiteMove(int turn, string prev, string ptn, byte aux = 0) =>
        SignedScript.Sign(TestKeys.White, SignedGameAction.UnsignedMove(SignedScript.Game, turn, TestKeys.White.PublicKey.ToHex(), prev, ptn), aux);

    [Fact]
    public void OwnEcho_IsIgnored()
    {
        var (actions, _) = Game();
        var r = new Receiver(Script);
        r.Deliver(actions[0]);
        string hash = r.Session.CurrentStateHash;

        ExchangeStep step = r.Exchange.Receive(actions[1], "echo", T0);

        Assert.Equal(ReceiveOutcome.OwnEcho, step.Outcome);
        Assert.Equal(hash, r.Session.CurrentStateHash);
    }

    [Fact]
    public void NextTurn_IsApplied()
    {
        var (actions, _) = Game();
        var r = new Receiver(Script);

        ExchangeStep step = r.Deliver(actions[0]);

        Assert.Equal(ReceiveOutcome.Applied, step.Outcome);
        Assert.Equal(1, Assert.Single(step.Applied).Action.Turn);
        Assert.Equal(step.Applied[0].StateHashAfter, actions[1].PrevStateHash);
        Assert.Equal(2, r.Exchange.AppliedTurn); // Black answered with its own move
        Assert.Empty(r.Flags);
    }

    [Fact]
    public void LaterTurn_IsBuffered_ThenAppliedInOrder()
    {
        var (actions, _) = Game();
        var r = new Receiver(Script);

        ExchangeStep early = r.Deliver(actions[2]); // turn 3 before turn 1

        Assert.Equal(ReceiveOutcome.Buffered, early.Outcome);
        Assert.Equal(0, r.Exchange.AppliedTurn);
        Assert.Equal(1, r.Exchange.WaitingFor);

        r.Deliver(actions[0]);

        Assert.Equal(4, r.Exchange.AppliedTurn); // 1 applied, Black played 2, which unblocked 3, Black played 4
        Assert.Equal([1, 2, 3, 4], r.Applied.Select(a => a.Action.Turn));
        Assert.Null(r.Exchange.WaitingFor);
        Assert.Equal(0, r.Exchange.BufferedCount);
    }

    [Fact]
    public void GapStillOpenAfter20s_AsksForCatchUp_AndAgainEvery20s()
    {
        var (actions, _) = Game();
        var r = new Receiver(Script);
        Assert.False(r.Exchange.Tick(T0.AddHours(1))); // no gap, no request
        Assert.Null(r.Exchange.NextDeadline);

        r.Deliver(actions[2], T0);
        r.Deliver(actions[4], T0.AddSeconds(10)); // a second held turn does not restart the clock

        Assert.Equal(T0.AddSeconds(20), r.Exchange.NextDeadline);
        Assert.False(r.Exchange.Tick(T0.AddSeconds(19)));
        Assert.True(r.Exchange.Tick(T0.AddSeconds(20)));
        Assert.False(r.Exchange.Tick(T0.AddSeconds(21)));
        Assert.True(r.Exchange.Tick(T0.AddSeconds(40)));

        r.Deliver(actions[0], T0.AddSeconds(41));

        Assert.Null(r.Exchange.NextDeadline);
        Assert.False(r.Exchange.Tick(T0.AddMinutes(5)));
    }

    [Fact]
    public void GapThatIsOnlyPartlyClosed_StaysOpen()
    {
        var (actions, _) = Game();
        var r = new Receiver(Script);
        r.Deliver(actions[4], T0); // turn 5; turns 1 and 3 missing

        r.Deliver(actions[0], T0.AddSeconds(5));

        Assert.Equal(2, r.Exchange.AppliedTurn);
        Assert.Equal(3, r.Exchange.WaitingFor);
        Assert.NotNull(r.Exchange.NextDeadline);
        Assert.True(r.Exchange.Tick(T0.AddSeconds(20)));
    }

    [Fact]
    public void ReceiverPlayingWhite_TakesBlacksTurnsOnly()
    {
        var (actions, _) = Game();
        var white = SignedScript.Session(PlayerColor.White);
        var exchange = new TurnExchange(white, TestKeys.White.PublicKey);
        var blackOnTurnOne = SignedScript.Sign(TestKeys.Black,
            SignedGameAction.UnsignedMove(SignedScript.Game, 1, TestKeys.Black.PublicKey.ToHex(), white.CurrentStateHash, "e5"));

        Assert.Equal(ReceiveOutcome.Flagged, exchange.Receive(blackOnTurnOne, "x", T0).Outcome);

        Assert.True(LocalMoves.Submit(white, "e5").IsSuccess);
        Assert.Equal(ReceiveOutcome.Applied, exchange.Receive(actions[1], "y", T0).Outcome);
        Assert.Equal(2, exchange.AppliedTurn);
    }

    [Fact]
    public void DuplicateOfTheLatestTurn_IsIgnoredToo()
    {
        var (actions, _) = Game();
        var r = new Receiver(["e5"]); // Black has no move of its own to answer with
        r.Deliver(actions[0]);
        Assert.Equal(1, r.Exchange.AppliedTurn);

        Assert.Equal(ReceiveOutcome.Duplicate, r.Deliver(actions[0]).Outcome);
        Assert.Empty(r.Flags);
    }

    [Fact]
    public void NullInputs_AreRefused()
    {
        var session = SignedScript.Session(PlayerColor.Black);
        var exchange = new TurnExchange(session, TestKeys.Black.PublicKey);

        Assert.Throws<ArgumentNullException>(() => new TurnExchange(null!, TestKeys.Black.PublicKey));
        Assert.Throws<ArgumentNullException>(() => new TurnExchange(session, null!));
        Assert.Throws<ArgumentNullException>(() => exchange.Receive(null!, "x", T0));
        Assert.Throws<ArgumentNullException>(() => exchange.RecordLocal(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => IncomingClassifier.Classify(null!, true));
        Assert.Throws<ArgumentNullException>(() => LinkStatus.Own(null!));
    }

    [Fact]
    public void Buffer_HoldsAtMost64_DroppingTheFurthestAhead()
    {
        var r = new Receiver(Script);
        string junk = new('0', 64);
        for (int i = 0; i < 64; i++)
            r.Deliver(WhiteMove(3 + 2 * i, junk, "a1")); // turns 3..129
        Assert.Equal(64, r.Exchange.BufferedCount);

        r.Deliver(WhiteMove(131, junk, "a1")); // further than all: dropped
        Assert.Equal(64, r.Exchange.BufferedCount);
        Assert.Equal(ReceiveOutcome.Duplicate, r.Exchange.Receive(WhiteMove(129, junk, "a1"), "x", T0).Outcome);
        Assert.Equal(ReceiveOutcome.Buffered, r.Exchange.Receive(WhiteMove(131, junk, "a1"), "x", T0).Outcome);

        var (actions, _) = Game();
        r.Deliver(actions[2]); // turn 3 again: already held, same digest? no, this one differs from the junk turn 3
        Assert.Equal("conflicting move for turn 3", r.Flags[^1].Reason);
    }

    [Fact]
    public void Buffer_WhenFull_EvictsTheFurthestForANearerTurn()
    {
        var r = new Receiver(Script);
        string junk = new('0', 64);
        for (int i = 0; i < 64; i++)
            r.Deliver(WhiteMove(5 + 2 * i, junk, "a1")); // turns 5..131

        r.Deliver(WhiteMove(3, junk, "a1"));

        Assert.Equal(64, r.Exchange.BufferedCount);
        Assert.Equal(ReceiveOutcome.Duplicate, r.Exchange.Receive(WhiteMove(3, junk, "a1"), "x", T0).Outcome);
        Assert.Equal(ReceiveOutcome.Buffered, r.Exchange.Receive(WhiteMove(131, junk, "a1"), "x", T0).Outcome); // was evicted, re-held
        Assert.Equal(64, r.Exchange.BufferedCount);
    }

    [Fact]
    public void DuplicateOfAppliedTurn_IsIgnoredSilently_EvenWhenReSigned()
    {
        var (actions, _) = Game();
        var r = new Receiver(Script);
        r.Deliver(actions[0]);
        string hash = r.Session.CurrentStateHash;
        SignedGameAction resigned = WhiteMove(1, actions[0].PrevStateHash, "e5", aux: 7);
        Assert.NotEqual(actions[0].Signature, resigned.Signature);

        Assert.Equal(ReceiveOutcome.Duplicate, r.Deliver(actions[0]).Outcome);
        Assert.Equal(ReceiveOutcome.Duplicate, r.Deliver(resigned).Outcome);

        Assert.Equal(hash, r.Session.CurrentStateHash);
        Assert.Empty(r.Flags);
    }

    [Fact]
    public void DifferentMoveForAnAppliedTurn_IsFlagged_FirstSeenWins()
    {
        var (actions, _) = Game();
        var r = new Receiver(Script);
        r.Deliver(actions[0]);
        string hash = r.Session.CurrentStateHash;

        ExchangeStep step = r.Deliver(WhiteMove(1, actions[0].PrevStateHash, "d5"));

        Assert.Equal(ReceiveOutcome.Flagged, step.Outcome);
        Assert.Equal("conflicting move for turn 1", Assert.Single(step.Flags).Reason);
        Assert.Equal(hash, r.Session.CurrentStateHash);
    }

    [Fact]
    public void MoveForMyTurn_IsAViolation()
    {
        var (actions, _) = Game();
        var r = new Receiver(Script);
        r.Deliver(actions[0]);
        string hash = r.Session.CurrentStateHash;

        ExchangeStep mine = r.Deliver(WhiteMove(2, actions[1].PrevStateHash, "a1")); // turn 2 is Black's
        ExchangeStep zero = r.Deliver(WhiteMove(0, actions[0].PrevStateHash, "a1"));
        ExchangeStep ahead = r.Deliver(WhiteMove(6, actions[1].PrevStateHash, "a1"));

        Assert.All([mine, zero, ahead], s => Assert.StartsWith("illegal move from opponent: turn", Assert.Single(s.Flags).Reason));
        Assert.Equal(hash, r.Session.CurrentStateHash);
        Assert.Equal(0, r.Exchange.BufferedCount);
    }

    [Fact]
    public void WrongPrevHash_IsFlaggedAsAFork()
    {
        var r = new Receiver(Script);
        string genesis = r.Session.CurrentStateHash;

        ExchangeStep step = r.Deliver(WhiteMove(1, new string('f', 64), "e5"));

        Assert.Equal("hash chain mismatch at turn 1", Assert.Single(step.Flags).Reason);
        Assert.Equal(genesis, r.Session.CurrentStateHash);
        Assert.Equal(0, r.Exchange.AppliedTurn);
    }

    [Fact]
    public void IllegalMove_IsFlaggedWithTheEngineReason_AndAValidResendIsStillAccepted()
    {
        var (actions, _) = Game();
        var r = new Receiver(Script);
        string genesis = r.Session.CurrentStateHash;

        ExchangeStep step = r.Deliver(WhiteMove(1, genesis, "Ca1")); // no capstone on the first turn

        Assert.StartsWith("illegal move from opponent: ", Assert.Single(step.Flags).Reason);
        Assert.Equal(genesis, r.Session.CurrentStateHash);

        Assert.Equal(ReceiveOutcome.Applied, r.Deliver(actions[0]).Outcome);
    }

    [Fact]
    public void BufferedMoveThatFailsWhenItsTurnComes_IsFlagged()
    {
        var (actions, _) = Game();
        var r = new Receiver(Script);
        r.Deliver(WhiteMove(3, new string('f', 64), "b1"));

        r.Deliver(actions[0]);

        Assert.Equal(2, r.Exchange.AppliedTurn);
        Assert.Equal("hash chain mismatch at turn 3", Assert.Single(r.Flags).Reason);
        Assert.Equal(ReceiveOutcome.Applied, r.Deliver(actions[2]).Outcome);
    }

    [Fact]
    public void EventsAfterCompleted_AreIgnored()
    {
        var (actions, finalHash) = Game();
        var r = new Receiver(Script);
        r.Deliver(WhiteMove(11, new string('0', 64), "a2")); // held beyond the end of the game
        foreach (SignedGameAction a in actions.Where(a => a.Turn % 2 == 1))
            r.Deliver(a);
        Assert.True(r.Exchange.IsCompleted);
        Assert.Equal(finalHash, r.Session.CurrentStateHash);
        Assert.Equal(0, r.Exchange.BufferedCount);
        Assert.Null(r.Exchange.WaitingFor);
        Assert.Null(r.Exchange.NextDeadline);

        ExchangeStep late = r.Deliver(WhiteMove(11, finalHash, "a2"));
        ExchangeStep conflicting = r.Deliver(WhiteMove(9, actions[8].PrevStateHash, "a2"));

        Assert.Equal(ReceiveOutcome.Late, late.Outcome);
        Assert.Equal(ReceiveOutcome.Late, conflicting.Outcome);
        Assert.Equal(finalHash, r.Session.CurrentStateHash);
    }

    [Fact]
    public void LastMove_EndsTheGame_WithTheRoadWin()
    {
        var (actions, _) = Game();
        var r = new Receiver(Script);
        ExchangeStep? last = null;
        foreach (SignedGameAction a in actions.Where(a => a.Turn % 2 == 1))
            last = r.Deliver(a);

        Assert.NotNull(last!.Ended);
        Assert.Equal(PlayerColor.White, last.Ended.Winner);
        Assert.Equal(GameEndReason.Road, last.Ended.Reason);
    }

    [Fact]
    public void Resign_OneTurnAhead_WaitsForTheResignersMove_AndBothSidesEndOnTheSameHash()
    {
        // White played turn 3, then resigned (RESIGN turn 3). Black, at turn 2, gets the RESIGN first: turn 3 is still in flight.
        var (actions, _) = Game();
        string whiteFinalHash = SignedScript.Play(Script.Take(3).ToList()).FinalHash; // White's hash when it resigned
        var r = new Receiver(Script);
        r.Deliver(actions[0]); // applied 2
        var resign = SignedScript.Sign(TestKeys.White, SignedGameAction.Unsigned(GameActionType.Resign, SignedScript.Game, 3, TestKeys.White.PublicKey.ToHex(), "whatever"));

        ExchangeStep held = r.Deliver(resign);

        Assert.Equal(ReceiveOutcome.Buffered, held.Outcome);
        Assert.False(r.Exchange.IsCompleted);
        Assert.Equal(3, r.Exchange.WaitingFor);
        Assert.Equal(T0 + TurnExchange.GapTimeout, r.Exchange.NextDeadline); // a missing move 3 still gets a catch-up

        ExchangeStep step = r.Deliver(actions[2]);

        Assert.Equal(ReceiveOutcome.Applied, step.Outcome);
        Assert.Equal(3, r.Exchange.AppliedTurn);
        Assert.Equal(PlayerColor.Black, step.Ended!.Winner);
        Assert.Equal(GameEndReason.Resignation, step.Ended.Reason);
        Assert.Equal(whiteFinalHash, r.Session.CurrentStateHash);
        Assert.Null(r.Exchange.NextDeadline);
        Assert.Equal(ReceiveOutcome.Late, r.Deliver(actions[4]).Outcome);
    }

    [Fact]
    public void Resign_ForTheAppliedTurn_EndsAtOnce_AndALaterMoveIsIgnored()
    {
        var (actions, _) = Game();
        var r = new Receiver(Script);
        r.Deliver(actions[0]); // applied 2
        r.Deliver(actions[4]); // turn 5 held
        string hash = r.Session.CurrentStateHash;
        var resign = SignedScript.Sign(TestKeys.White, SignedGameAction.Unsigned(GameActionType.Resign, SignedScript.Game, 2, TestKeys.White.PublicKey.ToHex(), "whatever"));

        ExchangeStep step = r.Deliver(resign);

        Assert.Equal(0, r.Exchange.BufferedCount);
        Assert.Null(r.Exchange.NextDeadline);
        Assert.Equal(ReceiveOutcome.Applied, step.Outcome);
        Assert.Equal(PlayerColor.Black, step.Ended!.Winner);
        Assert.Equal(hash, r.Session.CurrentStateHash);
        Assert.Equal(ReceiveOutcome.Late, r.Deliver(actions[2]).Outcome);
    }

    [Fact]
    public void ResignAheadOfMissingMoves_WaitsForThem()
    {
        var (actions, _) = Game();
        var r = new Receiver(Script);
        var resign = SignedScript.Sign(TestKeys.White, SignedGameAction.Unsigned(GameActionType.Resign, SignedScript.Game, 3, TestKeys.White.PublicKey.ToHex(), "x"));

        Assert.Equal(ReceiveOutcome.Buffered, r.Deliver(resign).Outcome);
        Assert.False(r.Exchange.IsCompleted);
        Assert.Equal(1, r.Exchange.WaitingFor);

        r.Deliver(actions[0]); // turn 1, and Black plays turn 2

        Assert.False(r.Exchange.IsCompleted); // the resigner's turn 3 is still missing
        Assert.Equal(3, r.Exchange.WaitingFor);

        r.Deliver(actions[2]);

        Assert.True(r.Exchange.IsCompleted);
        Assert.Equal(PlayerColor.Black, r.Session.Result!.Winner);
        Assert.Equal(SignedScript.Play(Script.Take(3).ToList()).FinalHash, r.Session.CurrentStateHash);
    }

    [Fact]
    public void JoinOrAccept_IsNotAGameAction()
    {
        var r = new Receiver(Script);
        var join = SignedScript.Sign(TestKeys.White, SignedGameAction.Unsigned(GameActionType.Join, SignedScript.Game, 0, TestKeys.White.PublicKey.ToHex(), "x"));

        Assert.Equal(ReceiveOutcome.Ignored, r.Deliver(join).Outcome);
    }

    [Fact]
    public void AfterARestart_AMoveAlreadyInTheHistory_IsADuplicate()
    {
        var (actions, _) = Game();
        var first = new Receiver(Script);
        first.Deliver(actions[0]);
        first.Deliver(actions[2]);
        var restored = TakGameSession.Restore(new GameId(SignedScript.Game), PlayerColor.Black, TestKeys.Black, TestKeys.White.PublicKey,
            () => new byte[32], first.Session.CurrentTps, first.Session.CurrentStateHash, first.Exchange.AppliedTurn);

        var again = new Receiver(Script, restored, first.Applied.Select(a => a.Action));

        Assert.Equal(ReceiveOutcome.Duplicate, again.Deliver(actions[0]).Outcome);
        Assert.Equal(ReceiveOutcome.Duplicate, again.Deliver(actions[2]).Outcome);
        Assert.Equal(ReceiveOutcome.Applied, again.Deliver(actions[4]).Outcome);
        Assert.Empty(again.Flags);
    }
}

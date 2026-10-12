using TakEngine.Transport.Envelope;
using TakEngine.Transport.Relay;

namespace TakEngine.Multiplayer.Tests;

/// <summary>F-038 groundwork: rows 1-4 from the codec verdict; rows 5-7 texts used by the turn exchange.</summary>
public class IncomingClassifierTests
{
    [Theory]
    [InlineData(DecodeVerdict.NotForUs)]
    [InlineData(DecodeVerdict.Row1BadIdOrSig)]
    [InlineData(DecodeVerdict.Row2WrongAuthor)]
    public void NoiseAndStrangers_AreDroppedAndCountedOnly(DecodeVerdict verdict)
    {
        Classification c = IncomingClassifier.Classify(new DecodeResult(verdict, Detail: "x"), opponentKnown: true);

        Assert.Equal(IncomingDecision.Drop, c.Decision);
        Assert.Null(c.Reason);
    }

    [Fact]
    public void Undecryptable_FromTheOpponent_IsFlagged() =>
        Assert.Equal(
            new Classification(IncomingDecision.DropAndFlag, "undecryptable message from opponent"),
            IncomingClassifier.Classify(new DecodeResult(DecodeVerdict.Row3Undecryptable, Detail: "bad mac"), true));

    [Fact]
    public void BadPayload_IsFlaggedWithItsDetail() =>
        Assert.Equal(
            new Classification(IncomingDecision.DropAndFlag, "bad message from opponent: inner signature does not verify"),
            IncomingClassifier.Classify(new DecodeResult(DecodeVerdict.Row4BadPayload, Detail: "inner signature does not verify"), true));

    [Fact]
    public void BadPayloadWithoutDetail_StillSaysSomething() =>
        Assert.Equal(
            "bad message from opponent: unreadable payload",
            IncomingClassifier.Classify(new DecodeResult(DecodeVerdict.Row4BadPayload), true).Reason);

    [Fact]
    public void IncompatibleVersion_IsFlaggedApartFromTampering() =>
        Assert.Equal(
            new Classification(IncomingDecision.DropAndFlag, "opponent runs an incompatible version"),
            IncomingClassifier.Classify(new DecodeResult(DecodeVerdict.IncompatibleVersion), true));

    [Theory]
    [InlineData(DecodeVerdict.Row3Undecryptable)]
    [InlineData(DecodeVerdict.Row4BadPayload)]
    [InlineData(DecodeVerdict.IncompatibleVersion)]
    public void WhileInviting_ABadEventIsOnlyCounted(DecodeVerdict verdict) =>
        Assert.Equal(IncomingDecision.Drop, IncomingClassifier.Classify(new DecodeResult(verdict), opponentKnown: false).Decision);

    [Fact]
    public void Accepted_IsHandedOn() =>
        Assert.Equal(IncomingDecision.Accept, IncomingClassifier.Classify(new DecodeResult(DecodeVerdict.Accepted), false).Decision);

    [Fact]
    public void RowFiveToSevenTexts_MatchTheSpec()
    {
        Assert.Equal("hash chain mismatch at turn 7", IncomingClassifier.HashChainMismatch(7));
        Assert.Equal("illegal move from opponent: no stack there", IncomingClassifier.IllegalMove("no stack there"));
        Assert.Equal("conflicting move for turn 3", IncomingClassifier.ConflictingMove(3));
    }
}

public class LinkStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Opponent_IsUnknownActiveThenQuiet()
    {
        Assert.Equal(OpponentLink.Unknown, LinkStatus.Opponent(null, Now));
        Assert.Equal(OpponentLink.Active, LinkStatus.Opponent(Now, Now));
        Assert.Equal(OpponentLink.Active, LinkStatus.Opponent(Now.AddMinutes(-10).AddTicks(1), Now));
        Assert.Equal(OpponentLink.Quiet, LinkStatus.Opponent(Now.AddMinutes(-10), Now));
        Assert.Equal(OpponentLink.Quiet, LinkStatus.Opponent(Now.AddDays(-2), Now));
    }

    [Theory]
    [InlineData(OwnLink.Online, RelayLinkState.CaughtUp, RelayLinkState.Subscribed)]
    [InlineData(OwnLink.Degraded, RelayLinkState.CaughtUp, RelayLinkState.Backoff)]
    [InlineData(OwnLink.Degraded, RelayLinkState.Connecting, RelayLinkState.Subscribed)]
    [InlineData(OwnLink.Offline, RelayLinkState.Connecting, RelayLinkState.Backoff)]
    [InlineData(OwnLink.Offline, RelayLinkState.Disconnected, RelayLinkState.Disconnected)]
    public void Own_FollowsTheRelays(OwnLink expected, RelayLinkState a, RelayLinkState b) =>
        Assert.Equal(expected, LinkStatus.Own([a, b]));

    [Fact]
    public void Own_WithNoRelays_IsOffline() => Assert.Equal(OwnLink.Offline, LinkStatus.Own([]));
}

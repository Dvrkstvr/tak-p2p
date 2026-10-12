using TakApp.Cli.Online;
using TakEngine.Abstractions;
using TakEngine.Transport.Relay;

namespace TakApp.Cli.Tests;

/// <summary>The <c>--plain</c> contract, line by line (pipeline/playbook.md "Verify method"). Agents and the two-process test parse
/// these lines; a change here is a contract change.</summary>
public class PlainOutputTests
{
    private static readonly Uri R1 = new("wss://nos.lol"), R2 = new("wss://relay.damus.io");
    private const string Hash = "4f2a0c1d9e8b7a6f5e4d3c2b1a0f9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b3a29";

    private static string One(Action<PlainOutput> write)
    {
        var text = new StringWriter();
        write(new PlainOutput(text));
        return Assert.Single(text.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Invite() => Assert.Equal("INVITE TAK2_abc", One(o => o.Invite("TAK2_abc")));

    [Theory]
    [InlineData(RelayLinkState.Connecting, "RELAY wss://nos.lol connecting")]
    [InlineData(RelayLinkState.CaughtUp, "RELAY wss://nos.lol caught-up")]
    [InlineData(RelayLinkState.Backoff, "RELAY wss://nos.lol backoff")]
    public void Relay(RelayLinkState state, string line) => Assert.Equal(line, One(o => o.Relay(R1, state)));

    [Fact]
    public void Waiting() => Assert.Equal("WAITING your move turn 3", One(o => o.Waiting("your  move\nturn 3")));

    [Fact]
    public void Sent_ListsEachRelay_RefusalsWithTheirReason()
    {
        var perRelay = new Dictionary<Uri, RelayPublishState>
        {
            [R2] = new(RelayPublishStatus.Refused, "rate-limited: slow down"),
            [R1] = new(RelayPublishStatus.Sent),
        };

        Assert.Equal("SENT turn 5 wss://nos.lol:ok wss://relay.damus.io:refused(rate-limited:_slow_down)", One(o => o.Sent(5, perRelay)));
    }

    [Fact]
    public void Sent_PendingRelay() =>
        Assert.Equal("SENT turn 1 wss://nos.lol:ok wss://relay.damus.io:pending(no_answer)", One(o => o.Sent(1,
            new Dictionary<Uri, RelayPublishState> { [R1] = new(RelayPublishStatus.Sent), [R2] = new(RelayPublishStatus.Pending, "no answer") })));

    [Fact]
    public void Applied() => Assert.Equal($"APPLIED turn 2 a1 {Hash}", One(o => o.Applied(2, "a1", Hash, "x1/5 x5/x5/x5/x5 1 1", 0, false)));

    [Fact]
    public void Rejected() => Assert.Equal("REJECTED turn 3 not your turn", One(o => o.Rejected(3, "not your turn")));

    [Fact]
    public void Flag() => Assert.Equal("FLAG turn 4 hash chain mismatch at turn 4", One(o => o.Flag(4, "hash chain mismatch at turn 4")));

    [Fact]
    public void Notice() => Assert.Equal("NOTICE ignored join from npub1x: game already taken", One(o => o.Notice("ignored join from npub1x: game already taken")));

    [Theory]
    [InlineData(false, PlayerColor.White, GameEndReason.Road, "white-road")]
    [InlineData(false, PlayerColor.Black, GameEndReason.FlatCount, "black-flats")]
    [InlineData(true, null, GameEndReason.FlatCount, "draw-flats")]
    [InlineData(false, PlayerColor.White, GameEndReason.Resignation, "white-resign")]
    public void Final(bool draw, PlayerColor? winner, GameEndReason reason, string result) =>
        Assert.Equal($"FINAL {result} {Hash}", One(o => o.Final(new GameResult(draw, winner, reason), Hash)));
}

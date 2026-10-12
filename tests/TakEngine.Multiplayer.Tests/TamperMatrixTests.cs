using TakEngine.Abstractions;
using TakEngine.Core.Cryptography;
using TakEngine.Crypto;
using TakEngine.Storage.Local;
using TakEngine.Testing;
using TakEngine.Transport.Nostr;
using TakEngine.Transport.Relay;

namespace TakEngine.Multiplayer.Tests;

/// <summary>
/// F-038, scope.md "A tampered, forged or illegal move is received": one case per row, delivered by a relay into a game in progress
/// (White has played e5, Black a1; White is to move at turn 3). Black is the receiver; the forgeries carry White's key except row 2.
/// </summary>
public class TamperMatrixTests
{
    public enum Row
    {
        BadSig,
        Stranger,
        Undecryptable,
        BadEnvelopeSig,
        Fork,
        Illegal,
        WrongTurnOwner,
        Equivocation,
    }

    private sealed record Setup(Table T, MultiplayerGame White, MultiplayerGame Black, SecretKey WhiteKey, MultiplayerOptions BlackOptions, string BlackDb);

    private static async Task<Setup> Start(Table t)
    {
        var (host, guest) = await t.Connect();
        await t.Play(host, guest, ["e5", "a1"]);
        bool hostIsWhite = host.MyColor == PlayerColor.White;
        return new Setup(t,
            hostIsWhite ? host : guest,
            hostIsWhite ? guest : host,
            hostIsWhite ? t.HostOptions.Key : t.GuestOptions.Key,
            hostIsWhite ? t.GuestOptions : t.HostOptions,
            Path.Combine(t.StoreDir!, hostIsWhite ? "guest.db" : "host.db"));
    }

    private static NostrEvent Forgery(Setup s, Row row)
    {
        Table t = s.T;
        PublicKey black = s.BlackOptions.Key.PublicKey;
        long now = t.Time.GetUtcNow().ToUnixTimeSeconds();
        Guid game = s.Black.GameId;
        string hash = s.Black.StateHash;
        NostrEvent Make(ForgeKind kind, int turn, string prev, string ptn) =>
            TamperForge.Forge(kind, s.WhiteKey, black, game, turn, prev, ptn, now, t.Random32);
        return row switch
        {
            Row.BadSig => TamperForge.WithBadSig(Make(ForgeKind.Illegal, 3, hash, "b1")),
            Row.Stranger => TamperForge.Forge(ForgeKind.Illegal, TestKeys.Stranger, black, game, 3, hash, "b1", now, t.Random32),
            Row.Undecryptable => Make(ForgeKind.Undecryptable, 3, hash, "b1"),
            Row.BadEnvelopeSig => Make(ForgeKind.BadEnvelopeSig, 3, hash, "b1"),
            Row.Fork => Make(ForgeKind.Fork, 3, hash, "b1"),
            Row.Illegal => Make(ForgeKind.Illegal, 3, hash, "a1"), // a1 is Black's
            Row.WrongTurnOwner => Make(ForgeKind.Illegal, 4, hash, "b1"),
            Row.Equivocation => Make(ForgeKind.Equivocate, 1, StateHasher.ComputeGenesisHash(BoardSize.Five), "c3"),
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        };
    }

    private static string? ExpectedFlag(Row row) => row switch
    {
        Row.BadSig or Row.Stranger => null,
        Row.Undecryptable => "undecryptable message from opponent",
        Row.BadEnvelopeSig => "bad message from opponent",
        Row.Fork => "hash chain mismatch at turn 3",
        Row.Illegal => "illegal move from opponent",
        Row.WrongTurnOwner => "illegal move from opponent: turn 4 is not the opponent's turn",
        Row.Equivocation => "conflicting move for turn 1",
        _ => throw new ArgumentOutOfRangeException(nameof(row)),
    };

    private static void Deliver(Setup s, NostrEvent evt) =>
        s.T.R1.Inject(new RelayMessage.Event(SubscriptionPlan.SubscriptionId(s.Black.GameId), evt).Format());

    [Theory]
    [InlineData(Row.BadSig)]
    [InlineData(Row.Stranger)]
    [InlineData(Row.Undecryptable)]
    [InlineData(Row.BadEnvelopeSig)]
    [InlineData(Row.Fork)]
    [InlineData(Row.Illegal)]
    [InlineData(Row.WrongTurnOwner)]
    [InlineData(Row.Equivocation)]
    public async Task TamperedEvent_IsDropped_TheGameGoesOn(Row row)
    {
        await using var t = new Table(seed: 31, sqlite: true);
        Setup s = await Start(t);
        var flags = new List<FlagEntry>();
        s.Black.Flagged += flags.Add;
        string hash = s.Black.StateHash;
        int dropped = s.Black.DroppedEvents;

        Deliver(s, Forgery(s, row));

        string? expected = ExpectedFlag(row);
        if (expected is null)
        {
            await Table.Eventually(() => s.Black.DroppedEvents == dropped + 1, $"{row} is counted");
            await Task.Delay(50);
            Assert.Empty(flags);
        }
        else
        {
            await Table.Eventually(() => flags.Count == 1, $"{row} is flagged");
            Assert.StartsWith(expected, flags[0].Reason, StringComparison.Ordinal);
            Assert.Equal(dropped, s.Black.DroppedEvents);
        }
        Assert.Equal(2, s.Black.AppliedTurn);
        Assert.Equal(hash, s.Black.StateHash);
        Assert.Null(s.Black.Result);

        await t.Play(s.White, s.Black, ["b1"], from: 3);
        Assert.Equal(s.White.StateHash, s.Black.StateHash);

        await s.Black.DisposeAsync();
        StoredGame stored = (await new SqliteGameStore(s.BlackDb).LoadAsync(s.Black.GameId))!;
        Assert.Equal(expected is null ? 0 : 1, stored.Flags.Count);
        if (expected is not null)
            Assert.StartsWith(expected, stored.Flags[0].Reason, StringComparison.Ordinal);
        MultiplayerGame again = t.Track((await MultiplayerGame.ResumeAsync(s.BlackOptions, s.Black.GameId))!);
        Assert.Equal(3, again.AppliedTurn);
    }

    [Fact]
    public async Task ThreeFlagsFromTheOpponent_SuggestResigning_NothingIsForfeited()
    {
        await using var t = new Table(seed: 32, sqlite: true);
        Setup s = await Start(t);
        var flags = new List<FlagEntry>();
        var notices = new List<string>();
        s.Black.Flagged += flags.Add;
        s.Black.Notice += n => { lock (notices) notices.Add(n); };

        foreach (Row row in new[] { Row.Fork, Row.Illegal })
            Deliver(s, Forgery(s, row));
        await Table.Eventually(() => flags.Count == 2, "two flags");
        await Task.Delay(50);
        Assert.DoesNotContain(notices, n => n.Contains("resign", StringComparison.OrdinalIgnoreCase));

        Deliver(s, Forgery(s, Row.Equivocation));
        await Table.Eventually(() => flags.Count == 3, "three flags");
        await Table.Eventually(() => { lock (notices) return notices.Any(n => n.Contains("resign", StringComparison.OrdinalIgnoreCase)); },
            "a notice suggests resigning");
        Assert.Null(s.Black.Result);
        Assert.Equal(HandshakeState.InGame, s.Black.Record.State);
        await t.Play(s.White, s.Black, ["b1"], from: 3);
    }

    [Theory]
    [InlineData("illegal", "a1", null, "illegal move from opponent")]
    [InlineData("equivocate", "c3", "1", "conflicting move for turn 1")]
    public async Task TakTestPeer_ForgesAsTheOpponent_TheReceiverFlagsIt_AndPlaysOn(string kind, string ptn, string? turn, string flag)
    {
        await using var t = new Table(seed: 33, sqlite: true);
        Setup s = await Start(t);
        var flags = new List<FlagEntry>();
        s.Black.Flagged += flags.Add;
        string keyFile = Path.Combine(t.StoreDir!, "white.key");
        await File.WriteAllTextAsync(keyFile, s.WhiteKey.ToNsec());
        var output = new StringWriter();
        var error = new StringWriter();
        string[] args = ["forge", "--relays", $"{t.R1.Url},{t.R2.Url}", "--invite", s.Black.Invite.ToCompactCode(), "--as-key", keyFile,
            "--ptn", ptn, "--kind", kind, .. turn is null ? Array.Empty<string>() : ["--turn", turn]];

        int exit = await TakTestPeer.TestPeer.RunAsync(args, t.Sockets, t.Time, output, error);

        Assert.True(exit == 0, error.ToString());
        Assert.Matches("^event [0-9a-f]{64} " + kind + " turn " + (turn ?? "3") + " sent$", output.ToString().Trim());
        await Table.Eventually(() => flags.Count == 1, "the forgery is flagged");
        Assert.StartsWith(flag, flags[0].Reason, StringComparison.Ordinal);
        Assert.Equal(2, s.Black.AppliedTurn);
        await t.Play(s.White, s.Black, ["b1"], from: 3);
        Assert.Equal(s.White.StateHash, s.Black.StateHash);
    }

    [Fact]
    public async Task TakTestPeer_WithoutTheRequiredOptions_PrintsUsage()
    {
        var error = new StringWriter();

        int exit = await TakTestPeer.TestPeer.RunAsync(["forge", "--ptn", "a1"], new FakeRelaySocketFactory(), TimeProvider.System,
            TextWriter.Null, error);

        Assert.Equal(1, exit);
        Assert.StartsWith("usage: TakTestPeer forge", error.ToString(), StringComparison.Ordinal);
    }
}

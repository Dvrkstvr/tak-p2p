using System.Security.Cryptography;
using System.Text;
using TakEngine.Abstractions;
using TakEngine.Core.Cryptography;
using TakEngine.Crypto;
using TakEngine.Multiplayer.Handshake;
using TakEngine.Transport.Matchmaking;

namespace TakEngine.Multiplayer.Tests;

/// <summary>F-017: scope.md "Invite create and accept handshake", host and guest states.</summary>
public class HandshakeTests
{
    private static readonly SecretKey Host = TestKeys.Create("host");
    private static readonly SecretKey Guest = TestKeys.Create("guest");
    private static readonly SecretKey Other = TestKeys.Create("other guest");
    private static readonly Guid Game = Guid.Parse("3c0a4a4e-6f6b-4f2b-9d36-6a2e2b7b5d10");
    private static readonly string[] Relays = ["wss://nos.lol"];
    private static readonly string[] Defaults = ["wss://nos.lol", "wss://relay.damus.io"];

    private static byte[] Nonce(string label) => SHA256.HashData(Encoding.UTF8.GetBytes(label));

    private static HostHandshake NewHost(BoardSize size = BoardSize.Six, int komi = 5) =>
        HostHandshake.Create(Game, Host.PublicKey, size, komi, Nonce("host nonce"), Relays, "Calvin");

    private static SignedGameAction SignedJoin(SecretKey guest, byte[] nonce, BoardSize size = BoardSize.Six) =>
        ActionDigest.Sign(guest, HandshakeMessages.UnsignedJoin(Game, guest.PublicKey.ToHex(), size, nonce), new byte[32]);

    private static SignedGameAction Sign(SecretKey key, SignedGameAction a) => ActionDigest.Sign(key, a, new byte[32]);

    private static GuestHandshake NewGuest(HostHandshake host, byte[]? nonce = null)
    {
        GuestStart start = GuestHandshake.Start(host.Invite.ToCompactCode(), Guest.PublicKey, nonce ?? Nonce("guest nonce"), Defaults);
        Assert.Null(start.Error);
        return start.Handshake!;
    }

    // ---- host ----

    [Fact]
    public void Create_IsInviting_WithTheNonceSavedAndOnlyItsCommitmentInTheInvite()
    {
        HostHandshake host = NewHost();

        Assert.Equal(HandshakeState.Inviting, host.State);
        Assert.Equal(NetRole.Host, host.Record.Role);
        Assert.Equal(Convert.ToHexStringLower(Nonce("host nonce")), host.Record.HostNonce);
        Assert.Equal(5, host.Record.KomiHalves);
        Assert.Equal(BoardSize.Six, host.Invite.BoardSize);
        Assert.Equal(ColorSeed.Commit(Nonce("host nonce")), host.Invite.SeedCommit);
        Assert.DoesNotContain(host.Record.HostNonce!, host.Invite.ToCompactCode() + host.Invite.ToUri());
        Assert.Null(host.Record.OpponentPubKey);
    }

    [Theory]
    [InlineData(9, 0)]
    [InlineData(5, 21)]
    public void Create_OutOfRange_Throws(int size, int komi) =>
        Assert.Throws<InviteFormatException>(() => NewHost((BoardSize)size, komi));

    [Fact]
    public void ValidJoin_LocksTheGuest_AndAcceptsWithColoursFromBothNonces()
    {
        HostHandshake host = NewHost();

        HostDecision d = host.OnJoin(SignedJoin(Guest, Nonce("guest nonce")));

        Assert.Equal(HostDecisionKind.Accept, d.Kind);
        Assert.Equal(HandshakeState.InGame, host.State);
        Assert.Equal(Guest.PublicKey.ToHex(), host.Record.OpponentPubKey);
        Assert.Equal(Convert.ToHexStringLower(Nonce("guest nonce")), host.Record.GuestNonce);
        AcceptTerms terms = HandshakeMessages.ReadAccept(d.Accept!)!;
        var (hostColor, guestColor) = ColorResolver.ResolveColors(
            ColorSeed.Derive(Nonce("host nonce"), Nonce("guest nonce")), Host.PublicKey.ToHex(), Guest.PublicKey.ToHex());
        Assert.Equal(hostColor, host.Record.MyColor);
        Assert.Equal(hostColor == PlayerColor.White ? Host.PublicKey.ToHex() : Guest.PublicKey.ToHex(), terms.White);
        Assert.Equal(guestColor == PlayerColor.White ? Guest.PublicKey.ToHex() : Host.PublicKey.ToHex(), terms.White);
        Assert.NotEqual(terms.White, terms.Black);
        Assert.Equal(Nonce("host nonce"), terms.HostNonce);
        Assert.Equal(BoardSize.Six, terms.Size);
        Assert.Equal(5, terms.KomiHalves);
        Assert.Equal(StateHasher.ComputeGenesisHash(BoardSize.Six), d.Accept!.PrevStateHash);
        Assert.Equal(0, d.Accept.Turn);
        Assert.Equal(Host.PublicKey.ToHex(), d.Accept.PlayerPubKey);
    }

    [Fact]
    public void SecondGuest_IsIgnoredAndSurfaced()
    {
        HostHandshake host = NewHost();
        host.OnJoin(SignedJoin(Guest, Nonce("guest nonce")));

        HostDecision d = host.OnJoin(SignedJoin(Other, Nonce("other nonce")));

        Assert.Equal(HostDecisionKind.Ignore, d.Kind);
        Assert.Equal($"ignored join from {Other.PublicKey.ToNpub()}: game already taken", d.Message);
        Assert.Equal(Guest.PublicKey.ToHex(), host.Record.OpponentPubKey);
    }

    [Fact]
    public void RepeatedJoin_WithNewNonce_GetsTheSameAccept()
    {
        HostHandshake host = NewHost();
        host.OnJoin(SignedJoin(Guest, Nonce("guest nonce")));
        NetGameRecord locked = host.Record;

        HostDecision again = host.OnJoin(SignedJoin(Guest, Nonce("a different nonce")));

        Assert.Equal(HostDecisionKind.ResendAccept, again.Kind);
        Assert.Null(again.Accept);
        Assert.Equal(locked, host.Record);
    }

    [Fact]
    public void MalformedJoin_IsIgnored_AndTheInviteStaysOpen()
    {
        HostHandshake host = NewHost();
        var noNonce = Sign(Guest, SignedGameAction.Unsigned(GameActionType.Join, Game, 0, Guest.PublicKey.ToHex(), "x"));
        var shortNonce = Sign(Guest, SignedGameAction.Unsigned(GameActionType.Join, Game, 0, Guest.PublicKey.ToHex(), "x",
            [new("guest_nonce", ActionValue.Of("abcd"))]));
        var extra = Sign(Guest, SignedGameAction.Unsigned(GameActionType.Join, Game, 0, Guest.PublicKey.ToHex(), "x",
            [new("guest_nonce", ActionValue.Of(new string('a', 64))), new("x", ActionValue.Of(1))]));
        var turnOne = Sign(Guest, SignedGameAction.Unsigned(GameActionType.Join, Game, 1, Guest.PublicKey.ToHex(), "x",
            [new("guest_nonce", ActionValue.Of(new string('a', 64)))]));

        Assert.All([noNonce, shortNonce, extra, turnOne], j => Assert.Equal(HostDecisionKind.Ignore, host.OnJoin(j).Kind));
        Assert.Equal(HandshakeState.Inviting, host.State);
        Assert.Equal(HostDecisionKind.Ignore, host.OnJoin(SignedJoin(Host, Nonce("self"))).Kind);
        Assert.Equal(HostDecisionKind.Accept, host.OnJoin(SignedJoin(Guest, Nonce("guest nonce"))).Kind);
    }

    [Fact]
    public void Restart_WhileInvitingOrLocked_LosesNothing()
    {
        HostHandshake inviting = NewHost();
        HostHandshake restored = HostHandshake.Restore(inviting.Record);
        Assert.Equal((inviting.Invite with { HostNickname = null }).ToCompactCode(), restored.Invite.ToCompactCode());
        Assert.Equal(inviting.Invite.SeedCommit, restored.Invite.SeedCommit);
        HostDecision first = restored.OnJoin(SignedJoin(Guest, Nonce("guest nonce")));
        Assert.Equal(HostDecisionKind.Accept, first.Kind);
        Assert.Equal(ActionDigest.Compute(inviting.OnJoin(SignedJoin(Guest, Nonce("guest nonce"))).Accept!), ActionDigest.Compute(first.Accept!));

        HostHandshake locked = HostHandshake.Restore(restored.Record);
        Assert.Equal(HostDecisionKind.ResendAccept, locked.OnJoin(SignedJoin(Guest, Nonce("guest nonce"))).Kind);
        Assert.Equal(HostDecisionKind.Ignore, locked.OnJoin(SignedJoin(Other, Nonce("x"))).Kind);
    }

    [Fact]
    public void Cancel_BeforeLock_EndsTheInvite_AfterLock_IsResignInstead()
    {
        HostHandshake host = NewHost();
        Assert.True(host.Cancel());
        Assert.Equal(HandshakeState.Cancelled, host.State);
        Assert.Equal(HostDecisionKind.Ignore, host.OnJoin(SignedJoin(Guest, Nonce("g"))).Kind);
        Assert.Equal(HandshakeState.Cancelled, HostHandshake.Restore(host.Record).State);

        HostHandshake locked = NewHost();
        locked.OnJoin(SignedJoin(Guest, Nonce("g")));
        Assert.False(locked.Cancel());
        Assert.Equal(HandshakeState.InGame, locked.State);
    }

    [Fact]
    public void Restore_RefusesAGuestRecord()
    {
        GuestHandshake guest = NewGuest(NewHost());
        Assert.Throws<ArgumentException>(() => HostHandshake.Restore(guest.Record));
        Assert.Throws<ArgumentException>(() => GuestHandshake.Restore(NewHost().Record));
    }

    // ---- guest ----

    [Theory]
    [InlineData("garbage", "not a Tak invite")]
    [InlineData("", "empty")]
    public void InvalidInvite_FailsBeforeAnyTraffic(string text, string message)
    {
        GuestStart start = GuestHandshake.Start(text, Guest.PublicKey, Nonce("g"), Defaults);

        Assert.Null(start.Handshake);
        Assert.Contains(message, start.Error);
    }

    [Fact]
    public void OwnInvite_Fails()
    {
        GuestStart start = GuestHandshake.Start(NewHost().Invite.ToCompactCode(), Host.PublicKey, Nonce("g"), Defaults);

        Assert.Null(start.Handshake);
        Assert.Contains("your own invite", start.Error);
    }

    [Fact]
    public void Joining_CarriesTheGuestNonce_AndUsesTheInviteRelaysPlusDefaults()
    {
        GuestHandshake guest = NewGuest(NewHost());

        Assert.Equal(HandshakeState.Joining, guest.State);
        Assert.Equal(["wss://nos.lol", "wss://relay.damus.io"], guest.Record.Relays);
        Assert.Equal(Nonce("guest nonce"), HandshakeMessages.ReadJoin(Sign(Guest, guest.UnsignedJoin)));
        Assert.Equal(Host.PublicKey.ToHex(), guest.Record.OpponentPubKey);
        Assert.Null(guest.Record.HostNonce);
    }

    [Fact]
    public void JoinRepublishSchedule_Is5_15_45_ThenEvery60s()
    {
        var t0 = new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

        int[] offsets = Enumerable.Range(1, 6).Select(n => (int)(GuestHandshake.NextJoinAt(t0, n) - t0).TotalSeconds).ToArray();

        Assert.Equal([5, 15, 45, 105, 165, 225], offsets);
        Assert.Throws<ArgumentOutOfRangeException>(() => GuestHandshake.NextJoinAt(t0, 0));
    }

    [Fact]
    public void GoodAccept_StartsTheGame_WithTheSameColoursAsTheHost()
    {
        HostHandshake host = NewHost();
        GuestHandshake guest = NewGuest(host);
        HostDecision d = host.OnJoin(Sign(Guest, guest.UnsignedJoin));

        GuestDecision g = guest.OnAccept(Sign(Host, d.Accept!));

        Assert.Equal(GuestDecisionKind.Start, g.Kind);
        Assert.Equal(HandshakeState.InGame, guest.State);
        Assert.NotNull(guest.Record.MyColor);
        Assert.NotEqual(host.Record.MyColor, guest.Record.MyColor);
        Assert.Equal(BoardSize.Six, guest.Record.Size);
        Assert.Equal(5, guest.Record.KomiHalves);
        Assert.Equal(GuestDecisionKind.Ignore, guest.OnAccept(Sign(Host, d.Accept!)).Kind); // repeated ACCEPT
    }

    [Fact]
    public void ColoursComeOutBothWays_AcrossNonces()
    {
        var seen = new HashSet<PlayerColor>();
        for (int i = 0; i < 16 && seen.Count < 2; i++)
        {
            HostHandshake host = HostHandshake.Create(Game, Host.PublicKey, BoardSize.Five, 0, Nonce($"h{i}"), Relays);
            GuestHandshake guest = NewGuest(host, Nonce($"g{i}"));
            guest.OnAccept(Sign(Host, host.OnJoin(Sign(Guest, guest.UnsignedJoin)).Accept!));
            Assert.NotEqual(host.Record.MyColor, guest.Record.MyColor);
            seen.Add(guest.Record.MyColor!.Value);
        }
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void Accept_FromANonHostKey_IsDroppedAndFlagged()
    {
        HostHandshake host = NewHost();
        GuestHandshake guest = NewGuest(host);
        SignedGameAction accept = host.OnJoin(Sign(Guest, guest.UnsignedJoin)).Accept!;
        SignedGameAction forged = Sign(Other, accept with { PlayerPubKey = Other.PublicKey.ToHex() });

        GuestDecision g = guest.OnAccept(forged);

        Assert.Equal(GuestDecisionKind.DropAndFlag, g.Kind);
        Assert.Equal(HandshakeState.Joining, guest.State);
        Assert.Equal(GuestDecisionKind.Start, guest.OnAccept(Sign(Host, accept)).Kind);
    }

    [Fact]
    public void Accept_WithNonceNotMatchingCommit_IsDroppedAndFails()
    {
        HostHandshake host = NewHost();
        GuestHandshake guest = NewGuest(host);
        var terms = HandshakeMessages.ReadAccept(host.OnJoin(Sign(Guest, guest.UnsignedJoin)).Accept!)!;
        // A host that wants another colour reveals a different nonce (and colours to match it).
        byte[] other = Nonce("a nonce the host prefers");
        var (hc, _) = ColorResolver.ResolveColors(ColorSeed.Derive(other, Nonce("guest nonce")), Host.PublicKey.ToHex(), Guest.PublicKey.ToHex());
        var cheat = terms with
        {
            HostNonce = other,
            White = hc == PlayerColor.White ? Host.PublicKey.ToHex() : Guest.PublicKey.ToHex(),
            Black = hc == PlayerColor.White ? Guest.PublicKey.ToHex() : Host.PublicKey.ToHex(),
        };

        GuestDecision g = guest.OnAccept(Sign(Host, HandshakeMessages.UnsignedAccept(Game, Host.PublicKey.ToHex(), cheat)));

        Assert.Equal(GuestDecisionKind.Fail, g.Kind);
        Assert.Contains("does not match its invite", g.Message);
        Assert.Equal(HandshakeState.Failed, guest.State);
        Assert.Null(guest.Record.MyColor);
    }

    [Theory]
    [InlineData(7, 5, "board size 7")]
    [InlineData(6, 4, "komi 4")]
    public void Accept_WithDifferentSizeOrKomi_Fails(int size, int komi, string message)
    {
        HostHandshake host = NewHost();
        GuestHandshake guest = NewGuest(host);
        var terms = HandshakeMessages.ReadAccept(host.OnJoin(Sign(Guest, guest.UnsignedJoin)).Accept!)!;

        GuestDecision g = guest.OnAccept(Sign(Host, HandshakeMessages.UnsignedAccept(Game, Host.PublicKey.ToHex(),
            terms with { Size = (BoardSize)size, KomiHalves = komi })));

        Assert.Equal(GuestDecisionKind.Fail, g.Kind);
        Assert.Contains(message, g.Message);
    }

    [Fact]
    public void Accept_WithSwappedColours_Fails()
    {
        HostHandshake host = NewHost();
        GuestHandshake guest = NewGuest(host);
        var terms = HandshakeMessages.ReadAccept(host.OnJoin(Sign(Guest, guest.UnsignedJoin)).Accept!)!;

        GuestDecision g = guest.OnAccept(Sign(Host, HandshakeMessages.UnsignedAccept(Game, Host.PublicKey.ToHex(),
            terms with { White = terms.Black, Black = terms.White })));

        Assert.Equal("host and guest disagree on colours", g.Message);
        Assert.Equal(GuestDecisionKind.Ignore, guest.OnAccept(Sign(Host, HandshakeMessages.UnsignedAccept(Game, Host.PublicKey.ToHex(), terms))).Kind);
    }

    [Fact]
    public void MalformedAccept_Fails()
    {
        HostHandshake host = NewHost();
        GuestHandshake guest = NewGuest(host);
        var bad = Sign(Host, SignedGameAction.Unsigned(GameActionType.Accept, Game, 0, Host.PublicKey.ToHex(), "x",
            [new("white", ActionValue.Of("a"))]));

        Assert.Equal(GuestDecisionKind.Fail, guest.OnAccept(bad).Kind);
    }

    [Fact]
    public void Cancel_WhileJoining_StopsTheGuest()
    {
        GuestHandshake guest = NewGuest(NewHost());

        Assert.True(guest.Cancel());
        Assert.Equal(HandshakeState.Cancelled, guest.State);
        Assert.False(guest.Cancel());
    }

    [Fact]
    public void GuestRestart_KeepsTheNonceAndTheJoin()
    {
        GuestHandshake guest = NewGuest(NewHost());

        GuestHandshake restored = GuestHandshake.Restore(guest.Record);

        Assert.Equal(ActionDigest.Compute(guest.UnsignedJoin), ActionDigest.Compute(restored.UnsignedJoin));
        Assert.Equal(HandshakeState.Joining, restored.State);
    }

    [Fact]
    public void Accept_WithOneColourWrong_Fails()
    {
        HostHandshake host = NewHost();
        GuestHandshake guest = NewGuest(host);
        var terms = HandshakeMessages.ReadAccept(host.OnJoin(Sign(Guest, guest.UnsignedJoin)).Accept!)!;

        GuestDecision g = guest.OnAccept(Sign(Host, HandshakeMessages.UnsignedAccept(Game, Host.PublicKey.ToHex(),
            terms with { White = Other.PublicKey.ToHex() })));

        Assert.Equal("host and guest disagree on colours", g.Message);
    }

    [Fact]
    public void Messages_SayWhatHappened()
    {
        HostHandshake host = NewHost();
        GuestHandshake guest = NewGuest(host);
        SignedGameAction accept = host.OnJoin(Sign(Guest, guest.UnsignedJoin)).Accept!;
        Assert.Equal("ACCEPT from a key that is not the host's",
            guest.OnAccept(Sign(Other, accept with { PlayerPubKey = Other.PublicKey.ToHex() })).Message);
        var bad = Sign(Host, SignedGameAction.Unsigned(GameActionType.Accept, Game, 0, Host.PublicKey.ToHex(), "x"));
        Assert.Equal("The host's answer is malformed.", guest.OnAccept(bad).Message);
        Assert.Equal("The host's answer is malformed.", guest.Record.Note);

        HostHandshake inviting = NewHost();
        var noNonce = Sign(Guest, SignedGameAction.Unsigned(GameActionType.Join, Game, 0, Guest.PublicKey.ToHex(), "x"));
        Assert.Equal("ignored a malformed join", inviting.OnJoin(noNonce).Message);
        inviting.Cancel();
        Assert.Equal("invite cancelled", inviting.Record.Note);

        GuestHandshake leaving = NewGuest(NewHost());
        leaving.Cancel();
        Assert.Equal("join cancelled", leaving.Record.Note);
    }

    [Fact]
    public void BadArguments_AreRefused()
    {
        HostHandshake host = NewHost();
        GuestHandshake guest = NewGuest(host);
        string code = host.Invite.ToCompactCode();

        Assert.Throws<ArgumentNullException>(() => HostHandshake.Create(Game, null!, BoardSize.Five, 0, Nonce("h"), Relays));
        Assert.Throws<ArgumentNullException>(() => HostHandshake.Create(Game, Host.PublicKey, BoardSize.Five, 0, Nonce("h"), null!));
        Assert.Throws<ArgumentException>(() => HostHandshake.Create(Game, Host.PublicKey, BoardSize.Five, 0, new byte[31], Relays));
        Assert.Throws<ArgumentNullException>(() => HostHandshake.Restore(null!));
        Assert.Throws<ArgumentNullException>(() => host.OnJoin(null!));
        Assert.Throws<ArgumentNullException>(() => GuestHandshake.Start(code, null!, Nonce("g"), Defaults));
        Assert.Throws<ArgumentNullException>(() => GuestHandshake.Start(code, Guest.PublicKey, Nonce("g"), null!));
        Assert.Throws<ArgumentException>(() => GuestHandshake.Start(code, Guest.PublicKey, new byte[16], Defaults));
        Assert.Throws<ArgumentException>(() => GuestHandshake.Start(code, Guest.PublicKey, null!, Defaults));
        Assert.Throws<ArgumentNullException>(() => GuestHandshake.Restore(null!));
        Assert.Throws<ArgumentNullException>(() => guest.OnAccept(null!));

        var hostWithoutNonce = Assert.Throws<ArgumentException>(() => HostHandshake.Restore(host.Record with { HostNonce = null }));
        Assert.Contains("host", hostWithoutNonce.Message);
        var guestWithoutNonce = Assert.Throws<ArgumentException>(() => GuestHandshake.Restore(guest.Record with { GuestNonce = null }));
        Assert.Contains("guest", guestWithoutNonce.Message);
    }
}

/// <summary>JOIN and ACCEPT action data (D-041) are read strictly.</summary>
public class HandshakeMessagesTests
{
    private static readonly Guid Game = Guid.Parse("3c0a4a4e-6f6b-4f2b-9d36-6a2e2b7b5d10");
    private static readonly string Hex64 = new('a', 64);

    private static SignedGameAction Accept(int turn = 0, GameActionType type = GameActionType.Accept, params (string Key, ActionValue Value)[] data) =>
        SignedGameAction.Unsigned(type, Game, turn, "host", "prev", data.Select(d => new KeyValuePair<string, ActionValue>(d.Key, d.Value)));

    private static (string, ActionValue)[] Good(long size = 5, long komi = 0, string? nonce = null) =>
    [
        ("white", ActionValue.Of("w")),
        ("black", ActionValue.Of("b")),
        ("host_nonce", ActionValue.Of(nonce ?? Hex64)),
        ("size", ActionValue.Of(size)),
        ("komi", ActionValue.Of(komi)),
    ];

    [Fact]
    public void GoodAccept_IsRead()
    {
        AcceptTerms t = HandshakeMessages.ReadAccept(Accept(data: Good(6, 5)))!;

        Assert.Equal(("w", "b", BoardSize.Six, 5), (t.White, t.Black, t.Size, t.KomiHalves));
        Assert.Equal(Convert.FromHexString(Hex64), t.HostNonce);
    }

    [Fact]
    public void Accept_WithWrongShape_IsNotRead()
    {
        Assert.Null(HandshakeMessages.ReadAccept(Accept(turn: 1, data: Good())));
        Assert.Null(HandshakeMessages.ReadAccept(Accept(type: GameActionType.Join, data: Good())));
        Assert.Null(HandshakeMessages.ReadAccept(Accept(data: [.. Good(), ("extra", ActionValue.Of(1))])));
        Assert.Null(HandshakeMessages.ReadAccept(Accept(data: Good().Where(d => d.Item1 != "komi").Append(("komy", ActionValue.Of(0))).ToArray())));
        Assert.Null(HandshakeMessages.ReadAccept(Accept(data: Good().Select(d => d.Item1 == "white" ? ("white", ActionValue.Of(1)) : d).ToArray())));
        Assert.Null(HandshakeMessages.ReadAccept(Accept(data: Good().Select(d => d.Item1 == "black" ? ("black", ActionValue.Of(1)) : d).ToArray())));
        Assert.Null(HandshakeMessages.ReadAccept(Accept(data: Good().Select(d => d.Item1 == "size" ? ("size", ActionValue.Of("5")) : d).ToArray())));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(101, 0)]
    [InlineData(5, -1)]
    [InlineData(5, 101)]
    [InlineData(5, 4294967301)] // would wrap to 5 as an int
    public void Accept_WithAbsurdNumbers_IsNotRead(long size, long komi) =>
        Assert.Null(HandshakeMessages.ReadAccept(Accept(data: Good(size, komi))));

    [Theory]
    [InlineData(100, 100)]
    [InlineData(0, 0)]
    public void Accept_AtTheEdgeOfTheSanityRange_IsRead(long size, long komi) =>
        Assert.NotNull(HandshakeMessages.ReadAccept(Accept(data: Good(size, komi))));

    [Theory]
    [InlineData("g aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("gaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaa")]
    public void Nonce_MustBe64Hex(string nonce)
    {
        Assert.Null(HandshakeMessages.ReadAccept(Accept(data: Good(nonce: nonce))));
        Assert.Null(HandshakeMessages.ReadJoin(SignedGameAction.Unsigned(GameActionType.Join, Game, 0, "g", "p", [new("guest_nonce", ActionValue.Of(nonce))])));
    }
}

using System.Security.Cryptography;
using System.Text;
using TakEngine.Abstractions;
using TakEngine.Crypto;
using TakEngine.Transport.Envelope;
using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Tests;

/// <summary>F-033 part 3: the envelope codec and its tamper-row verdicts, always between independent peers.</summary>
public class EnvelopeCodecTests
{
    private static readonly Guid Game = Guid.Parse("0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e");
    private static readonly SecretKey Alice = Key("alice");
    private static readonly SecretKey Bob = Key("bob");
    private static readonly SecretKey Carol = Key("carol");
    private static readonly string Genesis = new('0', 64);
    private const long CreatedAt = 1_760_000_000;

    private static SecretKey Key(string label) => SecretKey.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes("envelope test " + label)));

    private static byte[] Bytes(string label) => SHA256.HashData(Encoding.UTF8.GetBytes(label));

    private static DecodeContext BobExpectsAlice => new(Bob, Game, Alice.PublicKey);

    private static SignedGameAction SignedMove(SecretKey key, string ptn = "a1", Guid? game = null) =>
        ActionDigest.Sign(key, SignedGameAction.UnsignedMove(game ?? Game, 1, key.PublicKey.ToHex(), Genesis, ptn), Bytes("aux"));

    private static NostrEvent Encode(SecretKey sender, PublicKey recipient, SignedGameAction action) =>
        EnvelopeCodec.Encode(sender, recipient, action, CreatedAt, Bytes("nonce"), Bytes("event aux"));

    /// <summary>A correctly signed kind-3825 event whose NIP-44 content carries <paramref name="plaintext"/> verbatim.</summary>
    private static NostrEvent RawEvent(SecretKey sender, PublicKey recipient, string plaintext, Guid? gTag = null)
    {
        string content = Nip44.Encrypt(plaintext, Nip44.ConversationKey(sender, recipient), Bytes("nonce"));
        return SignContent(sender, recipient, content, gTag);
    }

    private static NostrEvent SignContent(SecretKey sender, PublicKey recipient, string content, Guid? gTag = null) =>
        NostrEvents.Sign(sender, CreatedAt, EnvelopeCodec.Kind,
            [new[] { "p", recipient.ToHex() }, new[] { "g", (gTag ?? Game).ToString("D") }], content, Bytes("event aux"));

    [Fact]
    public void Encode_ProducesKind3825_WithExactlyPAndGTags_AndVerifies()
    {
        NostrEvent evt = Encode(Alice, Bob.PublicKey, SignedMove(Alice));

        Assert.Equal(3825, evt.Kind);
        Assert.Equal(Alice.PublicKey.ToHex(), evt.Pubkey);
        Assert.Equal(CreatedAt, evt.CreatedAt);
        Assert.Equal(2, evt.Tags.Count);
        Assert.Equal(new[] { "p", Bob.PublicKey.ToHex() }, evt.Tags[0]);
        Assert.Equal(new[] { "g", "0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e" }, evt.Tags[1]);
        Assert.True(NostrEvents.Verify(evt));
        Assert.DoesNotContain("a1", evt.Content); // encrypted
    }

    public static TheoryData<string> ActionTypes() => ["JOIN", "ACCEPT", "MOVE", "RESIGN"];

    [Theory]
    [MemberData(nameof(ActionTypes))]
    public void RoundTrip_EveryActionType(string wire)
    {
        Assert.True(GameActionTypes.TryParseWire(wire, out GameActionType type));
        KeyValuePair<string, ActionValue>[] data = type switch
        {
            GameActionType.Join => [new("guest_nonce", ActionValue.Of(Convert.ToHexStringLower(Bytes("g"))))],
            GameActionType.Accept =>
            [
                new("white", ActionValue.Of(Alice.PublicKey.ToHex())), new("black", ActionValue.Of(Bob.PublicKey.ToHex())),
                new("host_nonce", ActionValue.Of(Convert.ToHexStringLower(Bytes("h")))), new("komi", ActionValue.Of(5)), new("size", ActionValue.Of(6)),
            ],
            GameActionType.Move => [new("ptn", ActionValue.Of("3c3>12*"))],
            _ => [],
        };
        int turn = type == GameActionType.Move ? 7 : 0;
        SignedGameAction sent = ActionDigest.Sign(Alice, SignedGameAction.Unsigned(type, Game, turn, Alice.PublicKey.ToHex(), Genesis, data), Bytes("aux"));

        DecodeResult result = EnvelopeCodec.Decode(Encode(Alice, Bob.PublicKey, sent), BobExpectsAlice);

        Assert.Equal(DecodeVerdict.Accepted, result.Verdict);
        Assert.False(result.IsOwn);
        SignedGameAction got = result.Action!;
        Assert.Equal(sent with { ActionData = got.ActionData }, got);
        Assert.Equal(sent.ActionData.OrderBy(p => p.Key, StringComparer.Ordinal), got.ActionData.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void OwnEcho_DecodesWithTheRecipientsConversationKey_AndIsMarkedOwn()
    {
        NostrEvent evt = Encode(Alice, Bob.PublicKey, SignedMove(Alice));

        DecodeResult result = EnvelopeCodec.Decode(evt, new DecodeContext(Alice, Game, Bob.PublicKey));

        Assert.Equal(DecodeVerdict.Accepted, result.Verdict);
        Assert.True(result.IsOwn);
    }

    [Fact]
    public void BadId_IsRow1()
    {
        NostrEvent evt = Encode(Alice, Bob.PublicKey, SignedMove(Alice));
        evt.CreatedAt += 1; // id no longer matches

        Assert.Equal(DecodeVerdict.Row1BadIdOrSig, EnvelopeCodec.Decode(evt, BobExpectsAlice).Verdict);
    }

    [Fact]
    public void BadSig_IsRow1()
    {
        NostrEvent evt = Encode(Alice, Bob.PublicKey, SignedMove(Alice));
        evt.Sig = Encode(Alice, Bob.PublicKey, SignedMove(Alice, "a2")).Sig;

        Assert.Equal(DecodeVerdict.Row1BadIdOrSig, EnvelopeCodec.Decode(evt, BobExpectsAlice).Verdict);
        Assert.Equal(DecodeVerdict.Row1BadIdOrSig, EnvelopeCodec.Decode(null, BobExpectsAlice).Verdict);
    }

    [Fact]
    public void StrangerAuthor_IsRow2()
    {
        NostrEvent evt = Encode(Carol, Bob.PublicKey, SignedMove(Carol));

        Assert.Equal(DecodeVerdict.Row2WrongAuthor, EnvelopeCodec.Decode(evt, BobExpectsAlice).Verdict);
    }

    [Fact]
    public void JoinFromAnyNonSelfAuthorWhileInviting_IsAccepted()
    {
        var join = ActionDigest.Sign(Carol, SignedGameAction.Unsigned(GameActionType.Join, Game, 0, Carol.PublicKey.ToHex(), Genesis,
            [new("guest_nonce", ActionValue.Of(Convert.ToHexStringLower(Bytes("n"))))]), Bytes("aux"));
        var inviting = new DecodeContext(Bob, Game, Opponent: null);

        DecodeResult result = EnvelopeCodec.Decode(Encode(Carol, Bob.PublicKey, join), inviting);

        Assert.Equal(DecodeVerdict.Accepted, result.Verdict);
        Assert.Equal(GameActionType.Join, result.Action!.ActionType);
    }

    [Fact]
    public void NonJoinFromUnknownAuthorWhileInviting_IsRow4()
    {
        var inviting = new DecodeContext(Bob, Game, Opponent: null);

        AssertRejected(DecodeVerdict.Row4BadPayload, EnvelopeCodec.Decode(Encode(Carol, Bob.PublicKey, SignedMove(Carol)), inviting));
    }

    [Fact]
    public void FlippedMac_IsRow3()
    {
        string content = Encode(Alice, Bob.PublicKey, SignedMove(Alice)).Content;
        byte[] raw = Convert.FromBase64String(content);
        raw[^1] ^= 0x01;
        NostrEvent evt = SignContent(Alice, Bob.PublicKey, Convert.ToBase64String(raw)); // re-signed, so row 1 passes

        AssertRejected(DecodeVerdict.Row3Undecryptable, EnvelopeCodec.Decode(evt, BobExpectsAlice));
    }

    [Fact]
    public void WrongConversationKey_IsRow3()
    {
        // Encrypted for Carol but tagged for Bob.
        string content = Nip44.Encrypt(EnvelopeCodec.ToPlaintext(SignedMove(Alice)), Nip44.ConversationKey(Alice, Carol.PublicKey), Bytes("nonce"));

        AssertRejected(DecodeVerdict.Row3Undecryptable, EnvelopeCodec.Decode(SignContent(Alice, Bob.PublicKey, content), BobExpectsAlice));
    }

    [Theory]
    [InlineData("not base64 at all !!")]
    [InlineData("")]
    [InlineData("#unsupported")]
    public void JunkContent_IsRow3(string content)
    {
        AssertRejected(DecodeVerdict.Row3Undecryptable, EnvelopeCodec.Decode(SignContent(Alice, Bob.PublicKey, content), BobExpectsAlice));
    }

    [Fact]
    public void BadPadding_IsRow3()
    {
        // Re-MAC a real ciphertext after flipping the encrypted length prefix from 1 to 0: the MAC is valid, the padding is not.
        byte[] nonce = Bytes("pad nonce");
        byte[] conversationKey = Nip44.ConversationKey(Alice, Bob.PublicKey);
        var (_, _, hmacKey) = Nip44.MessageKeys(conversationKey, nonce);
        byte[] ciphertext = Convert.FromBase64String(Nip44.Encrypt("x", conversationKey, nonce))[33..^32];
        ciphertext[1] ^= 0x01;
        byte[] mac = HMACSHA256.HashData(hmacKey, (byte[])[.. nonce, .. ciphertext]);
        string content = Convert.ToBase64String([2, .. nonce, .. ciphertext, .. mac]);

        AssertRejected(DecodeVerdict.Row3Undecryptable, EnvelopeCodec.Decode(SignContent(Alice, Bob.PublicKey, content), BobExpectsAlice));
    }

    [Fact]
    public void InnerGameIdMismatch_IsRow4()
    {
        var other = Guid.Parse("11111111-2222-4333-8444-555555555555");
        NostrEvent evt = RawEvent(Alice, Bob.PublicKey, EnvelopeCodec.ToPlaintext(SignedMove(Alice, game: other)));

        AssertRejected(DecodeVerdict.Row4BadPayload, EnvelopeCodec.Decode(evt, BobExpectsAlice));
    }

    [Fact]
    public void BadInnerSignature_IsRow4()
    {
        SignedGameAction action = SignedMove(Alice) with { Signature = SignedMove(Alice, "a2").Signature };

        AssertRejected(DecodeVerdict.Row4BadPayload, EnvelopeCodec.Decode(RawEvent(Alice, Bob.PublicKey, EnvelopeCodec.ToPlaintext(action)), BobExpectsAlice));
    }

    [Fact]
    public void PlayerPubkeyNotEventAuthor_IsRow4()
    {
        // Alice relays a move Carol signed: the inner signature is valid, but the author is not the player.
        NostrEvent evt = RawEvent(Alice, Bob.PublicKey, EnvelopeCodec.ToPlaintext(SignedMove(Carol)));

        AssertRejected(DecodeVerdict.Row4BadPayload, EnvelopeCodec.Decode(evt, BobExpectsAlice));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"pv\":1}")]
    [InlineData("{\"pv\":\"1\"}")]
    public void BadJson_IsRow4(string plaintext)
    {
        AssertRejected(DecodeVerdict.Row4BadPayload, EnvelopeCodec.Decode(RawEvent(Alice, Bob.PublicKey, plaintext), BobExpectsAlice));
    }

    [Fact]
    public void ExtraOrNonScalarFields_AreRow4()
    {
        string plaintext = EnvelopeCodec.ToPlaintext(SignedMove(Alice));
        string extraKey = plaintext.Replace("\"pv\":1,", "\"pv\":1,\"x\":0,");
        string nestedData = plaintext.Replace("{\"ptn\":\"a1\"}", "{\"ptn\":{\"a\":1}}");

        AssertRejected(DecodeVerdict.Row4BadPayload, EnvelopeCodec.Decode(RawEvent(Alice, Bob.PublicKey, extraKey), BobExpectsAlice));
        AssertRejected(DecodeVerdict.Row4BadPayload, EnvelopeCodec.Decode(RawEvent(Alice, Bob.PublicKey, nestedData), BobExpectsAlice));
    }

    [Fact]
    public void PvNot1_IsIncompatibleVersion_NotTamper()
    {
        SignedGameAction v2 = ActionDigest.Sign(Alice, SignedMove(Alice) with { Pv = 2 }, Bytes("aux"));

        DecodeResult result = EnvelopeCodec.Decode(RawEvent(Alice, Bob.PublicKey, EnvelopeCodec.ToPlaintext(v2)), BobExpectsAlice);

        AssertRejected(DecodeVerdict.IncompatibleVersion, result);
    }

    [Fact]
    public void OtherGameTag_IsNotForUs()
    {
        var other = Guid.Parse("11111111-2222-4333-8444-555555555555");
        NostrEvent evt = RawEvent(Alice, Bob.PublicKey, EnvelopeCodec.ToPlaintext(SignedMove(Alice, game: other)), gTag: other);

        AssertRejected(DecodeVerdict.NotForUs, EnvelopeCodec.Decode(evt, BobExpectsAlice));
    }

    [Fact]
    public void DecodeNeverDecryptsBeforeVerifying()
    {
        int decryptCalls = 0;
        string Spy(string payload, byte[] key)
        {
            decryptCalls++;
            return Nip44.Decrypt(payload, key);
        }

        NostrEvent good = Encode(Alice, Bob.PublicKey, SignedMove(Alice));
        NostrEvent badSig = Encode(Alice, Bob.PublicKey, SignedMove(Alice));
        badSig.Sig = new string('0', 128);
        NostrEvent stranger = Encode(Carol, Bob.PublicKey, SignedMove(Carol));

        Assert.Equal(DecodeVerdict.Row1BadIdOrSig, EnvelopeCodec.Decode(badSig, BobExpectsAlice, Spy).Verdict);
        Assert.Equal(DecodeVerdict.Row2WrongAuthor, EnvelopeCodec.Decode(stranger, BobExpectsAlice, Spy).Verdict);
        Assert.Equal(0, decryptCalls);

        Assert.Equal(DecodeVerdict.Accepted, EnvelopeCodec.Decode(good, BobExpectsAlice, Spy).Verdict);
        Assert.Equal(1, decryptCalls);
    }

    [Fact]
    public void Plaintext_IsRelaxedEscaped()
    {
        string plaintext = EnvelopeCodec.ToPlaintext(SignedMove(Alice, "3c3>12+"));

        Assert.Contains("\"ptn\":\"3c3>12+\"", plaintext);
        Assert.DoesNotContain("\\u", plaintext);
        Assert.StartsWith("{\"pv\":1,\"action_type\":\"MOVE\",\"game_id\":\"0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e\",\"turn\":1,", plaintext);
    }

    [Fact]
    public void Encode_RefusesAnActionSignedForAnotherKey()
    {
        Assert.Throws<ArgumentException>(() => Encode(Alice, Bob.PublicKey, SignedMove(Carol)));
    }

    public static TheoryData<string> BadShapes() => ["kind", "three tags", "long p tag", "q instead of p", "h instead of g", "swapped tags"];

    [Theory]
    [MemberData(nameof(BadShapes))]
    public void WrongKindOrTagShape_IsNotForUs(string shape)
    {
        string content = Encode(Alice, Bob.PublicKey, SignedMove(Alice)).Content;
        string bob = Bob.PublicKey.ToHex();
        string gid = Game.ToString("D");
        int kind = shape == "kind" ? 3826 : EnvelopeCodec.Kind;
        IReadOnlyList<string>[] tags = shape switch
        {
            "three tags" => [new[] { "p", bob }, new[] { "g", gid }, new[] { "t", "tak" }],
            "long p tag" => [new[] { "p", bob, "wss://relay" }, new[] { "g", gid }],
            "q instead of p" => [new[] { "q", bob }, new[] { "g", gid }],
            "h instead of g" => [new[] { "p", bob }, new[] { "h", gid }],
            "swapped tags" => [new[] { "g", gid }, new[] { "p", bob }],
            _ => [new[] { "p", bob }, new[] { "g", gid }],
        };
        NostrEvent evt = NostrEvents.Sign(Alice, CreatedAt, kind, tags, content, Bytes("event aux"));

        AssertRejected(DecodeVerdict.NotForUs, EnvelopeCodec.Decode(evt, BobExpectsAlice));
    }

    [Fact]
    public void AddressedToSomeoneElse_IsNotForUs()
    {
        AssertRejected(DecodeVerdict.NotForUs, EnvelopeCodec.Decode(Encode(Alice, Carol.PublicKey, SignedMove(Alice)), BobExpectsAlice));
    }

    [Fact]
    public void OwnEventToSomeoneElse_OrWithABadPTag_IsNotForUs()
    {
        var aliceExpectsBob = new DecodeContext(Alice, Game, Bob.PublicKey);
        NostrEvent toCarol = Encode(Alice, Carol.PublicKey, SignedMove(Alice));
        NostrEvent badP = NostrEvents.Sign(Alice, CreatedAt, EnvelopeCodec.Kind,
            [new[] { "p", Bob.PublicKey.ToHex().ToUpperInvariant() }, new[] { "g", Game.ToString("D") }], toCarol.Content, Bytes("event aux"));

        AssertRejected(DecodeVerdict.NotForUs, EnvelopeCodec.Decode(toCarol, aliceExpectsBob));
        AssertRejected(DecodeVerdict.NotForUs, EnvelopeCodec.Decode(badP, aliceExpectsBob));
        // While inviting (no opponent yet) our own event to anyone still decodes.
        Assert.Equal(DecodeVerdict.Accepted, EnvelopeCodec.Decode(toCarol, new DecodeContext(Alice, Game, null)).Verdict);
    }

    public static TheoryData<string> BadFields() =>
    [
        "unknown action_type", "uppercase game_id", "game_id not a uuid", "negative turn", "string turn", "numeric pubkey",
        "null prev", "numeric signature", "action_data array", "action_data bool", "action_data float", "repeated action_data key",
        "repeated top-level key", "missing key",
    ];

    [Theory]
    [MemberData(nameof(BadFields))]
    public void MalformedField_IsRow4(string field)
    {
        string p = EnvelopeCodec.ToPlaintext(SignedMove(Alice));
        string pub = Alice.PublicKey.ToHex();
        string bad = field switch
        {
            "unknown action_type" => p.Replace("\"MOVE\"", "\"PASS\""),
            "uppercase game_id" => p.Replace(Game.ToString("D"), Game.ToString("D").ToUpperInvariant()),
            "game_id not a uuid" => p.Replace(Game.ToString("D"), "game-1"),
            "negative turn" => p.Replace("\"turn\":1", "\"turn\":-1"),
            "string turn" => p.Replace("\"turn\":1", "\"turn\":\"1\""),
            "numeric pubkey" => p.Replace($"\"{pub}\"", "7"),
            "null prev" => p.Replace($"\"{Genesis}\"", "null"),
            "numeric signature" => p[..p.IndexOf("\"signature\"", StringComparison.Ordinal)] + "\"signature\":1}",
            "action_data array" => p.Replace("{\"ptn\":\"a1\"}", "[\"a1\"]"),
            "action_data bool" => p.Replace("{\"ptn\":\"a1\"}", "{\"ptn\":true}"),
            "action_data float" => p.Replace("{\"ptn\":\"a1\"}", "{\"ptn\":1.5}"),
            "repeated action_data key" => p.Replace("{\"ptn\":\"a1\"}", "{\"ptn\":\"a1\",\"ptn\":\"a1\"}"),
            "repeated top-level key" => p.Replace("\"turn\":1,", "\"turn\":1,\"turn\":1,"),
            "missing key" => p.Replace("\"turn\":1,", ""),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        Assert.NotEqual(p, bad);

        AssertRejected(DecodeVerdict.Row4BadPayload, EnvelopeCodec.Decode(RawEvent(Alice, Bob.PublicKey, bad), BobExpectsAlice));
    }

    [Fact]
    public void Plaintext_HasAFixedLayout()
    {
        var accept = SignedGameAction.Unsigned(GameActionType.Accept, Game, 0, "ab", "cd",
            [new("white", ActionValue.Of("w")), new("komi", ActionValue.Of(5)), new("black", ActionValue.Of("b"))]) with
        { Signature = "ef" };

        Assert.Equal(
            "{\"pv\":1,\"action_type\":\"ACCEPT\",\"game_id\":\"0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e\",\"turn\":0,\"player_pubkey\":\"ab\"," +
            "\"prev_state_hash\":\"cd\",\"action_data\":{\"black\":\"b\",\"komi\":5,\"white\":\"w\"},\"signature\":\"ef\"}",
            EnvelopeCodec.ToPlaintext(accept));
    }

    /// <summary>Rejections after row 2 carry a reason for the flag log (F-038).</summary>
    private static void AssertRejected(DecodeVerdict expected, DecodeResult result)
    {
        Assert.Equal(expected, result.Verdict);
        Assert.Null(result.Action);
        Assert.False(string.IsNullOrEmpty(result.Detail), "a rejection must say why");
    }
}

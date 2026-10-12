using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TakEngine.Abstractions;

namespace TakEngine.Crypto.Tests;

public class ActionDigestTests
{
    private static readonly Guid Game = Guid.Parse("0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e");

    private static SecretKey Key(string label) => SecretKey.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes(label)));

    private static byte[] Aux(int n) => SHA256.HashData(Encoding.UTF8.GetBytes($"aux {n}"));

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (JsonElement c in Vectors().GetProperty("cases").EnumerateArray())
            data.Add(c.GetProperty("name").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Digest_MatchesGoldenVector(string name)
    {
        JsonElement root = Vectors();
        JsonElement c = root.GetProperty("cases").EnumerateArray().Single(e => e.GetProperty("name").GetString() == name);
        SecretKey key = SecretKey.FromHex(root.GetProperty("secret_key").GetString()!);
        Assert.Equal(root.GetProperty("pubkey").GetString(), key.PublicKey.ToHex());

        Assert.True(GameActionTypes.TryParseWire(c.GetProperty("action_type").GetString(), out GameActionType type));
        var data = c.GetProperty("action_data").EnumerateObject().Select(p => new KeyValuePair<string, ActionValue>(
            p.Name,
            p.Value.ValueKind == JsonValueKind.Number ? ActionValue.Of(p.Value.GetInt64()) : ActionValue.Of(p.Value.GetString()!)));
        var action = SignedGameAction.Unsigned(
            type,
            Guid.Parse(c.GetProperty("game_id").GetString()!),
            c.GetProperty("turn").GetInt32(),
            key.PublicKey.ToHex(),
            c.GetProperty("prev_state_hash").GetString()!,
            data);

        Assert.Equal(c.GetProperty("canonical_action_data").GetString(), ActionDigest.CanonicalActionData(action.ActionData));
        Assert.Equal(c.GetProperty("preimage").GetString(), ActionDigest.Preimage(action));
        Assert.Equal(c.GetProperty("digest").GetString(), Convert.ToHexStringLower(ActionDigest.Compute(action)));

        SignedGameAction signed = ActionDigest.Sign(key, action, Convert.FromHexString(c.GetProperty("aux").GetString()!));
        Assert.Equal(c.GetProperty("signature").GetString(), signed.Signature);
        Assert.True(ActionDigest.Verify(signed));
    }

    [Fact]
    public void CanonicalActionData_SortsKeys_AndUsesRelaxedEscaping()
    {
        var data = SignedGameAction.Unsigned(GameActionType.Accept, Game, 0, "p", "h", [
            new("size", ActionValue.Of(5)),
            new("b", ActionValue.Of("x+<>&\"y")),
            new("a", ActionValue.Of(0)),
            new("B", ActionValue.Of("upper")),
        ]).ActionData;

        Assert.Equal("{\"B\":\"upper\",\"a\":0,\"b\":\"x+<>&\\\"y\",\"size\":5}", ActionDigest.CanonicalActionData(data));
    }

    [Fact]
    public void CanonicalActionData_Empty_IsEmptyObject()
    {
        Assert.Equal("{}", ActionDigest.CanonicalActionData(new Dictionary<string, ActionValue>()));
    }

    [Fact]
    public void SignAndVerify_TwoIndependentKeys()
    {
        SecretKey alice = Key("alice");
        SecretKey bob = Key("bob");
        var action = SignedGameAction.UnsignedMove(Game, 1, alice.PublicKey.ToHex(), new string('0', 64), "a1");

        SignedGameAction signed = ActionDigest.Sign(alice, action, Aux(1));
        Assert.True(ActionDigest.Verify(signed)); // Bob's side: only Alice's public key is used

        SignedGameAction byBob = ActionDigest.Sign(bob, action, Aux(1)); // claims Alice's key, signed by Bob
        Assert.False(ActionDigest.Verify(byBob));
    }

    public static TheoryData<string> Mutations() =>
    [
        "pv", "type", "gameId", "turn", "pubkey", "prevHash", "ptn", "extraKey", "integerValue",
    ];

    [Theory]
    [MemberData(nameof(Mutations))]
    public void AnyFieldChange_BreaksVerification(string field)
    {
        SecretKey alice = Key("alice");
        SecretKey other = Key("other");
        var action = SignedGameAction.Unsigned(GameActionType.Move, Game, 3, alice.PublicKey.ToHex(), new string('a', 64), [
            new("ptn", ActionValue.Of("c3")),
            new("n", ActionValue.Of(7)),
        ]);
        SignedGameAction signed = ActionDigest.Sign(alice, action, Aux(2));
        Assert.True(ActionDigest.Verify(signed));

        SignedGameAction changed = field switch
        {
            "pv" => signed with { Pv = 2 },
            "type" => signed with { ActionType = GameActionType.Resign },
            "gameId" => signed with { GameId = Guid.Parse("00000000-0000-0000-0000-000000000001") },
            "turn" => signed with { Turn = 4 },
            "pubkey" => signed with { PlayerPubKey = other.PublicKey.ToHex() },
            "prevHash" => signed with { PrevStateHash = new string('b', 64) },
            "ptn" => signed with { ActionData = With(signed.ActionData, "ptn", ActionValue.Of("c4")) },
            "extraKey" => signed with { ActionData = With(signed.ActionData, "x", ActionValue.Of("")) },
            "integerValue" => signed with { ActionData = With(signed.ActionData, "n", ActionValue.Of(8)) },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.False(ActionDigest.Verify(changed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("zz")]
    [InlineData("not hex at all, but exactly one hundred and twenty eight characters long, padded out with more filler text...")]
    public void Verify_MalformedSignature_ReturnsFalse_NeverThrows(string signature)
    {
        SecretKey alice = Key("alice");
        var action = SignedGameAction.UnsignedMove(Game, 1, alice.PublicKey.ToHex(), new string('0', 64), "a1") with { Signature = signature };
        Assert.False(ActionDigest.Verify(action));
        Assert.False(ActionDigest.Verify(action with { Signature = new string('g', 128) }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    public void Verify_MalformedPubkey_ReturnsFalse_NeverThrows(string pubkey)
    {
        SecretKey alice = Key("alice");
        SignedGameAction signed = ActionDigest.Sign(alice, SignedGameAction.UnsignedMove(Game, 1, alice.PublicKey.ToHex(), "h", "a1"), Aux(3));
        Assert.False(ActionDigest.Verify(signed with { PlayerPubKey = pubkey }));
        Assert.False(ActionDigest.Verify(null));
    }

    private static Dictionary<string, ActionValue> With(IReadOnlyDictionary<string, ActionValue> data, string key, ActionValue value)
    {
        var copy = data.ToDictionary(p => p.Key, p => p.Value);
        copy[key] = value;
        return copy;
    }

    private static JsonElement Vectors()
    {
        using JsonDocument doc = JsonDocument.Parse(VectorFiles.ReadText("action-digest-vectors.json"));
        return doc.RootElement.Clone();
    }
}

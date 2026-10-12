using System.Globalization;
using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Tests;

/// <summary>F-032: NIP-01 ids equal the ids an independent implementation computed, for every escaping edge case.</summary>
public class Nip01SerializerTests
{
    public static TheoryData<string> VectorNames()
    {
        var data = new TheoryData<string>();
        foreach (Nip01Vector v in Nip01VectorFiles.Generated().Events)
            data.Add(v.Name);
        return data;
    }

    private static Nip01Vector Vector(string name) => Nip01VectorFiles.Generated().Events.Single(v => v.Name == name);

    [Fact]
    public void VectorFile_CoversTheEdgeCasesFromTheTestStrategy()
    {
        string[] expected =
        [
            "plain", "json-specials", "newline-cr-tab", "backspace-formfeed", "u0001-u001f", "del",
            "emoji-surrogate-pair", "u2028-u2029", "non-ascii-tag", "empty-content", "empty-tags", "kind-3825-envelope",
        ];
        Assert.Equal(expected, Nip01VectorFiles.Generated().Events.Select(v => v.Name));
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Serialize_MatchesTheIndependentSerialization(string name)
    {
        Nip01Vector v = Vector(name);
        string pubkey = Nip01VectorFiles.Generated().PubkeyHex;

        Assert.Equal(v.Serialized, Nip01Serializer.Serialize(pubkey, v.CreatedAt, v.Kind, v.Tags, v.Content));
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void ComputeId_MatchesTheIndependentId(string name)
    {
        Nip01Vector v = Vector(name);
        var evt = new NostrEvent
        {
            Pubkey = Nip01VectorFiles.Generated().PubkeyHex,
            CreatedAt = v.CreatedAt,
            Kind = v.Kind,
            Tags = v.Tags,
            Content = v.Content,
        };

        Assert.Equal(v.Id, Nip01Serializer.ComputeId(evt));
    }

    [Theory]
    [InlineData("spike-csharp-events.jsonl", 5)]
    [InlineData("spike-python-events.jsonl", 3)]
    public void ComputeId_MatchesTheSpikesPythonVerifiedEvents(string file, int count)
    {
        IReadOnlyList<NostrEvent> events = Nip01VectorFiles.SignedEvents(file);

        Assert.Equal(count, events.Count);
        Assert.All(events, e => Assert.Equal(e.Id, Nip01Serializer.ComputeId(e)));
    }

    [Fact]
    public void Serialize_IsCultureInvariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // A culture whose negative sign is not '-' (U+2212) would corrupt created_at if the serializer used it.
            var culture = (CultureInfo)CultureInfo.GetCultureInfo("sv-SE").Clone();
            culture.NumberFormat.NegativeSign = "−";
            CultureInfo.CurrentCulture = culture;

            Assert.Equal("[0,\"ab\",-5,1,[],\"\"]", Nip01Serializer.Serialize("ab", -5, 1, [], ""));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Serialize_EscapesLoneSurrogatesLikeJsonStringify()
    {
        // Expected output is node's JSON.stringify([0,"ab",1,1,[["t","x\ud83d"]],"a\udc00b\ud83d\ude00"]): lone surrogates
        // become lowercase \uXXXX escapes, a valid pair stays a raw character. UTF-8 encoding would otherwise turn a lone
        // surrogate into U+FFFD and give a different id than the JS client that signed the event.
        Assert.Equal(
            "[0,\"ab\",1,1,[[\"t\",\"x\\ud83d\"]],\"a\\udc00b\ud83d\ude00\"]",
            Nip01Serializer.Serialize("ab", 1, 1, [["t", "x\ud83d"]], "a\udc00b\ud83d\ude00"));
    }

    [Fact]
    public void Serialize_RejectsNullTagsAndTagValues()
    {
        List<List<string>> nullValue = [["p", null!]];
        List<List<string>> nullTag = [null!];

        Assert.ThrowsAny<ArgumentException>(() => Nip01Serializer.Serialize("ab", 1, 1, nullValue, "x"));
        Assert.ThrowsAny<ArgumentException>(() => Nip01Serializer.Serialize("ab", 1, 1, nullTag, "x"));
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Nip01Serializer.Serialize(null!, 1, 1, [], "x"));
        Assert.Throws<ArgumentNullException>(() => Nip01Serializer.Serialize("ab", 1, 1, null!, "x"));
        Assert.Throws<ArgumentNullException>(() => Nip01Serializer.Serialize("ab", 1, 1, [], null!));
        Assert.Throws<ArgumentNullException>(() => Nip01Serializer.Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => Nip01Serializer.ComputeId(null!));
    }
}

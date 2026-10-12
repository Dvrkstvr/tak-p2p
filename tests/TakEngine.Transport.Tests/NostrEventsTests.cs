using System.Text.Json;
using TakEngine.Crypto;
using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Tests;

/// <summary>F-032: events are signed with BIP-340 over the NIP-01 id and verified before anything else is done with them.</summary>
public class NostrEventsTests
{
    private static readonly SecretKey Alice = SecretKey.FromHex("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");
    private static readonly SecretKey Bob = SecretKey.FromHex("c90fdaa22168c234c4c6628b80dc1cd129024e088a67cc74020bbea63b14e5c9");

    public static TheoryData<string> VectorNames()
    {
        var data = new TheoryData<string>();
        foreach (Nip01Vector v in Nip01VectorFiles.Generated().Events)
            data.Add(v.Name);
        return data;
    }

    private static NostrEvent SignedByAlice(string content = "AgAAAA+/==") =>
        NostrEvents.Sign(Alice, 1_700_000_000, 3825, [["p", Bob.PublicKey.ToHex()], ["g", "0b7e3d4c-1f2a-4b5c-8d9e-0f1a2b3c4d5e"]], content, new byte[32]);

    private static NostrEvent Copy(NostrEvent e) => JsonSerializer.Deserialize<NostrEvent>(JsonSerializer.Serialize(e))!;

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Sign_ProducesExactlyThePythonReferenceEvent(string name)
    {
        var (secretHex, pubkeyHex, events) = Nip01VectorFiles.Generated();
        Nip01Vector v = events.Single(e => e.Name == name);

        NostrEvent signed = NostrEvents.Sign(SecretKey.FromHex(secretHex), v.CreatedAt, v.Kind, v.Tags, v.Content, Convert.FromHexString(v.Aux));

        Assert.Equal(pubkeyHex, signed.Pubkey);
        Assert.Equal(v.Id, signed.Id);
        Assert.Equal(v.Sig, signed.Sig); // byte-identical to what the BIP-340 reference produced, so the reference verifies it
        Assert.True(NostrEvents.Verify(signed));
    }

    [Theory]
    [InlineData("spike-csharp-events.jsonl")]
    [InlineData("spike-python-events.jsonl")]
    public void Verify_AcceptsTheSpikesIndependentlyVerifiedEvents(string file)
    {
        Assert.All(Nip01VectorFiles.SignedEvents(file), e => Assert.True(NostrEvents.Verify(e), e.Content));
    }

    [Fact]
    public void Sign_SetsKindTagsContentAndCreatedAt_AndCopiesTheTags()
    {
        List<List<string>> tags = [["p", Bob.PublicKey.ToHex()]];

        NostrEvent e = NostrEvents.Sign(Alice, 42, 3825, tags, "c", new byte[32]);
        tags[0][1] = "changed";

        Assert.Equal(42, e.CreatedAt);
        Assert.Equal(3825, e.Kind);
        Assert.Equal("c", e.Content);
        Assert.Equal(Bob.PublicKey.ToHex(), e.Tags[0][1]);
        Assert.True(NostrEvents.Verify(e));
    }

    [Fact]
    public void FlippedIdCharacter_FailsVerification()
    {
        NostrEvent e = Copy(SignedByAlice());
        e.Id = (e.Id[0] == '0' ? "1" : "0") + e.Id[1..];

        Assert.False(NostrEvents.Verify(e));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(127)]
    public void FlippedSignatureBit_FailsVerification(int hexIndex)
    {
        NostrEvent e = Copy(SignedByAlice());
        char[] sig = e.Sig.ToCharArray();
        int nibble = Convert.ToInt32(sig[hexIndex].ToString(), 16) ^ 1;
        sig[hexIndex] = "0123456789abcdef"[nibble];
        e.Sig = new string(sig);

        Assert.False(NostrEvents.Verify(e));
    }

    [Theory]
    [InlineData("content")]
    [InlineData("created_at")]
    [InlineData("kind")]
    [InlineData("tag")]
    [InlineData("pubkey")]
    public void TamperedField_FailsVerification(string field)
    {
        NostrEvent e = Copy(SignedByAlice());
        switch (field)
        {
            case "content": e.Content += "x"; break;
            case "created_at": e.CreatedAt += 1; break;
            case "kind": e.Kind = 1; break;
            case "tag": e.Tags[0][1] = Alice.PublicKey.ToHex(); break;
            case "pubkey": e.Pubkey = Bob.PublicKey.ToHex(); break;
        }

        Assert.False(NostrEvents.Verify(e));
    }

    [Theory]
    [InlineData(62)]  // 31 bytes
    [InlineData(66)]  // 33 bytes
    [InlineData(0)]
    public void PubkeyOfWrongLength_FailsVerification(int hexLength)
    {
        NostrEvent e = Copy(SignedByAlice());
        e.Pubkey = (e.Pubkey + e.Pubkey)[..hexLength];

        Assert.False(NostrEvents.Verify(e));
    }

    [Fact]
    public void UppercasePubkeyOrId_FailsVerification()
    {
        NostrEvent upperPub = Copy(SignedByAlice());
        upperPub.Pubkey = upperPub.Pubkey.ToUpperInvariant();
        NostrEvent upperId = Copy(SignedByAlice());
        upperId.Id = upperId.Id.ToUpperInvariant();

        Assert.False(NostrEvents.Verify(upperPub));
        Assert.False(NostrEvents.Verify(upperId));
    }

    [Fact]
    public void OffCurvePubkey_FailsVerification()
    {
        NostrEvent e = Copy(SignedByAlice());
        e.Pubkey = "eefdea4cdb677750a420fee807eacf21eb9898ae79b9768766e4faa04a2d4a34";

        Assert.False(NostrEvents.Verify(e));
    }

    [Theory]
    [InlineData("""{"id":null,"pubkey":"a","created_at":1,"kind":1,"tags":[],"content":"","sig":"b"}""")]
    [InlineData("""{"pubkey":"a","created_at":1,"kind":1,"tags":[],"content":""}""")]
    [InlineData("""{"id":"00","pubkey":null,"created_at":1,"kind":1,"tags":null,"content":null,"sig":null}""")]
    [InlineData("""{"id":"zz","pubkey":"zz","created_at":1,"kind":1,"tags":[["p",null]],"content":"","sig":"zz"}""")]
    public void RelayJunk_FailsVerification_WithoutThrowing(string json)
    {
        NostrEvent? e = JsonSerializer.Deserialize<NostrEvent>(json);

        Assert.False(NostrEvents.Verify(e));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("sig")]
    [InlineData("content")]
    [InlineData("tags")]
    public void ExactlyOneNullField_InAnOtherwiseValidEvent_FailsVerification_WithoutThrowing(string field)
    {
        NostrEvent e = Copy(SignedByAlice());
        Assert.True(NostrEvents.Verify(e)); // the copy is valid until one field is nulled
        switch (field)
        {
            case "id": e.Id = null!; break;
            case "sig": e.Sig = null!; break;
            case "content": e.Content = null!; break;
            case "tags": e.Tags = null!; break;
        }

        Assert.False(NostrEvents.Verify(e));
    }

    [Fact]
    public void NullTagValue_InAnOtherwiseValidEvent_FailsVerification()
    {
        NostrEvent e = Copy(SignedByAlice());
        e.Tags[1][1] = null!;

        Assert.False(NostrEvents.Verify(e));
    }

    [Fact]
    public void Sign_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => NostrEvents.Sign(null!, 1, 1, [], "c", new byte[32]));
        Assert.Throws<ArgumentNullException>(() => NostrEvents.Sign(Alice, 1, 1, null!, "c", new byte[32]));
        Assert.Throws<ArgumentNullException>(() => NostrEvents.Sign(Alice, 1, 1, [], null!, new byte[32]));
    }

    [Fact]
    public void NonHexSignature_FailsVerification()
    {
        NostrEvent e = Copy(SignedByAlice());
        e.Sig = new string('z', 128);

        Assert.False(NostrEvents.Verify(e));
        Assert.False(NostrEvents.Verify(null));
    }
}

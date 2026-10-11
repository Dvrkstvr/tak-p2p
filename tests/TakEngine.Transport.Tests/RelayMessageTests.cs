using TakEngine.Transport.Nostr;

namespace TakEngine.Transport.Tests;

public class RelayMessageTests
{
    private static NostrEvent SampleEvent() => new()
    {
        Id = "aa",
        Pubkey = "bb",
        CreatedAt = 1700000000,
        Kind = 3825,
        Tags = [["p", "cc"], ["g", "dd"]],
        Content = "x+y<z>&\"q\"",
        Sig = "ee",
    };

    [Fact]
    public void Publish_FormatsAndParses()
    {
        string frame = new RelayMessage.Publish(SampleEvent()).Format();

        Assert.StartsWith("[\"EVENT\",{", frame);
        Assert.Contains("x+y<z>&", frame); // relaxed escaping
        var parsed = Assert.IsType<RelayMessage.Publish>(RelayMessage.Parse(frame));
        Assert.Equal("x+y<z>&\"q\"", parsed.NostrEvent.Content);
        Assert.Equal([["p", "cc"], ["g", "dd"]], parsed.NostrEvent.Tags);
    }

    [Fact]
    public void RelayEvent_FormatsAndParses()
    {
        string frame = new RelayMessage.Event("sub1", SampleEvent()).Format();

        var parsed = Assert.IsType<RelayMessage.Event>(RelayMessage.Parse(frame));
        Assert.Equal("sub1", parsed.SubscriptionId);
        Assert.Equal(3825, parsed.NostrEvent.Kind);
        Assert.Equal(1700000000, parsed.NostrEvent.CreatedAt);
    }

    [Fact]
    public void Req_WithEveryFilterField_RoundTrips()
    {
        var f1 = new NostrFilter { Kinds = [3825], Authors = ["a1"], TagP = ["p1"], TagG = ["g1"], Since = 10, Limit = 500 };
        var f2 = new NostrFilter { Ids = ["i1", "i2"], Until = 20 };
        string frame = new RelayMessage.Req("tak-0b7e3d4c", [f1, f2]).Format();

        Assert.Equal(
            "[\"REQ\",\"tak-0b7e3d4c\",{\"authors\":[\"a1\"],\"kinds\":[3825],\"#p\":[\"p1\"],\"#g\":[\"g1\"],\"since\":10,\"limit\":500},{\"ids\":[\"i1\",\"i2\"],\"until\":20}]",
            frame);
        var parsed = Assert.IsType<RelayMessage.Req>(RelayMessage.Parse(frame));
        Assert.Equal("tak-0b7e3d4c", parsed.SubscriptionId);
        Assert.Equal(2, parsed.Filters.Count);
        Assert.Equal(["g1"], parsed.Filters[0].TagG!);
        Assert.Equal(["p1"], parsed.Filters[0].TagP!);
        Assert.Equal(500, parsed.Filters[0].Limit);
        Assert.Equal(["i1", "i2"], parsed.Filters[1].Ids!);
    }

    [Fact]
    public void Close_Eose_Notice_RoundTrip()
    {
        Assert.Equal(new RelayMessage.Close("s"), RelayMessage.Parse(new RelayMessage.Close("s").Format()));
        Assert.Equal(new RelayMessage.Eose("s"), RelayMessage.Parse("[\"EOSE\",\"s\"]"));
        Assert.Equal(new RelayMessage.Notice("hi"), RelayMessage.Parse("[\"NOTICE\",\"hi\"]"));
    }

    [Theory]
    [InlineData("[\"OK\",\"id1\",true,\"\"]", true, "", "")]
    [InlineData("[\"OK\",\"id1\",false,\"rate-limited: slow down\"]", false, "rate-limited: slow down", "rate-limited")]
    [InlineData("[\"OK\",\"id1\",false,\"invalid: bad sig\"]", false, "invalid: bad sig", "invalid")]
    [InlineData("[\"OK\",\"id1\",true]", true, "", "")]
    public void Ok_ParsesAcceptanceAndReasonPrefix(string frame, bool accepted, string message, string prefix)
    {
        var ok = Assert.IsType<RelayMessage.Ok>(RelayMessage.Parse(frame));
        Assert.Equal("id1", ok.EventId);
        Assert.Equal(accepted, ok.Accepted);
        Assert.Equal(message, ok.Message);
        Assert.Equal(prefix, ok.Prefix);
    }

    [Fact]
    public void Closed_And_Auth_Parse()
    {
        var closed = Assert.IsType<RelayMessage.Closed>(RelayMessage.Parse("[\"CLOSED\",\"s\",\"auth-required: sign in\"]"));
        Assert.Equal("auth-required", closed.Prefix);
        Assert.Equal(new RelayMessage.AuthChallenge("xyz"), RelayMessage.Parse("[\"AUTH\",\"xyz\"]"));
        Assert.IsType<RelayMessage.AuthResponse>(RelayMessage.Parse(new RelayMessage.AuthResponse(SampleEvent()).Format()));
        Assert.Equal("[\"OK\",\"e\",false,\"blocked: no\"]", new RelayMessage.Ok("e", false, "blocked: no").Format());
        Assert.Equal("[\"CLOSED\",\"s\",\"auth-required: x\"]", new RelayMessage.Closed("s", "auth-required: x").Format());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[1,2]")]
    [InlineData("[\"WHAT\",\"x\"]")]
    [InlineData("[\"EVENT\"]")]
    [InlineData("[\"EVENT\",\"s\",\"not an object\"]")]
    [InlineData("[\"EVENT\",\"s\",{\"kind\":\"not a number\"}]")]
    [InlineData("[\"OK\",\"id\",\"yes\",\"\"]")]
    [InlineData("[\"REQ\",\"s\",\"not a filter\"]")]
    [InlineData("[\"EOSE\",1]")]
    [InlineData("[\"CLOSE\"]")]
    public void Junk_IsUnknown_NeverThrows(string? raw)
    {
        Assert.IsType<RelayMessage.Unknown>(RelayMessage.Parse(raw));
    }
}

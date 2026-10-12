using Spectre.Console;
using TakApp.Cli.Online;
using TakEngine.Abstractions;
using TakEngine.Core.Text;
using TakEngine.Transport.Relay;

namespace TakApp.Cli.Tests;

/// <summary>
/// Security review M0 finding 1: text the opponent controls (flag reasons that echo their JSON keys or PTN, relay reasons,
/// nicknames) must not reach the terminal with control characters or bidi overrides, in <c>--plain</c> or rich mode.
/// </summary>
public class PeerTextTests
{
    private const string Hostile = "unknown key \"\u001b]0;pwned\u0007\u001b[2J FINAL white-road deadbeef\u001b[8m\u0085\u009b31m\u007f\"";
    private const string Bidi = "move \u202Ea1\u202C \u2066b2\u2069 \u202A\u202B\u202D\u2067\u2068";
    private static readonly Uri R1 = new("wss://nos.lol");

    private static void AssertPrintable(string output)
    {
        foreach (char c in output.Replace(Environment.NewLine, "\n", StringComparison.Ordinal))
        {
            if (c == '\n')
                continue;
            Assert.False(char.IsControl(c), $"control character U+{(int)c:X4} in: {output}");
            Assert.False(c is (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069'), $"bidi override U+{(int)c:X4} in: {output}");
        }
    }

    private static void EveryPeerPath(IGameOutput o)
    {
        o.Flag(4, Hostile);
        o.Flag(5, Bidi);
        o.Notice(Hostile);
        o.Rejected(3, Hostile);
        o.Waiting(Hostile);
        o.Applied(2, "a1\u001b[2J", new string('0', 64), "x5/x5/x5/x5/x5 1 1", 0, false);
        o.Sent(6, new Dictionary<Uri, RelayPublishState> { [R1] = new(RelayPublishStatus.Refused, Hostile) });
        o.Invite("TAK2_\u001b[2J");
    }

    [Fact]
    public void Plain_AFlagReasonWithEscAndBel_ComesOutWithNoControlCharacters()
    {
        var text = new StringWriter();
        new PlainOutput(text).Flag(4, "bad key \u001b[2J FINAL white-road \u0007");

        string line = Assert.Single(text.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        AssertPrintable(line);
        Assert.StartsWith("FLAG turn 4 bad key ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Plain_EveryPathIsSanitized()
    {
        var text = new StringWriter();
        EveryPeerPath(new PlainOutput(text));

        AssertPrintable(text.ToString());
        Assert.Equal(8, text.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private static (RichOutput Output, StringWriter Text) Rich()
    {
        var text = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(text),
            // Spectre's CI enrichers (GitHub Actions among them) switch ANSI back on, so the styling escapes would fail the check.
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
        });
        console.Profile.Width = 200; // a StringWriter has no width, and Spectre writes nothing at -1
        return (new RichOutput(console), text);
    }

    [Fact]
    public void Rich_AFlagReasonWithEscAndBel_ComesOutWithNoControlCharacters()
    {
        (RichOutput output, StringWriter text) = Rich();
        output.Flag(4, "bad key \u001b[2J FINAL white-road \u0007");

        AssertPrintable(text.ToString());
        Assert.Contains("Rejected a move from your opponent at turn 4", text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rich_EveryPathIsSanitized()
    {
        (RichOutput output, StringWriter text) = Rich();
        EveryPeerPath(output);

        AssertPrintable(text.ToString());
        foreach (string shown in new[] { "turn 5:", "opponent played", "refused by nos.lol", "Not played:", "Invite code" })
            Assert.Contains(shown, text.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a1", "a1")]
    [InlineData("x\u001by", "x\\u001By")]
    [InlineData("bell\u0007", "bell\\u0007")]
    [InlineData("del\u007f c1\u009b nel\u0085", "del\\u007F c1\\u009B nel\\u0085")]
    [InlineData("rlo\u202Eoff\u202C iso\u2066\u2069", "rlo\\u202Eoff\\u202C iso\\u2066\\u2069")]
    [InlineData("two\nlines\r\tand\u2028sep", "two lines  and sep")]
    [InlineData("", "")]
    public void DisplayText_EscapesControlAndBidi_KeepsTheRest(string input, string expected) =>
        Assert.Equal(expected, DisplayText.Sanitize(input));

    [Fact]
    public void DisplayText_Null_IsEmpty() => Assert.Equal("", DisplayText.Sanitize(null));
}

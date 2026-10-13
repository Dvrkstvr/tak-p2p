using TakEngine.Abstractions;
using TakEngine.Transport.Matchmaking;

namespace TakApp.Cli.Online;

/// <summary>
/// <c>host [--size N] [--komi H]</c>, <c>join &lt;invite&gt;</c>, <c>resume [&lt;game id&gt; | --invite &lt;code&gt;]</c>, each with
/// <c>[--profile NAME] [--data-dir DIR] [--relays CSV] [--plain]</c>.
/// </summary>
public sealed record OnlineArgs(
    string Command,
    string? Target = null,
    string? Profile = null,
    string? DataDir = null,
    IReadOnlyList<string>? Relays = null,
    BoardSize Size = BoardSize.Five,
    int KomiHalves = 0,
    bool Plain = false,
    string? Invite = null)
{
    public const string Usage =
        "usage: tak host [--size 3-8] [--komi HALF_FLATS] | join <invite> | resume [<game id> | --invite <code>]\n"
        + "       options: --profile NAME, --data-dir DIR, --relays wss://a,wss://b, --plain";

    public static readonly IReadOnlyList<string> Commands = ["host", "join", "resume"];

    /// <summary>Null (with <paramref name="error"/>) when the arguments don't parse.</summary>
    public static OnlineArgs? Parse(IReadOnlyList<string> args, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        error = null;
        if (args.Count == 0 || !Commands.Contains(args[0]))
        {
            error = Usage;
            return null;
        }
        var a = new OnlineArgs(args[0]);
        for (int i = 1; i < args.Count; i++)
        {
            string arg = args[i];
            string? next = i + 1 < args.Count ? args[i + 1] : null;
            bool takesValue = arg is "--profile" or "--data-dir" or "--relays" or "--size" or "--komi" or "--invite";
            if (takesValue && next is null)
                return Fail($"{arg} needs a value.", out error);
            switch (arg)
            {
                case "--plain":
                    a = a with { Plain = true };
                    continue;
                case "--profile" when !IsProfileName(next!):
                    return Fail("A profile name is letters, digits, '-' and '_' only.", out error);
                case "--profile":
                    a = a with { Profile = next };
                    break;
                case "--data-dir":
                    a = a with { DataDir = next };
                    break;
                case "--relays":
                    a = a with { Relays = next!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) };
                    if (CheckRelays(a.Relays) is { } relayError)
                        return Fail(relayError, out error);
                    break;
                case "--size" when int.TryParse(next, out int size) && size is >= 3 and <= 8:
                    a = a with { Size = (BoardSize)size };
                    break;
                case "--size":
                    return Fail("--size is 3 to 8.", out error);
                case "--komi" when int.TryParse(next, out int komi) && komi is >= 0 and <= 20:
                    a = a with { KomiHalves = komi };
                    break;
                case "--komi":
                    return Fail("--komi is 0 to 20 half flats.", out error);
                case "--invite":
                    a = a with { Invite = next };
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal) || a.Target is not null)
                        return Fail($"Unknown argument '{arg}'.", out error);
                    a = a with { Target = arg };
                    continue;
            }
            i++;
        }
        if (a.Command == "join" && a.Target is null)
            return Fail("join needs the invite code.", out error);
        if (a.Command == "host" && a.Target is not null)
            return Fail($"Unknown argument '{a.Target}'.", out error);
        return a;
    }

    /// <summary>
    /// Null if every relay may go into an invite, else the message to show. It is the invite's own rule
    /// (<see cref="InviteCode.Validate"/>: wss://, or plain ws:// on 127.0.0.1 or localhost for tools/TakRelay.Local), checked
    /// on a probe invite so the two can't disagree (code review M0, finding 3).
    /// </summary>
    public static string? CheckRelays(IReadOnlyList<string> relays)
    {
        ArgumentNullException.ThrowIfNull(relays);
        if (relays.Count == 0)
            return "--relays needs at least one wss:// URL.";
        foreach (string relay in relays)
        {
            try
            {
                new InviteCode(ProbeGameId, ProbeHostKey, BoardSize.Five, 0, [relay], ProbeSeedCommit).Validate();
            }
            catch (InviteFormatException)
            {
                return $"The relay '{relay}' can't go into an invite: use a wss:// URL (plain ws:// only on 127.0.0.1 or localhost).";
            }
        }
        return null;
    }

    // A well-formed invite apart from the relay under test: the secp256k1 generator's x coordinate is a valid x-only key.
    private static readonly Guid ProbeGameId = new("00000000-0000-0000-0000-000000000001");
    private const string ProbeHostKey = "79be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798";
    private static readonly string ProbeSeedCommit = new('0', 64);

    private static bool IsProfileName(string name) =>
        name.Length is > 0 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static OnlineArgs? Fail(string message, out string? error)
    {
        error = message + "\n" + Usage;
        return null;
    }
}

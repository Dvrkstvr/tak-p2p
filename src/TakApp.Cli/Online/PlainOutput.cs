using TakEngine.Abstractions;
using TakEngine.Core.Text;
using TakEngine.Transport.Relay;

namespace TakApp.Cli.Online;

/// <summary>
/// The <c>--plain</c> contract (pipeline/playbook.md "Verify method"): one event per line, a keyword first, fields split by single
/// spaces; free text comes last. Agents and the two-process tests read these lines, so a change here needs its test. Every line
/// goes through <see cref="DisplayText.Sanitize"/> on its way out: much of it (flag reasons, PTN, relay reasons) is the
/// opponent's or a relay's text, and must not reach the terminal with control characters in it.
/// </summary>
public sealed class PlainOutput(TextWriter writer) : IGameOutput
{
    private readonly object _gate = new();

    public void Invite(string code) => Line($"INVITE {code}");

    public void Relay(Uri url, RelayLinkState state) => Line($"RELAY {url.ToString().TrimEnd('/')} {State(state)}");

    public void Waiting(string text) => Line($"WAITING {OneLine(text)}");

    public void Sent(int turn, IReadOnlyDictionary<Uri, RelayPublishState> perRelay) =>
        Line($"SENT turn {turn} " + string.Join(' ', perRelay.OrderBy(p => p.Key.ToString(), StringComparer.Ordinal)
            .Select(p => $"{p.Key.ToString().TrimEnd('/')}:{Status(p.Value)}")));

    public void Applied(int turn, string ptn, string hashAfter, string tpsAfter, int komiHalves, bool mine) =>
        Line($"APPLIED turn {turn} {ptn} {hashAfter}");

    public void Rejected(int turn, string reason) => Line($"REJECTED turn {turn} {OneLine(reason)}");

    public void Flag(int turn, string reason) => Line($"FLAG turn {turn} {OneLine(reason)}");

    public void Notice(string text) => Line($"NOTICE {OneLine(text)}");

    public void Final(GameResult result, string hash) => Line($"FINAL {Result(result)} {hash}");

    /// <summary><c>white-road</c>, <c>black-flats</c>, <c>draw-flats</c>, <c>white-resign</c> (the winner; the resigner lost).</summary>
    public static string Result(GameResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string who = result.IsDraw || result.Winner is null ? "draw" : result.Winner.Value.ToString().ToLowerInvariant();
        string why = result.Reason switch
        {
            GameEndReason.Road => "road",
            GameEndReason.FlatCount => "flats",
            GameEndReason.Resignation => "resign",
            GameEndReason.TimeoutDraw => "timeout",
            GameEndReason.Agreement => "agreement",
            _ => result.Reason.ToString().ToLowerInvariant(),
        };
        return $"{who}-{why}";
    }

    /// <summary><c>ok</c>, <c>pending</c>, or <c>refused(reason)</c> with the relay's reason, spaces as underscores.</summary>
    public static string Status(RelayPublishState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Status switch
        {
            RelayPublishStatus.Sent => "ok",
            RelayPublishStatus.Refused => $"refused({string.Join('_', (state.Reason ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))})",
            _ => state.Reason is null ? "pending" : $"pending({string.Join('_', state.Reason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))})",
        };
    }

    private static string State(RelayLinkState state) => state switch
    {
        RelayLinkState.CaughtUp => "caught-up",
        _ => state.ToString().ToLowerInvariant(),
    };

    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private void Line(string line)
    {
        lock (_gate)
        {
            writer.WriteLine(DisplayText.Sanitize(line));
            writer.Flush();
        }
    }
}

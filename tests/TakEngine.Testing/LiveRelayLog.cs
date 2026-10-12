using System.Text;

namespace TakEngine.Testing;

/// <summary>
/// The M0 live-relay event log (D-032): every event a LiveRelay test publishes to a public relay is a row in
/// <c>pipeline/evidence/live-relay-log.md</c>. <see cref="Reserve"/> refuses a run that would take M0 past <see cref="Budget"/>
/// events, so the cap holds before anything is sent.
/// </summary>
public static class LiveRelayLog
{
    public const int Budget = 150;

    private static readonly Lock Gate = new();

    public static string FilePath => Path.Combine(RepoRoot(), "pipeline", "evidence", "live-relay-log.md");

    /// <summary>Events logged so far (one row each).</summary>
    public static int Total()
    {
        lock (Gate) return File.Exists(FilePath) ? File.ReadLines(FilePath).Count(l => l.StartsWith("| 20", StringComparison.Ordinal)) : 0;
    }

    /// <summary>Throws unless <paramref name="events"/> more still fit in the budget.</summary>
    public static void Reserve(int events)
    {
        int total = Total();
        if (total + events > Budget)
            throw new InvalidOperationException($"Live budget: {total} of {Budget} events used; this run needs {events}. Ask the user first (D-032).");
    }

    /// <summary>Appends one row: when, which test, the event id, and each relay's answer.</summary>
    public static void Record(string test, string eventId, string outcomes)
    {
        lock (Gate)
        {
            if (!File.Exists(FilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, """
                    # Live-relay event log (M0, D-032)

                    Every event published to a public relay by a LiveRelay test or a hand run. Budget: 150 events across M0.

                    | when (UTC) | test | event id | relay answers |
                    |---|---|---|---|

                    """.Replace("\r\n", "\n", StringComparison.Ordinal), Encoding.UTF8);
            }
            File.AppendAllText(FilePath, $"| {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} | {test} | {eventId} | {outcomes} |\n", Encoding.UTF8);
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TakGame.Ci.slnf")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Run from inside the repository (TakGame.Ci.slnf not found above the test binaries).");
    }
}

namespace TakApp.Cli.Online;

/// <summary>Where a profile keeps its key (<c>identity.json</c>) and games (<c>games.db</c>): <c>--data-dir</c> if given, else
/// <c>&lt;LocalApplicationData&gt;/tak-p2p/&lt;profile&gt;</c>. Two profiles on one machine are two players.</summary>
public static class Profile
{
    public const string DefaultName = "default";

    public static string Directory(string? dataDir, string? profile, string localAppData) =>
        dataDir is { Length: > 0 } ? Path.GetFullPath(dataDir) : Path.Combine(localAppData, "tak-p2p", profile ?? DefaultName);
}

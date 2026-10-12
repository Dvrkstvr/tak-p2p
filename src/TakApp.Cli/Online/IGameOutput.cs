using TakEngine.Abstractions;
using TakEngine.Transport.Relay;

namespace TakApp.Cli.Online;

/// <summary>What an online game reports to the person playing it. Plain mode is one line per call (the agent contract); rich mode
/// draws the same events with Spectre.Console.</summary>
public interface IGameOutput
{
    void Invite(string code);

    void Relay(Uri url, RelayLinkState state);

    void Waiting(string text);

    /// <summary>Our event for <paramref name="turn"/> was answered: sent (one relay said OK true) or failed (every relay refused).</summary>
    void Sent(int turn, IReadOnlyDictionary<Uri, RelayPublishState> perRelay);

    /// <param name="tpsAfter">The board after the move (rich mode draws it).</param>
    void Applied(int turn, string ptn, string hashAfter, string tpsAfter, int komiHalves, bool mine);

    /// <summary>Our own input was refused locally; nothing was signed or sent.</summary>
    void Rejected(int turn, string reason);

    /// <summary>A move or message from the opponent was dropped (tamper rows 3-7); the game is unchanged.</summary>
    void Flag(int turn, string reason);

    void Notice(string text);

    void Final(GameResult result, string hash);
}

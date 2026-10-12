using Spectre.Console;
using TakApp.Cli.Rendering;
using TakEngine.Abstractions;
using TakEngine.Core.Serialization;
using TakEngine.Transport.Relay;

namespace TakApp.Cli.Online;

/// <summary>The same events as <see cref="PlainOutput"/>, as coloured status lines, with the board drawn after each move (D-006: no
/// new screens).</summary>
public sealed class RichOutput : IGameOutput
{
    private readonly object _gate = new();

    public void Invite(string code) => Line($"[bold yellow]Invite code[/] (send it to your opponent): [bold lime]{Markup.Escape(code)}[/]");

    public void Relay(Uri url, RelayLinkState state)
    {
        string colour = state switch
        {
            RelayLinkState.CaughtUp => "green",
            RelayLinkState.Backoff or RelayLinkState.Disconnected => "red",
            _ => "yellow",
        };
        Line($"[grey]relay[/] {Markup.Escape(url.ToString())} [{colour}]{state}[/]");
    }

    public void Waiting(string text) => Line($"[cyan]{Markup.Escape(text)}[/]");

    public void Sent(int turn, IReadOnlyDictionary<Uri, RelayPublishState> perRelay) =>
        Line($"[grey]turn {turn}:[/] " + string.Join(", ", perRelay.Select(p => p.Value.Status switch
        {
            RelayPublishStatus.Sent => $"[green]accepted by {Markup.Escape(p.Key.Host)}[/]",
            RelayPublishStatus.Refused => $"[red]refused by {Markup.Escape(p.Key.Host)}: {Markup.Escape(p.Value.Reason ?? "")}[/]",
            _ => $"[yellow]waiting for {Markup.Escape(p.Key.Host)}[/]",
        })));

    public void Applied(int turn, string ptn, string hashAfter, string tpsAfter, int komiHalves, bool mine)
    {
        lock (_gate)
        {
            AnsiBoardRenderer.Render(TpsSerializer.Deserialize(tpsAfter, komiHalves), ptn);
            AnsiConsole.MarkupLine($"[grey]turn {turn}:[/] {(mine ? "you played" : "opponent played")} [bold]{Markup.Escape(ptn)}[/]");
        }
    }

    public void Rejected(int turn, string reason) => Line($"[red]Not played:[/] {Markup.Escape(reason)}");

    public void Flag(int turn, string reason) =>
        Line($"[bold red]Rejected a move from your opponent at turn {turn}:[/] {Markup.Escape(reason)}. Your game is unchanged.");

    public void Notice(string text) => Line($"[yellow]{Markup.Escape(text)}[/]");

    public void Final(GameResult result, string hash) =>
        Line($"[bold green]Game over:[/] {Markup.Escape(PlainOutput.Result(result))} [grey](final hash {hash[..12]}…)[/]");

    private void Line(string markup)
    {
        lock (_gate) AnsiConsole.MarkupLine(markup);
    }
}

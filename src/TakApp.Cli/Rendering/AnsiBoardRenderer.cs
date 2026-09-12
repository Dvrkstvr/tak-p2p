using System;
using System.Collections.Generic;
using System.Text;
using Spectre.Console;
using TakEngine.Abstractions;
using TakEngine.Core.Board;

namespace TakApp.Cli.Rendering;

public static class AnsiBoardRenderer
{
    private static readonly string[] Subscripts = ["⁰", "¹", "²", "³", "⁴", "⁵", "⁶", "⁷", "⁸", "⁹"];

    public static void Render(GameBoard board, string? lastMove = null, string? statusMessage = null)
    {
        int size = (int)board.Size;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        // Header: Rank column + files
        table.AddColumn(new TableColumn("[grey]#[/]").Centered());
        for (int x = 0; x < size; x++)
        {
            char file = (char)('a' + x);
            table.AddColumn(new TableColumn($"[bold yellow]{file}[/]").Centered());
        }
        table.AddColumn(new TableColumn("[grey]#[/]").Centered());

        // Rows from size-1 down to 0 (top rank to bottom)
        for (int y = size - 1; y >= 0; y--)
        {
            var rowItems = new List<string> { $"[bold yellow]{y + 1}[/]" };

            for (int x = 0; x < size; x++)
            {
                var stack = board.GetStack(new Coord(x, y));
                rowItems.Add(FormatCell(stack));
            }

            rowItems.Add($"[bold yellow]{y + 1}[/]");
            table.AddRow(rowItems.ToArray());
        }

        // Active Player and Reserves Panel
        string activeColor = board.ActivePlayer == PlayerColor.White ? "white" : "gold1";
        string activeText = board.Phase == GamePhase.Completed
            ? $"[bold red]GAME COMPLETED[/] - Winner: [bold {board.Result?.Winner?.ToString().ToLower() ?? "grey"}]{board.Result?.Winner?.ToString() ?? "Draw"}[/] ({board.Result?.Reason})"
            : $"Turn [bold]{board.TurnNumber}[/] - Active: [bold {activeColor}]{board.ActivePlayer}[/]";

        var infoGrid = new Grid();
        infoGrid.AddColumn();
        infoGrid.AddColumn();

        string whiteRes = $"[white]White:[/] Stones: [bold]{board.WhiteReserves.Stones}[/] | Capstones: [bold]{board.WhiteReserves.Capstones}[/]";
        string blackRes = $"[gold1]Black:[/] Stones: [bold]{board.BlackReserves.Stones}[/] | Capstones: [bold]{board.BlackReserves.Capstones}[/]";

        infoGrid.AddRow(new Markup(whiteRes), new Markup(blackRes));

        var layoutTable = new Table().HideHeaders().Border(TableBorder.None);
        layoutTable.AddColumn(new TableColumn("Board"));
        layoutTable.AddRow(table);
        layoutTable.AddRow(new Panel(infoGrid).Header(activeText).BorderColor(Color.Teal));

        if (!string.IsNullOrEmpty(lastMove))
        {
            layoutTable.AddRow(new Markup($"Last Move: [bold lime]{lastMove}[/]"));
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            layoutTable.AddRow(new Markup($"[italic]{statusMessage}[/]"));
        }

        AnsiConsole.Write(layoutTable);
    }

    private static string FormatCell(PieceStack stack)
    {
        if (stack.IsEmpty)
            return "[grey50]·[/]";

        var top = stack.TopPiece!.Value;
        string colorTag = top.Color == PlayerColor.White ? "white" : "gold1";

        string symbol = top.Type switch
        {
            PieceType.Flat => "○",
            PieceType.Standing => "▲",
            PieceType.Capstone => "◈",
            _ => "?"
        };

        string heightStr = stack.Height > 1 ? ToSubscript(stack.Height) : "";
        return $"[bold {colorTag}]{symbol}{heightStr}[/]";
    }

    private static string ToSubscript(int number)
    {
        string s = number.ToString();
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (char.IsDigit(c))
                sb.Append(Subscripts[c - '0']);
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}

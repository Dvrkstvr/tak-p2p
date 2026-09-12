using System;
using Spectre.Console;
using TakEngine.Abstractions;
using TakEngine.Core.Board;

namespace TakApp.Cli.Rendering;

public static class StackInspector
{
    public static void Inspect(GameBoard board, Coord coord)
    {
        if (!board.IsInBounds(coord))
        {
            AnsiConsole.MarkupLine($"[red]Coordinate {coord} is out of bounds.[/]");
            return;
        }

        var stack = board.GetStack(coord);
        if (stack.IsEmpty)
        {
            AnsiConsole.MarkupLine($"[grey]Square {coord.ToAlgebraic()} is empty.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold cyan]Stack Inspector: {coord.ToAlgebraic()}[/] (Height: {stack.Height})");

        table.AddColumn(new TableColumn("Layer").Centered());
        table.AddColumn(new TableColumn("Owner").Centered());
        table.AddColumn(new TableColumn("Piece Type").Centered());
        table.AddColumn(new TableColumn("Position").Centered());

        var pieces = stack.Pieces;
        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            var p = pieces[i];
            bool isTop = (i == pieces.Count - 1);
            string colorTag = p.Color == PlayerColor.White ? "white" : "gold1";
            string layerTag = isTop ? "[bold green]TOP[/]" : (i == 0 ? "[grey]BASE[/]" : $"[grey]L{i + 1}[/]");

            string symbol = p.Type switch
            {
                PieceType.Flat => "○ Flat",
                PieceType.Standing => "▲ Standing Wall",
                PieceType.Capstone => "◈ Capstone",
                _ => "?"
            };

            table.AddRow(
                $"#{i + 1}",
                $"[{colorTag}]{p.Color}[/]",
                $"[{colorTag}]{symbol}[/]",
                layerTag);
        }

        AnsiConsole.Write(table);
    }
}

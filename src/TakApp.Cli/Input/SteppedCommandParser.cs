using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using TakApp.Cli.Rendering;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Rules;
using TakEngine.Core.Serialization;

namespace TakApp.Cli.Input;

public enum PlayerActionType
{
    Move,
    Resign,
    None
}

public sealed record PlayerCommand(PlayerActionType Type, TakMove? Move = null);

public static class SteppedCommandParser
{
    public static PlayerCommand PromptForMove(GameBoard board)
    {
        while (true)
        {
            string promptColor = board.ActivePlayer == PlayerColor.White ? "white" : "gold1";
            AnsiConsole.Markup($"[{promptColor}][[Turn {board.TurnNumber} - {board.ActivePlayer}]][/] Enter square [bold]c3[/], command ([bold]3c3+12[/], [bold]inspect c3[/], [bold]resign[/], [bold]help[/]): ");
            string? raw = Console.ReadLine();

            if (raw == null)
                return new PlayerCommand(PlayerActionType.Resign);

            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string input = raw.Trim();

            // Commands
            if (string.Equals(input, "resign", StringComparison.OrdinalIgnoreCase))
            {
                return new PlayerCommand(PlayerActionType.Resign);
            }

            if (string.Equals(input, "help", StringComparison.OrdinalIgnoreCase))
            {
                ShowHelp();
                continue;
            }

            if (input.StartsWith("inspect ", StringComparison.OrdinalIgnoreCase) || input.StartsWith("?"))
            {
                string target = input.StartsWith("?") ? input[1..].Trim() : input[8..].Trim();
                try
                {
                    var coord = Coord.FromAlgebraic(target);
                    StackInspector.Inspect(board, coord);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Invalid coordinate: {ex.Message}[/]");
                }
                continue;
            }

            if (input.StartsWith("moves ", StringComparison.OrdinalIgnoreCase) || input.StartsWith("legal ", StringComparison.OrdinalIgnoreCase))
            {
                string target = input[(input.IndexOf(' ') + 1)..].Trim();
                try
                {
                    var coord = Coord.FromAlgebraic(target);
                    var moves = MoveValidator.GetLegalMovesForSquare(board, coord);
                    AnsiConsole.MarkupLine($"[cyan]Legal moves for {coord.ToAlgebraic()}:[/] {string.Join(", ", moves.Select(m => m.ToPtn()))}");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Invalid coordinate: {ex.Message}[/]");
                }
                continue;
            }

            // Check if input is a simple square coordinate [a-h][1-8]
            if (input.Length == 2 && char.IsLetter(input[0]) && char.IsDigit(input[1]))
            {
                var steppedMove = HandleSteppedSquareInput(board, input);
                if (steppedMove != null)
                {
                    return new PlayerCommand(PlayerActionType.Move, steppedMove);
                }
                continue;
            }

            // Otherwise, try parsing as direct full PTN move
            try
            {
                var move = PtnParser.ParseMove(input);
                return new PlayerCommand(PlayerActionType.Move, move);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Could not parse move '{input}': {ex.Message}[/]");
                AnsiConsole.MarkupLine("[grey]Type a coordinate (e.g. 'c3') for guided placement/movement, or full PTN like '3c3+12'.[/]");
            }
        }
    }

    private static TakMove? HandleSteppedSquareInput(GameBoard board, string squareInput)
    {
        Coord coord;
        try
        {
            coord = Coord.FromAlgebraic(squareInput);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Invalid coordinate: {ex.Message}[/]");
            return null;
        }

        if (!board.IsInBounds(coord))
        {
            AnsiConsole.MarkupLine($"[red]Coordinate {coord.ToAlgebraic()} is out of bounds for {board.Size} board.[/]");
            return null;
        }

        var stack = board.GetStack(coord);

        // Case A: Empty Square -> Place piece
        if (stack.IsEmpty)
        {
            if (board.Phase == GamePhase.FirstTurnPlacement)
            {
                AnsiConsole.MarkupLine($"[cyan]First turn swap rule:[/] Placing opponent's flat stone on {coord.ToAlgebraic()}.");
                return new PlaceMove(coord, PieceType.Flat);
            }

            AnsiConsole.Markup($"Square [bold yellow]{coord.ToAlgebraic()}[/] is empty. Place [[F]]lat, [[S]]tanding wall, [[C]]apstone, or 'cancel': ");
            string? pieceChoice = Console.ReadLine()?.Trim().ToLowerInvariant();

            return pieceChoice switch
            {
                "f" or "flat" => new PlaceMove(coord, PieceType.Flat),
                "s" or "standing" or "wall" or "w" => new PlaceMove(coord, PieceType.Standing),
                "c" or "cap" or "capstone" => new PlaceMove(coord, PieceType.Capstone),
                _ => null
            };
        }

        // Case B: Occupied by Opponent
        if (stack.Owner != board.ActivePlayer)
        {
            AnsiConsole.MarkupLine($"[red]Square {coord.ToAlgebraic()} is controlled by {stack.Owner}. Please select your own stack or an empty square.[/]");
            return null;
        }

        // Case C: Occupied by Active Player -> Move Stack
        int maxLift = Math.Min(stack.Height, board.CarryLimit);
        int liftCount = maxLift;

        if (maxLift > 1)
        {
            AnsiConsole.Markup($"Stack height: [bold]{stack.Height}[/] (top: {stack.TopPiece!.Value.Type}). How many pieces to lift? [[1-{maxLift}]], default {maxLift}, or 'cancel': ");
            string? liftInput = Console.ReadLine()?.Trim();
            if (string.Equals(liftInput, "cancel", StringComparison.OrdinalIgnoreCase) || string.Equals(liftInput, "q", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!string.IsNullOrEmpty(liftInput))
            {
                if (!int.TryParse(liftInput, out liftCount) || liftCount < 1 || liftCount > maxLift)
                {
                    AnsiConsole.MarkupLine($"[red]Invalid lift count. Must be between 1 and {maxLift}.[/]");
                    return null;
                }
            }
        }

        AnsiConsole.Markup($"Direction for {liftCount} piece(s)? [[N]]orth (+), [[S]]outh (-), [[E]]ast (>), [[W]]est (<), or 'cancel': ");
        string? dirInput = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.Equals(dirInput, "cancel", StringComparison.OrdinalIgnoreCase) || string.Equals(dirInput, "q", StringComparison.OrdinalIgnoreCase))
            return null;

        Direction direction;
        switch (dirInput)
        {
            case "n" or "north" or "+":
                direction = Direction.North;
                break;
            case "s" or "south" or "-":
                direction = Direction.South;
                break;
            case "e" or "east" or ">":
                direction = Direction.East;
                break;
            case "w" or "west" or "<":
                direction = Direction.West;
                break;
            default:
                AnsiConsole.MarkupLine($"[red]Invalid direction '{dirInput}'.[/]");
                return null;
        }

        // Drops distribution
        var drops = new List<int>();
        if (liftCount == 1)
        {
            drops.Add(1);
        }
        else
        {
            AnsiConsole.Markup($"Enter drop counts across squares (e.g. '1 2' for {liftCount} pieces, or Enter to drop all on next square): ");
            string? dropsInput = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(dropsInput))
            {
                drops.Add(liftCount);
            }
            else
            {
                string[] parts = dropsInput.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1 && parts[0].Length > 1 && parts[0].All(char.IsDigit))
                {
                    foreach (char c in parts[0])
                        drops.Add(c - '0');
                }
                else
                {
                    foreach (string p in parts)
                    {
                        if (int.TryParse(p, out int d))
                            drops.Add(d);
                    }
                }

                if (drops.Sum() != liftCount)
                {
                    AnsiConsole.MarkupLine($"[red]Sum of drops ({drops.Sum()}) does not match lifted count ({liftCount}).[/]");
                    return null;
                }
            }
        }

        return new SlideMove(coord, direction, liftCount, drops.ToArray());
    }

    private static void ShowHelp()
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold cyan]Tak CLI Controls & Help[/]");
        table.AddColumn("Command / Input");
        table.AddColumn("Action");

        table.AddRow("[bold yellow]c3[/]", "Select square c3 (places on empty, or moves owned stack)");
        table.AddRow("[bold yellow]3c3+12[/]", "Direct PTN: Lift 3 from c3, drop 1 then 2 North");
        table.AddRow("[bold yellow]Sc3[/]", "Direct PTN: Place standing wall at c3");
        table.AddRow("[bold yellow]Cc3[/]", "Direct PTN: Place capstone at c3");
        table.AddRow("[bold yellow]inspect c3[/] (or [bold]?c3[/])", "Open stack layer inspector for square c3");
        table.AddRow("[bold yellow]moves c3[/]", "List all legal moves for square c3");
        table.AddRow("[bold yellow]resign[/]", "Resign the active game");

        AnsiConsole.Write(table);
    }
}

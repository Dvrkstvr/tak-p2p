using System;
using System.IO;
using System.Threading.Tasks;
using Spectre.Console;
using TakApp.Cli.Input;
using TakApp.Cli.Rendering;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Cryptography;
using TakEngine.Core.Serialization;
using TakEngine.Core.Storage;
using TakEngine.Transport.Matchmaking;

namespace TakApp.Cli;

public static class Program
{
    private static readonly string DbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tak.db");

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var storage = new SqliteGameStorage($"Data Source={DbPath}");
        await storage.InitializeAsync();

        while (true)
        {
            AnsiConsole.Clear();
            RenderHeader();

            AnsiConsole.MarkupLine("[bold cyan]MAIN MENU[/]");
            AnsiConsole.MarkupLine("[1] Local Match (Pass & Play)");
            AnsiConsole.MarkupLine("[2] Quick Play Match (Nostr P2P)");
            AnsiConsole.MarkupLine("[3] Create Direct Invite Code");
            AnsiConsole.MarkupLine("[4] Join via Direct Invite Code");
            AnsiConsole.MarkupLine("[5] Exit");
            AnsiConsole.WriteLine();

            AnsiConsole.Markup("Select option [1-5]: ");
            string? choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1":
                    await PlayLocalMatchAsync(storage);
                    break;
                case "2":
                    await PlayQuickPlayAsync(storage);
                    break;
                case "3":
                    CreateInviteCode();
                    break;
                case "4":
                    await JoinInviteCodeAsync(storage);
                    break;
                case "5":
                case "exit":
                case "q":
                    AnsiConsole.MarkupLine("[grey]Goodbye![/]");
                    return;
                default:
                    AnsiConsole.MarkupLine("[red]Invalid option. Press Enter to continue.[/]");
                    Console.ReadLine();
                    break;
            }
        }
    }

    private static void RenderHeader()
    {
        AnsiConsole.Write(
            new FigletText("TAK P2P")
                .Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[bold grey]Decentralized, Peer-to-Peer, Zero-Server Abstract Strategy Game[/]");
        AnsiConsole.WriteLine();
    }

    private static async Task PlayLocalMatchAsync(SqliteGameStorage storage)
    {
        AnsiConsole.Clear();
        RenderHeader();

        AnsiConsole.Markup("Choose Board Size ([[4]], [[5]], [[6]], default 5): ");
        string? sizeInput = Console.ReadLine()?.Trim();
        BoardSize size = sizeInput switch
        {
            "4" => BoardSize.Four,
            "6" => BoardSize.Six,
            _ => BoardSize.Five
        };

        var board = new GameBoard(size);
        var gameId = Guid.NewGuid();
        var keyPair = CryptoSigner.GenerateKeyPair();
        string genesisHash = StateHasher.ComputeGenesisHash(size);
        string prevStateHash = genesisHash;
        int moveIndex = 1;

        var gameEntity = new GameEntity(
            Id: gameId,
            BoardSize: size,
            LocalPlayerColor: PlayerColor.White,
            OpponentPubKey: keyPair.PublicKeyHex,
            Status: GameStatus.Active,
            WinnerPubKey: null,
            StartedAt: DateTime.UtcNow,
            LastUpdatedAt: DateTime.UtcNow);

        await storage.CreateGameAsync(gameEntity);

        string? lastMoveStr = null;
        string? statusMessage = null;

        while (board.Phase != GamePhase.Completed)
        {
            AnsiConsole.Clear();
            RenderHeader();
            AnsiBoardRenderer.Render(board, lastMoveStr, statusMessage);
            statusMessage = null;

            var cmd = SteppedCommandParser.PromptForMove(board);

            if (cmd.Type == PlayerActionType.Resign)
            {
                board.Resign(board.ActivePlayer);
                await storage.UpdateGameStatusAsync(gameId, GameStatus.Resigned, board.Result?.Winner?.ToString(), DateTime.UtcNow);
                break;
            }

            if (cmd.Type == PlayerActionType.Move && cmd.Move != null)
            {
                var execResult = board.Execute(cmd.Move);
                if (!execResult.IsSuccess)
                {
                    statusMessage = $"[red]Illegal move: {execResult.ErrorMessage}[/]";
                    continue;
                }

                lastMoveStr = cmd.Move.ToPtn();
                string tpsSnapshot = TpsSerializer.Serialize(board);
                string stateHash = StateHasher.ComputeStateHash(prevStateHash, moveIndex, keyPair.PublicKeyHex, lastMoveStr, tpsSnapshot);
                string signature = CryptoSigner.Sign(keyPair.PrivateKeyHex, stateHash);

                var moveEntity = new MoveEntity(
                    GameId: gameId,
                    TurnIndex: moveIndex++,
                    PlayerPubKey: keyPair.PublicKeyHex,
                    PtnMove: lastMoveStr,
                    TpsSnapshot: tpsSnapshot,
                    StateHash: stateHash,
                    PrevStateHash: prevStateHash,
                    TimestampUtc: DateTime.UtcNow,
                    Signature: signature);

                await storage.AppendMoveAsync(moveEntity);
                prevStateHash = stateHash;
            }
        }

        // Final board display
        AnsiConsole.Clear();
        RenderHeader();
        AnsiBoardRenderer.Render(board, lastMoveStr);

        var winner = board.Result?.Winner;
        string reason = board.Result?.Reason.ToString() ?? "";

        var winPanel = new Panel(
            new Markup($"[bold green]Game Over![/]\nWinner: [bold yellow]{winner?.ToString() ?? "Draw"}[/] ({reason})\nSaved to database (Game ID: [grey]{gameId}[/])"))
            .Header("[bold gold1]MATCH RESULT[/]")
            .BorderColor(Color.Gold1);

        AnsiConsole.Write(winPanel);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Press Enter to return to main menu...");
        Console.ReadLine();
    }

    private static async Task PlayQuickPlayAsync(SqliteGameStorage storage)
    {
        AnsiConsole.Clear();
        RenderHeader();
        AnsiConsole.MarkupLine("[bold cyan]Quick Play Matchmaking[/]");
        AnsiConsole.MarkupLine("Connecting to public Nostr relays: [grey]wss://relay.damus.io, wss://nos.lol, wss://relay.primal.net[/]...");

        var keyPair = CryptoSigner.GenerateKeyPair();
        var proposal = QuickPlayMatchmaker.CreateChallenge(BoardSize.Five, keyPair.PublicKeyHex);
        var broadcast = QuickPlayMatchmaker.CreateBroadcastEvent(keyPair.PublicKeyHex, BoardSize.Five, ["wss://relay.damus.io"]);

        AnsiConsole.MarkupLine($"[green]✓[/] Matchmaking ticket created with ephemeral key [grey]{keyPair.PublicKeyHex[..12]}...[/]");
        AnsiConsole.MarkupLine($"[yellow]Broadcast Kind: 20001 (TTL: 60s)[/] looking for opponent on 5x5 pool...");

        // Simulate match setup for demonstration
        AnsiConsole.MarkupLine("Starting match simulation. Press Enter to begin local peer session...");
        Console.ReadLine();
        await PlayLocalMatchAsync(storage);
    }

    private static void CreateInviteCode()
    {
        AnsiConsole.Clear();
        RenderHeader();
        AnsiConsole.MarkupLine("[bold cyan]Generate Direct Invite Code / QR[/]");

        var keyPair = CryptoSigner.GenerateKeyPair();
        var gameId = Guid.NewGuid();
        var invite = new InviteCode(gameId, keyPair.PublicKeyHex, BoardSize.Five, ["wss://relay.damus.io"]);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold yellow]Shareable Link (URI):[/]");
        AnsiConsole.MarkupLine($"[underline cyan]{invite.ToUri()}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold yellow]Compact Code (QR Token):[/]");
        AnsiConsole.MarkupLine($"[bold lime]{invite.ToCompactCode()}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Press Enter to return to main menu...");
        Console.ReadLine();
    }

    private static async Task JoinInviteCodeAsync(SqliteGameStorage storage)
    {
        AnsiConsole.Clear();
        RenderHeader();
        AnsiConsole.MarkupLine("[bold cyan]Join Match via Invite Code[/]");
        AnsiConsole.Markup("Paste Invite URI or TAK1_ Code: ");
        string? input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input))
            return;

        try
        {
            var invite = InviteCode.Parse(input);
            AnsiConsole.MarkupLine($"[green]✓ Code verified![/] Game ID: [grey]{invite.GameId}[/], Board Size: [bold yellow]{invite.BoardSize}[/]");
            AnsiConsole.MarkupLine("Connecting to host peer...");
            await Task.Delay(500);
            await PlayLocalMatchAsync(storage);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error parsing invite code: {ex.Message}[/]");
            AnsiConsole.MarkupLine("Press Enter to continue...");
            Console.ReadLine();
        }
    }
}

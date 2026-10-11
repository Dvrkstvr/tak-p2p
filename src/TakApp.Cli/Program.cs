using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Spectre.Console;
using System.Threading;
using TakApp.Cli.Input;
using TakApp.Cli.Online;
using TakApp.Cli.Rendering;
using TakEngine.Abstractions;
using TakEngine.Core.AI;
using TakEngine.Core.Board;
using TakEngine.Core.Cryptography;
using TakEngine.Core.Serialization;
using TakEngine.Core.Storage;
using TakEngine.Storage.Local.Legacy;
using TakEngine.Crypto;
using TakEngine.Transport.Relay;

namespace TakApp.Cli;

public static class Program
{
    private static readonly string DbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tak.db");

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length > 0)
            return await RunOnlineAsync(args);

        var storage = new SqliteGameStorage($"Data Source={DbPath}");
        await storage.InitializeAsync();

        while (true)
        {
            SafeClear();
            RenderHeader();

            AnsiConsole.MarkupLine("[bold cyan]MAIN MENU[/]");
            AnsiConsole.MarkupLine("[[1]] Local Match (Pass & Play)");
            AnsiConsole.MarkupLine("[[2]] Practice vs AI Bot (Offline)");
            AnsiConsole.MarkupLine("[[3]] Exit");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Online play: start the program with [bold]host[/] to create an invite, or [bold]join <invite>[/] to answer one.[/]");
            AnsiConsole.WriteLine();

            AnsiConsole.Markup("Select option [[1-3]]: ");
            string? choice = Console.ReadLine()?.Trim();
            if (choice == null)
            {
                return 0;
            }

            switch (choice)
            {
                case "1":
                    await PlayLocalMatchAsync(storage);
                    break;
                case "2":
                    await PlayVsAiMatchAsync(storage);
                    break;
                case "3":
                case "exit":
                case "q":
                    AnsiConsole.MarkupLine("[grey]Goodbye![/]");
                    return 0;
                default:
                    AnsiConsole.MarkupLine("[red]Invalid option. Press Enter to continue.[/]");
                    Console.ReadLine();
                    break;
            }
        }
    }

    private static void SafeClear()
    {
        try
        {
            if (!Console.IsOutputRedirected && !Console.IsInputRedirected)
            {
                AnsiConsole.Clear();
            }
        }
        catch
        {
            // Silently ignore clear errors on redirected or virtual handles
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
        SafeClear();
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
        var keyPair = SecretKey.Generate(() => RandomNumberGenerator.GetBytes(SecretKey.Length));
        string genesisHash = StateHasher.ComputeGenesisHash(size);
        string prevStateHash = genesisHash;
        int moveIndex = 1;

        var gameEntity = new GameEntity(
            Id: gameId,
            BoardSize: size,
            LocalPlayerColor: PlayerColor.White,
            OpponentPubKey: keyPair.PublicKey.ToHex(),
            Status: GameStatus.Active,
            WinnerPubKey: null,
            StartedAt: DateTime.UtcNow,
            LastUpdatedAt: DateTime.UtcNow);

        await storage.CreateGameAsync(gameEntity);

        string? lastMoveStr = null;
        string? statusMessage = null;

        while (board.Phase != GamePhase.Completed)
        {
            SafeClear();
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
                string stateHash = StateHasher.ComputeStateHash(prevStateHash, moveIndex, keyPair.PublicKey.ToHex(), lastMoveStr, tpsSnapshot);
                string signature = ActionDigest.Sign(keyPair, SignedGameAction.UnsignedMove(gameId, moveIndex, keyPair.PublicKey.ToHex(), prevStateHash, lastMoveStr), RandomNumberGenerator.GetBytes(Schnorr.AuxLength)).Signature;

                var moveEntity = new MoveEntity(
                    GameId: gameId,
                    TurnIndex: moveIndex++,
                    PlayerPubKey: keyPair.PublicKey.ToHex(),
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
        SafeClear();
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

    private static async Task PlayVsAiMatchAsync(SqliteGameStorage storage)
    {
        SafeClear();
        RenderHeader();

        AnsiConsole.MarkupLine("[bold cyan]PRACTICE VS AI PRACTICE BOT[/]");
        AnsiConsole.Markup("Choose Board Size ([[4]], [[5]], [[6]], default 5): ");
        string? sizeInput = Console.ReadLine()?.Trim();
        BoardSize size = sizeInput switch
        {
            "4" => BoardSize.Four,
            "6" => BoardSize.Six,
            _ => BoardSize.Five
        };

        AnsiConsole.Markup("Choose Difficulty ([[1]] Easy, [[2]] Medium, [[3]] Hard, default 2): ");
        string? diffInput = Console.ReadLine()?.Trim();
        BotDifficulty difficulty = diffInput switch
        {
            "1" => BotDifficulty.Easy,
            "3" => BotDifficulty.Hard,
            _ => BotDifficulty.Medium
        };

        AnsiConsole.Markup("Choose Your Color ([[W]]hite, [[B]]lack, default White): ");
        string? colorInput = Console.ReadLine()?.Trim().ToLowerInvariant();
        PlayerColor humanColor = colorInput == "b" || colorInput == "black" ? PlayerColor.Black : PlayerColor.White;
        PlayerColor botColor = humanColor == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;

        var bot = new MinimaxTakBot(difficulty);
        var board = new GameBoard(size);
        var gameId = Guid.NewGuid();
        var keyPair = SecretKey.Generate(() => RandomNumberGenerator.GetBytes(SecretKey.Length));
        string genesisHash = StateHasher.ComputeGenesisHash(size);
        string prevStateHash = genesisHash;
        int moveIndex = 1;

        var gameEntity = new GameEntity(
            Id: gameId,
            BoardSize: size,
            LocalPlayerColor: humanColor,
            OpponentPubKey: $"BOT_{difficulty.ToString().ToUpperInvariant()}",
            Status: GameStatus.Active,
            WinnerPubKey: null,
            StartedAt: DateTime.UtcNow,
            LastUpdatedAt: DateTime.UtcNow);

        await storage.CreateGameAsync(gameEntity);

        string? lastMoveStr = null;
        string? statusMessage = null;

        while (board.Phase != GamePhase.Completed)
        {
            SafeClear();
            RenderHeader();
            AnsiBoardRenderer.Render(board, lastMoveStr, statusMessage);
            statusMessage = null;

            TakMove? move;
            if (board.ActivePlayer == botColor)
            {
                AnsiConsole.MarkupLine($"[bold yellow]🤖 AI ({difficulty}) is thinking...[/]");
                move = bot.SelectMove(board);
            }
            else
            {
                var cmd = SteppedCommandParser.PromptForMove(board);
                if (cmd.Type == PlayerActionType.Resign)
                {
                    board.Resign(board.ActivePlayer);
                    await storage.UpdateGameStatusAsync(gameId, GameStatus.Resigned, board.Result?.Winner?.ToString(), DateTime.UtcNow);
                    break;
                }
                move = cmd.Move;
            }

            if (move != null)
            {
                var execResult = board.Execute(move);
                if (!execResult.IsSuccess)
                {
                    statusMessage = $"[red]Illegal move: {execResult.ErrorMessage}[/]";
                    continue;
                }

                lastMoveStr = move.ToPtn();
                string tpsSnapshot = TpsSerializer.Serialize(board);
                string stateHash = StateHasher.ComputeStateHash(prevStateHash, moveIndex, keyPair.PublicKey.ToHex(), lastMoveStr, tpsSnapshot);
                string signature = ActionDigest.Sign(keyPair, SignedGameAction.UnsignedMove(gameId, moveIndex, keyPair.PublicKey.ToHex(), prevStateHash, lastMoveStr), RandomNumberGenerator.GetBytes(Schnorr.AuxLength)).Signature;

                var moveEntity = new MoveEntity(
                    GameId: gameId,
                    TurnIndex: moveIndex++,
                    PlayerPubKey: keyPair.PublicKey.ToHex(),
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
        SafeClear();
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

    private static async Task<int> RunOnlineAsync(string[] args)
    {
        OnlineArgs? parsed = OnlineArgs.Parse(args, out string? error);
        if (parsed is null)
        {
            await Console.Error.WriteLineAsync(error);
            return 2;
        }
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        var env = new CliEnvironment(new ClientWebSocketRelaySocketFactory(), TimeProvider.System,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            () => RandomNumberGenerator.GetBytes(32));
        IGameOutput output = parsed.Plain ? new PlainOutput(Console.Out) : new RichOutput();
        return await OnlineCommand.RunAsync(parsed, Console.In, output, Console.Error, env, stop.Token);
    }
}

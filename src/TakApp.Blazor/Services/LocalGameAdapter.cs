using TakEngine.Abstractions;
using TakEngine.Core.AI;
using TakEngine.Core.Board;
using TakEngine.Core.Session;

namespace TakApp.Blazor.Services;

/// <summary>
/// The Play page's view of a local (pass-and-play) or vs-AI game (F-041, D-008). Every move goes to a <see cref="TakGameSession"/>,
/// which decides legality, roads, results and the hash chain; this class forwards moves, runs the bot's turn and keeps what the
/// views draw (names, PTN history, a read-only board copy). It decides nothing about the rules.
/// </summary>
public sealed class LocalGameAdapter
{
    private static readonly TimeSpan BotPause = TimeSpan.FromMilliseconds(350);

    private readonly Func<TimeSpan, Task> _delay;
    private readonly List<string> _moveHistory = new();
    private TakGameSession? _session;
    private MinimaxTakBot? _bot;
    private int _generation;

    /// <param name="delay">The pause before the bot answers (human ergonomics); tests pass an instant or gated one.</param>
    public LocalGameAdapter(Func<TimeSpan, Task>? delay = null)
    {
        _delay = delay ?? (t => Task.Delay(t));
    }

    /// <summary>A copy of the session's board for the views, rebuilt after every change. Changing it does not change the game.</summary>
    public GameBoard? Board { get; private set; }

    public BoardSize CurrentSize { get; private set; } = BoardSize.Five;
    public PlayerColor LocalPlayerColor { get; private set; } = PlayerColor.White;
    public bool IsLocalOnly { get; private set; } = true;
    public bool IsBotMatch { get; private set; }
    public BotDifficulty? BotDifficulty { get; private set; }
    public bool IsBotThinking { get; private set; }
    public string WhitePlayerName { get; private set; } = "White";
    public string BlackPlayerName { get; private set; } = "Black";

    public IReadOnlyList<string> MoveHistoryPtn => _moveHistory;

    /// <summary>The session's current state hash (engine hash chain).</summary>
    public string? StateHash => _session?.CurrentStateHash;

    /// <summary>The winner's road squares from the engine when the game ended on a road; empty otherwise.</summary>
    public HashSet<Coord> WinningRoadCoords { get; private set; } = new();

    /// <summary>The bot's pending turn, if one was started (tests await it).</summary>
    public Task? BotTurn { get; private set; }

    public event Action? OnStateChanged;

    public void StartLocalMatch(BoardSize size, string? whiteName = null, string? blackName = null)
    {
        Begin(size);
        LocalPlayerColor = PlayerColor.White;
        IsLocalOnly = true;
        IsBotMatch = false;
        BotDifficulty = null;
        _bot = null;
        WhitePlayerName = !string.IsNullOrWhiteSpace(whiteName) ? whiteName : "White";
        BlackPlayerName = !string.IsNullOrWhiteSpace(blackName) ? blackName : "Black";
        NotifyStateChanged();
    }

    public void StartBotMatch(BoardSize size, BotDifficulty difficulty, PlayerColor humanColor = PlayerColor.White, string? humanName = null)
    {
        Begin(size);
        LocalPlayerColor = humanColor;
        IsLocalOnly = false;
        IsBotMatch = true;
        BotDifficulty = difficulty;
        _bot = new MinimaxTakBot(difficulty);

        string userDisplayName = !string.IsNullOrWhiteSpace(humanName) ? humanName : "You";
        string botDisplayName = $"AI ({difficulty})";
        WhitePlayerName = humanColor == PlayerColor.White ? userDisplayName : botDisplayName;
        BlackPlayerName = humanColor == PlayerColor.White ? botDisplayName : userDisplayName;
        NotifyStateChanged();

        StartBotTurnIfDue();
    }

    /// <summary>The engine's legal moves from <paramref name="coord"/> in the current position.</summary>
    public IReadOnlyList<TakMove> LegalMovesFor(Coord coord) =>
        _session?.GetLegalMovesForSquare(coord) ?? Array.Empty<TakMove>();

    /// <summary>Sends a move to the session. A refusal is the engine's own result; nothing changes then.</summary>
    public CommandResult ExecuteMove(TakMove move)
    {
        if (_session is null)
            return CommandResult.Fail("No active game.");

        var result = move switch
        {
            PlaceMove place => _session.SubmitPlacement(place.Target, place.PieceType),
            SlideMove slide => _session.SubmitMove(slide.Origin, slide.Direction, slide.Drops),
            _ => CommandResult.Fail($"Unsupported move {move.ToPtn()}."),
        };
        if (!result.IsSuccess)
            return result;

        NotifyStateChanged();
        StartBotTurnIfDue();
        return result;
    }

    /// <summary>
    /// Pass-and-play: the side to move resigns. vs-AI: the human resigns, also during the bot's pause, and the bot's pending
    /// turn is dropped (D-052).
    /// </summary>
    public CommandResult Resign()
    {
        if (_session is null)
            return CommandResult.Fail("No active game.");

        var result = IsBotMatch ? _session.Resign(LocalPlayerColor) : _session.Resign();
        if (result.IsSuccess && IsBotThinking)
        {
            _generation++;
            IsBotThinking = false;
        }
        Refresh();
        NotifyStateChanged();
        return result;
    }

    private void Begin(BoardSize size)
    {
        _generation++;
        CurrentSize = size;
        IsBotThinking = false;
        BotTurn = null;
        _moveHistory.Clear();
        _session = TakGameSession.CreateLocal(size);
        _session.OnMoveExecuted += (_, move) => _moveHistory.Add(move.ToPtn());
        Refresh();
    }

    private void Refresh()
    {
        if (_session is null)
            return;
        Board = GameBoard.FromSnapshot(_session.CurrentBoard);
        WinningRoadCoords = new HashSet<Coord>(_session.WinningRoad);
    }

    private void NotifyStateChanged()
    {
        Refresh();
        OnStateChanged?.Invoke();
    }

    private void StartBotTurnIfDue()
    {
        if (_session is null || _bot is null || _session.CurrentPhase == GamePhase.Completed)
            return;
        if (_session.CurrentBoard.ActivePlayer == LocalPlayerColor)
            return;
        BotTurn = PlayBotTurnAsync(_generation);
    }

    private async Task PlayBotTurnAsync(int generation)
    {
        IsBotThinking = true;
        NotifyStateChanged();

        await _delay(BotPause);

        // The player left or started another game while the bot waited.
        if (generation != _generation || _session is null || _bot is null)
            return;

        IsBotThinking = false;
        try
        {
            var move = _bot.SelectMove(GameBoard.FromSnapshot(_session.CurrentBoard));
            ExecuteMove(move);
        }
        catch (InvalidOperationException)
        {
            // The bot found no move (no legal move left); the page stays as it is.
        }
        finally
        {
            if (generation == _generation)
                NotifyStateChanged();
        }
    }
}

using TakEngine.Abstractions;
using TakEngine.Crypto;
using TakEngine.Transport.Matchmaking;
using TakEngine.Transport.Relay;

namespace TakEngine.Multiplayer;

/// <summary>What a head supplies to play over relays.</summary>
/// <param name="Key">The player's identity (D-011): signs every action and event.</param>
/// <param name="DefaultRelays">Used with an invite's relay hints, and by a host for its invite.</param>
/// <param name="Random32">32 random bytes per call: colour nonces, NIP-44 nonces and BIP-340 aux (shell: the system RNG; tests: seeded).</param>
public sealed record MultiplayerOptions(
    SecretKey Key,
    IGameStore Store,
    IRelaySocketFactory Sockets,
    TimeProvider Time,
    IReadOnlyList<string> DefaultRelays,
    Func<byte[]> Random32)
{
    /// <summary>The client's publish budget (4 events per 10 s). A head shares one across every game it opens.</summary>
    public PublishBudget Budget { get; init; } = new();
}

/// <summary>Either a game being joined, or why the invite can't be used (nothing was saved or sent).</summary>
public sealed record JoinOutcome(MultiplayerGame? Game, string? Error);

/// <summary>
/// The game over relays that heads call (F-035, F-017, F-016): host or join by invite, submit moves, resign, and follow events.
/// A head renders these events and never touches the protocol. Events are raised on the game's own worker, one at a time.
/// </summary>
public sealed class MultiplayerGame : IAsyncDisposable
{
    private readonly GameCoordinator _coordinator;

    private MultiplayerGame(MultiplayerOptions options) => _coordinator = new GameCoordinator(options, this);

    /// <summary>Creates an invite and waits for a guest. Throws <see cref="InviteFormatException"/> for a size or komi out of range.</summary>
    public static async Task<MultiplayerGame> HostAsync(MultiplayerOptions options, BoardSize size, int komiHalves, string? nickname = null)
    {
        var game = new MultiplayerGame(options);
        await game._coordinator.HostAsync(size, komiHalves, nickname).ConfigureAwait(false);
        return game;
    }

    /// <summary>Joins by invite. A bad invite (or your own) returns its error with no store write and no network traffic.</summary>
    public static async Task<JoinOutcome> JoinAsync(MultiplayerOptions options, string inviteText)
    {
        var game = new MultiplayerGame(options);
        string? error = await game._coordinator.JoinAsync(inviteText).ConfigureAwait(false);
        if (error is null)
            return new JoinOutcome(game, null);
        await game.DisposeAsync().ConfigureAwait(false);
        return new JoinOutcome(null, error);
    }

    /// <summary>
    /// Rebuilds a game this device has the key for but lost the state of (F-037, D-045), from the invite and the relays' history,
    /// then continues it like <see cref="ResumeAsync"/>. The record's note says "rebuilt from relays". An error (no history, a fork,
    /// a missing move) stores nothing.
    /// </summary>
    public static async Task<JoinOutcome> RebuildAsync(MultiplayerOptions options, string inviteText)
    {
        var game = new MultiplayerGame(options);
        string? error = await game._coordinator.RebuildAsync(inviteText).ConfigureAwait(false);
        if (error is null)
            return new JoinOutcome(game, null);
        await game.DisposeAsync().ConfigureAwait(false);
        return new JoinOutcome(null, error);
    }

    /// <summary>Reopens a stored game after a restart, or null if it isn't stored.</summary>
    public static async Task<MultiplayerGame?> ResumeAsync(MultiplayerOptions options, Guid gameId)
    {
        var game = new MultiplayerGame(options);
        if (await game._coordinator.ResumeAsync(gameId).ConfigureAwait(false))
            return game;
        await game.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    public Guid GameId => _coordinator.Record.GameId;

    /// <summary>The game record as last saved (handshake state, colour, result).</summary>
    public NetGameRecord Record => _coordinator.Record;

    /// <summary>The invite to share (host), or the one joined (guest).</summary>
    public InviteCode Invite => _coordinator.Invite;

    public PlayerColor? MyColor => _coordinator.Record.MyColor;

    public int AppliedTurn => _coordinator.AppliedTurn;

    public string StateHash => _coordinator.StateHash;

    public GameResult? Result => _coordinator.Record.Result;

    /// <summary>Events dropped and only counted (relay junk, strangers): tamper rows 1-2.</summary>
    public int DroppedEvents => _coordinator.DroppedEvents;

    public OwnLink OwnLink => _coordinator.OwnLink;

    public OpponentLink OpponentLink => _coordinator.OpponentLink;

    /// <summary>The handshake finished: colours are settled and play can start.</summary>
    public event Action<PlayerColor>? Started;

    /// <summary>A move applied to the board; <c>mine</c> is true for our own.</summary>
    public event Action<AppliedAction, bool>? ActionApplied;

    /// <summary>A rejected action from the opponent; the game is unchanged.</summary>
    public event Action<FlagEntry>? Flagged;

    /// <summary>Something the user should see that isn't a flag (an ignored second guest, a save that failed).</summary>
    public event Action<string>? Notice;

    /// <summary>The handshake failed; nothing is played.</summary>
    public event Action<string>? Failed;

    public event Action<GameResult>? Ended;

    /// <summary>Per event id: Sent once any relay accepted it, Failed when all refused.</summary>
    public event Action<string, PublishOutcome>? PublishStatus;

    public event Action<OwnLink, OpponentLink>? LinkChanged;

    /// <summary>Plays a move. An illegal move is refused locally and nothing is published.</summary>
    public Task<CommandResult> SubmitAsync(string ptn) => _coordinator.SubmitAsync(ptn);

    public Task<CommandResult> SubmitAsync(TakMove move) => _coordinator.SubmitAsync(move);

    /// <summary>Resigns (at any time once the game has started); the game ends here at once and the RESIGN is delivered.</summary>
    public Task<CommandResult> ResignAsync() => _coordinator.ResignAsync();

    /// <summary>Cancels an invite no guest has joined, or a join not yet accepted. False once the game has started.</summary>
    public Task<bool> CancelAsync() => _coordinator.CancelAsync();

    public ValueTask DisposeAsync() => _coordinator.DisposeAsync();

    internal void RaiseStarted(PlayerColor color) => Started?.Invoke(color);

    internal void RaiseApplied(AppliedAction action, bool mine) => ActionApplied?.Invoke(action, mine);

    internal void RaiseFlagged(FlagEntry flag) => Flagged?.Invoke(flag);

    internal void RaiseNotice(string message) => Notice?.Invoke(message);

    internal void RaiseFailed(string message) => Failed?.Invoke(message);

    internal void RaiseEnded(GameResult result) => Ended?.Invoke(result);

    internal void RaisePublishStatus(string eventId, PublishOutcome outcome) => PublishStatus?.Invoke(eventId, outcome);

    internal void RaiseLinkChanged(OwnLink own, OpponentLink opponent) => LinkChanged?.Invoke(own, opponent);
}

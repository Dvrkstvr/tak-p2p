using Microsoft.Extensions.Time.Testing;
using TakEngine.Abstractions;
using TakEngine.Crypto;
using TakEngine.Storage.Local;
using TakEngine.Testing;

namespace TakEngine.Multiplayer.Tests;

/// <summary>
/// Two players with their own keys and stores, sharing two in-memory relays and one fake clock (F-016 setup). Randomness is
/// seeded; the seed is part of every failure message that needs it.
/// </summary>
internal sealed class Table : IAsyncDisposable
{
    private readonly Random _random;
    private readonly object _randomGate = new();
    private readonly List<MultiplayerGame> _games = [];
    private readonly string? _dir;

    /// <param name="sqlite">Each player gets a <see cref="SqliteGameStore"/> on a temp file instead of the in-memory fake.</param>
    public Table(int seed = 1, bool sqlite = false)
    {
        Seed = seed;
        if (sqlite)
        {
            _dir = Directory.CreateTempSubdirectory("tak-table-").FullName;
            HostDb = new SqliteGameStore(Path.Combine(_dir, "host.db"));
            GuestDb = new SqliteGameStore(Path.Combine(_dir, "guest.db"));
        }
        else
        {
            HostDb = new InMemoryGameStore();
            GuestDb = new InMemoryGameStore();
        }
        _random = new Random(seed);
        R1 = new InMemoryRelay("wss://r1.test", Time, seed);
        R2 = new InMemoryRelay("wss://r2.test", Time, seed + 1);
        Sockets = new FakeRelaySocketFactory(R1, R2);
    }

    public int Seed { get; }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));

    public InMemoryRelay R1 { get; }

    public InMemoryRelay R2 { get; }

    public FakeRelaySocketFactory Sockets { get; }

    /// <summary>The host's store, whichever kind this table uses.</summary>
    public IGameStore HostDb { get; }

    public IGameStore GuestDb { get; }

    /// <summary>The in-memory host store (its fault knobs); only on a table without <c>sqlite</c>.</summary>
    public InMemoryGameStore HostStore => (InMemoryGameStore)HostDb;

    public InMemoryGameStore GuestStore => (InMemoryGameStore)GuestDb;

    public MultiplayerOptions HostOptions => Options(TestKeys.Create("table host"), HostDb);

    public MultiplayerOptions GuestOptions => Options(TestKeys.Create("table guest"), GuestDb);

    public MultiplayerOptions Options(SecretKey key, IGameStore store) =>
        new(key, store, Sockets, Time, [R1.Url.ToString(), R2.Url.ToString()], Random32);

    public byte[] Random32()
    {
        var bytes = new byte[32];
        lock (_randomGate) _random.NextBytes(bytes);
        return bytes;
    }

    public MultiplayerGame Track(MultiplayerGame game)
    {
        lock (_games) _games.Add(game);
        return game;
    }

    public async Task<MultiplayerGame> Host(BoardSize size = BoardSize.Five, int komi = 0) =>
        Track(await MultiplayerGame.HostAsync(HostOptions, size, komi, "host"));

    public async Task<MultiplayerGame> Join(MultiplayerGame host)
    {
        JoinOutcome outcome = await MultiplayerGame.JoinAsync(GuestOptions, host.Invite.ToCompactCode());
        Assert.True(outcome.Game is not null, outcome.Error);
        return Track(outcome.Game);
    }

    /// <summary>Host + guest, both InGame.</summary>
    public async Task<(MultiplayerGame Host, MultiplayerGame Guest)> Connect(BoardSize size = BoardSize.Five, int komi = 0, Action? nudge = null)
    {
        MultiplayerGame host = await Host(size, komi);
        MultiplayerGame guest = await Join(host);
        await Eventually(() => host.Record.State == HandshakeState.InGame && guest.Record.State == HandshakeState.InGame, "both in game", nudge: nudge);
        return (host, guest);
    }

    /// <summary>
    /// Plays <paramref name="ptns"/> from ply <paramref name="from"/>, each move by whoever owns the turn, waiting for both sides.
    /// While a move is in flight the fake clock moves on (the publish budget allows 4 events per 10 s per player).
    /// </summary>
    public async Task Play(MultiplayerGame a, MultiplayerGame b, IEnumerable<string> ptns, int from = 1, Action? nudge = null)
    {
        int calls = 0;
        nudge = (nudge ?? (() => { })) + (() =>
        {
            if (++calls % 10 == 0)
                Time.Advance(TimeSpan.FromSeconds(1));
        });
        int ply = from;
        foreach (string ptn in ptns)
        {
            var (mover, other) = a.MyColor == TurnExchange.TurnOwner(ply) ? (a, b) : (b, a);
            CommandResult result = await mover.SubmitAsync(ptn);
            Assert.True(result.IsSuccess, $"ply {ply} '{ptn}': {result.ErrorMessage} (seed {Seed})");
            int expected = ply;
            await Eventually(() => other.AppliedTurn == expected || other.Result is not null, $"ply {ply} to arrive (seed {Seed})", nudge: nudge);
            ply++;
        }
    }

    /// <summary>Waits in real time (async hand-offs only; protocol time is the fake clock) until <paramref name="condition"/> holds.</summary>
    /// <param name="nudge">Run between checks (e.g. flush a relay's reorder window).</param>
    public static async Task Eventually(Func<bool> condition, string what, int timeoutMs = 5000, Action? nudge = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(5);
            nudge?.Invoke();
        }
        Assert.True(condition(), "timed out waiting for: " + what);
    }

    /// <summary>Advances the fake clock in 1 s steps so per-second ticks and backoffs fire in order.</summary>
    public async Task Advance(TimeSpan by)
    {
        for (var done = TimeSpan.Zero; done < by; done += TimeSpan.FromSeconds(1))
        {
            Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10);
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<MultiplayerGame> games;
        lock (_games) games = [.. _games];
        foreach (MultiplayerGame g in games)
            await g.DisposeAsync();
        if (_dir is not null)
            Directory.Delete(_dir, recursive: true);
    }
}

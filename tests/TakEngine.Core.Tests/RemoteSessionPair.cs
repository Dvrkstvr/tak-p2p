using System.Security.Cryptography;
using System.Text;
using TakEngine.Abstractions;
using TakEngine.Core.Serialization;
using TakEngine.Core.Session;
using TakEngine.Crypto;

namespace TakEngine.Core.Tests;

/// <summary>
/// Two remote sessions with independent keys (Alice = White, Bob = Black) wired by hand: whatever one signs, the test hands to
/// the other. Aux randomness is seeded so signatures (and so the whole run) are reproducible.
/// </summary>
internal sealed class RemoteSessionPair
{
    /// <summary>A 5x5 game to a White road on row 1 at ply 9 (ply 1 and 2 are the first-turn swap).</summary>
    public static readonly IReadOnlyList<string> FiveByFiveRoadWin = ["e5", "a1", "b1", "e4", "c1", "e3", "d1", "e2", "e1"];

    public static readonly GameId Game = new(Guid.Parse("6f1d2c3b-4a59-4e8d-9c7b-1a2b3c4d5e6f"));

    public RemotePairKeys Keys { get; } = new(TestKeys.Create(1), TestKeys.Create(2));

    public TakGameSession Alice { get; }
    public TakGameSession Bob { get; }
    public List<SignedGameAction> Emitted { get; } = [];

    public RemoteSessionPair(BoardSize size = BoardSize.Five)
    {
        Alice = TakGameSession.CreateRemote(Game, size, PlayerColor.White, Keys.Alice, Keys.Bob.PublicKey, SeededAux("alice"));
        Bob = TakGameSession.CreateRemote(Game, size, PlayerColor.Black, Keys.Bob, Keys.Alice.PublicKey, SeededAux("bob"));
        Alice.OnLocalActionSigned += Emitted.Add;
        Bob.OnLocalActionSigned += Emitted.Add;
    }

    public static Func<byte[]> SeededAux(string seed)
    {
        int counter = 0;
        return () => SHA256.HashData(Encoding.UTF8.GetBytes($"{seed} aux {counter++}"));
    }

    /// <summary>The mover submits <paramref name="ptn"/>; the signed action is returned (not yet delivered).</summary>
    public static SignedGameAction Submit(TakGameSession mover, string ptn)
    {
        SignedGameAction? signed = null;
        void Capture(SignedGameAction a) => signed = a;
        mover.OnLocalActionSigned += Capture;
        try
        {
            CommandResult result = PtnParser.ParseMove(ptn) switch
            {
                PlaceMove p => mover.SubmitPlacement(p.Target, p.PieceType),
                SlideMove m => mover.SubmitMove(m.Origin, m.Direction, m.Drops),
                _ => throw new InvalidOperationException(),
            };
            Assert.True(result.IsSuccess, $"{ptn}: {result.ErrorMessage}");
        }
        finally
        {
            mover.OnLocalActionSigned -= Capture;
        }
        return signed ?? throw new InvalidOperationException($"{ptn} produced no signed action");
    }

    /// <summary>Plays <paramref name="plies"/> with each action delivered to the other side; returns the hash after each ply.</summary>
    public List<string> Play(IEnumerable<string> plies)
    {
        var hashes = new List<string>();
        foreach (string ptn in plies)
        {
            bool aliceMoves = Alice.CurrentBoard.ActivePlayer == PlayerColor.White;
            TakGameSession mover = aliceMoves ? Alice : Bob;
            TakGameSession receiver = aliceMoves ? Bob : Alice;
            SignedGameAction action = Submit(mover, ptn);
            CommandResult received = receiver.ProcessRemoteAction(action);
            Assert.True(received.IsSuccess, $"{ptn}: {received.ErrorMessage}");
            Assert.Equal(mover.CurrentStateHash, receiver.CurrentStateHash);
            hashes.Add(mover.CurrentStateHash);
        }
        return hashes;
    }
}

internal sealed record RemotePairKeys(SecretKey Alice, SecretKey Bob);

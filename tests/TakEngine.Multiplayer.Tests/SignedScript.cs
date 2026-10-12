using System.Security.Cryptography;
using System.Text;
using TakEngine.Abstractions;
using TakEngine.Core.Session;
using TakEngine.Crypto;

namespace TakEngine.Multiplayer.Tests;

internal static class TestKeys
{
    public static SecretKey Create(string label) => SecretKey.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes($"tak-p2p multiplayer test key {label}")));

    public static readonly SecretKey White = Create("white");
    public static readonly SecretKey Black = Create("black");
    public static readonly SecretKey Stranger = Create("stranger");
}

/// <summary>
/// Plays a PTN script between two remote sessions and returns every signed MOVE in turn order (aux = zero bytes, so signing is
/// deterministic and a receiver replaying its own side produces the same actions).
/// </summary>
internal static class SignedScript
{
    public static readonly Guid Game = Guid.Parse("6d1f6a0e-5c3b-4c55-9a51-2a8f0b9c7e11");

    public static TakGameSession Session(PlayerColor color, Guid? game = null, BoardSize size = BoardSize.Five, int komi = 0) =>
        TakGameSession.CreateRemote(
            new GameId(game ?? Game),
            size,
            color,
            color == PlayerColor.White ? TestKeys.White : TestKeys.Black,
            color == PlayerColor.White ? TestKeys.Black.PublicKey : TestKeys.White.PublicKey,
            () => new byte[32],
            komi);

    public static (IReadOnlyList<SignedGameAction> Actions, string FinalHash, GameResult? Result) Play(
        IReadOnlyList<string> ptns, Guid? game = null, BoardSize size = BoardSize.Five, int komi = 0)
    {
        var white = Session(PlayerColor.White, game, size, komi);
        var black = Session(PlayerColor.Black, game, size, komi);
        var actions = new List<SignedGameAction>();
        white.OnLocalActionSigned += a => actions.Add(a);
        black.OnLocalActionSigned += a => actions.Add(a);
        for (int i = 0; i < ptns.Count; i++)
        {
            var (mover, other) = i % 2 == 0 ? (white, black) : (black, white);
            var result = LocalMoves.Submit(mover, ptns[i]);
            Assert.True(result.IsSuccess, $"script move {i + 1} '{ptns[i]}': {result.ErrorMessage}");
            Assert.True(other.ProcessRemoteAction(actions[^1]).IsSuccess);
        }
        Assert.Equal(white.CurrentStateHash, black.CurrentStateHash);
        return (actions, white.CurrentStateHash, white.Result);
    }

    public static SignedGameAction Sign(SecretKey key, SignedGameAction unsigned, byte aux = 0)
    {
        byte[] auxBytes = new byte[32];
        auxBytes[0] = aux;
        return ActionDigest.Sign(key, unsigned, auxBytes);
    }

    public static string EventId(SignedGameAction action) => "evt-" + action.ActionType + "-" + action.Turn + "-" + action.Signature[..8];
}

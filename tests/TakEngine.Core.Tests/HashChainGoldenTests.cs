namespace TakEngine.Core.Tests;

/// <summary>
/// Pins the hash chain (StateHasher formula + D-040 ply index + fixed keys) over a fixed 5x5 game. If this fails, every stored
/// game and every peer on an older build forks: change it only with a new <c>pv</c> and a decision record.
/// </summary>
public class HashChainGoldenTests
{
    // Ply 1 cross-checked with Python: sha256(f"{genesis}:1:{alice}:e5:x4,2/x5/x5/x5/x5 2 1").
    private static readonly string[] Golden =
    [
        "945568fc381e68de5dff2043e2ae48dce847e3a8b398ef034d5023a1db217d35",
        "d054c9181edfbc82baff5aa649faca5459915abc5e9fc4135e2b95762649bcbe",
        "c2e53fc2d0bf810d61258e3052cdc7083abdba66048df31ff7574d0c51e5427e",
        "63ee87c94b7d86cf8aa7081bdf5056afa0a4f7b31a93532a48afce5a90970f8f",
        "43a2ac96839892eef982cb775c55940769cbc3cb16f34b30403873060972944b",
        "0fe9a606a96e206f172804c60522a883dda761766b2928b011d1043c0cf93f97",
        "d58097b6c450aa129fb95e52577f733e5355c804e757a4c43d672698b35f0004",
        "e6be3b0c62d12dca6111c09a23c27c5ad7dce29e4bfb357cad9bd5b259f77acc",
        "b2888b2b7716c2de682e2ded320e3b44b9cfd75bc41b240eb76a69e076b7d284",
    ];

    [Fact]
    public void HashChain_FixedGame_MatchesGoldenSequence()
    {
        var pair = new RemoteSessionPair();

        List<string> hashes = pair.Play(RemoteSessionPair.FiveByFiveRoadWin);

        Assert.Equal(Golden, hashes);
    }
}

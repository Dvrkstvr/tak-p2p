using TakEngine.Abstractions;
using TakEngine.Core.Cryptography;
using Xunit;

namespace TakEngine.Core.Tests;

public class CryptoTests
{
    [Fact]
    public void GenesisHash_IsDeterministic_ForBoardSizes()
    {
        string gen4a = StateHasher.ComputeGenesisHash(BoardSize.Four);
        string gen4b = StateHasher.ComputeGenesisHash(BoardSize.Four);
        string gen5 = StateHasher.ComputeGenesisHash(BoardSize.Five);

        Assert.Equal(64, gen4a.Length);
        Assert.Equal(gen4a, gen4b);
        Assert.NotEqual(gen4a, gen5);
    }

    [Fact]
    public void VerifyChain_Succeeds_ForValidChainedMoves()
    {
        string genesisHash = StateHasher.ComputeGenesisHash(BoardSize.Five);

        // Move 1
        string hash1 = StateHasher.ComputeStateHash(
            genesisHash,
            1,
            "pubkey1",
            "a1",
            "x5/x5/x5/x5/2,x4 2 1");

        // Move 2
        string hash2 = StateHasher.ComputeStateHash(
            hash1,
            2,
            "pubkey2",
            "e5",
            "x4,1/x5/x5/x5/2,x4 1 2");

        var chain = new[]
        {
            (genesisHash, 1, "pubkey1", "a1", "x5/x5/x5/x5/2,x4 2 1", hash1),
            (hash1, 2, "pubkey2", "e5", "x4,1/x5/x5/x5/2,x4 1 2", hash2)
        };

        bool isValid = StateHasher.VerifyChain(chain, genesisHash);
        Assert.True(isValid);
    }

    [Fact]
    public void VerifyChain_Fails_WhenMoveTampered()
    {
        string genesisHash = StateHasher.ComputeGenesisHash(BoardSize.Five);

        string hash1 = StateHasher.ComputeStateHash(
            genesisHash,
            1,
            "pubkey1",
            "a1",
            "x5/x5/x5/x5/2,x4 2 1");

        string hash2 = StateHasher.ComputeStateHash(
            hash1,
            2,
            "pubkey2",
            "e5",
            "x4,1/x5/x5/x5/2,x4 1 2");

        // Attacker tampers move 1 PTN from "a1" to "b2" without updating StateHash
        var tamperedChain = new[]
        {
            (genesisHash, 1, "pubkey1", "b2", "x5/x5/x5/x5/2,x4 2 1", hash1),
            (hash1, 2, "pubkey2", "e5", "x4,1/x5/x5/x5/2,x4 1 2", hash2)
        };

        bool isValid = StateHasher.VerifyChain(tamperedChain, genesisHash);
        Assert.False(isValid);
    }
}

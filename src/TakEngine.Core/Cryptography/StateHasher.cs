using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using TakEngine.Abstractions;
using TakEngine.Core.Board;
using TakEngine.Core.Serialization;

namespace TakEngine.Core.Cryptography;

public static class StateHasher
{
    public static string ComputeGenesisHash(BoardSize size)
    {
        var board = new GameBoard(size);
        string initialTps = TpsSerializer.Serialize(board);
        return ComputeSha256Hex($"GENESIS:{size}:{initialTps}");
    }

    public static string ComputeStateHash(
        string prevStateHash,
        int turnIndex,
        string playerPubKey,
        string ptnMove,
        string tpsSnapshot)
    {
        string payload = $"{prevStateHash}:{turnIndex}:{playerPubKey}:{ptnMove}:{tpsSnapshot}";
        return ComputeSha256Hex(payload);
    }

    public static bool VerifyChain(
        IReadOnlyList<(string PrevStateHash, int TurnIndex, string PlayerPubKey, string PtnMove, string TpsSnapshot, string StateHash)> moves,
        string expectedGenesisHash)
    {
        if (moves == null || moves.Count == 0)
            return true;

        for (int i = 0; i < moves.Count; i++)
        {
            var m = moves[i];

            // Verify previous state hash link
            if (i == 0)
            {
                if (!string.Equals(m.PrevStateHash, expectedGenesisHash, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else
            {
                if (!string.Equals(m.PrevStateHash, moves[i - 1].StateHash, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Verify state hash calculation
            string expectedHash = ComputeStateHash(
                m.PrevStateHash,
                m.TurnIndex,
                m.PlayerPubKey,
                m.PtnMove,
                m.TpsSnapshot);

            if (!string.Equals(m.StateHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static string ComputeSha256Hex(string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    public static string ComputeSha256Hex(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}

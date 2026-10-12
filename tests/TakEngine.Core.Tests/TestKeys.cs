using System.Security.Cryptography;
using System.Text;
using TakEngine.Crypto;

namespace TakEngine.Core.Tests;

/// <summary>Deterministic, independent secp256k1 test keys (no unseeded randomness in tests).</summary>
internal static class TestKeys
{
    public static SecretKey Create(int n) => SecretKey.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes($"tak-p2p core test key {n}")));
}

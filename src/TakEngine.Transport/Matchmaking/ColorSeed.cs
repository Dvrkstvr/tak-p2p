using System.Security.Cryptography;
using System.Text;

namespace TakEngine.Transport.Matchmaking;

/// <summary>
/// The commit-reveal colour seed (D-038). The invite carries <see cref="Commit"/> of the host's nonce, JOIN carries the guest's
/// nonce, ACCEPT reveals the host's nonce, and both sides feed <see cref="Derive"/> into <see cref="ColorResolver.ResolveColors"/>.
/// Neither side can choose the outcome: the host is bound before it sees the guest's nonce, and the guest can't see the host's.
/// Pure; nonces are passed in.
/// </summary>
public static class ColorSeed
{
    public const int NonceLength = 32;

    private static readonly byte[] Tag = Encoding.UTF8.GetBytes("tak/seed/v1\n");

    /// <summary><c>SHA-256("tak/seed/v1\n" || host_nonce)</c> as lowercase hex.</summary>
    public static string Commit(byte[] hostNonce)
    {
        RequireNonce(hostNonce, nameof(hostNonce));
        return Hash(hostNonce);
    }

    /// <summary>True when <paramref name="hostNonce"/> is the nonce <paramref name="commit"/> was made from. Never throws.</summary>
    public static bool Verify(string? commit, byte[]? hostNonce)
    {
        if (commit is null || hostNonce is null || hostNonce.Length != NonceLength)
            return false;
        return string.Equals(commit, Hash(hostNonce), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>SHA-256("tak/seed/v1\n" || host_nonce || guest_nonce)</c> as lowercase hex, the seed for the colour draw.</summary>
    public static string Derive(byte[] hostNonce, byte[] guestNonce)
    {
        RequireNonce(hostNonce, nameof(hostNonce));
        RequireNonce(guestNonce, nameof(guestNonce));
        return Hash(hostNonce, guestNonce);
    }

    private static string Hash(params byte[][] parts)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(Tag);
        foreach (byte[] part in parts)
            sha.AppendData(part);
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static void RequireNonce(byte[] nonce, string name)
    {
        ArgumentNullException.ThrowIfNull(nonce, name);
        if (nonce.Length != NonceLength)
            throw new ArgumentException($"A colour nonce is {NonceLength} bytes, not {nonce.Length}.", name);
    }
}

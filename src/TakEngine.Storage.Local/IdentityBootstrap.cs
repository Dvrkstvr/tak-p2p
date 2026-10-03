using TakEngine.Abstractions;
using TakEngine.Crypto;

namespace TakEngine.Storage.Local;

/// <summary>Shell step 1 of the core-promise path: load the player's key, or create and save it on first run.</summary>
public static class IdentityBootstrap
{
    /// <summary>
    /// Returns the stored key; on first run generates one from <paramref name="random32"/> (the head passes
    /// <c>() =&gt; RandomNumberGenerator.GetBytes(32)</c>) and saves it. An unreadable store throws
    /// <see cref="KeyStoreException"/> and nothing is generated or written.
    /// </summary>
    public static async Task<SecretKey> LoadOrCreateAsync(IKeyStore store, Func<byte[]> random32, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(random32);

        byte[]? stored = await store.LoadSecretAsync(cancellationToken).ConfigureAwait(false);
        if (stored is not null)
            return SecretKey.FromBytes(stored);

        SecretKey created = SecretKey.Generate(random32);
        await store.SaveNewSecretAsync(created.ToBytes(), cancellationToken).ConfigureAwait(false);
        return created;
    }
}

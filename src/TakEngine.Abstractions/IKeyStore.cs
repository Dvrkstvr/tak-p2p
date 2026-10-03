using System;
using System.Threading;
using System.Threading.Tasks;

namespace TakEngine.Abstractions;

/// <summary>
/// Seam for the one persisted player secret (D-011). Real: FileKeyStore (CLI/desktop), BrowserKeyStore (M1+).
/// Implementations validate what they read and never overwrite an existing identity.
/// </summary>
public interface IKeyStore
{
    /// <summary>
    /// The stored 32-byte secret, or null when no identity has been saved yet.
    /// Throws <see cref="KeyStoreException"/> (specific message) when an identity exists but cannot be read; the stored data is left untouched.
    /// </summary>
    Task<byte[]?> LoadSecretAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves a new identity. Throws <see cref="KeyStoreException"/> if one already exists: an identity is never overwritten.</summary>
    Task SaveNewSecretAsync(byte[] secret32, CancellationToken cancellationToken = default);
}

/// <summary>The stored identity is unreadable or an identity already exists. The message says which and where.</summary>
public sealed class KeyStoreException(string message, Exception? innerException = null) : Exception(message, innerException);

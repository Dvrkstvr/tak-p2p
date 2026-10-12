using TakEngine.Abstractions;

namespace TakEngine.Testing;

/// <summary><see cref="IKeyStore"/> in memory. Can be pre-loaded with a secret or with a corrupt blob that fails to load.</summary>
public sealed class InMemoryKeyStore : IKeyStore
{
    private byte[]? _secret;
    private bool _corrupt;

    public InMemoryKeyStore(byte[]? secret = null) => _secret = secret is null ? null : (byte[])secret.Clone();

    public static InMemoryKeyStore Corrupt() => new() { _corrupt = true };

    public Task<byte[]?> LoadSecretAsync(CancellationToken cancellationToken = default)
    {
        if (_corrupt)
            return Task.FromException<byte[]?>(new KeyStoreException("The stored identity is corrupt (in-memory fake)."));
        return Task.FromResult(_secret is null ? null : (byte[]?)_secret.Clone());
    }

    public Task SaveNewSecretAsync(byte[] secret32, CancellationToken cancellationToken = default)
    {
        if (_secret is not null || _corrupt)
            return Task.FromException(new KeyStoreException("An identity already exists; it is never overwritten."));
        _secret = (byte[])secret32.Clone();
        return Task.CompletedTask;
    }
}

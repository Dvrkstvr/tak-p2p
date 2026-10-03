using TakEngine.Abstractions;
using TakEngine.Crypto;

namespace TakEngine.Storage.Local;

/// <summary>
/// The CLI/desktop identity: <c>&lt;data-dir&gt;/identity.json</c> = <c>{"v":1,"nsec":"nsec1…"}</c> (IdentityDocument).
/// Reads validate fully; a file that cannot be read is reported and never rewritten; an existing file is never overwritten.
/// </summary>
public sealed class FileKeyStore : IKeyStore
{
    public const string FileName = "identity.json";

    public FileKeyStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        FilePath = Path.Combine(dataDirectory, FileName);
    }

    public string FilePath { get; }

    public async Task<byte[]?> LoadSecretAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
            return null;

        string document = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
        try
        {
            return IdentityDocument.Parse(document).ToBytes();
        }
        catch (IdentityFormatException ex)
        {
            throw new KeyStoreException($"Cannot read the identity in {FilePath}: {ex.Message} The file was left unchanged.", ex);
        }
    }

    public async Task SaveNewSecretAsync(byte[] secret32, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret32);
        SecretKey key = SecretKey.FromBytes(secret32);
        if (File.Exists(FilePath))
            throw new KeyStoreException($"An identity already exists in {FilePath}; it is never overwritten.");

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temp, IdentityDocument.Serialize(key), cancellationToken).ConfigureAwait(false);
        try
        {
            File.Move(temp, FilePath, overwrite: false);
        }
        catch (IOException ex)
        {
            File.Delete(temp);
            throw new KeyStoreException($"An identity already exists in {FilePath}; it is never overwritten.", ex);
        }
    }
}

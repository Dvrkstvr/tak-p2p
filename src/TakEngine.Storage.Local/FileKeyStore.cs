using System.Text;
using TakEngine.Abstractions;
using TakEngine.Crypto;

namespace TakEngine.Storage.Local;

/// <summary>
/// The CLI/desktop identity: <c>&lt;data-dir&gt;/identity.json</c> = <c>{"v":1,"nsec":"nsec1…"}</c> (IdentityDocument).
/// Reads validate fully; a file that cannot be read is reported and never rewritten; an existing file is never overwritten.
/// On Unix the file is created owner-read/write only; nothing here puts the secret in a message.
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

        string document;
        try
        {
            document = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new KeyStoreException($"The identity in {FilePath} could not be read: {ex.Message} The file was left unchanged.", ex);
        }

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
        bool moved = false;
        try
        {
            await WriteOwnerOnlyAsync(temp, IdentityDocument.Serialize(key), cancellationToken).ConfigureAwait(false);
            try
            {
                File.Move(temp, FilePath, overwrite: false);
                moved = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw File.Exists(FilePath)
                    ? new KeyStoreException($"An identity already exists in {FilePath}; it is never overwritten.", ex)
                    : new KeyStoreException($"The identity could not be written to {FilePath}: {ex.Message}", ex);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new KeyStoreException($"The identity could not be written to {FilePath}: {ex.Message}", ex);
        }
        finally
        {
            if (!moved)
                TryDelete(temp);
        }
    }

    private static async Task WriteOwnerOnlyAsync(string path, string content, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; // throws on Windows; the profile ACL applies there

        await using var stream = new FileStream(path, options);
        await stream.WriteAsync(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content), cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a failing cleanup must not mask the exception that is already on its way.
        }
    }
}

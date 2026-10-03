using TakEngine.Abstractions;
using TakEngine.Crypto;
using TakEngine.Storage.Local;

namespace TakEngine.Storage.Tests;

/// <summary>F-031: the key is generated once, persisted, reloaded after a restart, and a bad key file is reported, never overwritten.</summary>
public sealed class FileKeyStoreTests : IDisposable
{
    private const string SpecNsecHex = "67dea2ed018072d675f5415ecfaed7d2597555e202d85b3d65ea4e58d2d92ffa";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tak-keystore-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string IdentityPath => Path.Combine(_dir, FileKeyStore.FileName);

    private static Func<byte[]> FixedRandom(string hex) => () => Convert.FromHexString(hex);

    [Fact]
    public async Task FirstRun_GeneratesAndSaves_AndARestartLoadsTheSameKey()
    {
        SecretKey first = await IdentityBootstrap.LoadOrCreateAsync(new FileKeyStore(_dir), FixedRandom(SpecNsecHex));

        // "Restart": a new store instance over the same directory, with a random source that must not be used.
        SecretKey second = await IdentityBootstrap.LoadOrCreateAsync(
            new FileKeyStore(_dir), () => throw new InvalidOperationException("must not generate on restart"));

        Assert.Equal(SpecNsecHex, first.ToHex());
        Assert.Equal(first.ToHex(), second.ToHex());
        Assert.Equal(first.PublicKey, second.PublicKey);
    }

    [Fact]
    public async Task SavedFile_IsTheVersionedNsecDocument()
    {
        await IdentityBootstrap.LoadOrCreateAsync(new FileKeyStore(_dir), FixedRandom(SpecNsecHex));

        Assert.Equal(
            "{\"v\":1,\"nsec\":\"nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe5\"}",
            await File.ReadAllTextAsync(IdentityPath));
        Assert.Single(Directory.GetFiles(_dir)); // no temp file left behind
    }

    [Fact]
    public async Task NoFile_LoadsNull()
    {
        Assert.Null(await new FileKeyStore(_dir).LoadSecretAsync());
    }

    public static TheoryData<string, string> BadFiles() => new()
    {
        { "{\"v\":1,\"nsec\":\"" + Nip19.Encode("nsec", new byte[31]) + "\"}", "32 bytes, got 31" },
        { "{\"v\":1,\"nsec\":\"" + Nip19.Encode("nsec", new byte[33]) + "\"}", "32 bytes, got 33" },
        { "{\"v\":1,\"nsec\":\"" + Nip19.Encode("nsec", Convert.FromHexString("fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141")) + "\"}", "out of range" },
        { "{\"v\":1,\"nsec\":\"" + Nip19.Encode("nsec", new byte[32]) + "\"}", "out of range" },
        { "{\"v\":2,\"nsec\":\"nsec1vl029mgpspedva04g90vltkh6fvh240zqtv9k0t9af8935ke9laqsnlfe5\"}", "newer version" },
        { "not json", "not valid JSON" },
        { "{\"v\":1,\"nsec\":\"nsec1", "not valid JSON" },
        { "", "not valid JSON" },
    };

    [Theory]
    [MemberData(nameof(BadFiles))]
    public async Task BadKeyFile_GivesAClearError_AndIsNotOverwritten(string content, string expectedMessagePart)
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(IdentityPath, content);
        byte[] before = await File.ReadAllBytesAsync(IdentityPath);

        var loadError = await Assert.ThrowsAsync<KeyStoreException>(() => new FileKeyStore(_dir).LoadSecretAsync());
        var bootstrapError = await Assert.ThrowsAsync<KeyStoreException>(
            () => IdentityBootstrap.LoadOrCreateAsync(new FileKeyStore(_dir), FixedRandom(SpecNsecHex)));

        Assert.Contains(expectedMessagePart, loadError.Message);
        Assert.Contains(IdentityPath, loadError.Message);
        Assert.Contains(expectedMessagePart, bootstrapError.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(IdentityPath));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task SaveNewSecret_RefusesToOverwriteAnExistingIdentity()
    {
        var store = new FileKeyStore(_dir);
        await store.SaveNewSecretAsync(Convert.FromHexString(SpecNsecHex));
        byte[] before = await File.ReadAllBytesAsync(IdentityPath);

        var ex = await Assert.ThrowsAsync<KeyStoreException>(
            () => store.SaveNewSecretAsync(SecretKey.FromHex("0000000000000000000000000000000000000000000000000000000000000003").ToBytes()));

        Assert.Contains("never overwritten", ex.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(IdentityPath));
    }

    [Fact]
    public async Task SaveNewSecret_RejectsAnInvalidSecret_AndWritesNothing()
    {
        await Assert.ThrowsAsync<InvalidKeyException>(() => new FileKeyStore(_dir).SaveNewSecretAsync(new byte[32]));

        Assert.False(File.Exists(IdentityPath));
    }
}

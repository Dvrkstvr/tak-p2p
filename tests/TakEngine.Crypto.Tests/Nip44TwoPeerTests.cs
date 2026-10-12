using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace TakEngine.Crypto.Tests;

/// <summary>
/// F-015: two independent peers. The receiver always decrypts with HIS OWN secret and the sender's public key;
/// nothing here decrypts with the sender's own keys (that is how the deleted Nip44Encryption passed while wrong).
/// </summary>
public class Nip44TwoPeerTests
{
    private const string Move = "{\"pv\":1,\"action_type\":\"MOVE\",\"turn\":7,\"action_data\":{\"ptn\":\"3c3+12\"}}";

    private static SecretKey NewKey(Random random) => SecretKey.Generate(() =>
    {
        byte[] bytes = new byte[32];
        random.NextBytes(bytes);
        return bytes;
    });

    private static byte[] NewNonce(Random random)
    {
        byte[] nonce = new byte[32];
        random.NextBytes(nonce);
        return nonce;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(20261002)]
    public void IndependentPeers_DeriveTheSameKey_AndDecryptEachOther(int seed)
    {
        var random = new Random(seed);
        var alice = NewKey(random);
        var bob = NewKey(random);

        byte[] aliceSide = Nip44.ConversationKey(alice, bob.PublicKey);
        byte[] bobSide = Nip44.ConversationKey(bob, alice.PublicKey);
        Assert.Equal(aliceSide, bobSide);

        string toBob = Nip44.Encrypt(Move, aliceSide, NewNonce(random));
        Assert.Equal(Move, Nip44.Decrypt(toBob, bobSide));

        string toAlice = Nip44.Encrypt("ack é 表 🦄", bobSide, NewNonce(random));
        Assert.Equal("ack é 表 🦄", Nip44.Decrypt(toAlice, aliceSide));
    }

    [Fact]
    public void ThirdKey_CannotDecrypt()
    {
        var random = new Random(7);
        var alice = NewKey(random);
        var bob = NewKey(random);
        var eve = NewKey(random);
        string toBob = Nip44.Encrypt(Move, Nip44.ConversationKey(alice, bob.PublicKey), NewNonce(random));

        var ex = Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(toBob, Nip44.ConversationKey(eve, alice.PublicKey)));
        Assert.Equal(Nip44Error.InvalidMac, ex.Error);
    }

    [Theory]
    [InlineData(1)]     // first nonce byte
    [InlineData(40)]    // ciphertext
    [InlineData(-1)]    // last MAC byte
    public void FlippedBit_FailsTheMac(int position)
    {
        var random = new Random(11);
        var alice = NewKey(random);
        var bob = NewKey(random);
        string payload = Nip44.Encrypt(Move, Nip44.ConversationKey(alice, bob.PublicKey), NewNonce(random));
        byte[] raw = Convert.FromBase64String(payload);
        raw[position >= 0 ? position : raw.Length + position] ^= 0x01;

        var ex = Assert.Throws<Nip44Exception>(
            () => Nip44.Decrypt(Convert.ToBase64String(raw), Nip44.ConversationKey(bob, alice.PublicKey)));
        Assert.Equal(Nip44Error.InvalidMac, ex.Error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(0)]
    public void WrongVersionByte_IsUnknownVersion(byte version)
    {
        var random = new Random(13);
        var alice = NewKey(random);
        var bob = NewKey(random);
        byte[] raw = Convert.FromBase64String(Nip44.Encrypt(Move, Nip44.ConversationKey(alice, bob.PublicKey), NewNonce(random)));
        raw[0] = version;

        var ex = Assert.Throws<Nip44Exception>(
            () => Nip44.Decrypt(Convert.ToBase64String(raw), Nip44.ConversationKey(bob, alice.PublicKey)));
        Assert.Equal(Nip44Error.UnknownVersion, ex.Error);
    }

    [Theory]
    [InlineData(0)]  // zero length
    [InlineData(31)] // calc_padded_len(31) = 32, but the block is 64 bytes
    [InlineData(65)] // calc_padded_len(65) = 96: claims more bytes than the 64-byte block holds
    public void BadPadding_WithAValidMac_IsInvalidPadding(int claimedLength)
    {
        var random = new Random(17);
        var alice = NewKey(random);
        var bob = NewKey(random);
        byte[] conversationKey = Nip44.ConversationKey(alice, bob.PublicKey);
        byte[] nonce = NewNonce(random);

        // A sender who authenticates correctly but pads wrongly: 2-byte length prefix + a 64-byte block.
        byte[] padded = new byte[2 + 64];
        BinaryPrimitives.WriteUInt16BigEndian(padded, (ushort)claimedLength);
        string payload = SealWithValidMac(conversationKey, nonce, padded);

        var ex = Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(payload, Nip44.ConversationKey(bob, alice.PublicKey)));
        Assert.Equal(Nip44Error.InvalidPadding, ex.Error);
    }

    [Fact]
    public void PayloadSizeLimits_AreChecked_BeforeAnyCrypto()
    {
        byte[] key = new byte[32];
        byte[] tooShortData = new byte[98];
        tooShortData[0] = 2;
        byte[] tooLongData = new byte[65604];
        tooLongData[0] = 2;

        Assert.Equal(Nip44Error.UnknownVersion, Assert.Throws<Nip44Exception>(() => Nip44.Decrypt("#" + new string('A', 140), key)).Error);
        Assert.Equal(Nip44Error.InvalidPayloadSize, Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(new string('A', 131), key)).Error);
        Assert.Equal(Nip44Error.InvalidPayloadSize, Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(new string('A', 87473), key)).Error);
        Assert.Equal(Nip44Error.InvalidPayloadSize, Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(Convert.ToBase64String(tooShortData), key)).Error);
        Assert.Equal(Nip44Error.InvalidPayloadSize, Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(Convert.ToBase64String(tooLongData), key)).Error);
    }

    [Fact]
    public void PlaintextLimits_AreCountedInUtf8Bytes()
    {
        var random = new Random(19);
        byte[] key = Nip44.ConversationKey(NewKey(random), NewKey(random).PublicKey);
        string maxAscii = new('x', Nip44.MaxPlaintextBytes);
        string maxThreeByte = new('表', Nip44.MaxPlaintextBytes / 3);

        Assert.Equal(maxAscii, Nip44.Decrypt(Nip44.Encrypt(maxAscii, key, NewNonce(random)), key));
        Assert.Equal(maxThreeByte, Nip44.Decrypt(Nip44.Encrypt(maxThreeByte, key, NewNonce(random)), key));
        Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt(maxAscii + "x", key, NewNonce(random)));
        Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt(maxThreeByte + "表", key, NewNonce(random)));
        Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt("", key, NewNonce(random)));
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        var key = SecretKey.FromHex("0000000000000000000000000000000000000000000000000000000000000003");
        Assert.Throws<ArgumentNullException>(() => Nip44.ConversationKey(null!, key.PublicKey));
        Assert.Throws<ArgumentNullException>(() => Nip44.ConversationKey(key, null!));
        Assert.Throws<ArgumentNullException>(() => Nip44.MessageKeys(null!, new byte[32]));
        Assert.Throws<ArgumentNullException>(() => Nip44.MessageKeys(new byte[32], null!));
        Assert.Throws<ArgumentNullException>(() => Nip44.Encrypt(null!, new byte[32], new byte[32]));
        Assert.Throws<ArgumentNullException>(() => Nip44.Decrypt(null!, new byte[32]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Nip44.CalcPaddedLength(0));
    }

    [Fact]
    public void Encrypt_RejectsWrongKeyOrNonceLength()
    {
        Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt("a", new byte[31], new byte[32]));
        Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt("a", new byte[32], new byte[31]));
        Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt("a", new byte[32], new byte[33]));
    }

    [Fact]
    public void TwoPeerRoundTrip_IsFastEnoughForOneMovePerTurn()
    {
        // Replaces TransportBenchmarkTests (which decrypted with the sender's own secret). Best of 5 runs, so a
        // cold JIT or a busy CI runner does not make it flaky; the bound is generous on purpose.
        var random = new Random(23);
        var alice = NewKey(random);
        var bob = NewKey(random);
        long best = long.MaxValue;
        for (int run = 0; run < 5; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            string payload = Nip44.Encrypt(Move, Nip44.ConversationKey(alice, bob.PublicKey), NewNonce(random));
            string received = Nip44.Decrypt(payload, Nip44.ConversationKey(bob, alice.PublicKey));
            stopwatch.Stop();
            Assert.Equal(Move, received);
            best = Math.Min(best, stopwatch.ElapsedMilliseconds);
        }

        Assert.True(best < 300, $"best of 5 two-peer round trips took {best} ms");
    }

    private static string SealWithValidMac(byte[] conversationKey, byte[] nonce, byte[] padded)
    {
        var (chachaKey, chachaNonce, hmacKey) = Nip44.MessageKeys(conversationKey, nonce);
        var engine = new ChaCha7539Engine();
        engine.Init(true, new ParametersWithIV(new KeyParameter(chachaKey), chachaNonce));
        byte[] ciphertext = new byte[padded.Length];
        engine.ProcessBytes(padded, 0, padded.Length, ciphertext, 0);
        byte[] mac = HMACSHA256.HashData(hmacKey, (byte[])[.. nonce, .. ciphertext]);
        return Convert.ToBase64String([2, .. nonce, .. ciphertext, .. mac]);
    }
}

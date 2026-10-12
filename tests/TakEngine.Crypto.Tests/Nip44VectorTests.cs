using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TakEngine.Crypto.Tests;

/// <summary>F-015: paulmillr/nip44 nip44.vectors.json, every section.</summary>
public class Nip44VectorTests
{
    private static readonly JsonElement V2 = VectorFiles.Nip44V2();
    private static readonly JsonElement Valid = V2.GetProperty("valid");
    private static readonly JsonElement Invalid = V2.GetProperty("invalid");

    private static byte[] Hex(JsonElement e, string name) => Convert.FromHexString(e.GetProperty(name).GetString()!);

    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;

    [Fact]
    public void VectorFile_HasTheExpectedSectionSizes()
    {
        Assert.Equal(35, Valid.GetProperty("get_conversation_key").GetArrayLength());
        Assert.Equal(32, Valid.GetProperty("get_message_keys").GetProperty("keys").GetArrayLength());
        Assert.Equal(24, Valid.GetProperty("calc_padded_len").GetArrayLength());
        Assert.Equal(10, Valid.GetProperty("encrypt_decrypt").GetArrayLength());
        Assert.Equal(3, Valid.GetProperty("encrypt_decrypt_long_msg").GetArrayLength());
        Assert.Equal(8, Invalid.GetProperty("get_conversation_key").GetArrayLength());
        Assert.Equal(12, Invalid.GetProperty("decrypt").GetArrayLength());
        Assert.Equal(4, Invalid.GetProperty("encrypt_msg_lengths").GetArrayLength());
    }

    [Fact]
    public void ConversationKey_MatchesAllValidVectors()
    {
        foreach (JsonElement t in Valid.GetProperty("get_conversation_key").EnumerateArray())
        {
            byte[] actual = Nip44.ConversationKey(SecretKey.FromHex(Str(t, "sec1")), PublicKey.FromHex(Str(t, "pub2")));

            Assert.Equal(Str(t, "conversation_key"), Convert.ToHexStringLower(actual));
        }
    }

    [Fact]
    public void ConversationKey_InvalidVectors_AreRejectedAtTheKeyBoundary()
    {
        foreach (JsonElement t in Invalid.GetProperty("get_conversation_key").EnumerateArray())
        {
            string note = Str(t, "note");
            var ex = Assert.Throws<InvalidKeyException>(
                () => Nip44.ConversationKey(SecretKey.FromHex(Str(t, "sec1")), PublicKey.FromHex(Str(t, "pub2"))));
            Assert.True(ex.Message.Length > 0, note);
        }
    }

    [Fact]
    public void MessageKeys_MatchAllVectors()
    {
        JsonElement section = Valid.GetProperty("get_message_keys");
        byte[] conversationKey = Hex(section, "conversation_key");
        foreach (JsonElement t in section.GetProperty("keys").EnumerateArray())
        {
            var (chachaKey, chachaNonce, hmacKey) = Nip44.MessageKeys(conversationKey, Hex(t, "nonce"));

            Assert.Equal(Str(t, "chacha_key"), Convert.ToHexStringLower(chachaKey));
            Assert.Equal(Str(t, "chacha_nonce"), Convert.ToHexStringLower(chachaNonce));
            Assert.Equal(Str(t, "hmac_key"), Convert.ToHexStringLower(hmacKey));
        }
    }

    [Fact]
    public void CalcPaddedLength_MatchesAllVectors()
    {
        foreach (JsonElement t in Valid.GetProperty("calc_padded_len").EnumerateArray())
            Assert.Equal(t[1].GetInt32(), Nip44.CalcPaddedLength(t[0].GetInt32()));
    }

    [Fact]
    public void EncryptDecrypt_MatchesAllVectors_InBothKeyDirections()
    {
        foreach (JsonElement t in Valid.GetProperty("encrypt_decrypt").EnumerateArray())
        {
            var sec1 = SecretKey.FromHex(Str(t, "sec1"));
            var sec2 = SecretKey.FromHex(Str(t, "sec2"));
            byte[] expectedKey = Hex(t, "conversation_key");
            string plaintext = Str(t, "plaintext");
            string payload = Str(t, "payload");

            Assert.Equal(expectedKey, Nip44.ConversationKey(sec1, sec2.PublicKey));
            Assert.Equal(expectedKey, Nip44.ConversationKey(sec2, sec1.PublicKey));
            Assert.Equal(payload, Nip44.Encrypt(plaintext, expectedKey, Hex(t, "nonce")));
            Assert.Equal(plaintext, Nip44.Decrypt(payload, expectedKey));
        }
    }

    [Fact]
    public void LongMessages_MatchAllVectors()
    {
        foreach (JsonElement t in Valid.GetProperty("encrypt_decrypt_long_msg").EnumerateArray())
        {
            byte[] conversationKey = Hex(t, "conversation_key");
            string plaintext = string.Concat(Enumerable.Repeat(Str(t, "pattern"), t.GetProperty("repeat").GetInt32()));

            string payload = Nip44.Encrypt(plaintext, conversationKey, Hex(t, "nonce"));

            Assert.Equal(Str(t, "plaintext_sha256"), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext))));
            Assert.Equal(Str(t, "payload_sha256"), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
            Assert.Equal(plaintext, Nip44.Decrypt(payload, conversationKey));
        }
    }

    [Fact]
    public void InvalidPayloads_FailToDecrypt_WithTheReasonTheVectorNames()
    {
        foreach (JsonElement t in Invalid.GetProperty("decrypt").EnumerateArray())
        {
            string note = Str(t, "note");
            Nip44Error expected = note switch
            {
                _ when note.StartsWith("unknown encryption version", StringComparison.Ordinal) => Nip44Error.UnknownVersion,
                "invalid base64" => Nip44Error.InvalidBase64,
                "invalid MAC" => Nip44Error.InvalidMac,
                "invalid padding" => Nip44Error.InvalidPadding,
                _ when note.StartsWith("invalid payload length", StringComparison.Ordinal) => Nip44Error.InvalidPayloadSize,
                _ => throw new InvalidOperationException("unmapped vector note: " + note),
            };

            var ex = Assert.Throws<Nip44Exception>(() => Nip44.Decrypt(Str(t, "payload"), Hex(t, "conversation_key")));
            Assert.True(expected == ex.Error, $"'{note}': expected {expected}, got {ex.Error}");
        }
    }

    [Fact]
    public void InvalidMessageLengths_AreRejectedOnEncrypt()
    {
        byte[] conversationKey = Convert.FromHexString("c41c775356fd92eadc63ff5a0dc1da211b268cbea22316767095b2871ea1412d");
        foreach (JsonElement length in Invalid.GetProperty("encrypt_msg_lengths").EnumerateArray())
        {
            string plaintext = new('x', length.GetInt32());
            Assert.ThrowsAny<ArgumentException>(() => Nip44.Encrypt(plaintext, conversationKey, new byte[32]));
        }
    }
}

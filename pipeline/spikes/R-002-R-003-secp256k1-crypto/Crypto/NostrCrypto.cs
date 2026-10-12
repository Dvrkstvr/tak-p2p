using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NBitcoin.Secp256k1;
using HMACSHA256 = System.Security.Cryptography.HMACSHA256;
using SHA256 = System.Security.Cryptography.SHA256;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace Spike.Crypto;

/// <summary>One secp256k1 key for everything (D-011): npub identity, BIP-340 signing, NIP-44 v2 ECDH.</summary>
public sealed class NostrKey
{
    public byte[] Secret { get; }
    public byte[] XOnlyPub { get; }
    public string PubHex => Convert.ToHexStringLower(XOnlyPub);
    private readonly ECPrivKey _priv;

    public NostrKey(byte[] secret32)
    {
        if (!ECPrivKey.TryCreate(secret32, out var p) || p is null) throw new ArgumentException("invalid secret key");
        _priv = p; Secret = secret32;
        XOnlyPub = p.CreateXOnlyPubKey().ToBytes();
    }

    public static NostrKey Generate()
    {
        while (true)
        {
            var b = RandomNumberGenerator.GetBytes(32);
            if (ECPrivKey.TryCreate(b, out _)) return new NostrKey(b);
        }
    }

    /// <summary>BIP-340 Schnorr signature over a 32-byte message (fresh aux randomness).</summary>
    public byte[] SignBip340(byte[] msg32) => _priv.SignBIP340(msg32).ToBytes();

    public static bool VerifyBip340(byte[] xonlyPub32, byte[] msg32, byte[] sig64)
    {
        if (!ECXOnlyPubKey.TryCreate(xonlyPub32, out var pub) || pub is null) return false;
        if (!SecpSchnorrSignature.TryCreate(sig64, out var sig) || sig is null) return false;
        return pub.SigVerifyBIP340(sig, msg32);
    }

    /// <summary>NIP-44 conversation key: HKDF-extract(salt="nip44-v2", IKM = unhashed x of a*B). conv(a,B)==conv(b,A).</summary>
    public byte[] ConversationKey(byte[] peerXOnlyPub32)
    {
        var lifted = new byte[33]; lifted[0] = 0x02; peerXOnlyPub32.CopyTo(lifted, 1); // x-only => even y
        if (!ECPubKey.TryCreate(lifted, Context.Instance, out _, out var peer) || peer is null)
            throw new ArgumentException("peer pubkey not on curve");
        var shared = peer.GetSharedPubkey(_priv);               // raw point a*B, NOT hashed
        var x = shared.ToBytes(true).AsSpan(1, 32).ToArray();   // unhashed 32-byte x coordinate
        return Nip44.HkdfExtract(Encoding.UTF8.GetBytes("nip44-v2"), x);
    }
}

public static class Nip44
{
    public static byte[] HkdfExtract(byte[] salt, byte[] ikm) => HMACSHA256.HashData(salt, ikm);

    public static byte[] HkdfExpand(byte[] prk, byte[] info, int len)
    {
        var okm = new byte[len]; var t = Array.Empty<byte>(); int pos = 0; byte i = 1;
        while (pos < len)
        {
            var input = new byte[t.Length + info.Length + 1];
            t.CopyTo(input, 0); info.CopyTo(input, t.Length); input[^1] = i++;
            t = HMACSHA256.HashData(prk, input);
            int n = Math.Min(t.Length, len - pos); Array.Copy(t, 0, okm, pos, n); pos += n;
        }
        return okm;
    }

    public static (byte[] chachaKey, byte[] chachaNonce, byte[] hmacKey) MessageKeys(byte[] convKey, byte[] nonce)
    {
        if (convKey.Length != 32) throw new ArgumentException("invalid conversation_key length");
        if (nonce.Length != 32) throw new ArgumentException("invalid nonce length");
        var k = HkdfExpand(convKey, nonce, 76);
        return (k[0..32], k[32..44], k[44..76]);
    }

    public static int CalcPaddedLen(int len)
    {
        if (len <= 32) return 32;
        int nextPower = 1 << ((int)Math.Floor(Math.Log2(len - 1)) + 1);
        int chunk = nextPower <= 256 ? 32 : nextPower / 8;
        return chunk * (((len - 1) / chunk) + 1);
    }

    public static byte[] Pad(string plaintext)
    {
        var u = Encoding.UTF8.GetBytes(plaintext);
        if (u.Length < 1) throw new ArgumentException("invalid plaintext length");
        bool ext = u.Length >= 65536;
        int prefixLen = ext ? 6 : 2;
        var res = new byte[prefixLen + CalcPaddedLen(u.Length)];
        if (ext) BinaryPrimitives.WriteUInt32BigEndian(res.AsSpan(2), (uint)u.Length);
        else BinaryPrimitives.WriteUInt16BigEndian(res, (ushort)u.Length);
        u.CopyTo(res, prefixLen);
        return res;
    }

    public static string Unpad(byte[] padded)
    {
        int first = BinaryPrimitives.ReadUInt16BigEndian(padded);
        int prefixLen, len;
        if (first == 0)
        {
            len = checked((int)BinaryPrimitives.ReadUInt32BigEndian(padded.AsSpan(2)));
            if (len < 65536) throw new CryptographicException("invalid padding");
            prefixLen = 6;
        }
        else { len = first; prefixLen = 2; }
        if (len == 0 || padded.Length < prefixLen + len || padded.Length != prefixLen + CalcPaddedLen(len))
            throw new CryptographicException("invalid padding");
        return Encoding.UTF8.GetString(padded, prefixLen, len);
    }

    private static byte[] ChaCha20(byte[] key, byte[] nonce12, byte[] data)
    {
        var e = new ChaCha7539Engine(); // RFC 8439, counter starts at 0
        e.Init(true, new ParametersWithIV(new KeyParameter(key), nonce12));
        var o = new byte[data.Length]; e.ProcessBytes(data, 0, data.Length, o, 0); return o;
    }

    private static byte[] HmacAad(byte[] key, byte[] msg, byte[] aad)
    {
        if (aad.Length != 32) throw new ArgumentException("AAD associated data must be 32 bytes");
        return HMACSHA256.HashData(key, ((byte[])[.. aad, .. msg]));
    }

    public static string Encrypt(string plaintext, byte[] convKey, byte[]? nonce = null)
    {
        nonce ??= RandomNumberGenerator.GetBytes(32);
        var (ck, cn, hk) = MessageKeys(convKey, nonce);
        var ct = ChaCha20(ck, cn, Pad(plaintext));
        var mac = HmacAad(hk, ct, nonce);
        return Convert.ToBase64String([2, .. nonce, .. ct, .. mac]);
    }

    public static string Decrypt(string payload, byte[] convKey)
    {
        int plen = payload.Length;
        if (plen == 0 || payload[0] == '#') throw new NotSupportedException("unknown version");
        if (plen < 132) throw new CryptographicException("invalid payload size");
        var data = Convert.FromBase64String(payload);
        if (data.Length < 99) throw new CryptographicException("invalid data size");
        if (data[0] != 2) throw new NotSupportedException("unknown version " + data[0]);
        var nonce = data[1..33]; var ct = data[33..^32]; var mac = data[^32..];
        var (ck, cn, hk) = MessageKeys(convKey, nonce);
        if (!CryptographicOperations.FixedTimeEquals(HmacAad(hk, ct, nonce), mac)) throw new CryptographicException("invalid MAC");
        return Unpad(ChaCha20(ck, cn, ct));
    }
}

public sealed class NostrEventLite
{
    public string Id = ""; public string Pubkey = ""; public long CreatedAt; public int Kind;
    public List<List<string>> Tags = new(); public string Content = ""; public string Sig = "";

    private static readonly System.Text.Encodings.Web.JavaScriptEncoder Enc = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>NIP-01 serialisation [0,pubkey,created_at,kind,tags,content], compact UTF-8. Only newline, quote, backslash, CR, tab,
    /// backspace and form-feed get short escapes; other control chars become \u00xx (as JSON.stringify does); everything else is verbatim.
    /// System.Text.Json is deliberately NOT used: its default encoder escapes + &lt; &gt; and all non-ASCII, and even
    /// UnsafeRelaxedJsonEscaping still escapes astral (surrogate-pair) characters, so the id would differ from what relays compute.</summary>
    public string Serialize()
    {
        var sb = new StringBuilder();
        sb.Append("[0,"); Str(sb, Pubkey); sb.Append(',').Append(CreatedAt).Append(',').Append(Kind).Append(",[");
        for (int i = 0; i < Tags.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('[');
            for (int j = 0; j < Tags[i].Count; j++) { if (j > 0) sb.Append(','); Str(sb, Tags[i][j]); }
            sb.Append(']');
        }
        sb.Append("],"); Str(sb, Content); sb.Append(']');
        return sb.ToString();
    }

    private static void Str(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                    break;
            }
        sb.Append('"');
    }

    public string ComputeId() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize())));

    public void Sign(NostrKey k) { Pubkey = k.PubHex; Id = ComputeId(); Sig = Convert.ToHexStringLower(k.SignBip340(Convert.FromHexString(Id))); }

    public bool Verify() =>
        Id == ComputeId() && NostrKey.VerifyBip340(Convert.FromHexString(Pubkey), Convert.FromHexString(Id), Convert.FromHexString(Sig));

    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object> {
        ["id"] = Id, ["pubkey"] = Pubkey, ["created_at"] = CreatedAt, ["kind"] = Kind, ["tags"] = Tags, ["content"] = Content, ["sig"] = Sig },
        new JsonSerializerOptions { Encoder = Enc });
}

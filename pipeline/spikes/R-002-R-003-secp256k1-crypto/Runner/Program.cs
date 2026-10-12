using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spike.Crypto;

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) pass++; else fail++;
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? "  -- " + detail : "")}");
}
byte[] H(string s) => Convert.FromHexString(s);
string X(byte[] b) => Convert.ToHexStringLower(b);
bool Throws(Action a) { try { a(); return false; } catch { return true; } }
string dir = AppContext.BaseDirectory;
string vec = Path.GetFullPath(Path.Combine(dir, "../../../../vectors"));

// ---------------------------------------------------------------------------------------------
Console.WriteLine("== Q1: R-002 against CURRENT src (Nip44Encryption + CryptoSigner Ed25519 identities) ==");
{
    var alice = TakEngine.Core.Cryptography.CryptoSigner.GenerateKeyPair();
    var bob = TakEngine.Core.Cryptography.CryptoSigner.GenerateKeyPair();
    // exactly what NostrTransportClient does (lines 103 / 215): sender uses (myPriv, theirPub), receiver uses (myPriv, senderPub)
    var aliceSecret = TakEngine.Transport.Nostr.Nip44Encryption.DeriveSharedSecret(alice.PrivateKeyHex, bob.PublicKeyHex);
    var bobSecret = TakEngine.Transport.Nostr.Nip44Encryption.DeriveSharedSecret(bob.PrivateKeyHex, alice.PublicKeyHex);
    Check("Alice and Bob derive DIFFERENT shared secrets (ECDH would make them equal)", !aliceSecret.SequenceEqual(bobSecret),
        $"alice={X(aliceSecret)[..16]}.. bob={X(bobSecret)[..16]}..");
    var payload = TakEngine.Transport.Nostr.Nip44Encryption.Encrypt("e2e2 e3", aliceSecret);
    string? got = null; string err = "";
    try { got = TakEngine.Transport.Nostr.Nip44Encryption.Decrypt(payload, bobSecret); } catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
    Check("Bob CANNOT decrypt Alice's message with his own priv + Alice's pub", got is null, err);
    // sender-decrypts-own-message (what the existing tests do) passes, which is why the bug is hidden
    var self = TakEngine.Transport.Nostr.Nip44Encryption.Decrypt(payload, aliceSecret);
    Check("(control) Alice decrypting her own message with her own secret passes -> existing tests are blind to R-002", self == "e2e2 e3");
    var raw = Convert.FromBase64String(payload);
    Check("(format) current payload is not NIP-44 v2 layout: 12-byte nonce + Poly1305 tag, nonce not 32 bytes", raw[0] == 2 && raw.Length == 1 + 12 + Encoding.UTF8.GetByteCount("e2e2 e3") + 16,
        $"len={raw.Length}, spec min is 99 bytes / 132 base64 chars");
    // R-003 companion: the app's own NostrEvent.ComputeId (System.Text.Json default encoder) vs the NIP-01 id
    {
        var cur = new TakEngine.Transport.Nostr.NostrEvent { Pubkey = new string('a', 64), CreatedAt = 1700000000, Kind = 4, Content = "ab+cd/ef==", Tags = { new() { "p", "b" } } };
        var spec = new NostrEventLite { Pubkey = cur.Pubkey, CreatedAt = cur.CreatedAt, Kind = cur.Kind, Content = cur.Content, Tags = { new() { "p", "b" } } };
        Check("current NostrEvent.ComputeId differs from NIP-01 id for base64 content containing '+' (every encrypted event)", cur.ComputeId() != spec.ComputeId(),
            $"current={cur.ComputeId()[..12]}.. nip01={spec.ComputeId()[..12]}..");
        Check("current NostrEvent.Sig is empty string (never assigned) -> relays see no signature", cur.Sig == "");
    }
    // definitive R-003 identity check: does Ed25519 seed -> secp256k1 pub equal Ed25519 pub? (never)
    var derived = new NostrKey(H(alice.PrivateKeyHex)).PubHex;
    Check("(identity) secp256k1 pubkey of the same 32-byte secret != the Ed25519 pubkey the app publishes as 'npub'", derived != alice.PublicKeyHex);
}

// ---------------------------------------------------------------------------------------------
Console.WriteLine("== Q2a: NIP-44 v2 official vectors (paulmillr/nip44 nip44.vectors.json) ==");
{
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(vec, "nip44.vectors.json")));
    var v2 = doc.RootElement.GetProperty("v2"); var valid = v2.GetProperty("valid"); var invalid = v2.GetProperty("invalid");

    int n = 0, ok = 0;
    foreach (var t in valid.GetProperty("get_conversation_key").EnumerateArray())
    {
        n++; var k = new NostrKey(H(t.GetProperty("sec1").GetString()!));
        if (X(k.ConversationKey(H(t.GetProperty("pub2").GetString()!))) == t.GetProperty("conversation_key").GetString()) ok++;
    }
    Check($"get_conversation_key valid ({n})", ok == n, $"{ok}/{n}");

    n = ok = 0;
    var mk = valid.GetProperty("get_message_keys"); var ck = H(mk.GetProperty("conversation_key").GetString()!);
    foreach (var t in mk.GetProperty("keys").EnumerateArray())
    {
        n++; var (a, b, c) = Nip44.MessageKeys(ck, H(t.GetProperty("nonce").GetString()!));
        if (X(a) == t.GetProperty("chacha_key").GetString() && X(b) == t.GetProperty("chacha_nonce").GetString() && X(c) == t.GetProperty("hmac_key").GetString()) ok++;
    }
    Check($"get_message_keys ({n})", ok == n, $"{ok}/{n}");

    n = ok = 0;
    foreach (var t in valid.GetProperty("calc_padded_len").EnumerateArray()) { n++; if (Nip44.CalcPaddedLen(t[0].GetInt32()) == t[1].GetInt32()) ok++; }
    Check($"calc_padded_len ({n})", ok == n, $"{ok}/{n}");

    n = ok = 0; string firstFail = "";
    foreach (var t in valid.GetProperty("encrypt_decrypt").EnumerateArray())
    {
        n++;
        try
        {
            var s1 = new NostrKey(H(t.GetProperty("sec1").GetString()!)); var s2 = new NostrKey(H(t.GetProperty("sec2").GetString()!));
            var conv = H(t.GetProperty("conversation_key").GetString()!);
            bool good = X(s1.ConversationKey(s2.XOnlyPub)) == X(conv) && X(s2.ConversationKey(s1.XOnlyPub)) == X(conv); // symmetric both directions
            var payload = t.GetProperty("payload").GetString()!; var pt = t.GetProperty("plaintext").GetString()!;
            good &= Nip44.Encrypt(pt, conv, H(t.GetProperty("nonce").GetString()!)) == payload;
            good &= Nip44.Decrypt(payload, conv) == pt;
            if (good) ok++; else if (firstFail == "") firstFail = "vector " + n;
        }
        catch (Exception e) { if (firstFail == "") firstFail = e.Message; }
    }
    Check($"encrypt_decrypt: both-direction conv key, deterministic encrypt == payload, decrypt == plaintext ({n})", ok == n, $"{ok}/{n} {firstFail}");

    n = ok = 0;
    foreach (var t in valid.GetProperty("encrypt_decrypt_long_msg").EnumerateArray())
    {
        n++; var conv = H(t.GetProperty("conversation_key").GetString()!); var nonce = H(t.GetProperty("nonce").GetString()!);
        var pt = string.Concat(Enumerable.Repeat(t.GetProperty("pattern").GetString()!, t.GetProperty("repeat").GetInt32()));
        var payload = Nip44.Encrypt(pt, conv, nonce);
        if (X(SHA256.HashData(Encoding.UTF8.GetBytes(pt))) == t.GetProperty("plaintext_sha256").GetString()
            && X(SHA256.HashData(Encoding.UTF8.GetBytes(payload))) == t.GetProperty("payload_sha256").GetString()
            && Nip44.Decrypt(payload, conv) == pt) ok++;
    }
    Check($"encrypt_decrypt_long_msg ({n})", ok == n, $"{ok}/{n}");

    n = ok = 0;
    foreach (var t in invalid.GetProperty("decrypt").EnumerateArray())
    {
        n++; var conv = H(t.GetProperty("conversation_key").GetString()!);
        if (Throws(() => Nip44.Decrypt(t.GetProperty("payload").GetString()!, conv))) ok++;
        else Console.WriteLine("     accepted invalid: " + t.GetProperty("note").GetString());
    }
    Check($"invalid decrypt is rejected ({n})", ok == n, $"{ok}/{n}");

    n = ok = 0;
    foreach (var t in invalid.GetProperty("get_conversation_key").EnumerateArray())
    {
        n++;
        if (Throws(() => new NostrKey(H(t.GetProperty("sec1").GetString()!)).ConversationKey(H(t.GetProperty("pub2").GetString()!)))) ok++;
        else Console.WriteLine("     accepted invalid: " + t.GetProperty("note").GetString());
    }
    Check($"invalid get_conversation_key is rejected ({n})", ok == n, $"{ok}/{n}");

    var lens = invalid.GetProperty("encrypt_msg_lengths").EnumerateArray().Select(e => e.GetInt32()).ToArray();
    var ck2 = H("c41c775356fd92eadc63ff5a0dc1da211b268cbea22316767095b2871ea1412d");
    Check("empty plaintext rejected (0 bytes)", Throws(() => Nip44.Encrypt("", ck2)));
    bool oldRejects65536 = Throws(() => Nip44.Encrypt(new string('x', 65536), ck2));
    Console.WriteLine($"  [INFO] vectors file (2024-12) lists lengths {string.Join(",", lens)} as invalid; nostr-protocol/nips 44.md (2026-06-28, #1907) now allows >=65536 via 6-byte prefix. We follow current spec: 65536-byte message encrypt rejected={oldRejects65536}. Game payloads are <1 KB; irrelevant in practice.");
    var big = new string('x', 100000);
    Check("current-spec extended prefix (100000 bytes) round-trips", Nip44.Decrypt(Nip44.Encrypt(big, ck2), ck2) == big);
}

// ---------------------------------------------------------------------------------------------
Console.WriteLine("== Q2b: BIP-340 official vectors (bitcoin/bips test-vectors.csv): verify ==");
{
    int n = 0, ok = 0;
    foreach (var line in File.ReadAllLines(Path.Combine(vec, "bip340-test-vectors.csv")).Skip(1))
    {
        var f = line.Split(','); if (f.Length < 7) continue;
        if (f[4].Length != 64) { Console.WriteLine($"     skip index {f[0]}: message is {f[4].Length / 2} bytes (Nostr always signs the 32-byte event id; variable-length BIP-340 not needed)"); continue; }
        n++;
        bool expect = f[6] == "TRUE"; bool got;
        try { got = NostrKey.VerifyBip340(H(f[2]), H(f[4]), H(f[5])); } catch { got = false; }
        if (got == expect) ok++; else Console.WriteLine($"     mismatch index {f[0]} expect {expect} got {got} ({f[7]})");
    }
    Check($"BIP-340 verify vectors with 32-byte messages, incl. invalid ones ({n})", ok == n, $"{ok}/{n}");
    n = ok = 0;
    foreach (var line in File.ReadAllLines(Path.Combine(vec, "bip340-test-vectors.csv")).Skip(1))
    {
        var f = line.Split(','); if (f.Length < 7 || f[1].Length != 64) continue; n++;
        try { if (new NostrKey(H(f[1])).PubHex.ToUpperInvariant() == f[2]) ok++; } catch { }
    }
    Check($"secret -> x-only pubkey derivation matches vectors ({n})", ok == n, $"{ok}/{n}");
}

// ---------------------------------------------------------------------------------------------
Console.WriteLine("== Q2c: two independent peers, real random keys, sign / verify / NIP-44 round trip ==");
{
    var alice = NostrKey.Generate(); var bob = NostrKey.Generate();
    var ca = alice.ConversationKey(bob.XOnlyPub); var cb = bob.ConversationKey(alice.XOnlyPub);
    Check("conv(a,B) == conv(b,A)", ca.SequenceEqual(cb));
    var msg = "{\"g\":\"game1\",\"ptn\":\"a1>+\",\"prev\":\"00ff\"}";
    var payload = Nip44.Encrypt(msg, ca);                // Alice encrypts
    Check("Bob decrypts Alice's payload", Nip44.Decrypt(payload, cb) == msg);
    var reply = Nip44.Encrypt("ack", cb);
    Check("Alice decrypts Bob's reply", Nip44.Decrypt(reply, ca) == "ack");
    var eve = NostrKey.Generate();
    Check("Eve (third key) cannot decrypt", Throws(() => Nip44.Decrypt(payload, eve.ConversationKey(alice.XOnlyPub))));
    var flipped = Convert.FromBase64String(payload); flipped[40] ^= 1;
    Check("tampered ciphertext rejected by MAC", Throws(() => Nip44.Decrypt(Convert.ToBase64String(flipped), cb)));

    var ev = new NostrEventLite { Kind = 4, CreatedAt = 1_700_000_000, Content = payload, Tags = { new() { "p", bob.PubHex }, new() { "g", "game1" } } };
    ev.Sign(alice);
    Check("signed event verifies (id recomputed + BIP-340)", ev.Verify());
    var t2 = new NostrEventLite { Id = ev.Id, Pubkey = ev.Pubkey, CreatedAt = ev.CreatedAt, Kind = ev.Kind, Tags = ev.Tags, Content = ev.Content + "x", Sig = ev.Sig };
    Check("tampered content rejected", !t2.Verify());
    var t3 = new NostrEventLite { Id = ev.Id, Pubkey = bob.PubHex, CreatedAt = ev.CreatedAt, Kind = ev.Kind, Tags = ev.Tags, Content = ev.Content, Sig = ev.Sig };
    Check("wrong pubkey rejected", !t3.Verify());

    // Events for the independent Python verifier (bip340_reference.py + hashlib/json.dumps NIP-01 serialisation)
    var contents = new[] { "hello", "a1>+ b2< 3c3 \"q\" \\ / \n\t", "unicode é表 \u0001 \u007f", "emoji \U0001F984\U0001F355", "ctl \b \f \r end" };
    var lines = new List<string>();
    foreach (var c in contents)
    {
        var e = new NostrEventLite { Kind = 1, CreatedAt = 1_700_000_001, Content = c, Tags = { new() { "t", "tak", "é" } } };
        e.Sign(alice); lines.Add(e.ToJson());
    }
    Directory.CreateDirectory(Path.Combine(vec, "..", "out"));
    File.WriteAllLines(Path.Combine(vec, "..", "out", "events.jsonl"), lines);
    Console.WriteLine($"  wrote {lines.Count} signed events to out/events.jsonl for the independent Python verifier");
}

Console.WriteLine("== Q2d: events signed by the Python BIP-340 reference implementation verified by C# ==");
{
    var pyFile = Path.Combine(vec, "..", "out", "py-events.jsonl");
    if (!File.Exists(pyFile)) Console.WriteLine("  (run verify_events.py first to create out/py-events.jsonl)");
    else foreach (var line in File.ReadAllLines(pyFile).Where(l => l.Length > 0))
    {
        using var d = JsonDocument.Parse(line); var r = d.RootElement;
        var e = new NostrEventLite { Id = r.GetProperty("id").GetString()!, Pubkey = r.GetProperty("pubkey").GetString()!, CreatedAt = r.GetProperty("created_at").GetInt64(),
            Kind = r.GetProperty("kind").GetInt32(), Content = r.GetProperty("content").GetString()!, Sig = r.GetProperty("sig").GetString()! };
        foreach (var t in r.GetProperty("tags").EnumerateArray()) e.Tags.Add(t.EnumerateArray().Select(x => x.GetString()!).ToList());
        Check($"Python-signed event verifies in C# (content {e.Content.Replace("\n", "\\n")[..Math.Min(12, e.Content.Length)]}...)", e.Verify());
    }
}

Console.WriteLine($"\nSUMMARY: {pass} pass, {fail} fail");
return fail == 0 ? 0 : 1;

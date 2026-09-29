# Spike R-002 + R-003: secp256k1 identity, BIP-340 signing, NIP-44 v2

Date: 2026-09-30 · Track: deep · Time box: one session · Author: spike-runner

## Questions
1. Is R-002 real? Does a two-peer NIP-44 round trip fail against the CURRENT `src` code?
2. Which .NET option gives correct BIP-340 sign/verify, NIP-01 event id and NIP-44 v2 (official vectors)?
3. Does it run in Blazor WASM (net10.0 browser-wasm)?

## Pass / fail criterion (given)
Pass: two independent peers round-trip NIP-44 v2, official vectors pass, BIP-340 signatures verify, all on net10.0, plus a WASM result.
Fail: no option passes the vectors, or none runs on WASM.

## Verdicts
| Risk | Verdict | One line |
|---|---|---|
| R-002 (shared secret is not ECDH) | **proven** (defect confirmed, seen running) | Bob cannot decrypt Alice; fix path proven |
| R-003 (events unsigned, Ed25519 identity) | **proven** (defect confirmed, seen in code + running) | plus a new related defect: `ComputeId` produces wrong ids; fix path proven |
| Fix path (D-011 / D-004) | **proven** | NBitcoin.Secp256k1 4.0.1 + ~150 lines of NIP-44 glue passes every vector on net10.0 and in browser-wasm |

Overall spike outcome: **PASS**. No feasibility blocker on the crypto layer. Not covered: publishing a signed event to a real relay
(see "Not done").

## Evidence

Everything is in this folder. Reproduce:
```
cd pipeline/spikes/R-002-R-003-secp256k1-crypto/Runner && dotnet run        # writes ../out/events.jsonl
cd .. && PYTHONIOENCODING=utf8 python verify_events.py                      # independent verifier, writes ../out/py-events.jsonl
cd Runner && dotnet run                                                     # now also verifies the Python-signed events
```
Full runner output: `out/runner-output.txt` (29 checks, 0 failed).

### Q1. R-002 confirmed against the current source (seen running)
`Runner/Spike.Runner.csproj` compiles the unmodified `src/.../Nip44Encryption.cs`, `NostrModels.cs` and `CryptoSigner.cs` in place (linked, read-only).
Alice and Bob are generated with the app's own `CryptoSigner.GenerateKeyPair()`, and each side derives its secret exactly as
`NostrTransportClient` lines 103 (send) and 215 (receive) do:
```
Alice and Bob derive DIFFERENT shared secrets   alice=005e65e723b1315e.. bob=7486a41259f3b6fe..
Bob CANNOT decrypt Alice's message              InvalidCipherTextException: mac check in ChaCha20Poly1305 failed
(control) Alice decrypting her own message passes -> existing tests are blind to R-002
current payload is not NIP-44 v2 layout         len=36 bytes, spec minimum is 99 bytes / 132 base64 chars
```
Why the existing tests pass (seen in code): `Nip44EncryptionTests.cs:20-21` derives BOTH secrets with `(alicePriv, bobPub)`, and uses random bytes as
"keys". `TransportBenchmarkTests` decrypts with the sender's own keys. Neither ever has the receiver use his own private key.
The ciphertext format is also non-standard (12-byte nonce, ChaCha20-Poly1305 AEAD instead of 32-byte nonce, plain ChaCha20 and HMAC-SHA256), so even a correct ECDH would not
interoperate with any other NIP-44 client.

### Q1b. R-003 confirmed
- `NostrEvent.Sig` is declared once (`NostrModels.cs:31`) and never assigned anywhere in `src/` or `tests/` (`grep -rnw Sig` returns one hit). Runner check: `Sig == ""`. Relays verify `sig` (NIP-01), so these events would be rejected. (The relay's OK/NOTICE reply was not observed; see "Not done".)
- Identity is Ed25519 (`CryptoSigner`): the same 32-byte secret gives a different secp256k1 pubkey, so the app's published "npub" is not a Nostr identity.
- **New defect found (R-003 companion):** `NostrEvent.ComputeId()` uses `Utf8JsonWriter` with the default encoder, which escapes `+`, `<`, `>`, `&` and every non-ASCII char as `\uXXXX`.
  NIP-44 payloads are base64; a `+` appears in almost every payload of realistic length (about 1 char in 64), so the id computed by the app differs from the id a relay computes.
  Runner: `current NostrEvent.ComputeId differs from NIP-01 id for base64 content containing '+'  current=0ad740fb6c0a.. nip01=0ec505b17b9a..`.
  Even after adding a signature, such events would fail id verification at the relay.

### Q2. Library choice (seen running)
Chosen: **NBitcoin.Secp256k1 4.0.1** (MIT, managed, netstandard2.1/net8.0, no native code) for curve, BIP-340 and ECDH, plus BouncyCastle 2.7.0 (already a dependency) for
ChaCha20 (`ChaCha7539Engine`), and `System.Security.Cryptography` for HMAC-SHA256. NIP-44 v2 glue is hand-written in `Crypto/NostrCrypto.cs` (about 150 lines total:
conversation key, HKDF, padding incl. the 2026-06 extended prefix, MAC with AAD, base64) and validated against the official vectors, not trusted by inspection.

| Check | Source | Result |
|---|---|---|
| NIP-44 `get_conversation_key` valid | paulmillr/nip44 `nip44.vectors.json` | 35/35 |
| `get_message_keys` | same | 32/32 |
| `calc_padded_len` | same | 24/24 |
| `encrypt_decrypt` (both key directions, deterministic nonce -> exact payload, decrypt) | same | 10/10 |
| `encrypt_decrypt_long_msg` (65535-char messages) | same | 3/3 |
| `invalid decrypt` (bad version, bad base64, bad MAC, bad padding, bad length) | same | 12/12 rejected |
| `invalid get_conversation_key` (sec = 0, sec >= n, non-curve x, twist points) | same | 8/8 rejected |
| BIP-340 verify vectors (32-byte messages, includes the invalid ones) | bitcoin/bips `test-vectors.csv` | 15/15 |
| secret -> x-only pubkey | same | 8/8 |
| Two random independent peers: conv(a,B) == conv(b,A); Bob decrypts Alice; Alice decrypts Bob; third key fails; tampered MAC fails | Runner | all pass |
| C#-signed events (5, incl. `+ < > "` newline, `\u0001`, DEL, emoji, `\b \f`): id and BIP-340 sig verified by the **Python BIP-340 reference implementation + hashlib/json** | `verify_events.py` | 5/5 |
| Python-signed events (3) verified by C# | Runner Q2d | 3/3 |

Sources fetched this session (documented): NIP-44 spec `nostr-protocol/nips/44.md` (last changed 2026-06-28, #1907), NIP-01, vectors repo `paulmillr/nip44`
(last commit 2024-12), BIP-340 `test-vectors.csv` and `reference.py`; copies under `vectors/`.

Alternative checked: **NNostr.Client 0.0.55** (published 2026-08-18, depends on NBitcoin.Secp256k1 3.1.6, LibChaCha20, LinqKit, System.Interactive.Async; includes a relay client).
Scratch test (not kept in the repo): its `NIP44.Encrypt/Decrypt` passes the `encrypt_decrypt` vectors 10/10 in both directions, its `ComputeId` matches Python for
content with emoji, `+`, `<`, and `ComputeIdAndSignAsync(...).Verify()` is true. So it also works and is a valid fallback if we want a ready relay client, at the price
of a heavier dependency and a project (Kukks/NNostr) with a slower release cadence than NBitcoin. Not tested in WASM.
BouncyCastle alone was not tried: it has the curve and ECDH but no BIP-340, so it would need hand-written Schnorr, which we should not do.

### Q3. Blazor WASM (seen running in a browser)
`Wasm/` is a minimal Blazor WASM 10.0.7 project referencing `Crypto/`; the home page runs the checks on load and prints them into `#result`.
Run in the built-in browser pane, two ways, both `runtime=browser-wasm os=Browser browser=True`:
- Debug, `dotnet run` dev server: `DONE pass=10 fail=0`
- Release `dotnet publish` (IL-trimmed, interpreter, no wasm-tools/AOT), static server: `DONE pass=10 fail=0`
Covered in the browser: SHA256/HMAC/CSPRNG available; keygen; two-peer conv-key equality; NIP-44 round trip; event sign + verify + tamper reject;
NIP-44 vectors (35 + 10 + 12) and BIP-340 vectors (15) fetched over HTTP.
Timings (interpreter, Release): first key generation about 440 ms (includes secp256k1 context/table init; 1.5 s in Debug), ECDH+HKDF for two sides 21 ms, sign 16 ms,
verify 13 ms, NIP-44 encrypt+decrypt 19 ms. Comfortable for one move per turn. Files: `out/wasm-result.txt`, `out/wasm-debug-screenshot.jpg`.

## What the real build should copy
1. Add `NBitcoin.Secp256k1` to `TakEngine.Transport` (or Core, wherever identity lives). Keep BouncyCastle for ChaCha20 (`ChaCha7539Engine`).
2. Copy `Crypto/NostrCrypto.cs` deliberately (spike code is not imported). Split it into: `NostrKey` (identity, BIP-340, conversation key), `Nip44` (pure functions), `NostrEvent.Serialize/ComputeId/Sign/Verify`.
   Replace `Nip44Encryption`, `CryptoSigner`, and `NostrEvent.ComputeId`.
3. Use the spike's `Serialize()` (hand-written NIP-01 string escaper) for the event id; do NOT use `System.Text.Json` for it, even with `UnsafeRelaxedJsonEscaping`
   (AGENTS.md invariant 4 is insufficient: with relaxed escaping the first version of the spike still produced ids that differed from the Python/NIP-01 id for content containing DEL (0x7f) and for content containing an emoji; the current code does not even set the encoder in `ComputeId`).
   Wire JSON can still use STJ since relays re-parse and recompute the id.
4. Tests to write first (they must be two-independent-peer tests): conv(a,B) == conv(b,A); Bob decrypts Alice; official NIP-44 vectors (add `nip44.vectors.json` as a test resource);
   BIP-340 vectors; id of an event with `+`, `<`, emoji content equals a fixed known-good hash (the Python-computed ids in `out/events.jsonl`).
   Delete `Nip44EncryptionTests.Nip44_EncryptAndDecrypt_RoundTripsSuccessfully`, which passes for the wrong reason.
5. Always verify `id` and `sig` on every received event BEFORE decrypting (NIP-44 spec requires it). Reject any pubkey not 32 bytes / not on curve.
6. Alias `System.Security.Cryptography.SHA256`/`HMACSHA256` when both namespaces are imported: NBitcoin.Secp256k1 defines same-named types (compile error CS0104).
   `HMACSHA256.HashData(key, [.. a, .. b])` also hits an overload ambiguity; cast to `byte[]`.
7. Prefer `GetSharedPubkey` (raw point) over any "ECDH" helper that hashes the output; NIP-44 needs the unhashed x coordinate (the vectors catch this).
8. Key storage: the secret is now a 32-byte secp256k1 scalar; npub/nsec bech32 encoding (NIP-19) is still to be written (not part of this spike). Existing Ed25519 identities cannot be migrated (different curve); D-011 already retires them.

## Surprises
- The vectors repo (2024-12) is behind the NIP text (2026-06-28): the spec now allows plaintext >= 65536 bytes with a 6-byte length prefix, while the old vectors still list 65536, 100000
  and 10000000 as invalid lengths. We follow the current spec; game payloads are under 1 KB so it does not matter, but a fixed-cap of, say, 64 KB in our wrapper is a cheap safe choice.
- The 4 BIP-340 vectors with non-32-byte messages (added 2022) do not apply; NBitcoin's API takes 32-byte messages, which is all Nostr signs.
- Both the app's own `ComputeId` defect (above) and the test blind spot were invisible until compared with an independent implementation. Python reference verification found the
  STJ escaping mismatch (DEL, emoji), which the C#-only tests did not.
- `dotnet publish` for Blazor without the `wasm-tools` workload works fine for this (no AOT needed); the trimmed build did not break NBitcoin.Secp256k1 or BouncyCastle.

## Not done / remaining
- A C#-signed event was not published to a real relay (R-003's original check). That posts publicly under a throwaway key and was not authorised here; the parallel R-001/R-004 relay spike
  is the right place. Independent verification here is by the BIP-340 reference implementation and NNostr.Client, not by a relay.
- Constant-time behaviour of the hand-written MAC compare uses `CryptographicOperations.FixedTimeEquals` (good); no other side-channel review was done (security lens, stage 8).
- NNostr.Client was not run in WASM.
- Android/iOS not exercised (managed code, expected to work; unverified).

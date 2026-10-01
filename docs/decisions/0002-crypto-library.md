# 0002 · NBitcoin.Secp256k1 plus hand-written NIP-44 v2 glue
Status: accepted 2026-09-30 (spike, D-015; library allowed by D-004). Recorded 2026-10-02.

## Context
The hand-rolled `Nip44Encryption` derived `SHA256(myPriv || theirPub)`, so two peers derived different keys, and its
round-trip test passed only because it decrypted with the sender's own keys (R-002, seen running).

## Decision
`TakEngine.Crypto` uses NBitcoin.Secp256k1 (MIT, managed, no native code) for keys, BIP-340 and the ECDH point,
BouncyCastle `ChaCha7539Engine` for ChaCha20, and BCL HMAC-SHA256/HKDF. About 150 lines of NIP-44 v2 glue are written
by hand and trusted only because the official vectors pass (spike: NIP-44 35+32+24+10+3 valid, 12+8 invalid; BIP-340 15/15;
in net10.0 and in a trimmed browser-wasm build).

## Alternatives
- NNostr.Client 0.0.55: also passes the vectors and brings a relay client, but is heavier (LinqKit, System.Interactive.Async,
  LibChaCha20), has a slower release cadence and was not tried in WASM. Kept as the fallback.
- BouncyCastle alone: no BIP-340, would need hand-written Schnorr. Rejected.

## Consequences
- Vectors are test resources; a library bump is safe only with the full vector suite green (4.0.1 proven; 4.0.3 latest on NuGet 2026-10-02).
- Use `GetSharedPubkey` (raw x coordinate), never a hashing "ECDH" helper. Alias `SHA256`/`HMACSHA256` (NBitcoin defines
  same-named types, CS0104). Constant-time MAC compare via `CryptographicOperations.FixedTimeEquals`.
- Cap plaintext at 64 KiB: the vectors repo (2024-12) predates the NIP's 2026-06 extended length prefix.

## Revisit if
A vector fails after a bump, or the security review finds a side channel in the glue.

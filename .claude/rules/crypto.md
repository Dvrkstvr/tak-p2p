---
paths:
  - "src/TakEngine.Crypto/**"
  - "tests/TakEngine.Crypto.Tests/**"
  - "src/TakEngine.Core/Cryptography/**"
---
# Crypto (keys, BIP-340, NIP-44 v2, NIP-19)
- Pure module: no I/O, no clock, no RNG inside. Nonces, BIP-340 aux randomness and new secrets come in as arguments; the shell
  calls `RandomNumberGenerator`. That is what lets the official vectors pin exact outputs.
- Every behaviour is proven by official vectors (test resources: `nip44.vectors.json` from paulmillr/nip44, BIP-340
  `test-vectors.csv`) AND by two independent keypairs. Never test by decrypting with the sender's own keys: that is how the old
  `Nip44Encryption` passed while being wrong (docs/decisions/0002).
- NIP-44 conversation key = HKDF-extract over the unhashed shared x coordinate (`GetSharedPubkey`), never a hashing ECDH helper.
- Alias `SHA256`/`HMACSHA256` from System.Security.Cryptography (NBitcoin.Secp256k1 defines same-named types: CS0104).
- MAC compare with `CryptographicOperations.FixedTimeEquals`. Plaintext capped at 64 KiB.
- Reject pubkeys that are not 32 bytes or not on the curve, and secrets that are 0 or >= n, at the boundary (`PublicKey`/`SecretKey`).
- Spike reference implementation (copy deliberately, do not import): `pipeline/spikes/R-002-R-003-secp256k1-crypto/Crypto/NostrCrypto.cs`.
- A library version bump is allowed only with the full vector suite green.

# 0001 · One secp256k1 key per player for everything
Status: accepted 2026-09-30 (user, D-011). Recorded 2026-10-02.

## Context
The adopted code used Ed25519 identities (`CryptoSigner`) while Nostr needs secp256k1: BIP-340 Schnorr for event
signatures and secp256k1 ECDH for NIP-44. The npub shown to users was an Ed25519 key, so it was not a Nostr identity,
and no event was ever signed (spike R-002/R-003, seen running).

## Decision
Each player has exactly one secp256k1 secret key. It is the npub identity, signs Nostr events (BIP-340), derives NIP-44
v2 conversation keys, and signs game actions (the inner envelope signature). `CryptoSigner` is deleted at F-031.

## Alternatives
- Ed25519 for moves + secp256k1 for Nostr: two keys per user, two backups, more code; no requirement needs Ed25519.
- Drop Nostr compatibility and run an own protocol: contradicts "public relays, zero servers".

## Consequences
- One key to back up (M3, Q-017). Losing it loses every game in progress; leaking it lets someone play as the user (R-009).
- Existing Ed25519 data is not migrated (different curve): see 0010.
- Pubkeys everywhere are 32-byte x-only hex; inputs not 32 bytes or not on the curve are rejected.

## Revisit if
A browser path for secp256k1 fails (it was proven in browser-wasm by the spike, so unlikely).

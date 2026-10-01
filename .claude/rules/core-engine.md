---
paths:
  - "src/TakEngine.Core/**"
  - "src/TakEngine.Abstractions/**"
  - "tests/TakEngine.Core.Tests/**"
---
# Core engine and abstractions
- Rules, PTN/TPS, hash chain and `TakGameSession` are pure: no I/O, no clock, no RNG. Signing takes the `SecretKey` and aux
  randomness as inputs.
- Hash chain: `StateHash = SHA-256(prev || turn || pubkey || ptn || tps)` via `StateHasher`. A golden vector pins it; changing any
  input or its encoding breaks every stored game and peer.
- `ActionDigest` (what a player signs) layout is in pipeline/architecture.md; a golden vector pins it too.
- A remote action that fails any check returns a failure, raises `OnProtocolViolationDetected`, and leaves board and hash unchanged.
- Abstractions holds records and seam interfaces only (`IKeyStore`, `IGameStore`); no logic.
- Core must stay browser-wasm-safe. `SqliteGameStorage` moves to `TakEngine.Storage.Local` (architecture.md); don't add new SQLite
  or file-system code to Core.
- Adopted tests for rules/roads/PTN/TPS (F-001..F-003) stay green unchanged; M0 does not touch rules.

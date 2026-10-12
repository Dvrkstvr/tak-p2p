# 0009 · Module layout and pure modules
Status: assumed 2026-10-02 (architect, D-026). The user can overrule.

## Context
Core signs moves and Transport signs events and encrypts, so both need the secp256k1 key; Transport does not reference Core today.
The turn-exchange coordinator needs the session (Core) and the relay client (Transport) and must not live in a head's Program.cs.
SQLite in Core leaks a native dependency into the browser build.

## Decision
- New `TakEngine.Crypto` (keys, BIP-340, NIP-44, NIP-19), referenced by Core and Transport.
- New `TakEngine.Multiplayer` (handshakes, TurnExchange, tamper classifier, link status, GameCoordinator, MultiplayerGame facade),
  referencing Core and Transport; every head uses it.
- New `TakEngine.Storage.Local` (SQLite store, key file, the moved `SqliteGameStorage`), never referenced by the browser.
- New `tests/TakEngine.Testing` (fakes) and `tools/TakRelay.Local`, `tools/TakTestPeer`.
- "Pure" means deterministic with no I/O, no clock and no RNG; in-memory state machines qualify. Time, nonces and seeds are inputs.
- Clock seam = BCL `TimeProvider`, faked with `FakeTimeProvider`; no custom clock interface.

## Alternatives
- Crypto inside Core with Transport referencing Core: couples the wire layer to the rules engine and to SQLite.
- Coordinator inside Transport: Transport would need Core; same coupling.
- Folders instead of projects: the browser would still pull SQLite, and layering would rest on discipline instead of references.

## Consequences
Five more projects to keep in sln/slnx/slnf. Each has one job and its own test project.

## Revisit if
A project stays under ~200 lines after M3; merge it then.

# System Overview & Core Philosophy

This project is a decentralized, peer-to-peer (P2P), zero-server implementation of the abstract strategy game **Tak**, supporting both synchronous (live) and asynchronous play across Linux, Android, Windows, and iOS.

---

## Architectural Invariants

* **Zero Authoritative Game Servers:** The network layer functions strictly as an encrypted "dumb pipe" / store-and-forward mailbox. Clients never trust remote states; all moves and state transitions are verified deterministically on the local device.
* **Separation of Concerns:**
  * `TakEngine.Abstractions`: Shared contracts, immutable records, data structures (publicly distributed).
  * `TakEngine.Core`: Private game logic, DFS graph road traversal, cryptographic hashing, invariant checks, state storage.
  * `TakEngine.Transport`: Nostr WebSocket relay interface, NIP-44 encryption, envelope serialization.
  * Frontends (`TakApp.Avalonia`, `TakApp.Cli`): Pure UI views consuming reactive observables/events.
* **Deterministic Rule Adjudication:** Illegal moves are mathematically impossible to force onto a peer. If an opponent injects an invalid payload, the receiving client drops the payload and flags the peer.

---

## Component Boundaries

```
┌─────────────────────────────────────────────────────────────┐
│                       Frontends                             │
│       TakApp.Cli (Spectre)    │    TakApp.Avalonia (GUI)    │
└──────────────────────────────┬──────────────────────────────┘
                               │ Consumes ITakGameSession & Snapshots
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                 TakEngine.Abstractions                      │
│      Models, Enums, Interfaces, ITakGameSession             │
└──────────────────────────────▲──────────────────────────────┘
                               │ Implements
┌──────────────────────────────┴──────────────────────────────┐
│                    TakEngine.Core                           │
│  Board, DFS Rules, PTN/TPS, Crypto, SQLite Storage, Session │
└──────────────────────────────┬──────────────────────────────┘
                               │ Envelopes / Events
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                  TakEngine.Transport                        │
│    Nostr WebSocket Client, NIP-44 Encryption, Matchmaking   │
└─────────────────────────────────────────────────────────────┘
```

---

## Immediate Development Action Items

1. Initialize `TakEngine.Abstractions` with `Coord`, `TakMove`, `StackSnapshot`, and `ITakGameSession`.
2. Implement `TakEngine.Core.Board` and write the unit tests for orthogonal DFS road verification.
3. Benchmark Nostr WebSocket latency (`TakEngine.Transport.Tests`) across public relays to lock in baseline sync performance.

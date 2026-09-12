# System Overview & Core Philosophy

This project is a decentralized, peer-to-peer (P2P), zero-server implementation of the abstract strategy game **Tak**, supporting both synchronous (live) and asynchronous play across Linux, Android, Windows, macOS, and in-browser via WebAssembly.

---

## Architectural Invariants

* **Zero Authoritative Game Servers:** The network layer functions strictly as an encrypted "dumb pipe" / store-and-forward mailbox. Clients never trust remote states; all moves and state transitions are verified deterministically on the local device.
* **Separation of Concerns:**
  * `TakEngine.Abstractions`: Shared contracts, immutable records, data structures, and spectator interfaces (publicly distributed).
  * `TakEngine.Core`: Private game logic, DFS graph road traversal, cryptographic hashing, invariant checks, state storage, and spectator queue.
  * `TakEngine.Transport`: Nostr WebSocket relay interface, NIP-44 encryption, envelope serialization, and matchmaking handshakes.
  * Frontends (`TakApp.Avalonia`, `TakApp.Cli`, `TakApp.Blazor`): Pure UI views consuming reactive observables/events.
* **Deterministic Rule Adjudication:** Illegal moves are mathematically impossible to force onto a peer. If an opponent injects an invalid payload, the receiving client drops the payload and flags the peer.

---

## Component Boundaries

```
┌────────────────────────────────────────────────────────────────────────┐
│                               Frontends                                │
│   TakApp.Cli (Spectre)  │  TakApp.Avalonia (GUI)  │  TakApp.Blazor     │
└────────────────────────────┬───────────────────────────────────────────┘
                             │ Consumes ITakGameSession & Snapshots
                             ▼
┌────────────────────────────────────────────────────────────────────────┐
│                        TakEngine.Abstractions                          │
│   Models, Enums, Interfaces, ITakGameSession, ISpectatorGameSession    │
└────────────────────────────▲───────────────────────────────────────────┘
                             │ Implements
┌────────────────────────────┴───────────────────────────────────────────┐
│                           TakEngine.Core                               │
│ Board, DFS Rules, PTN/TPS, Crypto, SQLite Storage, Replay, Spectator, NTP
└────────────────────────────┬───────────────────────────────────────────┘
                             │ Envelopes / Events
                             ▼
┌────────────────────────────────────────────────────────────────────────┐
│                        TakEngine.Transport                             │
│   Nostr WebSocket Client, NIP-44 Encryption, Matchmaking, Broadcasts   │
└────────────────────────────────────────────────────────────────────────┘
```

---

## Completed Foundations

1. `TakEngine.Abstractions`: Defined all records, enums, `ITakGameSession`, and `ISpectatorGameSession`.
2. `TakEngine.Core`: Fully implemented orthogonal DFS road detection, Turn 1 swap rule, carry limits, wall flattening, PTN/TPS serializers, SHA-256 state hashing, Ed25519 signing, SQLite database persistence, instant replay scrubbing, and RFC 5905 NTP time sync.
3. `TakEngine.Transport`: Full NIP-01 message protocol, NIP-44 ChaCha20-Poly1305 encryption, multi-relay WebSocket coordination, direct invite codes (URI and compact QR token), and ephemeral `kind: 20001` quick-play matchmaking.
4. `TakApp.Cli`: Fully functional Spectre.Console terminal client with ANSI board rendering, conversational typed command input, and stack inspector.
5. `TakApp.Blazor`: WebAssembly client structure and automated GitHub Pages CI/CD workflow.

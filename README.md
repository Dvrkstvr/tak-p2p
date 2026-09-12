# Tak P2P: Decentralized Peer-to-Peer Tak

> A decentralized, peer-to-peer (P2P), zero-server implementation of the abstract strategy game **Tak**, supporting both synchronous (live) and asynchronous play across Linux, Android, Windows, and iOS.

---

## System Overview & Core Philosophy

### Architectural Invariants

* **Zero Authoritative Game Servers:** The network layer functions strictly as an encrypted "dumb pipe" / store-and-forward mailbox. Clients never trust remote states; all moves and state transitions are verified deterministically on the local device.
* **Separation of Concerns:**
  * `TakEngine.Abstractions`: Shared contracts, immutable records, data structures (publicly distributed).
  * `TakEngine.Core`: Private game logic, DFS graph road traversal, cryptographic hashing, invariant checks, state storage.
  * `TakEngine.Transport`: Nostr WebSocket relay interface, NIP-44 encryption, envelope serialization.
  * Frontends (`TakApp.Avalonia`, `TakApp.Cli`): Pure UI views consuming reactive observables/events.
* **Deterministic Rule Adjudication:** Illegal moves are mathematically impossible to force onto a peer. If an opponent injects an invalid payload, the receiving client drops the payload and flags the peer.

---

## Repository & Solution Layout

```
TakGame.sln
├── src/
│   ├── TakEngine.Abstractions/       # [Public NuGet candidate]
│   │   ├── Enums/                    # PieceType, PlayerColor, Direction, GamePhase
│   │   ├── Models/                   # Coord, StackSnapshot, BoardSnapshot, TakMove
│   │   └── ITakGameSession.cs        # Primary interface consumed by all frontends
│   │
│   ├── TakEngine.Core/               # [Private Implementation]
│   │   ├── Board/                    # Grid, Stacks, Piece Inventories, Move Execution
│   │   ├── Rules/                    # Invariant rules, Carry limits, DFS Road finder
│   │   ├── Serialization/            # PTN (Portable Tak Notation) & TPS (Tak Positional System)
│   │   ├── Cryptography/             # Keypairs, Signatures, SHA-256 State Hashing
│   │   ├── Storage/                  # SQLite database engine, Match logs, Replay provider
│   │   └── Session/                  # TakGameSession implementation, NTP time tracker
│   │
│   ├── TakEngine.Transport/          # [Private / Infrastructure]
│   │   ├── Nostr/                    # WebSocket client, NIP-01/NIP-44 wrappers
│   │   ├── Matchmaking/              # Invite code parser, Ephemeral broadcast handler
│   │   └── TransportEnvelope.cs      # Signed wire models
│   │
│   ├── TakApp.Cli/                   # [Runnable Console App]
│   │   ├── Program.cs                # Entry point, Interactive menus
│   │   ├── Rendering/                # Spectre.Console ANSI board, stack layer inspector
│   │   └── Input/                    # PTN CLI command parser
│   │
│   └── TakApp.Avalonia/              # [Runnable Cross-Platform GUI]
│       ├── ViewModels/               # MVVM ViewModels (CommunityToolkit.Mvvm)
│       ├── Views/                    # Canvas/Skia board renderer, Match controls
│       └── Services/                 # Local OS notification scheduler
│
└── tests/
    ├── TakEngine.Core.Tests/         # Rule engine unit tests, DFS validation, PTN parser tests
    └── TakEngine.Transport.Tests/    # Relay serialization, Round-trip latency tests
```

---

## Documentation Index

Detailed specifications are organized in the following documents:

1. [Full Specification & Handoff Document](docs/PROJECT_SPECIFICATION.md) - Complete consolidated specification.
2. [System Overview](docs/system-overview.md) - High-level architecture, principles, and invariants.
3. [Version 1.0 (MVP) Specification](docs/v1-mvp.md) - Deliverables, layout, Nostr transport, SQLite schema, `ITakGameSession` API, and milestones.
4. [Version 2.0 (Competitive & Tournaments) Specification](docs/v2-tournaments.md) - Co-signed receipts, Swiss tournaments, Elo oracle, anti-cheat, and v2 database schema.
5. [Wire Protocol Specification](docs/wire-protocol.md) - Nostr envelopes, NIP-44 encryption, and matchmaking handshakes.
6. [Database Schema Specification](docs/database-schema.md) - Complete SQLite schema for v1 and v2 migrations.
7. [Blazor WebAssembly & GitHub Pages Specification](docs/blazor-web-github-pages.md) - Design and zero-cost deployment architecture for the browser client.

---

## Immediate Development Action Items

1. Initialize `TakEngine.Abstractions` with `Coord`, `TakMove`, `StackSnapshot`, and `ITakGameSession`.
2. Implement `TakEngine.Core.Board` and write the unit tests for orthogonal DFS road verification.
3. Benchmark Nostr WebSocket latency (`TakEngine.Transport.Tests`) across public relays to lock in baseline sync performance.

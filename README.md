# Tak P2P: Decentralized Peer-to-Peer Tak

> A decentralized, peer-to-peer (P2P), zero-server implementation of the abstract strategy game **Tak**, supporting both synchronous (live) and asynchronous play across Linux, Android, Windows, macOS, and WebAssembly in modern browsers.

---

## System Overview & Core Philosophy

### Architectural Invariants

* **Zero Authoritative Game Servers:** The network layer functions strictly as an encrypted "dumb pipe" / store-and-forward mailbox. Clients never trust remote states; all moves and state transitions are verified deterministically on the local device.
* **Separation of Concerns:**
  * `TakEngine.Abstractions`: Shared contracts, immutable records, data structures (publicly distributed).
  * `TakEngine.Core`: Private game logic, DFS graph road traversal, cryptographic hashing, invariant checks, state storage.
  * `TakEngine.Transport`: Nostr WebSocket relay interface, NIP-44 encryption, envelope serialization.
  * Frontends (`TakApp.Avalonia`, `TakApp.Cli`, `TakApp.Blazor`): Pure UI views consuming reactive observables/events.
* **Deterministic Rule Adjudication:** Illegal moves are mathematically impossible to force onto a peer. If an opponent injects an invalid payload, the receiving client drops the payload and flags the peer.

---

## Repository & Solution Layout

```
TakGame.sln / TakGame.slnx
├── src/
│   ├── TakEngine.Abstractions/       # [Public NuGet candidate]
│   │   ├── Enums/                    # PieceType, PlayerColor, Direction, GamePhase
│   │   ├── Models/                   # Coord, StackSnapshot, BoardSnapshot, TakMove, BroadcastModels
│   │   ├── ITakGameSession.cs        # Primary interface consumed by all frontends
│   │   └── ISpectatorGameSession.cs  # Spectator/broadcast observable interface
│   │
│   ├── TakEngine.Core/               # [Engine & Rules Core]
│   │   ├── Board/                    # Grid, Stacks, Piece Inventories, Move Execution
│   │   ├── Rules/                    # Invariant rules, Carry limits, DFS Road finder, MoveValidator
│   │   ├── Serialization/            # PTN (Portable Tak Notation) & TPS (Tak Positional System)
│   │   ├── Cryptography/             # Keypairs, Signatures, SHA-256 State Hashing
│   │   ├── Storage/                  # SQLite database engine, Match logs, Replay provider
│   │   └── Session/                  # TakGameSession, DelayedBroadcastQueue, SpectatorSession, NTP
│   │
│   ├── TakEngine.Transport/          # [Nostr P2P Infrastructure]
│   │   ├── Nostr/                    # WebSocket client, NIP-01/NIP-44 wrappers
│   │   ├── Matchmaking/              # Invite code parser, Ephemeral broadcast handler
│   │   └── TransportEnvelope.cs      # Signed wire models
│   │
│   ├── TakApp.Cli/                   # [Runnable Console App]
│   │   ├── Program.cs                # Entry point, Interactive menus
│   │   ├── Rendering/                # Spectre.Console ANSI board, stack layer inspector
│   │   └── Input/                    # Conversational stepped typed input & PTN command parser
│   │
│   ├── TakApp.Avalonia/              # [Runnable Cross-Platform Desktop/Mobile GUI]
│   │   ├── ViewModels/               # MVVM ViewModels (CommunityToolkit.Mvvm)
│   │   ├── Views/                    # Canvas/Skia board renderer, Match controls
│   │   └── Services/                 # Local OS notification scheduler
│   │
│   └── TakApp.Blazor/                # [Runnable Zero-Install WebAssembly Client]
│       ├── Pages/                    # Interactive web board & match lobbies
│       ├── wwwroot/                  # Static assets & GitHub Pages deployment
│       └── .github/workflows/        # Automated GitHub Pages CI/CD pipeline
│
└── tests/
    ├── TakEngine.Core.Tests/         # Rule engine unit tests, DFS validation, PTN parser, Crypto, SQLite, Spectator tests
    └── TakEngine.Transport.Tests/    # Relay serialization, Round-trip latency tests, Invite codes, NIP-44 encryption
```

---

## Documentation Index

Detailed specifications and architectural guides:

1. [Full Specification & Handoff Document](docs/PROJECT_SPECIFICATION.md) - Complete consolidated specification.
2. [System Overview](docs/system-overview.md) - High-level architecture, principles, and invariants.
3. [Version 1.0 (MVP) Specification](docs/v1-mvp.md) - Deliverables, layout, Nostr transport, SQLite schema, `ITakGameSession` API, and milestones.
4. [Version 2.0 (Competitive & Tournaments) Specification](docs/v2-tournaments.md) - Co-signed receipts, Swiss tournaments, Elo oracle, anti-cheat, spectator broadcasting, and v2 database schema.
5. [Wire Protocol Specification](docs/wire-protocol.md) - Nostr envelopes, NIP-44 encryption, matchmaking, and spectator broadcast events.
6. [Database Schema Specification](docs/database-schema.md) - Complete SQLite schema for v1 and v2 migrations.
7. [Blazor WebAssembly & GitHub Pages Specification](docs/blazor-web-github-pages.md) - Design and zero-cost deployment architecture for the browser client.
8. [Spectator & Broadcast Implementation Plan](docs/spectator-implementation-plan.md) - Real-time observation, delayed public streams, and feature match directory.
9. [Development Log (DevLog)](docs/DEVLOG.md) - Chronological commit log, timestamps, and milestone progress synchronized with GitHub.

---

## Implementation Progress (Milestones)

| Task ID | Milestone Description | Completion Status |
| --- | --- | --- |
| **M1.1** | `TakEngine.Core` Rule Foundation | **Completed** (4x4, 5x5, 6x6 initialization; carry limits; DFS road finder; passing 100% tests). |
| **M1.2** | PTN / TPS Parser & Formatter | **Completed** (Standard PTN move/game parser and full TPS string generator and state restorer). |
| **M1.3** | Crypto & SQLite Persistence | **Completed** (SHA-256 hash chaining, Ed25519 signing, SQLite database engine, instant $O(1)$ replay scrubbing). |
| **M1.4** | Nostr Transport MVP | **Completed** (Multi-relay WebSocket pool, NIP-01 frames, NIP-44 direct encryption, round-trip verified < 10 ms). |
| **M1.5** | Quick Play & Direct Codes | **Completed** (Direct `tak://` URI and compact `TAK1_` QR tokens, `kind: 20001` ephemeral broadcasts, deterministic color resolution). |
| **M1.6** | Time & Stale System | **Completed** (RFC 5905 NTP network time service, Day 3 stale warnings, Day 7 auto-draw timeout adjudication). |
| **M1.7** | Spectre.Console CLI | **Completed** (ANSI board renderer, conversational stepped typed input, stack inspector, local/P2P game loop). |
| **M1.8** | Avalonia UI Prototype | **Completed** (2D vector board renderer, MVVM CommunityToolkit bindings to `ITakGameSession`, functioning across desktop and mobile). |
| **M1.9** | Blazor WASM Web Client | Planned (GitHub Pages CI/CD workflow configured, UI components authored). |

---

## Immediate Development Action Items

1. Complete Milestone **M1.9: Blazor WASM Web Client** (Deploy interactive WebAssembly frontend to GitHub Pages with live Nostr P2P matchmaking).
2. Wire mobile touch gestures and mobile application project heads for Avalonia (Android/iOS).

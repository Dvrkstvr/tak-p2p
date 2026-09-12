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
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                                       Frontends                                        │
│  TakApp.Cli    │  TakApp.Blazor (WASM)  │  TakApp.Avalonia (Desktop, Android, iOS)     │
└───────────────────────────────────┬────────────────────────────────────────────────────┘
                                    │ Consumes ITakGameSession, ITakBot, Snapshots
                                    ▼
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                                TakEngine.Abstractions                                  │
│   Models, Enums, Interfaces, ITakGameSession, ITakBot, ISpectatorGameSession           │
└───────────────────────────────────▲────────────────────────────────────────────────────┘
                                    │ Implements
┌───────────────────────────────────┴────────────────────────────────────────────────────┐
│                                    TakEngine.Core                                      │
│  Board, DFS Rules, Minimax AI, PTN/TPS, Crypto, SQLite Storage, Replay, Spectator, NTP │
└───────────────────────────────────┬────────────────────────────────────────────────────┘
                                    │ Envelopes / Events
                                    ▼
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                                 TakEngine.Transport                                    │
│   Nostr WebSocket Client, NIP-44 Encryption, Matchmaking, Profiles, Broadcasts         │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## Completed Foundations

1. **`TakEngine.Abstractions`**: Shared immutable records, enums (`PieceType`, `PlayerColor`, `Direction`, `BotDifficulty`), `ITakGameSession`, `ITakBot`, and `ISpectatorGameSession`.
2. **`TakEngine.Core`**: Orthogonal DFS road detection, Turn 1 swap rule, carry limits, wall flattening, PTN/TPS serializers, SHA-256 state hash chaining, Ed25519 signatures, NIP-19 Bech32 key codecs, SQLite persistence with $O(1)$ TPS scrubbing, `MinimaxTakBot` AI with Alpha-Beta pruning, `DelayedBroadcastQueue`, and RFC 5905 NTP time sync.
3. **`TakEngine.Transport`**: NIP-01 frames, NIP-44 ChaCha20-Poly1305 encryption, multi-relay WebSocket coordination (`wss://relay.damus.io`, `wss://nos.lol`, `wss://relay.primal.net`), invite codes (`tak://` URI, web URLs, compact QR tokens), Nostr profile publishing/querying (`kind: 0`), and ephemeral `kind: 20001` quick-play matchmaking.
4. **`TakApp.Cli`**: Spectre.Console terminal client with interactive ANSI board, conversational stepped typed input, stack inspector, and offline AI bot practice mode.
5. **`TakApp.Blazor`**: Zero-install Blazor WebAssembly client with interactive vector SVG board, 3D piece stacks, dark theme glassmorphic UI, offline AI bot play, QR code pairing modals, and automated GitHub Pages CI/CD.
6. **`TakApp.Avalonia`**: Cross-platform MVVM library with Skia/vector rendering, shared across three runnable heads: `TakApp.Avalonia.Desktop` (Windows, Linux, macOS), `TakApp.Avalonia.Android` (APK for phones & tablets), and `TakApp.Avalonia.iOS` (iPad & iPhone).
7. **Comprehensive Test Suite**: 107 passing unit tests (88 in `TakEngine.Core.Tests` + 19 in `TakEngine.Transport.Tests`) verifying mathematical soundness, cryptographic validity, and wire SLAs.

---

## Architectural Specification Map

* Master Spec: [docs/PROJECT_SPECIFICATION.md](file:///e:/repos/tak-p2p/docs/PROJECT_SPECIFICATION.md)
* Development Log: [docs/DEVLOG.md](file:///e:/repos/tak-p2p/docs/DEVLOG.md)
* MVP Acceptance & Milestones: [docs/v1-mvp.md](file:///e:/repos/tak-p2p/docs/v1-mvp.md)
* Competitive & Tournaments: [docs/v2-tournaments.md](file:///e:/repos/tak-p2p/docs/v2-tournaments.md)
* Wire Protocol & Nostr: [docs/wire-protocol.md](file:///e:/repos/tak-p2p/docs/wire-protocol.md)
* SQLite Database Schema: [docs/database-schema.md](file:///e:/repos/tak-p2p/docs/database-schema.md)
* Blazor Web & GitHub Pages: [docs/blazor-web-github-pages.md](file:///e:/repos/tak-p2p/docs/blazor-web-github-pages.md)
* Spectator & Broadcast System: [docs/spectator-implementation-plan.md](file:///e:/repos/tak-p2p/docs/spectator-implementation-plan.md)
* Project Audit Report: [docs/AUDIT.md](file:///e:/repos/tak-p2p/docs/AUDIT.md)

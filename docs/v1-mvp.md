# Handoff Specification: Version 1.0 (MVP)

---

## Deliverable Scope

1. Core Tak rules engine for **4x4, 5x5, and 6x6** boards.
2. Cryptographic append-only move chain via Ed25519 / Secp256k1.
3. Offline-first asynchronous & synchronous transport over public Nostr relays.
4. Two matchmaking modes: **Direct Invite Code/QR** and **Quick Play Pool**.
5. Local match storage in SQLite with instant move scrubbing/replay.
6. Stale match alerts (Day 3) and auto-draw timeouts (Day 7) via NTP time checks.
7. Three frontends: **Spectre.Console (CLI)**, **Avalonia UI (Desktop/Mobile GUI)**, and **Blazor WebAssembly (Zero-Install Browser App)**.

---

## 2.1 Repository & Solution Layout

```
TakGame.sln / TakGame.slnx
├── src/
│   ├── TakEngine.Abstractions/       # [Public NuGet candidate]
│   │   ├── Enums/                    # PieceType, PlayerColor, Direction, GamePhase
│   │   ├── Models/                   # Coord, StackSnapshot, BoardSnapshot, TakMove, BroadcastModels
│   │   ├── ITakGameSession.cs        # Primary interface consumed by all frontends
│   │   └── ISpectatorGameSession.cs  # Spectator observable interface
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
│   ├── TakApp.Avalonia/              # [Shared Cross-Platform UI & MVVM Library]
│   │   ├── ViewModels/               # MVVM ViewModels (CommunityToolkit.Mvvm)
│   │   ├── Views/                    # Canvas/Skia board renderer, Match controls, MainView
│   │   └── Services/                 # Local OS notification scheduler
│   │
│   ├── TakApp.Avalonia.Desktop/      # [Runnable Desktop GUI - Windows, macOS, Linux]
│   ├── TakApp.Avalonia.Android/      # [Runnable Android Native App - Phone & Tablet]
│   ├── TakApp.Avalonia.iOS/          # [Runnable iOS & iPadOS Native App]
│   └── TakApp.Blazor/                # [Runnable Zero-Install Web Client]
│       ├── Pages/                    # Web board renderer, Lobby view
│       └── wwwroot/                  # GitHub Pages deployment assets
│
└── tests/
    ├── TakEngine.Core.Tests/         # Rule engine unit tests, DFS validation, PTN parser, Crypto, SQLite, Spectator tests
    └── TakEngine.Transport.Tests/    # Relay serialization, Round-trip latency tests, Invite codes, NIP-44 encryption
```

---

## 2.2 Wire Protocol (Nostr Transport Specification)

Communication between peers occurs over free public Nostr relays (e.g., `wss://relay.damus.io`, `wss://nos.lol`, `wss://relay.primal.net`).

### Move Envelope (`kind: 4` / NIP-44 Direct Encrypted Event)

```json
{
  "game_id": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "turn": 15,
  "player_pubkey": "3bf0c63fcb93463407af97b5e0918838e64edd9d071293ad011663f1d7b6af94",
  "prev_state_hash": "a4f8c92b23a9d9b4009e879a8bc43428d223298c5d12ef4b476e3e577e3e9d89",
  "timestamp_utc": "2026-09-12T03:08:19Z",
  "action_type": "MOVE",
  "action_data": {
    "ptn": "3c3+12",
    "details": {
      "from": "c3",
      "direction": "+",
      "lift": 3,
      "drops": [1, 2]
    }
  },
  "signature": "3045022100e4b8108a38..."
}
```

### Matchmaking Handshake (Quick Play)

1. **Search Broadcast (Ephemeral Event `kind: 20001`):**
   * Tags: `[["t", "tak_quickplay"], ["board_size", "5"], ["client_version", "1.0"]]`
   * Content: Ephemeral public key + supported relay list.
   * TTL: 60 seconds (NIP-40 expiration).

2. **Challenge / Accept:**
   * Peer B discovers broadcast, connects directly via encrypted payload proposing `game_id` and random seed for player colors.
   * Both peers deterministically calculate player colors using `ColorResolver.ResolveColors(seed, peerA, peerB)`.
   * Peer A signs acceptance; both peers withdraw broadcast.

---

## 2.3 SQLite Storage Schema (v1 with v2 Anticipation)

```sql
CREATE TABLE IF NOT EXISTS Games (
    Id TEXT PRIMARY KEY NOT NULL,
    BoardSize INTEGER NOT NULL CHECK(BoardSize IN (4, 5, 6)),
    LocalPlayerColor INTEGER NOT NULL, -- 0: White, 1: Black
    OpponentPubKey TEXT NOT NULL,
    Status INTEGER NOT NULL,            -- 0: Active, 1: Stale, 2: Completed, 3: DrawTimeout, 4: Resigned
    WinnerPubKey TEXT NULL,
    StartedAt TEXT NOT NULL,
    LastUpdatedAt TEXT NOT NULL,
    TournamentId TEXT NULL              -- Reserved for v2
);

CREATE TABLE IF NOT EXISTS Moves (
    GameId TEXT NOT NULL,
    TurnIndex INTEGER NOT NULL,
    PlayerPubKey TEXT NOT NULL,
    PtnMove TEXT NOT NULL,
    TpsSnapshot TEXT NOT NULL,          -- Full board state after move
    StateHash TEXT NOT NULL,            -- SHA-256 of current state
    PrevStateHash TEXT NOT NULL,
    TimestampUtc TEXT NOT NULL,
    Signature TEXT NOT NULL,
    PRIMARY KEY (GameId, TurnIndex),
    FOREIGN KEY(GameId) REFERENCES Games(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_moves_game_turn ON Moves(GameId, TurnIndex);
```

---

## 2.4 API Surface: `ITakGameSession`

```csharp
namespace TakEngine.Abstractions;

public interface ITakGameSession
{
    GameId Id { get; }
    BoardSize Size { get; }
    PlayerColor LocalColor { get; }
    GamePhase CurrentPhase { get; }
    
    // Read-only snapshots
    TakBoardSnapshot CurrentBoard { get; }
    IReadOnlyList<TakMove> GetLegalMovesForSquare(Coord coord);

    // Command dispatch
    CommandResult SubmitPlacement(Coord target, PieceType piece);
    CommandResult SubmitMove(Coord origin, Direction direction, IReadOnlyList<int> drops);
    CommandResult Resign();

    // Reactive streams
    event Action<TakBoardSnapshot, TakMove> OnMoveExecuted;
    event Action<TakBoardSnapshot, GameResult> OnGameEnded;
    event Action<TimeSpan> OnStaleWarning;
    event Action<string> OnTransportStatusChanged;
    event Action<ProtocolViolationException> OnProtocolViolationDetected;
}
```

---

## 2.5 v1 Acceptance Criteria & Milestone Status

| Task ID | Milestone Description | Completion Criteria | Status |
| --- | --- | --- | --- |
| **M1.1** | `TakEngine.Core` Rule Foundation | 4x4, 5x5, 6x6 initialization; inventory rules; DFS road finder; passing 100% unit tests. | **COMPLETED** |
| **M1.2** | PTN / TPS Parser & Formatter | Able to serialize and deserialize games to standard PTN format; TPS string generator. | **COMPLETED** |
| **M1.3** | Crypto & SQLite Persistence | Hash chaining logic implemented; games and moves successfully written and restored from SQLite. | **COMPLETED** |
| **M1.4** | Nostr Transport MVP | Relay connection loop; NIP-44 encrypted payload round-trip verified under 300 ms on test peers. | **COMPLETED** |
| **M1.5** | Quick Play & Direct Codes | Invite code string parser and ephemeral Nostr broadcast discovery functional. | **COMPLETED** |
| **M1.6** | Time & Stale System | NTP time fetcher integrated; Day 3 warning and Day 7 auto-draw logic verified via mock timestamps. | **COMPLETED** |
| **M1.7** | Spectre.Console UI | Functional CLI game loop with live ANSI board updating, conversational stepped typed input, stack inspector, and PTN prompt. | **COMPLETED** |
| **M1.8** | Avalonia UI Prototype | 2D vector board rendering, MVVM bindings to `ITakGameSession`, functioning across desktop and mobile. | **COMPLETED** |
| **M1.9** | Blazor WASM Client | Zero-install browser client with GitHub Pages automated deployment. | **COMPLETED** |
| **M1.10** | Offline AI Practice Bot | Minimax bot with Alpha-Beta pruning, heuristic evaluation, and difficulty tiers. | **COMPLETED** |
| **M1.11** | Nostr Profiles & Invite UX | NIP-19 npub/nsec Bech32, 1-click playable web links, SVG QR codes, and profile metadata. | **COMPLETED** |
| **M1.12** | Multi-Platform Native Heads | Scaffolding dedicated Avalonia heads for Android (APK) and iOS/iPadOS with shared MVVM core. | **COMPLETED** |
| **M1.13** | UI Mockups Realization & 2.5D Board | Monochromatic B&W with amber accent, 2.5D perspective board, staggered towers, and mobile layout overhaul. | **COMPLETED** |
| **M1.14** | 2.5D Pillar Capstone & SVG Assets | Pre-rendered SVG piece symbols, commanding pillar capstone, 48px walls, and automated tile visual test suite. | **COMPLETED** |
| **M1.15** | PlayTak-Authentic Game Controls | Interactive reserves with click/right-click rotate-to-wall, quick-wall right-click placement, direct on-board slide targets, floating stack layer inspector, and hotkeys ([F], [W], [C], [Esc]). | **COMPLETED** |

---

## 2.6 Related Documentation & Deep Dives

* Master Architecture: [docs/PROJECT_SPECIFICATION.md](file:///e:/repos/tak-p2p/docs/PROJECT_SPECIFICATION.md)
* System Overview: [docs/system-overview.md](file:///e:/repos/tak-p2p/docs/system-overview.md)
* Wire Protocol & Nostr: [docs/wire-protocol.md](file:///e:/repos/tak-p2p/docs/wire-protocol.md)
* Database Schema: [docs/database-schema.md](file:///e:/repos/tak-p2p/docs/database-schema.md)
* Web WASM Architecture: [docs/blazor-web-github-pages.md](file:///e:/repos/tak-p2p/docs/blazor-web-github-pages.md)
* Spectator Architecture: [docs/spectator-implementation-plan.md](file:///e:/repos/tak-p2p/docs/spectator-implementation-plan.md)
* Project Audit Report: [docs/AUDIT.md](file:///e:/repos/tak-p2p/docs/AUDIT.md)
* Chronological Commit History: [docs/DEVLOG.md](file:///e:/repos/tak-p2p/docs/DEVLOG.md)

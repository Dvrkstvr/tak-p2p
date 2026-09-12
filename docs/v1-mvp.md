# Handoff Specification: Version 1.0 (MVP)

---

## Deliverable Scope

1. Core Tak rules engine for **4x4, 5x5, and 6x6** boards.
2. Cryptographic append-only move chain via Ed25519 / Secp256k1.
3. Offline-first asynchronous & synchronous transport over public Nostr relays.
4. Two matchmaking modes: **Direct Invite Code/QR** and **Quick Play Pool**.
5. Local match storage in SQLite with instant move scrubbing/replay.
6. Stale match alerts (Day 3) and auto-draw timeouts (Day 7) via NTP time checks.
7. Two frontends: **Spectre.Console (CLI)** and **Avalonia UI (GUI)**.

---

## 2.1 Repository & Solution Layout

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
   * TTL: 60 seconds.

2. **Challenge / Accept:**
   * Peer B discovers broadcast, connects directly via encrypted payload proposing `game_id` and random seed for player colors.
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

## 2.5 v1 Acceptance Criteria & Milestones

| Task ID | Milestone Description | Completion Criteria |
| --- | --- | --- |
| **M1.1** | `TakEngine.Core` Rule Foundation | 4x4, 5x5, 6x6 initialization; inventory rules; DFS road finder; passing 100% unit tests. |
| **M1.2** | PTN / TPS Parser & Formatter | Able to serialize and deserialize games to standard PTN format; TPS string generator. |
| **M1.3** | Crypto & SQLite Persistence | Hash chaining logic implemented; games and moves successfully written and restored from SQLite. |
| **M1.4** | Nostr Transport MVP | Relay connection loop; NIP-44 encrypted payload round-trip verified under 300 ms on test peers. |
| **M1.5** | Quick Play & Direct Codes | Invite code string parser and ephemeral Nostr broadcast discovery functional. |
| **M1.6** | Time & Stale System | NTP time fetcher integrated; Day 3 warning and Day 7 auto-draw logic verified via mock timestamps. |
| **M1.7** | Spectre.Console UI | Functional CLI game loop with live ANSI board updating, stack inspector, and PTN prompt. |
| **M1.8** | Avalonia UI Prototype | 2D vector board rendering, MVVM bindings to `ITakGameSession`, functioning across desktop and mobile. |

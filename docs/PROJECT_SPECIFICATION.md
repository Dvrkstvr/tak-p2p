# Project Specification & Handoff Document

---

## 1. System Overview & Core Philosophy

This project is a decentralized, peer-to-peer (P2P), zero-server implementation of the abstract strategy game **Tak**, supporting both synchronous (live) and asynchronous play across Linux, Android, Windows, and iOS.

### Architectural Invariants

* **Zero Authoritative Game Servers:** The network layer functions strictly as an encrypted "dumb pipe" / store-and-forward mailbox. Clients never trust remote states; all moves and state transitions are verified deterministically on the local device.
* **Separation of Concerns:**
  * `TakEngine.Abstractions`: Shared contracts, immutable records, data structures (publicly distributed).
  * `TakEngine.Core`: Private game logic, DFS graph road traversal, cryptographic hashing, invariant checks, state storage.
  * `TakEngine.Transport`: Nostr WebSocket relay interface, NIP-44 encryption, envelope serialization.
  * Frontends (`TakApp.Avalonia`, `TakApp.Cli`): Pure UI views consuming reactive observables/events.
* **Deterministic Rule Adjudication:** Illegal moves are mathematically impossible to force onto a peer. If an opponent injects an invalid payload, the receiving client drops the payload and flags the peer.

---

# 2. Handoff Specification: Version 1.0 (MVP)

### Deliverable Scope

1. Core Tak rules engine for **4x4, 5x5, and 6x6** boards.
2. Cryptographic append-only move chain via Ed25519 / Secp256k1.
3. Offline-first asynchronous & synchronous transport over public Nostr relays.
4. Two matchmaking modes: **Direct Invite Code/QR** and **Quick Play Pool**.
5. Local match storage in SQLite with instant move scrubbing/replay.
6. Stale match alerts (Day 3) and auto-draw timeouts (Day 7) via NTP time checks.
7. Two frontends: **Spectre.Console (CLI)** and **Avalonia UI (GUI)**.

---

### 2.1 Repository & Solution Layout

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

### 2.2 Wire Protocol (Nostr Transport Specification)

Communication between peers occurs over free public Nostr relays (e.g., `wss://relay.damus.io`, `wss://nos.lol`, `wss://relay.primal.net`).

#### Move Envelope (`kind: 4` / NIP-44 Direct Encrypted Event)

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

#### Matchmaking Handshake (Quick Play)

1. **Search Broadcast (Ephemeral Event `kind: 20001`):**
   * Tags: `[["t", "tak_quickplay"], ["board_size", "5"], ["client_version", "1.0"]]`
   * Content: Ephemeral public key + supported relay list.
   * TTL: 60 seconds.

2. **Challenge / Accept:**
   * Peer B discovers broadcast, connects directly via encrypted payload proposing `game_id` and random seed for player colors.
   * Peer A signs acceptance; both peers withdraw broadcast.

---

### 2.3 SQLite Storage Schema (v1 with v2 Anticipation)

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

### 2.4 API Surface: `ITakGameSession`

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

### 2.5 v1 Acceptance Criteria & Milestones

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

---

# 3. Handoff Specification: Version 2.0 (Competitive & Tournaments)

### Deliverable Scope

1. **Admin Broadcast Framework:** Hardcoded admin public key to sign announcements and tournament events.
2. **Decentralized Swiss Tournaments:** Serverless check-ins, deterministic pairings via signature seeds.
3. **Admin Oracle Elo Rating Engine:** Verified match receipts, Sybil attack filters, and signed leaderboard publications.
4. **Economic Anti-Cheat (Verified Profiles):** Cryptographic attestation receipts issued upon payment to gate verified matchmaking pools.
5. **UI Extensions:** Tournament brackets and global leaderboards in CLI and Avalonia.

---

### 3.1 Consensus Primitive: Co-Signed `MatchReceipt`

Every completed game generates a single terminal receipt signed by **both** participants:

```json
{
  "type": "MATCH_RECEIPT",
  "game_id": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "tournament_id": "swiss_autumn_2026",
  "final_tps_hash": "4f9d2a...",
  "winner_pubkey": "3bf0c63fcb9346...",
  "is_draw": false,
  "turn_count": 34,
  "completed_at": "2026-09-12T05:14:20Z",
  "player1_signature": "sig1_hex...",
  "player2_signature": "sig2_hex..."
}
```

*Dispute fallback:* If a player goes dark without signing, the opponent submits a `ForfeitClaim` accompanied by the last signed turn and proof of 7-day NTP expiry.

---

### 3.2 Tournament Lifecycle & Deterministic Pairing

```
       [Admin Publishes Tournament] (Kind: 31923)
                    │
                    ▼
     [Players Publish Check-In Tickets] (Within 30m window)
                    │
                    ▼
     [Seed Calculation: SHA256(Sorted Player Signatures)]
                    │
                    ▼
 [Deterministic Swiss Pairing Function: PRNG(Seed + Round)]
                    │
                    ▼
[P2P Matches Executed -> Co-Signed Match Receipts Published]
                    │
                    ▼
       [Next Round Seeded via Updated Standings]
```

#### Deterministic Pairing Algorithm

1. Every client downloads all check-in events referencing `TournamentId` at round start.
2. Filter invalid signatures; sort roster lexicographically by `PlayerPubKey`.
3. Compute:

$$\text{Seed} = \text{SHA-256}\left(\sum \text{Signatures}_{\text{sorted}}\right)$$

4. Initialize deterministic PRNG with $\text{Seed} \oplus \text{RoundNumber}$.
5. Run Swiss pairing: Match identical score brackets while prohibiting rematching. Because inputs and seeds are identical, **every client arrives at the exact same bracket with zero central server coordination**.

---

### 3.3 Elo Rating Oracle & Anti-Sybil Policy

To eliminate fake match collusion without running an authoritative game backend:

* **The Rating Oracle:** An open-source background worker run by the community/admin polls Nostr for valid `MatchReceipt` payloads.
* **Sybil Filtering Constraints:**
  1. Matches between Account A and Account B are capped at 2 rated matches per 7 rolling days.
  2. Accounts must possess either an **Admin-Signed Premium Attestation** or have completed at least one verified tournament match to participate in the global ladder.
* **Output:** Oracle signs and publishes a weekly Nostr event (`kind: 30000`) containing:
  * Public key $\rightarrow$ Elo rating dictionary.
  * Win / Loss / Draw stats.
  * Rank tiers.

---

### 3.4 Economic Anti-Cheat: Verified Profiles

```
[User Pays $5 via App Store / Stripe]
               │
               ▼
[Payment Webhook Triggers Serverless Worker]
               │
               ▼
[Worker Signs: "Pubkey 0xABC is Verified until [Date]" with AdminPrivateKey]
               │
               ▼
[User Stores Certificate & Attaches to Nostr Profile / Match Handshake]
               │
               ▼
[Opponent's Client Verifies Signature against Hardcoded AdminPublicKey]
```

* **Revocation:** Admin publishes an append-only `BlacklistEvent` (`kind: 30001`) containing burned public keys.
* **Client Guard:** If `Opponent.IsVerified == true` and `Opponent.PubKey NOT IN Blacklist`, client awards the "Verified Profile" badge and allows queuing in high-trust pools.

---

### 3.5 Database Schema Additions (v2 Migration)

```sql
CREATE TABLE IF NOT EXISTS MatchReceipts (
    GameId TEXT PRIMARY KEY NOT NULL,
    TournamentId TEXT NULL,
    FinalTpsHash TEXT NOT NULL,
    WinnerPubKey TEXT NULL,
    IsDraw INTEGER NOT NULL,
    TurnCount INTEGER NOT NULL,
    CompletedAt TEXT NOT NULL,
    Player1Signature TEXT NOT NULL,
    Player2Signature TEXT NOT NULL,
    FOREIGN KEY(GameId) REFERENCES Games(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Tournaments (
    Id TEXT PRIMARY KEY NOT NULL,
    AdminPubKey TEXT NOT NULL,
    Title TEXT NOT NULL,
    BoardSize INTEGER NOT NULL,
    CurrentRound INTEGER NOT NULL,
    StartsAt TEXT NOT NULL,
    IsCompleted INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS LeaderboardCache (
    PlayerPubKey TEXT PRIMARY KEY NOT NULL,
    EloRating INTEGER NOT NULL,
    MatchesPlayed INTEGER NOT NULL,
    TournamentWins INTEGER NOT NULL,
    LastUpdatedUtc TEXT NOT NULL,
    AdminSignature TEXT NOT NULL
);
```

---

## 4. Immediate Development Action Items

1. Initialize `TakEngine.Abstractions` with `Coord`, `TakMove`, `StackSnapshot`, and `ITakGameSession`.
2. Implement `TakEngine.Core.Board` and write the unit tests for orthogonal DFS road verification.
3. Benchmark Nostr WebSocket latency (`TakEngine.Transport.Tests`) across public relays to lock in baseline sync performance.

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
TakGame.sln / TakGame.slnx
├── src/
│   ├── TakEngine.Abstractions/       # [Shared NuGet candidate]
│   │   ├── Enums/                    # PieceType, PlayerColor, Direction, GamePhase, BotDifficulty
│   │   ├── Models/                   # Coord, StackSnapshot, BoardSnapshot, TakMove, BroadcastModels
│   │   ├── ITakGameSession.cs        # Primary interface consumed by all frontends
│   │   ├── ITakBot.cs                # Decoupled AI engine interface
│   │   └── ISpectatorGameSession.cs  # Spectator/broadcast observable interface
│   │
│   ├── TakEngine.Core/               # [Engine & Rules Core]
│   │   ├── Board/                    # Grid, Stacks, Piece Inventories, Move Execution
│   │   ├── Rules/                    # Invariant rules, Carry limits, DFS Road finder, MoveValidator
│   │   ├── AI/                       # MinimaxTakBot, TakEvaluator (Alpha-Beta pruning)
│   │   ├── Serialization/            # PTN (Portable Tak Notation) & TPS (Tak Positional System)
│   │   ├── Cryptography/             # Keypairs, Signatures, SHA-256 State Hashing, NIP-19 Bech32
│   │   ├── Storage/                  # SQLite database engine, Match logs, Replay provider
│   │   └── Session/                  # TakGameSession, DelayedBroadcastQueue, SpectatorSession, NTP
│   │
│   ├── TakEngine.Transport/          # [Nostr P2P Infrastructure]
│   │   ├── Nostr/                    # WebSocket client, NIP-01/NIP-44 wrappers, NostrProfile metadata
│   │   ├── Matchmaking/              # Invite code parser, Ephemeral broadcast handler, ColorResolver
│   │   └── TransportEnvelope.cs      # Signed wire models
│   │
│   ├── TakApp.Avalonia/              # [Shared Cross-Platform UI & MVVM Library]
│   │   ├── ViewModels/               # MVVM ViewModels (CommunityToolkit.Mvvm)
│   │   ├── Views/                    # Canvas/Skia board renderer, Match controls, MainView
│   │   └── Services/                 # Local OS notification scheduler
│   │
│   ├── TakApp.Avalonia.Desktop/      # [Runnable Desktop GUI - Windows, macOS, Linux]
│   │   ├── Program.cs                # Desktop entry point
│   │   └── app.manifest              # Windows DPI awareness & OS compatibility
│   │
│   ├── TakApp.Avalonia.Android/      # [Runnable Android Native App - Phone & Tablet]
│   │   ├── MainActivity.cs           # Android entry point & activity lifecycle
│   │   ├── Application.cs            # Android application bootstrap
│   │   └── Properties/               # AndroidManifest.xml & resources
│   │
│   ├── TakApp.Avalonia.iOS/          # [Runnable iOS & iPadOS Native App]
│   │   ├── Main.cs                   # iOS entry point
│   │   ├── AppDelegate.cs            # iOS application delegate
│   │   └── Info.plist                # iPad & iPhone device family configuration
│   │
│   ├── TakApp.Blazor/                # [Runnable Zero-Install Web Client]
│   │   ├── Components/               # TakBoardView (SVG), PieceStackSvg, SlideBar, Panels, Modals
│   │   ├── Pages/                    # Play.razor, Home.razor (Lobby)
│   │   ├── Services/                 # WebGameSessionManager, BrowserStorage, QrCodeSvgHelper
│   │   └── wwwroot/                  # Static assets & GitHub Pages deployment
│   │
│   └── TakApp.Cli/                   # [Runnable Console App]
│       ├── Program.cs                # Entry point, Interactive menus
│       ├── Rendering/                # Spectre.Console ANSI board, stack layer inspector
│       └── Input/                    # Conversational stepped typed input & PTN command parser
│
└── tests/
    ├── TakEngine.Core.Tests/         # 95 tests: Rule engine, DFS, PTN, Crypto, SQLite, Bot, Spectator, SvgTiles
    └── TakEngine.Transport.Tests/    # 19 tests: Relay serialization, Latency benchmark, Invites, Profiles
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

### 2.5 v1 Acceptance Criteria & Milestone Status

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

## 4. Documentation Architecture & Companion Specifications

Tak P2P utilizes a modular documentation suite to provide granular, authoritative specifications for each subsystem:

| Document | Focus & Scope |
|:---|:---|
| [System Overview](file:///e:/repos/tak-p2p/docs/system-overview.md) | High-level architecture, zero-server invariants, and component boundaries |
| [UI Mockups & Visual Reference](file:///e:/repos/tak-p2p/docs/UI-MOCKUPS.md) | Minimal monochromatic B&W + amber redesign specifications and desktop/mobile mockups |
| [MVP Milestone Guide (v1.0)](file:///e:/repos/tak-p2p/docs/v1-mvp.md) | Core deliverables, acceptance criteria, and M1.1–M1.9 status verification |
| [Competitive & Tournaments (v2.0)](file:///e:/repos/tak-p2p/docs/v2-tournaments.md) | Co-signed receipts, serverless Swiss tournaments, Elo oracle, and anti-cheat |
| [Wire Protocol & Nostr](file:///e:/repos/tak-p2p/docs/wire-protocol.md) | Nostr envelopes, NIP-01/NIP-44 schemas, and ephemeral matchmaking handshakes |
| [SQLite Database Schema](file:///e:/repos/tak-p2p/docs/database-schema.md) | Complete SQLite relational schema for v1 matches/moves and v2 migrations |
| [Blazor WebAssembly & GitHub Pages](file:///e:/repos/tak-p2p/docs/blazor-web-github-pages.md) | Zero-install web client design, SPA routing, and GitHub Actions CI/CD |
| [Spectator & Broadcast System](file:///e:/repos/tak-p2p/docs/spectator-implementation-plan.md) | Real-time observation, delayed public streams, and feature match directory |
| [Comprehensive MVP Project Audit](file:///e:/repos/tak-p2p/docs/AUDIT.md) | Independent audit report, test metrics, UI/UX evaluation, and findings |
| [Development Log (DevLog)](file:///e:/repos/tak-p2p/docs/DEVLOG.md) | Chronological commit history, deliverables breakdown, and test counts |

---

## 5. Post-MVP Roadmap & Active Priorities

1. **Blazor Web Nostr WebSocket P2P Wiring:** Bridge `NostrTransportClient` directly into `WebGameSessionManager` for live remote matches in the browser client.
2. **Offline-First PWA & Storage Bridge:** Implement Service Worker caching for 100% offline standalone usage and IndexedDB persistence for match archives.
3. **Audio & Animation Polish:** Add subtle sound effects and tactile piece placement animations.
4. **Native Mobile Packaging & Store Releases:** Generate signed Android App Bundles (`.aab`) for Google Play and prepare Apple Developer provisioning for App Store distribution.
5. **Version 2.0 Swiss Tournaments:** Implement serverless Swiss check-ins and deterministic bracket generation.

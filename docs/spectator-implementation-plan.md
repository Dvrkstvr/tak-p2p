# Spectator & Tournament Broadcast Implementation Plan

---

## 1. Executive Summary & Objectives

The **Spectator & Tournament Broadcast System** enables real-time, zero-server observation of active Tak matches across the decentralized P2P Nostr network. 

### Primary Objectives

1. **Tournament Administration & Arbitration:**
   * Provide tournament admins and referees with an authenticated, zero-latency feed of every active match within a tournament.
   * Facilitate dispute resolution, stall detection, and protocol violation inspection without needing central authoritative servers.
2. **High-Elo & Feature Match Public Broadcasting:**
   * Allow verified high-Elo players (or any player with mutual opt-in) to broadcast their games to the community.
   * Provide a public "Live Now" discovery directory on Nostr for both Avalonia GUI and CLI clients.
3. **Anti-Cheat & Ghosting Protection:**
   * Enforce a configurable **Broadcast Delay Buffer** (e.g., 120–180 seconds or 3-turn delay) for public spectators to neutralize real-time external engine assistance.
   * Provide an uninhibited, zero-delay encrypted stream exclusively for verified Tournament Moderators and Referees.
4. **Deterministic P2P Verification:**
   * Spectators never trust a host stream; spectator clients ingest signed `TransportEnvelope`s and execute moves locally via `TakEngine.Core` to guarantee tamper-proof replay and state validity.

---

## 2. Threat Model & Anti-Cheat Architecture

Unlike games with hidden information (e.g., Fog of War or hidden hands), Tak is a game of **perfect information**. The primary cheating vector in live spectating is **engine ghosting** (an external spectator running a high-depth AI engine like *Taktician* and whispering moves to a player over Discord/phone).

```
   ┌───────────────────┐
   │ Tournament Match  │
   │ Player A vs B     │
   └─────────┬─────────┘
             │
             ├─── [Zero-Delay / Direct / Admin Encrypted] ──────► [Admin / Referee Console]
             │    (Kind: 4 / Kind: 1059 Gift Wrap)
             │
             └─── [Configurable Delay Buffer (e.g., 2m)] ──────► [Public Relays] ──► [Public Spectators]
                  (Kind: 21000 Public Broadcast)
```

### 2.1 Mitigation Strategies

| Level | Target Audience | Transport Security | Delay Policy | Integrity Verification |
|---|---|---|---|---|
| **Admin Stream** | Tournament Directors & Arbiters | Co-encrypted to Admin PubKey (`kind: 4` / NIP-44) or Tournament Group (`NIP-29`) | **0 seconds** (Real-time) | Ed25519 / Secp256k1 signature per move |
| **Public Stream** | Community / Fans / High-Elo Viewers | Public Nostr Event (`kind: 21000`) | **120–300 seconds** (Configurable) | Canonical SHA-256 State Hash chain |

---

## 3. Wire Protocol & Nostr Event Specifications

### 3.1 Live Broadcast Directory (`kind: 20002` - Ephemeral Event)

Broadcast hosts emit a heartbeat every 30 seconds or upon significant game updates to announce their active match in the public lobby:

```json
{
  "kind": 20002,
  "pubkey": "<broadcaster_pubkey>",
  "tags": [
    ["t", "tak_live"],
    ["game_id", "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d"],
    ["tournament_id", "swiss_autumn_2026"],
    ["board_size", "5"],
    ["white_pubkey", "3bf0c63fcb9346..."],
    ["white_elo", "1850"],
    ["black_pubkey", "8a7c2b0e9f1234..."],
    ["black_elo", "1920"],
    ["turn", "22"],
    ["delay_sec", "120"],
    ["client_version", "2.0"]
  ],
  "content": "{\"status\":\"active\",\"last_move_ptn\":\"3c3+12\",\"started_at\":\"2026-09-12T05:00:00Z\"}"
}
```

### 3.2 Public Move Broadcast Envelope (`kind: 21000` - Regular / Ephemeral)

For public spectators, players (or the delayed local broadcast worker) publish each move with the calculated delay:

```json
{
  "kind": 21000,
  "pubkey": "<player_pubkey>",
  "tags": [
    ["t", "tak_broadcast"],
    ["game_id", "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d"],
    ["tournament_id", "swiss_autumn_2026"],
    ["turn", "22"],
    ["prev_state_hash", "a4f8c92b23a9d9b4009e879a8bc43428d223298c5d12ef4b476e3e577e3e9d89"]
  ],
  "content": "{\"game_id\":\"9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d\",\"turn\":22,\"action_type\":\"MOVE\",\"action_data\":{\"ptn\":\"3c3+12\",\"details\":{\"from\":\"c3\",\"direction\":\"+\",\"lift\":3,\"drops\":[1,2]}},\"signature\":\"3045022100e4b8108a38...\",\"timestamp_utc\":\"2026-09-12T05:14:20Z\"}"
}
```

### 3.3 Mid-Game Spectator Catch-Up (TPS Snapshot Event: `kind: 21001`)

To prevent new spectators from having to query 80+ individual Nostr events sequentially:
* Broadcasters publish a **State Checkpoint** (`kind: 21001`) every 10 turns.
* Contains full `tps_snapshot`, `state_hash`, `turn_index`, and recent move history.
* A connecting spectator downloads the latest checkpoint and replays only the remaining $(turn \pmod{10})$ moves.

---

## 4. Architecture & Component Changes

```
┌─────────────────────────────────────────────────────────────────┐
│                      Frontends                                  │
│  TakApp.Avalonia:                                               │
│    - Live Match Directory ("Watch" Tab)                         │
│    - Spectator Board View (Scrubbing, Live Feed, Eval Bar)      │
│    - Admin Tournament Monitoring Hub (Grid multi-match view)    │
│  TakApp.Cli:                                                    │
│    - tak live (discover matches)                                │
│    - tak watch <game_id>                                        │
│    - tak tournament monitor <tournament_id>                     │
└──────────────────────────────┬──────────────────────────────────┘
                               │ Consumes ISpectatorGameSession
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                 TakEngine.Abstractions                          │
│  - ISpectatorGameSession                                        │
│  - BroadcastSettings (Mode, DelaySeconds, MinEloThreshold)       │
│  - LiveMatchSummary record                                      │
└──────────────────────────────▲──────────────────────────────────┘
                               │ Implements
┌──────────────────────────────┴──────────────────────────────────┐
│                    TakEngine.Core                               │
│  - SpectatorGameSession (Deterministic local ingestion & replay)│
│  - DelayedBroadcastQueue (Buffer moves with timer release)       │
│  - StateHasher & CryptoSigner validation for third-party moves  │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                  TakEngine.Transport                            │
│  - NostrBroadcastPublisher (kind 20002 / 21000 / 21001)         │
│  - NostrSpectatorSubscriber (Filter by #game_id or #tournament) │
│  - AdminStreamRelay (NIP-44 direct pipeline to AdminPubKey)     │
└─────────────────────────────────────────────────────────────────┘
```

---

## 5. C# Abstraction Contracts

### 5.1 `ISpectatorGameSession`

```csharp
namespace TakEngine.Abstractions;

public interface ISpectatorGameSession : IDisposable
{
    Guid GameId { get; }
    string? TournamentId { get; }
    BoardSize Size { get; }
    PlayerColor CurrentTurnColor { get; }
    int CurrentTurnIndex { get; }
    
    // Player profiles
    string WhitePlayerPubKey { get; }
    string BlackPlayerPubKey { get; }
    int? WhitePlayerElo { get; }
    int? BlackPlayerElo { get; }
    
    // Read-only deterministic board
    TakBoardSnapshot CurrentBoard { get; }
    IReadOnlyList<TakMove> MoveHistory { get; }
    
    // Move scrubbing for spectators
    TakBoardSnapshot GetHistoricalSnapshot(int turnIndex);
    
    // Reactive streams
    event Action<TakBoardSnapshot, TakMove> OnMoveReceived;
    event Action<TakBoardSnapshot, GameResult> OnGameCompleted;
    event Action<string> OnSpectatorStatusChanged;
    event Action<ProtocolViolationException> OnStateDesyncDetected;
}
```

### 5.2 `BroadcastConfiguration`

```csharp
namespace TakEngine.Abstractions;

public enum BroadcastMode
{
    Disabled = 0,
    AdminOnly = 1,       // Zero-delay, encrypted to Tournament Admins
    PublicDelayed = 2,   // Delayed broadcast for community (anti-cheat)
    PublicRealtime = 3   // Casual unrated games only
}

public sealed record BroadcastConfiguration(
    BroadcastMode Mode,
    TimeSpan Delay = default, // Default 120s for PublicDelayed
    string? AdminPubKey = null,
    string? TournamentId = null,
    bool AllowSpectatorChat = false
);
```

---

## 6. Admin / Moderator Workflow in Tournaments

```
[Admin Initializes Tournament] (kind: 31923, AdminPubKey set)
              │
              ▼
[Players Pair & Launch Match]
              │
              ├───► 1. Move executed locally
              │
              ├───► 2. Co-encrypt move to Opponent (NIP-44, 0s delay)
              │
              ├───► 3. Co-encrypt move to AdminPubKey (NIP-44, 0s delay)
              │
              └───► 4. Enqueue move in DelayedBroadcastQueue (120s delay)
                                 │
                                 ▼
                     [Publish kind 21000 to Relays] ──► [Public Viewers]
```

### Tournament Director Features:
1. **Multi-Match Grid:** Admin UI monitors all paired tables in the current Swiss round in parallel.
2. **Stall & Timeout Detection:** Highlights matches exceeding turn time limits with 1-click forfeiture prompts.
3. **Dispute Resolution:** In case of a claim, Admin downloads the full signed cryptographic hash chain to identify which player submitted an invalid move or went unresponsive.

---

## 7. High-Elo Feature Match Broadcasting

### Qualification Logic

Players can broadcast to the global "Live Matches" directory if:
1. `Player.EloRating >= 1600` (verified via Oracle-signed `kind: 30000` leaderboard cache), **OR**
2. `Player.IsVerifiedProfile == true` (Admin economic anti-cheat attestation), **OR**
3. Both players explicitly enable "Broadcast Match" in game preferences.

---

## 8. Database Schema Additions (v2 Spectator Extensions)

```sql
CREATE TABLE IF NOT EXISTS SpectatorHistory (
    GameId TEXT PRIMARY KEY NOT NULL,
    TournamentId TEXT NULL,
    WhitePubKey TEXT NOT NULL,
    BlackPubKey TEXT NOT NULL,
    BoardSize INTEGER NOT NULL,
    FinalTps TEXT NULL,
    SpectatedAt TEXT NOT NULL,
    IsBookmarked INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS LiveBroadcastCache (
    GameId TEXT PRIMARY KEY NOT NULL,
    TournamentId TEXT NULL,
    BroadcasterPubKey TEXT NOT NULL,
    WhitePubKey TEXT NOT NULL,
    WhiteElo INTEGER NULL,
    BlackPubKey TEXT NOT NULL,
    BlackElo INTEGER NULL,
    BoardSize INTEGER NOT NULL,
    CurrentTurn INTEGER NOT NULL,
    LastActiveUtc TEXT NOT NULL,
    DelaySeconds INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_live_broadcast_active ON LiveBroadcastCache(LastActiveUtc);
```

---

## 9. Implementation Phasing & Milestones

| Milestone | Deliverables | Verification Strategy |
|---|---|---|
| **Phase 1: Contracts & Engine** | `ISpectatorGameSession`, `BroadcastConfiguration`, `DelayedBroadcastQueue` in `TakEngine.Core`. | Unit tests for delayed queue release and read-only session board advancement. |
| **Phase 2: Nostr Protocol Layer** | Handlers for `kind: 20002` (directory), `kind: 21000` (move), and `kind: 21001` (checkpoint). | Relay round-trip test with simulated 2-minute delay publisher. |
| **Phase 3: Admin Tournament Pipeline** | Dual-delivery publisher: opponent direct + admin direct stream. | Multi-client integration test with simulated Admin referee receiving instant moves. |
| **Phase 4: GUI & CLI Spectator Views** | Avalonia "Watch" tab, board scrubber, and CLI `tak watch <game_id>`. | Manual end-to-end match spectating with real-time stack inspection. |
| **Phase 5: Admin Multi-Match Hub** | Tournament Director grid monitoring view in Avalonia. | Stress test monitoring 16 concurrent simulated matches over Nostr. |

# SQLite Storage Schema Specification

The game client maintains an offline-first local SQLite database for active matches, historical move logs, state verification, and tournament records.

---

## 1. Version 1.0 (MVP) Schema

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

### Table Definitions: v1.0

#### `Games`
* `Id`: UUID string uniquely identifying the game.
* `BoardSize`: Dimension of the board (`4`, `5`, or `6`).
* `LocalPlayerColor`: Integer representing local player's side (`0` = White, `1` = Black).
* `OpponentPubKey`: Hex string of opponent's Nostr public key.
* `Status`: Game state (`0`: Active, `1`: Stale, `2`: Completed, `3`: DrawTimeout, `4`: Resigned).
* `WinnerPubKey`: Hex string of winning player, or `NULL` if active/draw.
* `StartedAt`: ISO 8601 UTC timestamp of game creation.
* `LastUpdatedAt`: ISO 8601 UTC timestamp of most recent activity.
* `TournamentId`: Identifier of parent tournament (optional in v1, used in v2).

#### `Moves`
* `GameId`: Foreign key to `Games(Id)`.
* `TurnIndex`: Sequential index of the move (1, 2, 3...).
* `PlayerPubKey`: Public key of the player submitting the move.
* `PtnMove`: Standard Portable Tak Notation string (e.g. `3c3+12`).
* `TpsSnapshot`: Full Tak Positional System string after applying the move.
* `StateHash`: SHA-256 hash calculated over current board state and history.
* `PrevStateHash`: SHA-256 hash of the previous state, forming an append-only hash chain.
* `TimestampUtc`: UTC ISO 8601 timestamp when the move was executed.
* `Signature`: Cryptographic signature of the move envelope.

---

## 2. Version 2.0 Migration Additions

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

### Table Definitions: v2.0 Additions

#### `MatchReceipts`
Stores terminal game receipts co-signed by both participants for consensus and rating calculation.

#### `Tournaments`
Tracks active and historical decentralized Swiss tournament brackets downloaded from admin announcements (`kind: 31923`).

#### `LeaderboardCache`
Locally cached copy of oracle-signed weekly leaderboard updates (`kind: 30000`).

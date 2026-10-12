"""Builds v0.db: the pre-M0 schema (user_version 0) exactly as src/TakEngine.Core/Storage/SqliteGameStorage.cs created it,
with one legacy local game and two Ed25519-era moves. The v0 -> v2 migration test opens a copy of it (docs/decisions/0010).
Run from the repo root: python3 tests/TakEngine.Storage.Tests/Fixtures/make_v0_fixture.py"""
import os
import sqlite3

HERE = os.path.dirname(os.path.abspath(__file__))
PATH = os.path.join(HERE, "v0.db")
if os.path.exists(PATH):
    os.remove(PATH)

db = sqlite3.connect(PATH)
db.executescript("""
CREATE TABLE IF NOT EXISTS Games (
    Id TEXT PRIMARY KEY NOT NULL,
    BoardSize INTEGER NOT NULL CHECK(BoardSize IN (4, 5, 6)),
    LocalPlayerColor INTEGER NOT NULL,
    OpponentPubKey TEXT NOT NULL,
    Status INTEGER NOT NULL,
    WinnerPubKey TEXT NULL,
    StartedAt TEXT NOT NULL,
    LastUpdatedAt TEXT NOT NULL,
    TournamentId TEXT NULL
);

CREATE TABLE IF NOT EXISTS Moves (
    GameId TEXT NOT NULL,
    TurnIndex INTEGER NOT NULL,
    PlayerPubKey TEXT NOT NULL,
    PtnMove TEXT NOT NULL,
    TpsSnapshot TEXT NOT NULL,
    StateHash TEXT NOT NULL,
    PrevStateHash TEXT NOT NULL,
    TimestampUtc TEXT NOT NULL,
    Signature TEXT NOT NULL,
    PRIMARY KEY (GameId, TurnIndex),
    FOREIGN KEY(GameId) REFERENCES Games(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_moves_game_turn ON Moves(GameId, TurnIndex);
""")
gid = "5d1a2c3b-0000-4000-8000-00000000c0de"
db.execute("INSERT INTO Games VALUES (?, 5, 0, 'legacy-ed25519-opponent', 1, NULL, '2026-09-12T10:00:00.0000000Z', '2026-09-12T10:05:00.0000000Z', NULL)", (gid,))
db.execute("INSERT INTO Moves VALUES (?, 1, 'legacy-white', 'a1', 'x5/x5/x5/x5/2,x4 2 1', 'aa' || hex(randomblob(0)), 'genesis', '2026-09-12T10:01:00.0000000Z', 'ed25519sig1')", (gid,))
db.execute("INSERT INTO Moves VALUES (?, 2, 'legacy-black', 'e5', 'x4,1/x5/x5/x5/2,x4 1 2', 'bb', 'aa', '2026-09-12T10:02:00.0000000Z', 'ed25519sig2')", (gid,))
db.commit()
db.close()

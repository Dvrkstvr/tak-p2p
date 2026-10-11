# M0-B Plan B1: Live Relays, SQLite Store, Restart, Tamper Matrix and the CLI Head (F-014, F-037, F-038, F-036, live rows of F-034/F-016) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** End M0-B, so that only the F-039 exit run on two machines remains. M0-A's headless game becomes something a person can play:
- two CLI processes, each with its own profile, key file and SQLite store;
- over a local relay (agent verify) and over nos.lol and damus (live, by hand);
- with restart and catch-up, a cold rebuild from relay history, and the tamper matrix.

**Architecture:** `pipeline/architecture.md`, sections "Modules", "Data", "Seams", "Core-promise path" steps 1, 8 and 9, and "Test strategy" items 4, 7, 8 and 9. New code:
- `SqliteGameStore` in `TakEngine.Storage.Local`.
- `tools/TakRelay.Local`, which serves `InMemoryRelay` over real WebSockets.
- `tools/TakTestPeer`, which publishes forged or illegal actions.
- The CLI `host` / `join` / `resume` commands with the `--plain` contract.

**Tech Stack:** as A2/A3, plus Microsoft.Data.Sqlite 10.0.12 (Storage.Local only) and ASP.NET Core minimal WebSockets for the local relay tool.

**Spec:**
- `pipeline/features.json`: F-014, F-034 (live row), F-016 (live row), F-036, F-037, F-038.
- `pipeline/scope.md` "Interaction specs": reconnect and catch-up, tamper rows 1-7, opponent offline, relay publish status.
- `pipeline/playbook.md`: "Verify method" (`--plain` contract) and "Live-relay tests".
- D-032 (live budget), D-042, D-043.
- `.claude/rules/{storage,frontends,multiplayer,transport,tests}.md`.

**Depends on:** M0-A (Plans A1-A3 and the review fixes, commit 851e25f).

**Live publishing:**
- Approved for M0 by D-032, and confirmed by the user on 2026-10-11 ("Go, with live runs").
- At most 150 events across M0. Throwaway keys, encrypted content, no kind 0/1 events.
- Every run appends its event ids to `pipeline/evidence/live-relay-log.md`, with a running total.
- The plan below spends about 70 events and leaves the rest for F-039. Ask the user before passing 150.

## Global Constraints

Plans A1-A3 apply. In addition:
- **Heads are views.** The CLI calls `MultiplayerGame` and prints its events. No rules, protocol or crypto logic lives in `Program.cs`.
- **`--plain` is a contract.** One line per event, in the exact formats in playbook.md "Verify method". Tests pin them.
- **Storage versioning.**
  - `user_version` 0 is the pre-M0 schema; 2 is M0.
  - One forward step, in one transaction, tested from a committed v0 fixture db.
  - A db with a newer version opens read-only with "written by a newer version".
  - Every `IGameStore` call is one transaction.
- **Live tests** carry `[Trait("Category", "LiveRelay")]`, read relays from `TAK_LIVE_RELAYS`, never run in CI, and log what they publish.
- **One publish budget per process.** `MultiplayerOptions` takes a `PublishBudget`, and heads share one across every game they open. This is review finding 10.

## Decisions taken in this plan (append to decisions.md when the task lands)
- **D-044 (Task 2):** applied multiplayer actions go into a new `NetMoves` table, not the legacy `Moves` table, which architecture.md "Data" planned to extend. `Moves` has a foreign key to `Games` with `CHECK(BoardSize IN (4,5,6))`, so it can't hold 3x3, 7x7 or 8x8 games (D-037). The legacy tables stay as they are (D-011). The v0 to v2 step only adds tables.
- **D-045 (Task 3):** a cold rebuild needs the invite (game id, host key, seed commitment). The guest recovers its nonce from its own JOIN on the relays, and the host recovers its nonce from its own ACCEPT. Both can decrypt their own events, because NIP-44's conversation key is symmetric. With no invite, nothing can be rebuilt; the CLI says so.

## File map

| Path | Task | Responsibility |
|---|---|---|
| `tests/TakEngine.Transport.Tests/Live/LiveRelayTests.cs`, `tests/TakEngine.Testing/LiveRelayLog.cs` | 1 | F-014 and the F-034 live row; log helper |
| `src/TakEngine.Transport/Relay/PublishBudget.cs`, `src/TakEngine.Multiplayer/MultiplayerOptions.cs` | 1 | one budget per process |
| `src/TakEngine.Storage.Local/SqliteGameStore.cs`, `Schema.cs`; `tests/TakEngine.Storage.Tests/SqliteGameStoreTests.cs`, `Fixtures/v0.db` | 2 | `IGameStore` on SQLite, migration v0 to v2 |
| `src/TakEngine.Multiplayer/{GameCoordinator,MultiplayerGame}.cs`, `RebuildPlan.cs` | 3 | restart from store (exists); cold rebuild |
| `tests/TakEngine.Multiplayer.Tests/{RestartTests,TamperMatrixTests}.cs`; `tools/TakTestPeer/` | 4 | F-037 and F-038 over the fake relay; forging tool |
| `tools/TakRelay.Local/` | 5 | `InMemoryRelay` over WebSockets on localhost |
| `src/TakApp.Cli/` (`Program.cs` split into `Commands/`, `PlainOutput.cs`, `RichOutput.cs`), `tests/TakApp.Cli.Tests/` | 6 | F-036 head and `--plain` contract |
| `pipeline/evidence/` | 7 | live runs, transcripts, event log |

## Review Focus
1. **Transactions in SQLite.**
   - `CommitAppliedAsync` and `UpdateRecordAsync(record, outbox)` must be one transaction each.
   - A failure in the middle leaves nothing behind. Test it with a trigger that makes the second insert fail.
2. **The newer-version guard.** Opening a `user_version` 3 db must not run any write, including the migration.
3. **Cold rebuild honesty.** A rebuilt game whose hash differs from the opponent's next `prev_state_hash` shows a fork warning and stops. It never continues on a guess.
4. **CLI contract drift.** Every `--plain` line has a test, and the two-process test reads only those lines.
5. **Live budget.** Each live test counts what it publishes before it publishes. A test that would pass the 150-event cap fails instead.

### Task 1: F-014 and the F-034 live row; shared publish budget

**Files:** Transport `PublishBudget.cs` (unchanged API), Multiplayer `MultiplayerOptions.cs`, `GameCoordinator.cs`; tests `Live/LiveRelayTests.cs`, Testing `LiveRelayLog.cs`.

- [x] Red, fake relay: two `MultiplayerGame`s opened with the same `PublishBudget` never send more than 4 events in 10 s between them. A restart reusing the budget doesn't burst.
- [x] `MultiplayerOptions.Budget`, with a default of `new PublishBudget()`, is passed to `RelayPool` (new optional constructor argument).
- [x] `LiveRelayLog`:
  - Appends `| when | test | relay | event id | outcome |` rows to `pipeline/evidence/live-relay-log.md`.
  - Keeps a running total.
  - `Reserve(n)` throws when the total would pass 150.
- [x] LiveRelay tests (written now, run in Task 7):
  - `PublishOne_IsAcceptedByEachRelay_AndFetchableById`: one kind-3825 event whose plaintext content has `+ < > & "`, an emoji, DEL and U+0001. Its id comes from `Nip01Serializer`. It reads back OK true from each relay. A second `RelayPool`, with no shared state, fetches it with an `ids` filter and verifies the id and sig.
  - `CorruptedSig_IsRefusedInvalid_NotSent`: one event with a flipped sig bit ends `Failed`, with a per-relay `Refused` whose reason starts `invalid`.
  - `TwoRelaysTogether_PublishSubscribeDedupe` (F-034 live row): two events, each delivered once.
- [x] Commit `M0 F-014: live relay tests and one publish budget per process`.

### Task 2: F-037 (part 1): `SqliteGameStore`

**Files:** Storage.Local `SqliteGameStore.cs`, `Schema.cs`, csproj (+ Microsoft.Data.Sqlite); tests `SqliteGameStoreTests.cs`, `Fixtures/v0.db` with `Fixtures/make_v0_fixture.py`.

- [x] Commit the fixture: a v0 db built with the pre-M0 `CREATE TABLE` text copied from `Core/Storage/SqliteGameStorage.cs`, holding one legacy game and two moves.
- [x] Red tests:
  - The `FakeStoreTests` contract, run against SQLite too (one shared abstract test class, two subclasses).
  - Migrating v0 to v2 keeps the legacy rows byte for byte. It adds `Moves.SigScheme = 'legacy-ed25519'` and `Moves.EventId`, creates `NetGames`, `NetMoves`, `Outbox` and `Flags`, and sets `user_version = 2`.
  - Opening a v2 db again is a no-op.
  - A v3 db raises `GameStoreException("written by a newer version …")`, and its file bytes stay unchanged.
  - `CommitAppliedAsync` with a failing second statement (a test trigger) leaves no move, no progress change and no outbox row.
  - `UpdateRecordAsync(record, outbox)` behaves the same way.
  - Data survives closing and reopening the db file.
- [x] Implement it:
  - One `SqliteConnection` per call. `BEGIN IMMEDIATE`, so two processes on one file serialise.
  - Records are stored as columns, not JSON, except `Relays` (a JSON array with `"v"`).
- [x] Record D-044. Commit `M0 F-037: SqliteGameStore with v0 to v2 migration`.

### Task 3: F-037 (part 2): restart, cold rebuild, missing turn

**Files:** Multiplayer `GameCoordinator.cs`, `MultiplayerGame.cs`, `RebuildPlan.cs` (pure); tests `RestartTests.cs`, `RebuildPlanTests.cs`.

- [x] Red, fake relay, run with both stores (`InMemoryGameStore` and `SqliteGameStore` on a temp file):
  - Crash B right after its turn 4. A plays turn 5. Restart B from its store: B shows turn 5 and the game goes on to a road win with equal hashes.
  - Cold rebuild: `MultiplayerGame.RebuildAsync(options, inviteText)` for the guest, and for the host with its own invite, on a fresh store. It reads history with no `since` and recovers the nonce from its own JOIN or ACCEPT, then replays. The record's note is "rebuilt from relays", it ends at the opponent's hash, and the game plays on to the end.
  - A move missing from every relay, with later moves present, fails the rebuild with "move n not found on any relay" and stores nothing. It never skips.
  - A move that doesn't chain, or an illegal one signed by its owner, fails the rebuild with "fork: …". A stranger's move is not part of the game.
  - Changed from the first draft: no live "move n+1 not found" notice. With stored state the gap can't open in a two-player game (D-045); a wrong next `prev_state_hash` after a rebuild is tamper row 5 (flagged, board unchanged).
- [x] `RebuildPlan` is pure. It takes the decoded actions and the invite, and returns the record to save plus the actions in turn order, or a reason it can't.
- [x] Record D-045. Commit `M0 F-037: restart and cold rebuild from relay history`.

### Task 4: F-038: tamper matrix and the test-peer tool

**Files:** tests `TamperMatrixTests.cs`; `tools/TakTestPeer/` (new console project, in sln/slnx/Ci.slnf, references Testing).

- [x] Red: one test per scope.md row, over the fake relay, during a game in progress. For each row:
  - The board and hash are unchanged.
  - Rows 1-2 bump `DroppedEvents` and raise no flag.
  - Rows 3-7 raise `Flagged` with the scope.md reason, which persists across a restart (SQLite store).
  - A following valid move from the real opponent is accepted.
  - After 3 flags from the same peer, `Notice` suggests resigning, and nothing is forfeited.
- [x] Deliberate-break check: remove the coordinator's drop for one row and see its test go red; then revert. Note the result in the commit body.
- [x] `TakTestPeer forge --relays <csv> --invite <code> --as-key <nsec file> --ptn <move> [--turn <n>] [--kind illegal|fork|equivocate|undecryptable]`:
  - Rebuilds the game from the relays as the key's player (`RelayHistory` + `RebuildPlan`), so the forged move chains to the real hash; `--turn` defaults to the next one.
  - Builds the event with the same `EnvelopeCodec` and publishes it once.
  - Prints the event id.
  - Tested against the fake relay.
- [x] Commit `M0 F-038: tamper matrix over the fake relay and TakTestPeer`.

### Task 5: `tools/TakRelay.Local`

**Files:** `tools/TakRelay.Local/` (web project, in sln/slnx/Ci.slnf); tests in `tests/TakEngine.Transport.Tests/LocalRelayTests.cs`.

- [x] Red: a `RelayPool` using the real `ClientWebSocketRelaySocket` against the tool on a free localhost port publishes, subscribes and gets OK true. Two pools see each other's events. This stays in CI, because it uses only loopback.
- [x] `InMemoryRelay.ServeAsync(WebSocket)` feeds a `Link` from a real socket (BCL only, so Testing needs no ASP.NET). The tool serves one relay per name at `ws://127.0.0.1:port/name` (`--port`, `--relays r1,r2`, `--rate-limit-like-damus`, `--auth-for-reads r2`).
- [x] Commit `M0: TakRelay.Local serves the fake relay over WebSockets`.

### Task 6: F-036: CLI `host` / `join` / `resume`

**Files:** `src/TakApp.Cli/Online/` (`OnlineArgs`, `OnlineCommand` for host/join/resume, `PlainOutput`, `RichOutput`, `Profile`); `tests/TakApp.Cli.Tests/` (new, in sln/slnx/Ci.slnf). The legacy `SqliteGameStorage` moved to Storage.Local first, so Core no longer pulls SQLite into Blazor.

- [x] Red, `PlainOutputTests`: each line format in playbook.md:
  - `INVITE`, `RELAY`, `SENT`, `APPLIED`, `REJECTED`, `WAITING`, `FINAL`
  - plus `NOTICE <text>` and `FLAG turn <n> <reason>`, added to playbook.md in the same commit.
- [x] Red, `ProfileTests`:
  - `--profile` and `--data-dir` resolve to `LocalApplicationData/tak-p2p/<profile>`.
  - The first run creates the key, and later runs reuse it.
  - A corrupt key file prints its error and exits 2 without touching the file.
- [x] Red, `TwoProcessTests` (CI, loopback only):
  - Start TakRelay.Local, then `host --plain`, then `join <invite> --plain`.
  - Feed the scripted 5x5 road win on stdin.
  - Both processes print the same `FINAL` line.
  - Kill the joiner mid-game, rerun the same command, and `APPLIED` resumes.
- [x] Red, failure exits:
  - A bad invite prints the invite spec's message and exits 2 with no relay traffic.
  - An unreachable relay list prints `RELAY <url> backoff …` and keeps retrying until Ctrl-C.
  - A taken invite prints "game already taken" on the host side, and the guest keeps waiting with a status line. (The guest never learns why; scope.md accepts this.)
- [x] Remove the simulated join, so `grep -i "simulation\|Connecting\.\.\." src/TakApp.Cli` is empty. The local game still starts from the menu.
- [x] Rich (non-plain) mode shows the same events in Spectre.Console with no new screens beyond status lines (D-006).
- [x] A finished game waits (up to 15 s) until a relay has taken its last event before the process exits; the two-process test caught the winning move being lost when the host exited at once.
- [x] Commit `M0 F-036: CLI host, join and resume with the --plain contract`.

### Task 7: live runs (by hand, approved), evidence, close-out

- [ ] Run `Category=LiveRelay` for Task 1 (about 6 events). Log it.
- [ ] Two CLI processes on this machine over nos.lol and damus: host and join, a 10-move opening, equal hashes (F-016 live row, about 16 events).
- [ ] Same setup: kill and restart the joiner after its turn 4 (F-037 live, about 14 events).
- [ ] Same setup: one `TakTestPeer forge --kind illegal`. The CLI shows `FLAG` and play continues (F-038 live, about 12 events).
- [ ] Save transcripts under `pipeline/evidence/m0b-*.txt`. Update `features.json` evidence, set `passes` to true where every row is now seen running, and update `STATUS.md`.
- [ ] Final review (code-review, high) over the M0-B range. Fix the findings, run the four checks, push.
- [ ] Hand F-039 to the user: the exact two-machine commands, and what to send back.

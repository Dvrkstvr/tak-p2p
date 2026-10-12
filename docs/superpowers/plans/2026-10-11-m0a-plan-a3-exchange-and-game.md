# M0-A Plan A3: Turn Exchange, Invite Handshake and the Headless Game (F-035, F-017, F-016) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** End M0-A. Two `MultiplayerGame`s with independent keys and stores, joined by an invite with a fair commit-reveal colour seed (D-038) and agreed komi (D-037), play a scripted 5x5 game to a road win through the in-memory fake relay. Both end Completed with the same result and final hash, including under duplicates and reordering.

**Architecture:** `pipeline/architecture.md` "Modules" (`src/TakEngine.Multiplayer`), "Core-promise path", "Seams". Pure parts: `HostHandshake`, `GuestHandshake`, `TurnExchange`, `IncomingClassifier`, `LinkStatus`, `ColorSeed` (Transport, next to `ColorResolver`), `InviteCode` v2. Shell: `GameCoordinator`, with the `MultiplayerGame` facade that heads call. Seam: `IGameStore` (Abstractions) with `InMemoryGameStore` (Testing); `SqliteGameStore` is M0-B (F-037).

**Tech Stack:** as Plan A2.

**Spec:**
- `pipeline/features.json`: F-035, F-017 (amended 2026-10-11 for D-038 and komi), F-016.
- `pipeline/scope.md` "Interaction specs": invite handshake, turn exchange, reconnect, tamper rows 5-7.
- `pipeline/architecture.md` and `pipeline/decisions.md`: D-036, D-037, D-038, D-040, D-041.
- `.claude/rules/{multiplayer,transport,tests}.md`.

**Depends on:** Plan A2, including `SignedGameAction`, `ActionDigest`, `EnvelopeCodec`, `RelayPool`, `InMemoryRelay`, `FakeTimeProvider` and `TakGameSession.Restore`.

**Prototype note:** not prototyped. Each task names the tests (write them first, see them red) and the interfaces; the code is written test-first during execution.

## Global Constraints

Plan A1's and A2's constraints apply. In addition:
- **Pure state machines.** `HostHandshake`, `GuestHandshake`, `TurnExchange`, `IncomingClassifier` and `LinkStatus` take inputs (event, now, nonce) and return decisions (publish this, apply that, flag this, wait until). They never await, read a clock or call an RNG. `GameCoordinator` executes the decisions.
- **Ordering.** Ordering is by `turn`, never by `created_at`. The reorder buffer holds at most 64 entries; when full, the entry furthest ahead is dropped.
- **Duplicates and equivocation.** A duplicate is the same turn with the same `ActionDigest`. A different digest for an applied turn is equivocation: the first one seen wins and the other is flagged.
- **Persistence order.** Persist the signed event to the outbox before publishing, and republish it verbatim. Commit the applied action and `{appliedTurn, hash, publishedTurn, maxCreatedAt}` in one store call.
- **Nothing auto-forfeits.** A rejected action never changes the board or the hash.
- **Colours.** Colours come only from `ColorResolver.ResolveColors(ColorSeed.Derive(hostNonce, guestNonce), host, guest)`.

## File map

| Path | Task | Responsibility |
|---|---|---|
| `src/TakEngine.Transport/Matchmaking/ColorSeed.cs` | 1 | `Commit(hostNonce)`, `Verify(commit, hostNonce)`, `Derive(hostNonce, guestNonce)`: domain-separated SHA-256 (D-038) |
| `src/TakEngine.Transport/Matchmaking/InviteCode.cs` | 1 | v2 fields: game id, host x-only key, size 3..8, komi 0..20, relays, `seed_commit`, nick; strict validation; `TAK2_` prefix |
| `src/TakEngine.Abstractions/IGameStore.cs` + records `NetGameRecord`, `ExchangeProgress`, `OutboxEntry`, `FlagEntry` | 2 | storage seam (architecture.md "Data") |
| `tests/TakEngine.Testing/InMemoryGameStore.cs`, `InMemoryKeyStore.cs` | 2 | fakes that survive a coordinator dispose (a restart) and can fail the next write |
| `src/TakEngine.Multiplayer/` (new project; sln, slnx, Ci.slnf) | 3 | `IncomingClassifier`, `TurnExchange`, `LinkStatus` |
| `src/TakEngine.Multiplayer/Handshake/{HostHandshake,GuestHandshake}.cs` | 4 | invite state machines |
| `src/TakEngine.Multiplayer/{GameCoordinator,MultiplayerGame}.cs` | 5 | shell and facade |
| `tests/TakEngine.Multiplayer.Tests/` (new project) | 3-6 | unit and headless-game tests |

## Review Focus

1. **The commitment check runs before colours.** A guest that resolves colours from an unverified `host_nonce` lets the host choose the outcome after seeing the guest's nonce. Test `Accept_WithNonceNotMatchingCommit_IsDroppedAndFails`.
2. **The host locks to the first JOIN's nonce.** A retried JOIN from the same guest with a different nonce must get the original ACCEPT, not a re-derived one. Test `RepeatedJoin_WithNewNonce_GetsTheSameAccept`.
3. **Komi and size agreement.** An ACCEPT whose size or komi differs from the invite fails the guest. The session is built from the invite's values, never from the ACCEPT's.
4. **Equivocation versus a re-signed republish.** These have the same turn and the same digest but a different event id, so the second is a duplicate. With a different digest it is equivocation.
5. **A gap that never closes.** Turn n+1 is missing, so the exchange stays waiting. It never skips, and it asks for catch-up after 20 s of fake time.

### Task 1: F-017 (part 1) — fair colour seed and invite v2

**Files:** Transport `ColorSeed.cs`, `InviteCode.cs`; tests `ColorSeedTests.cs`, `InviteCodeTests.cs` (rewritten).

- [ ] Red tests:
  - `Derive_IsFixedByGoldenVector` (python-generated next to the json, like A2's digest vectors).
  - `Commit_VerifiesOnlyTheCommittedNonce`.
  - `Derive_DependsOnBothNonces`.
  - `Colours_ComeOutOpposite_ForAnyNonces` (seeded loop).
- [ ] Invite tests:
  - Round trip through the compact code, the web URL and the `tak://` URI. Each carries `seed_commit`, `komi` and `size`.
  - Each of these fails with a specific message: a missing commitment, a commitment that is not 64 hex characters, size 2 or 9, komi -1 or 21, a host key off the curve or not 32 bytes, a self-invite (checked by the guest with its own key), relays that aren't `wss://` (or `ws://127.0.0.1`/`localhost` for tools).
  - An old `TAK1_` code is refused with "this invite is from an older version" instead of being half-parsed.
- [ ] Implement, deleting the `seed` field. `ColorResolver` stays as is.
- [ ] Commit `M0 F-017: commit-reveal colour seed and invite v2 (D-038)`.

### Task 2: storage seam and fakes

**Files:** Abstractions `IGameStore.cs` and records; Testing `InMemoryGameStore.cs`, `InMemoryKeyStore.cs`; tests in `tests/TakEngine.Testing` smoke class.

- [ ] `IGameStore`:
  - `SaveNewGame(NetGameRecord)`
  - `UpdateHandshake(gameId, state, guestKey?, guestNonce?)`
  - `CommitApplied(gameId, SignedGameAction, eventId, tps, hash, ExchangeProgress)` (one call, atomic)
  - `AddOutbox(gameId, OutboxEntry)` and `MarkOutbox(...)`
  - `AddFlag(gameId, FlagEntry)`
  - `Load(gameId)` returns the record, the applied actions, the progress, the outbox and the flags.
  - `ListGames()`.
- [ ] Red tests for the fake:
  - The data survives `Dispose` of the coordinator that used it.
  - `FailNextWrite()` makes the next write throw and leaves nothing half-written.
- [ ] `NetGameRecord` carries:
  - role, my key (pubkey only), opponent key
  - game id, size, komi
  - host nonce (host only), guest nonce, seed commitment
  - relays, handshake state, result
- [ ] Commit `M0 F-035: IGameStore seam and in-memory fakes`.

### Task 3: F-035 — `IncomingClassifier`, `TurnExchange`, `LinkStatus`

**Files:** Multiplayer `IncomingClassifier.cs`, `TurnExchange.cs`, `LinkStatus.cs`; tests one class each plus `TurnExchangePropertyTests.cs`.

- [ ] Red tests for `TurnExchange`, following the scope.md table row by row:
  - Own echo is ignored, and `maxCreatedAt` is updated.
  - A stranger is dropped and counted.
  - t == n+1 is applied, and the buffer is drained.
  - t > n+1 is buffered, with a cap of 64.
  - When a gap has been open for 20 s, the exchange returns `RequestCatchUp`.
  - A duplicate (same turn and digest) is ignored silently.
  - Equivocation is flagged and the first one seen wins.
  - An event after Completed is ignored.
  - A MOVE for my own turn is a violation.
  - RESIGN is accepted when `k <= appliedTurn + 1`, and a later MOVE is ignored.
- [ ] Property tests, seeded:
  - Every permutation of a 6-ply game, with each event duplicated, ends at the same hash.
  - 200 random permutations of the scripted 5x5 game, with duplicates, end at the same hash. The seed is printed on failure.
- [ ] `IncomingClassifier`: rows 5-7 on top of the codec verdicts 1-4. It returns `Drop(count)` or `DropAndFlag(reason)`. Reason strings come from scope.md, e.g. "hash chain mismatch at turn t" and "illegal move from opponent: <engine reason>".
- [ ] `LinkStatus`:
  - Opponent link: Unknown, Active (a verified event in the last 10 min), Quiet.
  - Own link: Online, Degraded, Offline, from the per-relay states.
- [ ] Run Stryker on the three files (bar 80 %). Commit `M0 F-035: TurnExchange, IncomingClassifier, LinkStatus`.

### Task 4: F-017 (part 2) — host and guest handshakes

**Files:** Multiplayer `Handshake/HostHandshake.cs`, `GuestHandshake.cs`; tests one class each.

- [ ] Red tests for the host, from scope.md's host states:
  - `Idle -> Inviting` saves the record with the host nonce and komi. Its decision is to subscribe without `authors`.
  - A valid JOIN moves the host to `Locked(G)`, with the guest nonce stored. The host publishes ACCEPT with `{white, black, host_nonce, size, komi}` (D-041) and moves to InGame.
  - A second guest's JOIN is ignored and surfaced as "game already taken".
  - A repeated JOIN, even with a new nonce, gets the same ACCEPT.
  - A restart while Inviting or Locked loses nothing.
  - Cancel before Locked publishes nothing.
- [ ] Red tests for the guest:
  - Each invalid invite gives `Failed` with its message, and the fake relay frame counter stays at 0.
  - JOIN carries a fresh guest nonce, passed in by the caller. It is republished at 5, 15 and 45 s, then every 60 s (fake time).
  - An ACCEPT from a non-host key is dropped and flagged.
  - An ACCEPT whose nonce doesn't match the commitment fails the guest.
  - An ACCEPT whose size or komi differs from the invite fails the guest.
  - A colour mismatch fails the guest with "host and guest disagree on colours".
  - A good ACCEPT moves the guest to InGame, with the session built from the invite's size and komi.
- [ ] Commit `M0 F-017: host and guest handshakes`.

### Task 5: `GameCoordinator` and `MultiplayerGame`

**Files:** Multiplayer `GameCoordinator.cs`, `MultiplayerGame.cs`; tests `GameCoordinatorTests.cs`.

- [ ] `MultiplayerGame`, the API heads call:
  - `Host(size, komi)` returns the invite.
  - `Join(invite)`.
  - `Submit(move)` is refused locally when illegal, and then nothing is published.
  - `Resign()`.
  - Events: `ActionApplied`, `Flagged`, `LinkChanged`, `Ended`, `PublishStatus` (per relay).
- [ ] The coordinator runs the pure decisions against `RelayPool`, `IGameStore` and `TimeProvider`.
  - The order for a local move is: sign, then outbox, then publish.
  - The order for a remote action is: classify, then apply, then `CommitApplied`.
- [ ] Red tests over `InMemoryRelay` and `FakeTimeProvider`:
  - `IllegalLocalMove_PublishesNothing` (frame counter).
  - `MovesEnteredOffline_AreSentOnReconnect`.
  - `PublishThenCrashBeforeSave_RepublishesTheSameTurn_PeerIgnoresTheDuplicate`. `FailNextWrite` simulates the crash.
- [ ] Deliberate-break check: remove the outbox write and see the crash test fail, then revert. Record the result in the F-035 evidence.
- [ ] Commit `M0 F-035: GameCoordinator and MultiplayerGame`.

### Task 6: F-016 — the headless full game (end of M0-A)

**Files:** `tests/TakEngine.Multiplayer.Tests/HeadlessGameTests.cs`.

- [ ] Red tests: two `MultiplayerGame`s, two keys, two stores and one `InMemoryRelay` exposing two relay endpoints.
  - `ScriptedFiveByFive_RoadWin_EqualResultAndFinalHash`.
  - The same game with `DuplicateDeliveries(2)` and `ReorderLive(seed)`, with seeds as a Theory.
  - The same game on 7x7 with komi 5, ending on flats or a road. This proves F-058 through the network path.
  - `IllegalMove_RefusedLocally_NothingPublished`.
- [ ] Fill in `evidence` for F-035, F-017 and F-016: test names, Stryker scores, deliberate breaks and the CI run id. Update `pipeline/STATUS.md`: M0-A done, next M0-B.
- [ ] Run the A1-style final review for A2 and A3 (code-review high over the M0-A range).
- [ ] Commit `M0 F-016: headless 5x5 game to a road win over the fake relay`.

## Handed to M0-B (not in this plan)

- **F-037:**
  - `SqliteGameStore` with migration v0 to v2.
  - Moving `SqliteGameStorage` out of Core.
  - Fixing its `BoardSize IN (4, 5, 6)` CHECK to 3..8 inside the migration (storage rule: a numbered forward step and a fixture test).
  - Fixing `DateTime.Parse` without `RoundtripKind` (dates come back in local time and skew the stale timers).
- **F-036:**
  - CLI `host`/`join`, with `--size 3..8` and `--komi`.
  - The CLI loads `identity.json`, which closes F-031.
  - A shared `SecretKey.Generate` shell helper replaces the seven inline RNG lambdas.
- **F-038:** the tamper matrix over the fake relay, plus `tools/TakTestPeer`.
- **F-014 and the LiveRelay runs:** within the 150-event budget (D-032).

## Spec coverage

| Acceptance (features.json) | Proven in |
|---|---|
| F-017 invite with commitment, size and komi; colours from the derived seed; White's first move on both | Tasks 1, 4, 6 |
| F-017 ACCEPT nonce, size or komi mismatch fails | Task 4 |
| F-017 repeated JOIN, second guest, non-host ACCEPT, malformed invite with no traffic, host restart | Task 4 |
| F-035 any order with duplicates gives the same hash | Task 3 property tests, Task 6 |
| F-035 buffer, then a 20 s gap triggers catch-up | Task 3 |
| F-035 duplicate ignored, equivocation flagged | Task 3 |
| F-035 late events ignored | Task 3 |
| F-035 publish-before-save restart | Task 5 |
| F-035 own echo ignored; offline moves sent on reconnect | Tasks 3, 5 |
| F-016 scripted 5x5 road win with equal hash; duplicates and reordering | Task 6 |
| F-016 illegal move refused locally, nothing published | Tasks 5, 6 |
| F-016 LiveRelay | M0-B |

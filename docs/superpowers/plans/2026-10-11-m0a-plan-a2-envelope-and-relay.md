# M0-A Plan A2: Signed Envelope and Relay Client (F-033, F-034) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every game action is signed over a pinned `ActionDigest`, wrapped by one `EnvelopeCodec` into a NIP-44 v2, BIP-340-signed kind-3825 event, and carried by a relay client (`RelayPool`) that publishes with per-relay status, subscribes with the two-filter REQ, reconnects with backoff and dedupes, all proven against an in-memory fake relay. The legacy `NostrTransportClient`, `NostrRelayConnection` and `TransportEnvelope` are deleted.

**Architecture:** `pipeline/architecture.md` "Modules", "Wire format", "Seams". Pure parts: `ActionDigest` (Core), `SignedGameAction` (Abstractions), `EnvelopeCodec`, `RelayLink`, `PublishTracker`, `PublishBudget`, `SubscriptionPlan`, `RelayMessage` parse/format (Transport). Shells: `IRelaySocket` + `ClientWebSocketRelaySocket`, `RelayConnection`, `RelayPool` (Transport). Fakes: new `tests/TakEngine.Testing` with `InMemoryRelay` + `FakeRelaySocketFactory`.

**Tech Stack:** as Plan A1, plus `Microsoft.Extensions.TimeProvider.Testing` (pin the newest 10.x that restores; the playbook saw 10.10.0 listed on 2026-10-02).

**Spec:** `pipeline/features.json` F-033, F-034; `pipeline/scope.md` "Interaction specs" (Reconnect and catch-up, Tamper rows 1-4); `pipeline/architecture.md`; `docs/decisions/0003`, `0004`, `0006`; `.claude/rules/{transport,core-engine,tests,ci}.md`.

**Prototype note:** unlike A1, the code in this plan was not prototyped before writing. Each task names the tests (write them first, see them red) and the interfaces; the code is written test-first during execution. Where a detail turns out wrong, fix the plan in the same commit.

## Global Constraints

Everything in Plan A1's "Global Constraints" still applies (branch, never `main`, the four checks, warnings as errors, browser-wasm-safe engine, purity, two independent keypairs, no LiveRelay in this plan, commit style, `features.json` evidence only). In addition:
- Wire format exactly as `pipeline/architecture.md` "Wire format": kind 3825, tags exactly `["p", recipient]` and `["g", game id lowercase]`, content NIP-44 v2, plaintext `pv: 1`. Changing it needs a new `pv` and a decision record.
- Inner JSON uses System.Text.Json with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`; event ids only via `Nip01Serializer`.
- Decode order is the tamper rows: (1) id + sig, (2) author, (3) decrypt, (4) JSON + `pv` + game id + inner signature + `player_pubkey == event.pubkey`. The codec returns a verdict per row; it never throws on peer input.
- "Sent" = at least one relay answered `OK true`. Never more than 4 published events per 10 s per client (`PublishBudget`). `ids` filters chunked to <= 5.
- Time comes from `TimeProvider` in shells only; pure parts take `DateTimeOffset now` / `long createdAt` as arguments. Nonces and BIP-340 aux bytes come from the caller.

## Decisions this plan records (append to `pipeline/decisions.md` in Task 1)

- **D-040 (assumed): one turn index everywhere.** The hash chain's `TurnIndex`, the envelope `turn` and the storage `TurnIndex` are the 1-based ply number of the action (White's first placement = 1, Black's = 2, ...). Today `TakGameSession` hashes the post-move full-move counter, `SpectatorGameSession` the envelope's pre-move index and the CLI a ply index (2026-10-11 audit), so a spectator cannot verify a player's chain. No multiplayer game has ever been stored, so this is the moment to fix it. The StateHasher golden vector is regenerated once and pinned again.
- **D-041 (assumed): canonical `action_data`.** `canonical(action_data)` in `ActionDigest` = compact JSON, keys sorted ordinally, relaxed escaping, integers without leading zeros, no whitespace. JOIN `{"guest_nonce": <64 hex>}`, ACCEPT `{"black": <hex>, "host_nonce": <64 hex>, "komi": <int half flats>, "size": <int>, "white": <hex>}` (D-037, D-038), MOVE `{"ptn": <canonical PTN>}`, RESIGN `{}`.

## File map

| Path | Task | Responsibility |
|---|---|---|
| `src/TakEngine.Abstractions/Models/SignedGameAction.cs` | 1 | record: Pv, ActionType, GameId, Turn, PlayerPubKey, PrevStateHash, ActionData (sorted string map), Signature |
| `src/TakEngine.Core/Cryptography/ActionDigest.cs` | 1 | the 32-byte digest a player signs; `Sign`/`Verify` helpers |
| `src/TakEngine.Core/Session/TakGameSession.cs` | 2 | emits `SignedGameAction` (replaces the bare signature event), verifies it on receive, ply turn index (D-040) |
| `src/TakEngine.Core/Session/SpectatorGameSession.cs`, `src/TakApp.Cli/Program.cs` | 2 | follow D-040 so all three agree |
| `src/TakEngine.Core/Cryptography/PayloadSignature.cs` | 2 | deleted once nothing uses `prevHash:ptn` signing |
| `src/TakEngine.Transport/Envelope/EnvelopeCodec.cs`, `DecodeVerdict.cs` | 3 | SignedGameAction <-> JSON <-> NIP-44 <-> kind-3825 event; verdict rows 1-4 |
| `src/TakEngine.Transport/Nostr/RelayMessage.cs` | 4 | parse/format EVENT, REQ, CLOSE, OK, EOSE, NOTICE, CLOSED, AUTH (replaces `NostrMessageParser`) |
| `tests/TakEngine.Testing/` (new project) | 4 | `InMemoryRelay`, `FakeRelaySocketFactory`, fault knobs, scripted 5x5 game |
| `src/TakEngine.Transport/Relay/{RelayLink,PublishTracker,PublishBudget,SubscriptionPlan}.cs` | 5 | pure relay logic |
| `src/TakEngine.Transport/Relay/{IRelaySocket,ClientWebSocketRelaySocket,RelayConnection,RelayPool}.cs` | 6 | shells |
| `src/TakEngine.Transport/Nostr/{NostrTransportClient,NostrRelayConnection}.cs`, `TransportEnvelope.cs` | 7 | deleted; callers rewired |
| `src/TakEngine.Transport/Nostr/NostrProfile.cs`, `Matchmaking/QuickPlayMatchmaker.cs` | 7 | events built with `NostrEvents.Sign` (A1 review finding) |

## Review Focus

1. **Digest drift.** Any change to field order, separators, number formatting or the canonical `action_data` forks every game. A golden vector (fixed key, aux, game id, every action type) pins `ActionDigest`, and another pins the hash sequence of a fixed 5x5 game under D-040.
2. **Decode order.** Decrypting before verifying id + sig, or attributing a row-1/row-2 event to the opponent, is a bug even when the end result looks the same. Tests assert the verdict *row*, not just "rejected".
3. **Relay echoes and fan-out.** The same event arrives from two relays and from our own subscription; `RelayPool` dedupes by event id before anything reaches the caller.
4. **Rate limits.** damus refuses the 6th event in a burst; the fake relay reproduces it, and `PublishBudget` keeps us under 4 per 10 s including retries.
5. **Backoff on a fake clock.** Reconnect timing is asserted with `FakeTimeProvider`, never with real sleeps.

### Task 1: F-033 (part 1) — `SignedGameAction` and the pinned `ActionDigest`

**Files:** Abstractions `SignedGameAction.cs`; Core `Cryptography/ActionDigest.cs`; tests `tests/TakEngine.Core.Tests/ActionDigestTests.cs` + `Vectors/action-digest-vectors.json`; `pipeline/decisions.md` (D-040, D-041).

- [ ] Write `ActionDigestTests` red:
  - `Digest_MatchesGoldenVector(actionType)` for JOIN, ACCEPT, MOVE, RESIGN. Vectors generated by a short Python script next to the json (`make_action_digest_vectors.py`, hashlib + json with sorted keys) so the C# side is checked by an independent implementation.
  - `CanonicalActionData_SortsKeys_AndUsesRelaxedEscaping` (`+ < > &` and a quote in a PTN-like value).
  - `SignAndVerify_TwoIndependentKeys`: Alice signs, Bob's code verifies with Alice's pubkey; Bob's key fails.
  - `AnyFieldChange_BreaksVerification` (Theory over pv, type, game id, turn, pubkey, prev hash, each action_data value).
- [ ] Implement `ActionDigest.Compute(SignedGameAction)` exactly per architecture.md, `ActionDigest.Sign(SecretKey, action, aux32)` and `ActionDigest.Verify(action)` (never throws on malformed hex).
- [ ] Record D-040 and D-041. Four checks green. Commit `M0 F-033: ActionDigest with golden vectors`.

### Task 2: F-033 (part 2) — session emits and verifies signed actions; one turn index

**Files:** `TakGameSession.cs`, `ITakGameSession.cs`, `StateHasher.cs` (genesis unchanged; turn input = ply), `SpectatorGameSession.cs`, CLI `Program.cs`; tests `TakGameSessionTests.cs`, `HashChainGoldenTests.cs` (new), `SpectatorTests.cs`.

- [ ] Red tests:
  - `HashChain_FixedGame_MatchesGoldenSequence`: scripted 5x5 road-win game, fixed keys and aux, every `CurrentStateHash` equals the pinned list (D-040).
  - `RemoteGame_TwoIndependentKeys_PlaysToRoadWin_WithEqualFinalHash` using `SignedGameAction` both ways (replaces `RemoteP2P_SynchronizesMovesAndHashChain`).
  - Failure rows (each: failure result, `OnProtocolViolationDetected`, board and hash unchanged): wrong author, bad signature, wrong prev hash, wrong turn number, wrong turn owner, malformed PTN, non-canonical PTN, illegal move.
  - `FirstTurnSwap_RoundTrips` over the remote path.
  - `Spectator_VerifiesAPlayersChain`: feed the player's emitted actions to a `SpectatorGameSession`; same hashes.
- [ ] `TakGameSession`: replace `event Action<string> OnRemoteEnvelopeReady` with `event Action<SignedGameAction> OnLocalActionSigned`; `ProcessRemoteAction(SignedGameAction)` replaces `ProcessRemoteMove(...)`; `ProcessRemoteMove` is deleted. The session needs the BIP-340 aux bytes per action: take a `Func<byte[]> aux32` at construction (shell passes the RNG; tests pass a seeded one). Keep the canonical-PTN check from F-057. Session restore from `(tps, hash, appliedTurn, komi)` for F-037 is added here as a factory `TakGameSession.Restore(...)` with a test that a restored session continues the same chain.
- [ ] Spectator and CLI switch to the ply index and to `ActionDigest`; delete `PayloadSignature` and its tests when unused.
- [ ] Four checks green. Commit `M0 F-033: sessions sign and verify SignedGameAction; one turn index (D-040)`.

### Task 3: F-033 (part 3) — `EnvelopeCodec` with per-row verdicts

**Files:** Transport `Envelope/EnvelopeCodec.cs`, `Envelope/DecodeVerdict.cs`; tests `EnvelopeCodecTests.cs`.

- [ ] Red tests (Alice and Bob independent keys; seeded nonces and aux):
  - `Encode_ProducesKind3825_WithExactlyPAndGTags_AndVerifies`.
  - `RoundTrip_EveryActionType` (Bob decodes what Alice encoded; never Alice decoding her own).
  - Verdict rows: `BadIdOrSig -> Row1`, `StrangerAuthor -> Row2` (decoder told the expected opponent), `JoinFromAnyNonSelfAuthorWhileInviting -> accepted`, `Undecryptable (flipped MAC, wrong conversation key, bad padding) -> Row3`, `GameIdMismatch | BadInnerSignature | PlayerPubkeyNotEventAuthor | BadJson -> Row4`, `PvNot1 -> IncompatibleVersion` (distinct from tamper).
  - `DecodeNeverDecryptsBeforeVerifying`: a decoder whose NIP-44 call is a spy sees no call for a Row-1 event (this closes F-032's "never decrypted").
  - `Plaintext_IsRelaxedEscaped` (PTN `+`/`<`/`>` not `+`).
- [ ] Implement `EnvelopeCodec.Encode(SecretKey sender, PublicKey recipient, SignedGameAction, long createdAt, byte[] nonce32, byte[] aux32) -> NostrEvent` and `EnvelopeCodec.Decode(NostrEvent, SecretKey me, DecodeContext ctx) -> DecodeResult` (verdict + action when accepted). Pure.
- [ ] Four checks green; run Stryker on `EnvelopeCodec.cs` + `ActionDigest.cs` (bar 80 %, survivors killed or listed). Commit `M0 F-033: EnvelopeCodec with tamper-row verdicts`.

### Task 4: F-034 (part 1) — relay messages and the in-memory fake relay

**Files:** Transport `Nostr/RelayMessage.cs`; new project `tests/TakEngine.Testing` (add to `TakGame.sln`, `.slnx`, `.Ci.slnf` in this commit) with `InMemoryRelay.cs`, `FakeRelaySocketFactory.cs`, `ScriptedGames.cs`; tests `tests/TakEngine.Transport.Tests/RelayMessageTests.cs`, `tests/TakEngine.Testing.Tests` or a smoke-test class inside Transport.Tests.

- [ ] Red tests for `RelayMessage`: parse and format EVENT, REQ (multi-filter, `#p`, `#g`, `authors`, `kinds`, `since`, `limit`, `ids`), CLOSE, OK (true/false + reason prefix such as `rate-limited:`), EOSE, NOTICE, CLOSED (`auth-required:`), AUTH; junk frames return a `RelayMessage.Unknown` without throwing.
- [ ] Red smoke tests for `InMemoryRelay`: stores valid events and answers `OK true`; invalid id/sig -> `OK false invalid:`; REQ returns history newest-first then EOSE, then live events; filters per NIP-01; CLOSE stops delivery. Knobs, each with a test: `Refuse(predicate, reason)`, `RateLimitLikeDamus()` (6th event in a burst refused), `RejectUpgrade(503)`, `DropConnections()`, `ReorderLive(seed)`, `DuplicateDeliveries(n)`, `Withhold(eventId)`, `AuthRequiredForReads()`, `Partition()`; all deterministic from a seed. Frame counter (used by F-017's "no network traffic" check).
- [ ] `ScriptedGames.FiveByFiveRoadWin` (PTN list both peers replay) for F-016.
- [ ] Delete `NostrMessageParser`/`NostrMessageTests` parts superseded by `RelayMessage` (keep coverage). Four checks green. Commit `M0 F-034: relay messages and InMemoryRelay`.

### Task 5: F-034 (part 2) — pure relay logic

**Files:** Transport `Relay/RelayLink.cs`, `PublishTracker.cs`, `PublishBudget.cs`, `SubscriptionPlan.cs`; tests one class each.

- [ ] `RelayLink` (per relay: Connecting -> Subscribed -> CaughtUp -> Disconnected -> Backoff): red tests for the state table in scope.md "Reconnect and catch-up", backoff 1, 2, 4, 8, 16, 30, 30 s, reset after a successful subscribe, 503 counts as a retry, `CLOSED auth-required` -> `ReadImpossible` (stays connected for writes).
- [ ] `PublishTracker`: per event id, per relay `Pending | Sent | Refused(reason)`; overall `Sent` when any relay says OK true, `Failed` when all refused, `Pending` otherwise; `rate-limited` -> retry due at a backoff time.
- [ ] `PublishBudget`: `TryTake(now)` allows at most 4 per sliding 10 s; retries count.
- [ ] `SubscriptionPlan`: builds the two-filter REQ from architecture.md (sub id `tak-<first 8 of gid>`, `since = maxCreatedAt - 30 s` or absent on cold rebuild, `limit 500`, host-while-Inviting drops `authors`); `ids` lookups chunked to 5.
- [ ] Stryker on the four files (bar 80 %). Commit `M0 F-034: pure relay logic`.

### Task 6: F-034 (part 3) — sockets, `RelayConnection`, `RelayPool`

**Files:** Transport `Relay/IRelaySocket.cs`, `ClientWebSocketRelaySocket.cs`, `RelayConnection.cs`, `RelayPool.cs`; tests `RelayPoolTests.cs` over `FakeRelaySocketFactory` + `FakeTimeProvider`.

- [ ] Red tests (two fake relays, `FakeTimeProvider`):
  - `Publish_IsSent_WhenAnyRelaySaysOk`; per-relay status surfaced.
  - `RateLimited_IsRefused_RetriedWithBackoff_AndNeverExceedsTheBudget` (count frames per 10 s of fake time).
  - `DroppedSocket_ReconnectsWithBackoff_AndResubscribesWithSince`; `Upgrade503_IsRetried`.
  - `AuthRequired_MarksOneRelayReadImpossible_TheOtherCarriesOn`.
  - `SameEventFromTwoRelays_IsDeliveredOnce`; `OwnEcho_IsDelivered_FlaggedAsOwn` (the exchange decides what to do with it).
  - `NoEventDeliveredTwice_AcrossReconnects`.
- [ ] `RelayConnection` (one socket, one send loop so frames never interleave: A1 review finding), `RelayPool` (fan-out, dedupe by event id with a bounded LRU, events surfaced as `IAsyncEnumerable`/callback with the relay url). `ClientWebSocketRelaySocket` sets no `Options`.
- [ ] Deliberate-break check for the shells: remove the `since` resubscribe and the dedupe, see the named tests fail, revert; record in F-034 evidence.
- [ ] Commit `M0 F-034: RelayPool over IRelaySocket with reconnect and dedupe`.

### Task 7: delete the legacy client; sign the remaining event builders

**Files:** delete `NostrTransportClient.cs`, `NostrRelayConnection.cs`, `TransportEnvelope.cs`, `NostrTransportClientReceiveTests.cs`; edit `NostrProfile.cs`, `QuickPlayMatchmaker.cs`, Blazor `ProfileModal.razor`.

- [ ] `NostrProfile.CreateMetadataEvent(SecretKey, profile, createdAt, aux32)` and `QuickPlayMatchmaker.CreateBroadcastEvent(SecretKey, ...)` build events with `NostrEvents.Sign`; tests assert `NostrEvents.Verify` is true (red first). Quick Play stays unwired until M4.
- [ ] Blazor `ProfileModal` stops publishing (it used `NostrTransportClient`): the profile saves locally, matching main's hotfix (D-039). No other UI change (D-006). Profile publishing returns with the browser relay work at M2.
- [ ] `git grep -n "NostrTransportClient\|TransportEnvelope\|NostrRelayConnection" -- src tests` is empty. Four checks green.
- [ ] Fill `evidence` for F-033 and F-034 (test names, Stryker scores, deliberate breaks, CI run id). Commit `M0 F-033 F-034: delete legacy Nostr client; sign profile and quick-play events`.

## Spec coverage

| Acceptance (features.json) | Proven in |
|---|---|
| F-033 two sessions, independent keys, road win, equal hash | Task 2 `RemoteGame_TwoIndependentKeys_PlaysToRoadWin_WithEqualFinalHash` |
| F-033 failure rows | Task 2 failure-row tests; Task 3 codec rows 1-4 |
| F-033 golden hash sequence | Task 2 `HashChain_FixedGame_MatchesGoldenSequence`; Task 1 digest vectors |
| F-033 first-turn swap round trip | Task 2 `FirstTurnSwap_RoundTrips` |
| F-034 Sent when any relay OK | Task 6 |
| F-034 rate-limited, backoff, budget | Tasks 5 and 6 |
| F-034 drop / 503, reconnect, `since`, no double apply | Tasks 5 and 6 |
| F-034 CLOSED auth-required | Tasks 5 and 6 |
| F-034 LiveRelay | not in this plan (M0-B, after the budget check in D-032) |
| F-032 "never decrypted" | Task 3 `DecodeNeverDecryptsBeforeVerifying` |

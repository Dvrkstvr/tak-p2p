# Architecture — Tak P2P (M0 and the shape later milestones build on)

<!-- Stage 6, 2026-10-02, architect. Evidence labels: seen in code / seen running / documented / inferred.
     Decisions referenced as D-### live in pipeline/decisions.md; long rationale in docs/decisions/NNNN-*.md. -->

## Principles that shape every module
- **Pure = deterministic, no I/O, no clock, no randomness.** Values in, answers out. Pure modules may hold in-memory
  state (`TakGameSession` and `TurnExchange` are state machines) but never await, read the clock or call an RNG:
  time, nonces, aux randomness, seeds and game ids are passed in. The shell (`GameCoordinator`, `RelayConnection`,
  heads) is the only code that awaits, reads `TimeProvider` or `RandomNumberGenerator`.
- **One implementation per concept.** One NIP-01 serializer, one envelope codec, one colour resolver
  (`ColorResolver`), one turn-exchange state machine shared by every head (CLI now, Blazor at M2, desktop at M6).
  The Blazor duplicate board/road code (`WebGameSessionManager`) is deleted at M1 (D-008), not extended.
- **browser-wasm-safe engine.** Every M0 library targets `net10.0` and uses only APIs available in browser-wasm:
  no threads you block on, no `Socket`, no file system, no SQLite. WebSockets only through `IRelaySocket`; the real
  implementation wraps `System.Net.WebSockets.ClientWebSocket` and sets no `Options` (several are not supported in
  the browser: inferred; F-042 proves the browser path at M1). Crypto is proven in browser-wasm (spike R-002/R-003, seen running).
  SQLite and the key file live in a separate local-storage project that the browser never references.

## Modules
New projects are marked (new). "Owner" = the M0 feature that creates or rewrites it. Every project is added to
`TakGame.sln`, `TakGame.slnx` and `TakGame.Ci.slnf` in the same commit (the CI filter builds only what it lists).

| Module | Its one job | Pure? | Tested by | Owner |
|---|---|---|---|---|
| `src/TakEngine.Abstractions` | Contracts and immutable records shared by all layers, plus the seam interfaces `IKeyStore`, `IGameStore` and their record types (`SignedGameAction`, `NetGameRecord`, `ExchangeProgress`, `OutboxEntry`, `FlagEntry`). No logic. | yes (types only) | compiler | F-033, F-035 |
| `src/TakEngine.Crypto` (new) | secp256k1 identity: `SecretKey`/`PublicKey` (x-only, validated: 32 bytes, on curve, scalar in range), BIP-340 sign/verify of 32-byte digests, NIP-44 v2 (`Nip44.ConversationKey`, `Encrypt(…, nonce)`, `Decrypt`), NIP-19 npub/nsec (moved from `Core/Cryptography/Nip19.cs`). NBitcoin.Secp256k1 + BouncyCastle ChaCha20 + BCL HMAC/SHA256. | yes | `tests/TakEngine.Crypto.Tests` (official vectors, two-peer) | F-031, F-015 |
| `src/TakEngine.Core` | Tak rules (board, moves, roads, PTN/TPS), `StateHasher` (chain formula unchanged), `ActionDigest` (new: the canonical 32-byte digest a player signs for one game action), `TakGameSession` (validates, applies, signs local actions with the player's `SecretKey`, verifies remote ones, extends the hash chain, can be restored from TPS + hash + turn), AI bot, spectator engine. | yes | `tests/TakEngine.Core.Tests` | F-033 (session), F-001..F-006 adopted |
| `src/TakEngine.Transport` | Nostr wire: `Nip01Serializer` (hand-written id serializer), `NostrEvents.Sign/Verify` (id + BIP-340), relay message parse/format, `EnvelopeCodec` (SignedGameAction <-> JSON <-> NIP-44 <-> kind-3825 event, and the decode verdict for tamper rows 1-4), `InviteCode` (seed mandatory), `ColorResolver`, relay state logic `RelayLink` (per-relay state + backoff), `PublishTracker` (per-relay OK -> Sent/Pending/Failed), `PublishBudget` (<= 4 events / 10 s), `SubscriptionPlan` (the two-filter REQ); and the I/O shells `IRelaySocket` + `ClientWebSocketRelaySocket`, `RelayConnection` (one socket), `RelayPool` (fan-out, dedupe by event id). | logic yes; `RelayConnection`/`RelayPool`/socket no | `tests/TakEngine.Transport.Tests` + fake relay; `Category=LiveRelay` by hand | F-032, F-034, F-014, F-017 (invite) |
| `src/TakEngine.Multiplayer` (new) | Head-agnostic game-over-relays: `HostHandshake` / `GuestHandshake` (invite state machines), `TurnExchange` (ordering, reorder buffer, dedupe, equivocation, late events, outbox decisions), `IncomingClassifier` (the ordered 7-row tamper classification), `LinkStatus` (opponent Unknown/Active/Quiet, own Online/Degraded/Offline); shell `GameCoordinator` (wires `RelayPool` + `IGameStore` + `TimeProvider` to the pure parts; restart and catch-up) and the `MultiplayerGame` facade heads call (Host, Join, Submit, Resign, events). | yes except `GameCoordinator` | `tests/TakEngine.Multiplayer.Tests` (fake relay, headless games) | F-035, F-017, F-016, F-037, F-038 |
| `src/TakEngine.Storage.Local` (new) | Local disk for CLI and desktop: `SqliteGameStore : IGameStore` (schema v2 + forward migrations), `FileKeyStore : IKeyStore` (`identity.json`), and the existing `SqliteGameStorage` moved out of Core so Core stops pulling SQLite into the browser build. | no | `tests/TakEngine.Storage.Tests` (migration from a v1 fixture db, key-file errors) | F-031 (key file), F-037 (store) |
| `src/TakApp.Cli` | Terminal head: menu (local, AI) plus `host` / `join <invite>` commands, `--profile`/`--data-dir`, `--relays`, `--plain` machine-readable output. Renders `MultiplayerGame` events; no rules or protocol logic. | no | two-process runs (verify method), `tests/TakApp.Cli.Tests` for argument parsing and the plain-output contract | F-036 |
| `tools/TakRelay.Local` (new) | Serves the in-memory fake relay over real WebSockets on localhost, so two CLI processes can play without public relays (agent verify, F-036). Same relay core as the tests. | no | used by the verify method | F-036 |
| `tools/TakTestPeer` (new) | Test peer that publishes a correctly signed but illegal or forged action for a given game (live tamper case F-038, F-039). Never used by the app. | no | by hand | F-038 |
| `tests/TakEngine.Testing` (new) | Shared fakes: `InMemoryRelay` (+ `FakeRelaySocketFactory`), `InMemoryGameStore`, `InMemoryKeyStore`, `FakeTimeProvider` (package), scripted 5x5 road-win games, vector loaders. Referenced only by tests and `tools/`. | n/a | its own smoke tests | F-034 |
| `src/TakApp.Blazor`, `src/TakApp.Avalonia*` | Views. M0 touches them only where deleting `CryptoSigner` breaks compilation (Blazor `BrowserStorage`, Avalonia `MainViewModel`), with no UI change (D-006). | no | build only in M0 | F-031 (compile fix) |

Project references (arrows = "references"): Abstractions <- Crypto <- Core; Abstractions + Crypto <- Transport;
Core + Transport <- Multiplayer <- heads; Storage.Local -> Abstractions + Crypto (+ Core for the moved `SqliteGameStorage`).
Transport does not reference Core and Core does not reference Transport (seen in code today; kept).

### M0 feature -> modules
| Feature | Modules |
|---|---|
| F-029 CI gate | `.github/workflows/ci.yml`, `deploy-gh-pages.yml`, `TakGame.Ci.slnf`, `Directory.Build.props` (all set up at stage 6; F-029 proves red/green on GitHub) |
| F-031 identity | Crypto (`SecretKey`, `PublicKey`, Nip19), Abstractions (`IKeyStore`), Storage.Local (`FileKeyStore`), compile fixes in Core/Blazor/Avalonia/CLI |
| F-032 NIP-01 id + BIP-340 | Transport (`Nip01Serializer`, `NostrEvents`), Crypto (Schnorr) |
| F-015 NIP-44 two-peer | Crypto (`Nip44`) |
| F-033 signed envelope + chain | Core (`ActionDigest`, `TakGameSession`, `StateHasher`), Abstractions (`SignedGameAction`), Transport (`EnvelopeCodec`) |
| F-034 relay client | Transport (`RelayLink`, `PublishTracker`, `PublishBudget`, `SubscriptionPlan`, `RelayConnection`, `RelayPool`, `IRelaySocket`), Testing (`InMemoryRelay`) |
| F-035 turn exchange | Multiplayer (`TurnExchange`, `IncomingClassifier`, `GameCoordinator`), Abstractions (`IGameStore`) |
| F-017 invite handshake | Multiplayer (`HostHandshake`, `GuestHandshake`), Transport (`InviteCode`, `ColorResolver`, `EnvelopeCodec`) |
| F-016 headless full game | Multiplayer.Tests over Testing (`InMemoryRelay`) through every module above |
| F-014 live acceptance | Transport (`RelayPool`, `NostrEvents`) in a `Category=LiveRelay` test |
| F-036 CLI host/join | TakApp.Cli, `MultiplayerGame`, Storage.Local, `tools/TakRelay.Local` |
| F-037 restart + catch-up | Multiplayer (`GameCoordinator`, `TurnExchange`), Storage.Local (`SqliteGameStore`), Core (session restore from TPS) |
| F-038 tamper matrix | Multiplayer (`IncomingClassifier`, flag log), Transport (`EnvelopeCodec` verdicts), `tools/TakTestPeer` |
| F-039 exit run | the CLI over nos.lol + damus on two machines (owed to the user if no second machine) |
| adopted F-001/2/3/5 | Core rules, PTN/TPS, local session: unchanged |
| adopted F-006 | Core session remote checks: rewritten for secp256k1 under F-033 (evidence re-earned) |
| adopted F-010 | Transport `InviteCode` (host key = 32-byte x-only, seed mandatory) + Crypto Nip19 |
| adopted F-012 | Transport message parse kept; `ComputeId` replaced by `Nip01Serializer` (F-032) |
| adopted F-027 | Pages deploy, now behind `needs: test` |

### What gets deleted, and when (test-first: the replacing test is red first)
| Delete | In step | Replaced by |
|---|---|---|
| `src/TakEngine.Core/Cryptography/CryptoSigner.cs` (Ed25519) and `CryptoTests.KeyPair_GeneratesValidEd25519_AndSignsVerifies` | F-031 (M0 step 2) | Crypto `SecretKey` + Schnorr; callers rewired: `TakGameSession`, `SpectatorGameSession`, CLI `Program.cs`, Avalonia `MainViewModel`, Blazor `BrowserStorage` |
| `src/TakEngine.Transport/Nostr/Nip44Encryption.cs` (SHA256 "shared secret"), `tests/TakEngine.Transport.Tests/Nip44EncryptionTests.cs` (both tests; `Nip44_EncryptAndDecrypt_RoundTripsSuccessfully` passes for the wrong reason) | F-015 (M0 step 2) | Crypto `Nip44` + vector and two-peer tests |
| `TransportBenchmarkTests.RoundTripPayloadProcessing_CompletesWellUnder300ms` (decrypts with the sender's own secret) | F-015 | a two-peer timing test in Crypto.Tests |
| `NostrEvent.ComputeId` (STJ-based) | F-032 | `Nip01Serializer` |
| `TransportEnvelope` (+ its `SerializerOptions`) | F-033 | `EnvelopeCodec` (keeps relaxed escaping for the inner JSON) |
| `NostrTransportClient`, `NostrRelayConnection` (no callers outside tests: seen in code) | F-034 | `RelayPool`, `RelayConnection` |
| `src/TakEngine.Core/Cryptography/Nip19.cs` | F-031 | moved to Crypto |
| CLI simulated Join / Quick Play text | F-036 | real `host`/`join`; Quick Play menu item says "not available yet" until M4 |

## Wire format (behaviour level; full rationale docs/decisions/0003, 0004)
**Event kind 3825**, a regular event (NIP-01: 1000 <= n < 10000 is stored by relays, documented in nips/01.md).
Not in the NIPs README kind table (fetched 2026-10-02, commit 6631b3e) nor in nostr-protocol/registry-of-kinds
`schema.yaml` (fetched 2026-10-02, commit 5cf2b84); clear of the DVM ranges 5000-7000 (NIP-90). Mnemonic: T-A-K = 8-2-5
on a phone keypad. Spike 9999 was a test value only. Relay acceptance of 3825 on nos.lol and damus is inferred from 9999
(both accepted it, seen running) and is checked again by F-014.

```
event   = { kind: 3825, pubkey: <sender x-only hex>, created_at: <sender clock, s>,
            tags: [["p", <recipient x-only hex>], ["g", <game id, lowercase uuid>]],
            content: NIP-44 v2 (conversation key of sender secret + recipient pubkey) of <plaintext>, id, sig }
plaintext (UTF-8 JSON, <= 64 KiB) = {
  "pv": 1,                                   // protocol version, judged first after decrypt
  "action_type": "JOIN" | "ACCEPT" | "MOVE" | "RESIGN",
  "game_id": <uuid>, "turn": <int>, "player_pubkey": <x-only hex = event pubkey>,
  "prev_state_hash": <hex>,                  // genesis hash for JOIN/ACCEPT
  "action_data": { "ptn": <PTN> }            // MOVE;  ACCEPT: { "white": <hex>, "black": <hex> };  JOIN/RESIGN: {}
  "signature": <BIP-340 over ActionDigest> }
ActionDigest = SHA-256(UTF-8("tak/action/v1\n" + pv + "\n" + action_type + "\n" + game_id + "\n" + turn + "\n"
               + player_pubkey + "\n" + prev_state_hash + "\n" + canonical(action_data)))   // pinned by a golden vector
```
- No other tags: no `d`, no `t`, no client tag. Relays index `#p` and `#g` (seen running).
- `turn`: JOIN and ACCEPT use 0; MOVE uses the 1-based ply number; RESIGN uses the sender's `appliedTurn` at resign time.
- The inner signature makes a stored move verifiable without its Nostr event (storage, replay, spectators) and binds game id,
  turn and previous hash; same key as the event (D-011).
- A republished action is the **stored event, verbatim** (same id), never re-signed: sign -> persist to outbox -> publish.
  Receivers still treat "same turn + same action digest" as a duplicate, because a peer that lost its outbox re-signs.
- Subscription, one REQ per relay per (re)connect, sub id `tak-<first 8 of game id>`:
  `{kinds:[3825], authors:[opponent], "#p":[me], "#g":[gid], since, limit:500}` and `{kinds:[3825], authors:[me], "#g":[gid], since, limit:500}`;
  a host still `Inviting` drops `authors` from the first filter. `since = maxCreatedAt - 30 s`, absent on a cold rebuild.
  `ids` lookups chunked to <= 5 (nos.lol silently returns nothing for 20; spike, seen running).
- Decode order = tamper rows: (1) id + sig, (2) author is the opponent (or any non-self author for a JOIN while Inviting),
  (3) decrypt, (4) JSON + `pv` + game id + inner signature + `player_pubkey == event.pubkey`, then in `TurnExchange`:
  (5) prev hash, (6) turn owner / PTN / legality, (7) equivocation. `pv != 1` from the opponent: drop and flag
  "opponent runs an incompatible version", kept distinct from tampering.
- NIP-01 id: the hand-written serializer escapes `\n \" \\ \r \t \b \f` and writes every other control character as `\u00xx`,
  everything else verbatim (spike serializer, cross-checked against Python/JSON.stringify, seen running). nips/01.md says
  "all other characters verbatim"; the control-character case is ambiguous between the text and implementations, so
  F-014 publishes one event containing U+0001 and checks the relay accepts it. Our own events carry only base64 content
  and hex/uuid tags, so the ambiguity cannot affect a game.

## Data
| What | Where | Version key | Leaves the device? |
|---|---|---|---|
| Secret key (32-byte scalar) | CLI/desktop: `<data-dir>/identity.json` = `{"v":1,"nsec":"nsec1…"}`; browser (M1+): localStorage `tak.identity.v1` | `v` | never (only the pubkey, in events) |
| Game record: role, my/opponent pubkey, game id, seed, size, relays, handshake state, result | `SqliteGameStore` table `NetGames` | db `PRAGMA user_version` | no (game id + pubkeys are visible as event metadata) |
| Progress `{appliedTurn, currentHash, publishedTurn, maxCreatedAt}` | `NetGames`, written in the same transaction as the applied move | same | no |
| Applied actions (turn, pubkey, ptn, tps, hash, prev, signature, event id, `SigScheme`) | existing `Moves` table + new columns | same | yes, as encrypted events |
| Outbox (signed event JSON, attempts, next attempt) | `Outbox` | same | yes when sent |
| Flag log (turn, reason, event id, author, seen at) | `Flags` | same | no |
- `<data-dir>` = `--data-dir`, else `LocalApplicationData/tak-p2p/<profile>` with `--profile` (default `default`).
  Two profiles on one machine are two players (the verify method needs this). The old `tak.db` next to the CLI binary is
  not read by multiplayer code.
- **Migration rule.** `user_version` 0 = the pre-M0 schema (Games, Moves). M0 ships version 2 via one forward step in one
  transaction: add `Moves.SigScheme TEXT NOT NULL DEFAULT 'legacy-ed25519'` and `Moves.EventId TEXT NULL`, create
  `NetGames`, `Outbox`, `Flags`. Additive change = new numbered step + bump + a test that migrates a committed v0 fixture
  db. Shape change (rename, drop, re-encode) = a decision record, a step that reads the raw old rows, and a test named
  for its trap. Never down-migrate. A db with a higher `user_version` than the build knows is opened read-only with an
  error ("written by a newer version"), never rewritten. JSON blobs (identity file, browser keys) carry `v` with the same rule.
- **Existing Ed25519 data (D-011: no migration).** Pre-M0 CLI rows are local/AI games signed with throwaway Ed25519 keys
  (seen in code: `CryptoSigner.GenerateKeyPair()` per game in CLI Program.cs). They keep their bytes, get
  `SigScheme = 'legacy-ed25519'`, stay replayable from TPS, and no code ever verifies their signatures. In the browser,
  `tak_p2p_privkey` / `tak_p2p_pubkey` hold an Ed25519 key: the new identity uses the new name `tak.identity.v1` and never
  reads the old names, because a 32-byte Ed25519 secret is also a valid secp256k1 scalar and would silently become a
  different npub. The old entries are removed only by the existing "reset identity" action. No user has a multiplayer game
  to lose (multiplayer never worked: risks.md R-001).

## Seams
| Seam | Real | Fake (tests / local multi-instance) |
|---|---|---|
| Relay WebSocket (R-001, R-004) | `ClientWebSocketRelaySocket : IRelaySocket` (text frames: connect, send, receive, close; `IRelaySocketFactory.Create(url)`) | `InMemoryRelay` in `tests/TakEngine.Testing`: NIP-01 subset (EVENT, REQ with ids/authors/kinds/#p/#g/since/limit, EOSE, CLOSE, OK, CLOSED, NOTICE); verifies id and sig and answers `OK false invalid` otherwise; stores history and returns it newest-first. Fault knobs, all deterministic from a seed: `Refuse(predicate, "rate-limited: …")`, `RejectUpgrade(503)`, `DropConnections()`, `ReorderLive()`, `DuplicateDeliveries(n)`, `Withhold(eventId)`, `AuthRequiredForReads()`, `Partition(relay)`, and a rate limit like damus (refuse the 6th event in a burst). Served over real sockets by `tools/TakRelay.Local`. |
| Clock (R-011, backoff, 20 s gap, 8 s NoAnswer, 10 min Active) | `TimeProvider.System` (BCL, browser-safe) | `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing; NuGet lists 10.10.0 on 2026-10-02) advanced by tests |
| Key store (R-009) | `FileKeyStore` (CLI/desktop); `BrowserKeyStore` over localStorage (M1+) | `InMemoryKeyStore` (can be pre-loaded with a corrupt blob) |
| Game storage (R-004 retention, F-037) | `SqliteGameStore` (CLI/desktop); browser store at M3 | `InMemoryGameStore` that survives a coordinator being disposed (= a restart) and can fail the next write (crash between publish and save) |
| Randomness | `RandomNumberGenerator` in shells | pure functions take nonce / aux / seed as arguments, so vectors fix them |
| Browser tab (R-005) | same `IRelaySocket`; if `ClientWebSocket` fails in a tab, a JS-interop socket implements the same interface | F-042 spike + Playwright at M1; nothing to fake in M0 |
| NTP (R-011) | existing `NtpTimeService` | not used in M0; M5 design (Q-016) |
| Platform heads (R-010) | — | no seam; Avalonia.Headless at M6 |

## Core-promise path through the code (M0)
1. Identity: head loads `IKeyStore` -> `SecretKey` (Crypto); first run generates and saves.
2. Host: `MultiplayerGame.Host(size)` -> `HostHandshake` creates game id + seed (shell RNG) -> `IGameStore` saves the record -> `InviteCode` printed by the CLI.
3. Guest: `InviteCode.Parse` + validation (no network on failure) -> `GuestHandshake` -> `RelayPool` connects to invite hints + defaults (nos.lol, damus).
4. Both subscribe with `SubscriptionPlan`; guest publishes JOIN (republish 5/15/45/60 s); host locks and publishes ACCEPT with colours from `ColorResolver.ResolveColors(seed, H, G)`; guest compares.
5. Mover: head -> `TakGameSession.SubmitPlacement/SubmitMove` (legal or refused locally) -> `ActionDigest` + Schnorr -> `SignedGameAction` -> `EnvelopeCodec` (NIP-44 + event + id + sig) -> outbox persisted -> `RelayPool` publishes under `PublishBudget`, `PublishTracker` reports Sent/Pending/Failed per relay.
6. Receiver: `RelayPool` dedupes by event id -> `EnvelopeCodec` decode (rows 1-4) -> `TurnExchange` (buffer, rows 5-7, `TakGameSession.ProcessRemoteMove`) -> progress + move committed to `IGameStore` -> head shows it.
7. End: both sessions reach the same `GameResult` and final `CurrentStateHash`; the CLI prints `FINAL <result> <hash>`.
8. Restart: head reopens the store -> session restored from the last TPS + hash + turn -> `GameCoordinator` resubscribes with `since`, republishes the outbox and the last own move.
9. Tamper: `IncomingClassifier` drops, the flag goes to `Flags`, the head shows the banner, the game continues.

## Test strategy (by risk)
Order is the build order; each test is written and seen red before the code.
1. **Crypto (F-031, F-015, F-032)** — silent and fatal if wrong (the old code passed its own tests while two peers could not talk).
   Official vectors as test resources (NIP-44 `nip44.vectors.json`; BIP-340 `test-vectors.csv`), always two independent
   keypairs, never sender-decrypts-own. NIP-01 ids against fixed known-good ids from the spike's Python-verified `events.jsonl`,
   with content edge cases: `+ < > & " \`, newline, CR, tab, `\b \f`, U+0001, U+001F, DEL (0x7F), an emoji (surrogate pair),
   U+2028, empty content, empty tags. Invalid: flipped id/sig bit, tampered content, 31/33-byte pubkey, off-curve x.
2. **Envelope codec + ActionDigest + hash chain (F-033)** — any encoding drift forks every game. Golden vectors for
   `ActionDigest` and for a fixed 5x5 game's hash sequence; round trip of every action type; decode verdict per tamper row 1-4.
3. **TurnExchange (F-035)** — the logic that real relays stress (10 of 12 arrivals out of order in the spike). Seeded
   permutation tests (every permutation of a short game, 200 random ones of a full game) with duplicates must end at the
   same hash; gap -> catch-up after 20 s of fake time; equivocation first-seen-wins; late events; own echo; restart after
   publish-before-save.
4. **Tamper matrix (F-038)** — one test per row 1-7 over the fake relay: board and hash unchanged, flag (rows 3-7) persisted
   across a restart, a following valid move accepted. Deliberate-break check: remove the drop, see the matrix go red.
5. **Relay client (F-034)** — fake relay: OK false rate-limited -> Refused + backoff + budget; drop/503 -> reconnect
   1, 2, 4 … 30 s and resubscribe with `since`; `CLOSED auth-required` marks one relay read-impossible; no event applied twice.
6. **Handshake (F-017)** — host/guest state tables from scope.md, including second guest, repeated JOIN, ACCEPT from a
   non-host, colour mismatch, malformed invite with zero network traffic (fake relay counts frames).
7. **Storage (F-031 key file, F-037)** — migration from a committed v0 fixture db; key file wrong length / out-of-range
   scalar -> clear error and the file untouched.
8. **End to end, the core promise in CI (F-016, F-037)** — two `MultiplayerGame`s, two stores, one `InMemoryRelay` with
   two relay endpoints: scripted 5x5 game to a road win, equal final hash; same with duplicates and reordering; crash B after
   its turn 4, A plays turn 5, restart B -> same final hash; cold rebuild from relay history.
9. **LiveRelay by hand (F-014, F-016 live, F-034 live, F-037 live, F-038 live)** — `Category=LiveRelay`, never in CI;
   needs the user's approval to publish (Q-018).
10. **Two machines (F-039)** — owed to the user if the agent has no second machine.
Mutation check: Stryker.NET 5.0.0 on the pure modules (playbook "Quality bar"); deliberate-break for the shells.

## Context map
- `CLAUDE.md`: what it is, check commands, costly invariants (layering, purity, hash chain, NIP-01 serializer, key rules,
  kind and envelope, storage versioning, no public publishing without approval), index.
- `.claude/rules/`: `crypto.md` (Crypto, Core/Cryptography), `transport.md` (Transport), `multiplayer.md` (Multiplayer),
  `core-engine.md` (Core, Abstractions), `storage.md` (Storage.Local, Core/Storage, Blazor BrowserStorage),
  `frontends.md` (TakApp.*), `tests.md` (tests/**, tools/**), `ci.md` (.github, build props, solution files, tool manifest).
- `docs/decisions/`: 0001 secp256k1 single key, 0002 crypto library, 0003 event kind and relays, 0004 envelope and
  signing, 0005 CLI-first M0, 0006 fake relay in CI and LiveRelay by hand, 0007 mutation check, 0008 check commands and
  warning policy, 0009 module layout, 0010 stored data versions and legacy Ed25519 data.

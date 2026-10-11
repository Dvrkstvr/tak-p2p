# Scope — MVP

<!-- Drafted 2026-10-01 by scope-cutter. SIGNED OFF by the user 2026-10-01 (D-022): cut as proposed, v1 platforms
     Web + CLI + Windows desktop, Quick Play is the first cut if late. Features and pass flags live in features.json; this file holds the milestone
     plan, interaction specs and the cut lists. Evidence labels: seen in code / seen running / documented / inferred. -->

## Reading guide
- Core promise (D-001): two people on different devices play a complete, independently verified game of Tak over public Nostr relays, no game server.
- Approach: spec-first, deep track (D-012). Every behaviour below has acceptance criteria that become tests before code. Public relays are never in CI
  (flaky, rate-limited); CI uses an in-memory fake relay, and live-relay tests carry `Category=LiveRelay`, run by hand.
- UI freeze (D-006): no new UI features, controls or frontends until M0 passes. M1 onward touches UI only to wire it to the engine, not to add controls.
- Identity (D-011): one secp256k1 key per player. Transport design inputs come from D-015 (custom regular kind, NIP-44 v2, p + g tags, nos.lol + damus,
  hand-written NIP-01 id, NBitcoin.Secp256k1 + BouncyCastle). The event kind number and exact envelope are the architect's call (stage 6); this file
  fixes behaviour, not wire format. Action names JOIN / ACCEPT / MOVE / RESIGN below are working names.
- Feature ids F-001..F-030 are the adopted set and keep their ids and evidence; F-031.. are new. All new features start `passes:false, evidence:null`.

## Milestone overview
| M | Name | Head(s) | Risks retired | Size |
|---|---|---|---|---|
| M0 | Core promise on real relays | CLI (+ fake-relay tests) | R-001 R-002 R-003 R-004 (rest), R-008 | M0-A ~1 wk, M0-B ~1 wk |
| M1 | Browser on the engine | Blazor (local + AI only) | R-005 (WebSocket part), R-006, R-007 | ~1 wk |
| M2 | Browser multiplayer + invite UX | Blazor + CLI | R-005 (rest) | ~1 wk |
| M3 | Resume, resign, offline, key backup | CLI + Blazor | R-004 retention, R-009, R-011 (part) | ~1 wk |
| M4 | Quick Play | CLI + Blazor | R-004 (ephemeral kind) | ~1 wk, first thing cut if late |
| M5 | Stale warning and auto-draw | all heads | R-011 | ~1 wk |
| M6 | Desktop (Avalonia) multiplayer | Desktop | R-010 (desktop part) | ~1 wk |
| M7 | Release readiness and polish | all v1 heads | R-008, R-012 | ~1 wk |

M0 is the one place where the one-week guideline is stretched: it is two checkpoints (M0-A, M0-B), each about a week, and each is independently
checkable. If M0-A alone takes more than ~1.5 weeks, stop and re-plan before M0-B.

## M0 — Core promise: two real processes play a verified game over nos.lol + damus
What the user can do when M0 is done: on two machines (or two terminals) run the CLI, one hosts and prints an invite, the other joins with it, they
play a 5x5 game to a road win through the public relays; both show the same result and the same final state hash; one side can be killed mid-game and
resumes; a forged or illegal move from a test peer is dropped and shown as a warning. The browser is NOT in M0 (see decision below).

Feature ids: F-029, F-031, F-032, F-015, F-033, F-034, F-035, F-017, F-016 (M0-A); F-014, F-036, F-037, F-038, F-039 (M0-B).
Adopted features M0 builds on (already `passes:true`, keep evidence): F-001, F-002, F-003, F-005, F-006, F-010, F-012, F-027.

Order inside M0 (each step test-first; the failing test is written and seen red before the code):
1. F-029 CI gate. Nothing else lands without a gate. See "CI and the gate" below.
2. F-031 identity, F-032 NIP-01 id + BIP-340, F-015 NIP-44 two-peer. Official vectors added as test resources. Delete the old Ed25519 `CryptoSigner`,
   the SHA256 "shared secret" `Nip44Encryption`, and the test that passes for the wrong reason (`Nip44_EncryptAndDecrypt_RoundTripsSuccessfully`).
3. F-033 signed envelope + hash chain on `TakGameSession` (secp256k1; the session emits the full envelope, today it emits only a signature).
4. F-034 relay client against an in-memory fake relay (NIP-01 subset that verifies id and sig, can inject `OK false`, drop sockets, reorder).
5. F-035 turn-exchange coordinator (head-agnostic, lives in a library, not in a frontend's Program.cs) and F-017 invite/join handshake.
6. F-016 headless full game over the fake relay: two `TakGameSession`s to a road win, equal final hash. End of M0-A.
7. F-014 first live check: .NET-signed event accepted by nos.lol and damus (spike already saw this; the real build code must do it again).
8. F-036 CLI host/join (replaces the simulated "Join"/"Quick Play" text), F-037 restart + catch-up, F-038 tamper matrix (fake relay) plus one live tamper.
9. F-039 exit run on two machines. Owed to the user if a second device is not available to the agent.
M0 also carries: mutation check or deliberate-break per pure module (NIP-44, NIP-01 id/sign/verify, turn-exchange state machine, envelope codec) per the
deep-track bar. Stryker.NET compatibility with .NET 10 is unverified; fallback is "break the code once per module and see the test fail".

### Decision: browser head is in M1, not M0
Reason (D-008, R-005, R-006): (1) The core promise says "different devices"; two CLI processes on two machines satisfy it and are the cheapest to automate
and to drive from an agent. (2) The browser path is a rewrite, not a wire-up: Blazor does not use `TakGameSession` (seen in code), so putting it in M0 forces
the D-008 migration (delete `WebGameSessionManager`'s duplicate board/road code) into the same milestone as the crypto replacement, which doubles the
size and mixes two kinds of failure. (3) The crypto is already proven in browser-wasm (spike R-002/R-003: sign 16 ms, verify 13 ms, trimmed Release build
works), so the remaining browser unknowns (relay WebSockets from a tab, a backgrounded tab) are separable and get a day-one spike in M1 (F-042).
(4) M0 code must keep the door open: the coordinator and relay client sit behind interfaces, target `net10.0` with no API that is unavailable in
browser-wasm, and use `System.Net.WebSockets.ClientWebSocket` (believed to work in Blazor WASM; unverified, F-042 proves it). Cost of this choice: the
user cannot click anything on the public site until M2. The user's own MVP-done check (brief.md: browser + another head) is met at the end of M2.

### CI and the gate (D-010)
`.github/workflows/` has no test step today (seen in code: deploy-gh-pages.yml publishes straight from main; release.yml likewise).
Plan (F-029):
- New `ci.yml` on `push` and `pull_request`: checkout, setup-dotnet 10.0.x, `dotnet test TakGame.sln --filter "Category!=LiveRelay"`. Open risk
  (unverified): the .sln contains Android/iOS projects, so restore on ubuntu-latest may demand workloads. Fallback: run `dotnet test` per test project
  (`tests/TakEngine.Core.Tests`, `tests/TakEngine.Transport.Tests`), or a `.slnf` solution filter. Whichever works must be what AGENTS.md/playbook say.
- `deploy-gh-pages.yml` gets a `test` job and `needs: test` on the publish job, so main cannot auto-deploy red. `release.yml` gets the same in M7.
- Proof the gate works: on a throwaway branch push one deliberately failing test, see the run fail, see it pass after reverting; record both run ids.
- Owed to the user (cannot be done from the repo): turn on "require status check" branch protection for main. Until then CI informs, deploy gating protects Pages.
- Test counts are not written into docs (D-013). Live-relay tests never run in CI.

## M1 — Browser on the engine (no new features)
What the user can do: open the Pages build (or `dotnet run` locally) and play local and vs-AI games exactly as before, but every move now goes through
`TakGameSession`, and a Playwright script proves place, slide, cancel and win. Features: F-041, F-042, F-043, F-021, F-022, F-023, F-009, F-027.
Why now: highest risk after M0 (R-005, R-006, R-007), and every later browser feature builds on it. UI behaviour is characterised and frozen, not extended.
Order: F-042 first (a one-day spike: can a Blazor WASM tab publish/receive on nos.lol + damus, and survive 5 minutes backgrounded). If F-042 fails, the
fallback in R-005 applies: v1 multiplayer heads are CLI + desktop, browser stays local/AI only, and M2 is replaced by M6 moving up.

## M2 — Browser multiplayer and invite UX
What the user can do: open the site, press "Host", copy a link or show a QR; the other person opens the link in a browser or pastes it in the CLI; both
play a 5x5 game to a road win, browser vs CLI, with the same hash; connection status and rejected moves are visible. Features: F-044, F-047, F-046, F-045.
This is the brief's MVP-done check #1. F-045 is an owed two-device check if the agent cannot do it.

## M3 — Resume, resign, offline, key backup
What the user can do: close the tab or terminal mid-game and come back days later to the same game; see "waiting for opponent, last seen ..."; resign;
export and import their key so a lost browser profile is not a lost game. Features: F-019, F-048, F-050, F-051, F-049, F-040.
This is the brief's MVP-done check #2 in every v1 head (the CLI restart path is already proven in M0). F-040 needs calendar time: the first re-run of
`check-retention.mjs` can happen any day after the spike; the 7-day result is the acceptance for "async play over days".

## M4 — Quick Play
What the user can do: press Quick Play 5x5, get paired with another searcher within ~60 s, same game id, opposite colours. Features: F-018, F-011 (adopted
message layer). Cut-first: R-004's fallback is to drop Quick Play from v1 (ephemeral kind 20001 worked on both relays, but the two-searcher matching was never run).
A design task precedes code (see Quick Play spec).

## M5 — Stale warning and auto-draw
What the user can do: open a game idle for 3 days and see a warning; a game idle for 7 days ends as a draw, with an honest note that it is each client's
local verdict. Features: F-020, F-008 (adopted engine logic), F-052. Design task first (Q-016, R-011): without an authority, two clients can disagree.

## M6 — Desktop multiplayer
What the user can do: run the Avalonia desktop app on Windows, host or join with an invite, play and resume. The simulated "remote" mode (two random keys in one
process, seen in code MainViewModel.cs L21-25) is deleted. Features: F-025, F-053. Linux/macOS builds exist from the release workflow but are labelled unverified.

## M7 — Release readiness and polish
LICENSE file (Q-014), README device matrix matches features.json, CLI local game hand-checked, release workflow gated on tests, Android APK smoke on one phone
(owed; relabelled experimental either way), key-storage threat model written (R-009), the small polish picks from Q-005. Features: F-054, F-055, F-056, F-024, F-028, F-026.

## Platform set proposal for v1 (answers Q-006; default recorded as D-018, user to confirm at sign-off)
- In v1: Web (GitHub Pages, primary and zero-install), CLI (Windows, Linux, macOS via `dotnet publish`; Windows hand-verified, others "should work"), Desktop
  Avalonia on Windows (hand-verified at M6; Linux archive built by the release workflow but unverified).
- Experimental, not a v1 deliverable: Android APK. It has never run on a device (docs/AUDIT.md). One owed smoke test at M7; the README says "experimental".
- Not v1: iOS/iPadOS and a macOS desktop app (iOS needs a Mac, Xcode and Apple signing; the auditor found no evidence either runs). README "Implemented" is
  relabelled to match, which D-013 keeps as the one maintained status column.
- Why: the promise needs two heads that can talk to each other; web + CLI are the cheapest to automate; desktop is the brief's second MVP head; mobile is a
  reach feature with an unproven build chain (R-010, fallback "web + desktop only" accepted).

## Defaults proposed for the deferred questions (re-checked at scope; none became blocking)
- Q-005 polish list (now assumable, D-019). In v1: a "My games" list (a by-product of resume, M3), resume, resign, key backup, connection/relay status, rejected-move
  warnings. Later: board flip, move-history scrub UI, sound, animations, drag and drop, settings page beyond relay list and key backup, PWA/offline, match history of
  finished games, themes. Not doing in v1: undo/takeback (needs a peer-consent protocol; no authority to arbitrate), draw offer (see Later). Reason: every item
  here adds surface to test before the promise is proven; F-030 stays as the Later bundle.
- Q-006 platform set: above (assumable, D-018).
- Q-014 licence: stays deferred; due at M7 (F-054). Proposed default MIT because the README badge already says so, but a licence is the owner's legal call and
  can't be assumed by an agent; the item blocks the release milestone, not M0-M6.
- Q-015 v2 docs: assumable, D-020. Keep docs/v2-tournaments.md and docs/spectator-implementation-plan.md untouched; at M7 add a one-line "not in v1" banner
  to each (docs are outside this task's lane). Spectator engine (F-013) stays tested and unused.
- New: Q-016 (stage 5/M5 design) how two clients agree on "idle for 7 days" with no authority; Q-017 (M3) key backup UX. Both deferred.

## Interaction specs
Common conventions: "relay set" = the configured relays (default nos.lol + damus, D-015). "Sent" means at least one relay answered `OK true`; a relay
answering `OK false` (`rate-limited`, `auth-required`, `invalid`) or `CLOSED`/`NOTICE` is surfaced per relay, never collapsed into "sent" (R-001 note).
The client never publishes more than 4 events in any 10 s window and retries `rate-limited` with backoff (damus refuses the 6th event in a burst; seen running).
Every received event is checked for `id` and `sig` before it is decrypted (NIP-44 spec, spike R-002/R-003 item 5).

### Invite create and accept handshake (F-017, F-044, F-047)
Roles: Host H, Guest G. The invite carries game id, H's pubkey, board size, komi, relay hints and a commitment to the host's colour nonce (D-038; an invite without one is rejected). JOIN carries the guest's nonce; ACCEPT reveals the host's nonce and repeats size and komi; seed = SHA-256(tag || host_nonce || guest_nonce).
Host states: `Idle` -> `Inviting` -> `Locked(G)` -> `InGame` | `Cancelled`.
- Idle -> Inviting: user chooses size and komi, host creates game id + host nonce, persists the game record (key, game id, host nonce, size, komi), subscribes on `#g=gid, #p=H`, shows the invite.
- Inviting -> Locked(G): a valid JOIN (id, sig, decrypts, game id matches, author != H) from pubkey P. H locks to P and P's nonce, resolves colours with
  `ColorResolver.ResolveColors(seed, H, G)` (AGENTS invariant 1), publishes ACCEPT (carries the resolved colours so a mismatch is visible), moves to InGame.
- Locked: a JOIN from another pubkey is ignored and surfaced ("ignored join from npub1...: game already taken"). A repeated JOIN from P (its retry) re-sends ACCEPT (idempotent).
- Host restart while Inviting or Locked: reload the record and resubscribe; nothing is lost; Cancelled invites stay dead.
- Cancel (Ctrl-C / "Cancel invite"): before Locked, nothing is published; the invite simply never gets an ACCEPT (guest sees `Waiting` forever until its own timeout). After
  Locked, cancel means resign (see Resign spec). No timeout on the host side in M0; M2 shows "waiting for opponent since ..." and lets the user cancel.
Guest states: `Idle` -> `InviteParsed` -> `Joining` -> `InGame` | `Failed`.
- Idle -> InviteParsed: a malformed or truncated invite, size not 3..8, komi outside 0..20 half flats, missing seed commitment or host key, or host key == own key -> `Failed` with a specific message and no network traffic.
- InviteParsed -> Joining: connect to the relay set (invite hints plus defaults); publish JOIN; republish at 5 s, 15 s, 45 s, then every 60 s; show per-relay state.
- Joining -> InGame: ACCEPT arrives from H's pubkey (id, sig, game id), its host nonce matches the invite's commitment, its size and komi equal the invite's, and its colours equal the guest's own `ResolveColors` result. ACCEPT from any other pubkey is dropped and flagged.
  Colour mismatch -> `Failed` ("host and guest disagree on colours"), nothing played.
- Joining, all relays unreachable: stay in Joining with "offline, retrying". Guest cancel -> `Idle`; nothing more is published, but a JOIN that already reached the host still locks the game to this guest
  (no un-join message in v1). The host's way out is cancel/resign, so a cancelled guest can strand the invite; accepted for v1 and listed under the bearer-invite gap below.
- Known gap, accepted for v1: anyone holding the invite can win the race to JOIN; the invite is a bearer secret like a link to a private room (R-009 note in security review).

### Turn exchange, including out-of-order, duplicate and late events (F-033, F-035, F-016)
Each client holds: `appliedTurn` n, current hash, a reorder buffer keyed by turn, `publishedTurn`, `maxCreatedAt` (used only for the `since` of subscriptions, never for ordering or trust).
Mover side: local engine validates the move (illegal -> rejected locally, nothing sent) -> sign -> persist the intended turn (`publishedTurn = n+1`) -> publish -> `Sent` / `Pending` /
`Failed(all relays refused)`; `Pending` and `Failed` retry with backoff and survive restarts (an unsent move is republished on start). Moves entered while every relay is down are accepted
locally (outbox) and sent on reconnect.
Receiver side, for an event with turn t from the pubkey the event claims:
- author is me (relay echo): ignore, update `maxCreatedAt`.
- author is neither me nor the locked opponent: drop, count, no flag (could be a stranger replaying the game tag).
- t == n+1: verify envelope signature, `prev_state_hash == current hash`, it is the opponent's turn, PTN parses, move is legal (`ProcessRemoteMove`); on success apply, n = t, then drain the
  buffer for t+1, t+2...
- t > n+1: store in the buffer (cap 64, oldest-far-ahead dropped), status "waiting for move n+1"; if the gap is still open after 20 s trigger a catch-up REQ (see Reconnect). Out-of-order is normal
  (relays return history newest-first; 10 of 12 arrivals were out of order on a cold replay in the spike).
- t <= n and identical to the applied event (same event id, or same turn + same move + same hash): duplicate, ignore silently (multi-relay fan-out guarantees many).
- t <= n but different content for an already-applied turn: equivocation. First seen wins, the chain does not change, flag "conflicting move for turn t" (see Tamper).
- any event after the game is Completed: ignore (late event); the result stands.
- a MOVE whose turn belongs to me, or whose author is the opponent but the turn parity is wrong: protocol violation (Tamper).
Terminal: on road win / flat win / draw by the engine both sides compute the result independently and compare the final hash; the CLI prints it, F-039 compares the two printouts.
Cancel: leaving the app mid-game is not a cancel; the game persists (Resume). Only Resign ends a game early.

### Opponent offline (F-051; status line already in M0 CLI, full UI in M3)
States of the opponent link, derived and never authoritative: `Unknown` (never heard) -> `Active` (verified event within the last 10 min) -> `Quiet` (10 min to 3 days) -> `Stale` (> 3 days, M5).
Own link states: `Online` (>= 1 relay subscribed and caught up), `Degraded` (some relays down), `Offline` (none).
- Opponent's turn and the opponent is Quiet: the UI shows "waiting for opponent, last move seen <time>" (time from the relay `created_at` of the last verified event, labelled as the opponent's claim).
- The user can always resign or leave; there is no forfeit on silence before Day 7 (M5), and no "nudge" messages (no extra protocol surface).
- Own Offline: moves are queued (outbox), a banner says "not connected, moves will be sent when connected"; nothing is lost.

### Reconnect and catch-up (F-034, F-037)
States per relay connection: `Connecting` -> `Subscribed` -> `CaughtUp` (EOSE seen) -> `Disconnected` -> `Backoff(1,2,4,... up to 30 s)` -> `Connecting`. HTTP 503 on upgrade counts as a retry (seen on damus).
- On every (re)connect send one REQ with two filters (events from the opponent addressed to me for this game; my own events for this game), `since = maxCreatedAt - 30 s`, `limit <= 500`; dedupe against applied turns.
- App restart with state: load `{appliedTurn, hash, maxCreatedAt, publishedTurn}` and the move list, rebuild the board (O(1) restore via TPS where the storage allows), resubscribe, republish my last move once
  (idempotent for receivers, and a cheap resend mechanism since there is no "please resend" message).
- Restart with the key but no state (lost state file): cold rebuild from relay history with no `since`; the UI says "rebuilt from relays"; the result must equal the surviving side's hash or the client shows a
  fork warning instead of continuing.
- History is missing a turn (relay retention, or only one relay answered): stay in "waiting for move n+1", try the other relay, surface "move n+1 not found on any relay"; if the opponent is online it resends on its own reconnect;
  if not, the game waits. No guessing and no skipping.
- Relay NOTICE / `CLOSED auth-required`: shown per relay, that relay is treated as read-impossible and the other relay carries on (damus AUTH is broken, seen running).

### A tampered, forged or illegal move is received (F-038, F-006, F-016)
Classification of a received event, in order (first match wins). "Drop" = never applied; "Flag" = a visible warning plus a persisted entry in the game's flag log.
1. Bad event id or bad BIP-340 signature: drop, count only (relay junk or a stranger; cannot be attributed).
2. Valid event, author is not the opponent, game tag matches: drop, count only.
3. Opponent-authored but undecryptable (MAC fail, bad padding, wrong version): drop, flag "undecryptable message from opponent".
4. Decrypted, but game id mismatch / envelope signature invalid / `player_pubkey` != event author: drop, flag.
5. `prev_state_hash` != my current hash for the turn it claims (fork): drop, flag "hash chain mismatch at turn t".
6. Wrong turn owner, malformed PTN, or illegal move on the real board: drop, flag "illegal move from opponent: <engine reason>".
7. Conflicting duplicate for an applied turn (equivocation): drop, flag.
After a flag the game continues unchanged: the state is exactly what it was, the user keeps seeing "waiting for opponent" and a banner "Rejected a move from your opponent at turn t: <reason>. Your game is unchanged."
A valid resend from the opponent is still accepted. The flag log persists across restarts. After 3 flags from the same peer the banner suggests resigning; nothing auto-forfeits (no authority to rule on it).
Verified in M0 by an automated matrix over the fake relay (one case per row 1-7) and by one live case (a test-peer tool publishes an illegal-but-correctly-signed move to the real relays; the CLI under test must
show the warning and keep playing).

### Resign (F-050, M3; engine already has local resign F-005; today the remote resign is NOT sent over the wire, seen in code TakGameSession.Resign)
States: `InGame` -> `ConfirmResign` -> `ResignPending` -> `Completed(Resigned)`.
- InGame -> ConfirmResign: user chooses Resign (allowed at any time, also on the opponent's turn); the UI asks for confirmation. Cancel -> back to InGame, nothing published.
- ConfirmResign -> ResignPending: sign and publish RESIGN (game id, `appliedTurn` k at the time). The local game is Completed immediately ("You resigned"); delivery continues with retry and survives a restart.
- Receiver: accepts a RESIGN when the event is valid, authored by the opponent, the game id matches and `k <= its own appliedTurn + 1` (it is not conditioned on the hash chain, so a resign sent while a move was in flight still lands);
  game ends, result "opponent resigned". A RESIGN in a Completed game is ignored. A MOVE arriving after a RESIGN is ignored (late event).
- Resign is final; there is no un-resign. Draw offers are out of v1 (Later), so there is no negotiation state to specify.

### Quick Play pairing (F-018, M4) — design task before code
Proposed states (confirm in an M4 spike, since two-searcher matching was never run): `Idle` -> `Searching(size)` (publishes a signed ephemeral kind 20001 advert with a size tag, re-advertises every 20 s)
-> `Challenged` (saw a compatible advert: the side with the lexicographically lower pubkey sends the challenge, which removes the double-challenge race) -> `Matched` (handshake as the invite spec, seed derived
from the challenge) | `TimedOut` (60 s, back to Idle) | `Cancelled` (best-effort withdraw event). Errors: relay refuses ephemeral kind -> Quick Play unavailable message and the invite path remains.
Known risks to test: adverts from a stranger who never answers (timeout), spam adverts (rate cap per pubkey), same user in two tabs (own pubkey ignored).

### Stale and auto-draw (F-020, M5) — design task before code (Q-016)
Open design question: "idle for 7 days" depends on the last verified move's relay `created_at` (a sender-controlled number) and an NTP check. The default to specify at M5: each client computes the verdict locally
from the last verified event and labels it local; a client that stays online past Day 7 publishes a signed DRAW-BY-TIMEOUT notice that the other applies only if its own computation agrees. Do not build until the
design is accepted.

### Stepped tower movement gesture in Blazor (F-022, M1) — design task
There is no written spec for the M1.15 gestures (place, rotate-to-wall, right-click wall, stepped tower movement with counts, cancel on out-of-range click); the four commits in two hours were iterated by hand.
M1 first writes the state/transition table from the current behaviour (characterisation, read from the code and driven in a browser), then Playwright encodes it. No behaviour changes in M1.

### Relay publish status (F-034)
Per relay: `Idle` -> `Publishing` -> `Accepted` (OK true) | `Refused(reason)` (OK false: rate-limited, auth-required, invalid, blocked) | `NoAnswer` (8 s). Overall: `Sent` if any Accepted; `Pending` if none yet;
`Failed` if every relay Refused/NoAnswer. `rate-limited` and `NoAnswer` retry with backoff up to 5 minutes; `invalid` does not retry (a bug in our event); `auth-required` marks the relay write-impossible.

## Later
- Draw offer / draw by agreement (OFFER / ACCEPT / DECLINE state machine with expiry) — not needed to finish a game (road win, flat win, resign); needs its own spec; add after v1 feedback.
- Undo/takeback — needs a consent protocol and has no authority to arbitrate; v2 at the earliest.
- Android APK as a supported head — never run on a device (F-026, R-010); one owed smoke test at M7, supported only when someone runs it.
- Replay/scrub UI over the finished game (storage supports it, F-007) — a polish item; resume (M3) comes first.
- Polish bundle F-030: sound, animations, board flip, drag and drop, themes, settings page beyond relay list and key backup, PWA/offline — each adds surface before the promise has users (Q-005).
- Spectator mode and broadcast directory (F-013 engine kept) — the user's v2 docs; not on the core-promise path (Q-015).
- Gift-wrapped (NIP-59) moves as an optional privacy channel — damus cannot serve kinds 4/1059 (spike), so it cannot be the default; revisit if metadata privacy becomes a goal.
- Relay list UI beyond a plain text field; relay health scoring; user-hosted relay setup guide — the default pair works; configurable list is enough.
- primal relay as a default — unreachable from the spike network on every attempt; re-add only after it is shown reachable.
- Push notifications (a move arrived while the tab is closed) — v2 docs; needs a service worker or native push, neither exists.

## Not doing
- Tournaments, Swiss brackets, ELO/ratings, co-signed receipts, anti-cheat fingerprinting — docs/v2-tournaments.md asks for them; they presuppose a working verified game (not yet proven) and an authority question (R-011) that v1 does not answer. Parked, documents kept (D-020).
- iOS / iPadOS app and a macOS desktop app in v1 — needs a Mac, Xcode, Apple signing and a device nobody has run (R-010). README relabelled at M7 (D-018).
- Ed25519 identities and a second key per player — retired by D-011; also no migration (different curve, no live users with stored games depend on them: inferred, no evidence of external users).
- Authoritative timers, server-side adjudication, any game server — contradicts the core promise (D-001).
- Auto-forfeit for tampering — there is no authority to decide who cheated; v1 drops, flags and lets the user resign.
- New PlayTak-style controls and visual features in M0-M6 — frozen by D-006.
- DEVLOG entries, mirrored milestone tables and test counts in docs — retired by D-013; features.json and STATUS.md carry status.
- Hand-rolled cryptography beyond the ~150 lines of NIP-44 glue over NBitcoin.Secp256k1 — D-004/D-015; vectors prove the glue.

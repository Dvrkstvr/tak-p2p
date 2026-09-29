# Risks

feasibility: red · H-open 4 · M-open 7 · spiked 0

## Does the core promise work end to end today?
**No.** Evidence level: *seen in code* (call-graph trace), corroborated by docs/AUDIT.md (2026-09-12: "Scaffolded but not wired"),
*not* seen running against a relay. The promise is "two players on different devices complete a verified game over Nostr relays".
Every layer of that path exists as a library piece and each piece has passing unit tests (114/114, run 2026-09-30), but the
pieces are not connected, and at least two of them would fail if they were:

1. Nothing sends or receives a move. `NostrTransportClient.SendMoveEnvelopeAsync`, `SubscribeIncomingMovesAsync`, `OnMoveEnvelopeReceived`
   have no callers outside their own file. `TakGameSession.ProcessRemoteMove` / `OnRemoteEnvelopeReady` are called only from tests.
2. "Join" is fake in every frontend. Blazor `HandleJoinCode` -> `StartLocalMatch`; CLI Join and Quick Play print "Connecting..."/"simulation"
   then start a local hot-seat game; Avalonia "remote" mode creates two random keypairs in one process.
3. The Blazor frontend does not use `TakGameSession` at all (uses `GameBoard` + its own road code), so even if a transport were attached
   there is no hash chain/signature check in the browser path.
4. Latent defects that a real two-device test would hit immediately (R-002, R-003).
The README ("Real-time WebSocket connection to public Nostr relays", "Instant peer-to-peer match invites") and docs/v1-mvp.md
(all milestones COMPLETED, M1.4 "verified") describe this as done. That claim is not supported.

## Core-promise path (proposed M0)
1. Both clients hold a Nostr-valid identity (secp256k1 keypair, npub), persisted.
2. Host creates an invite (game id, host pubkey, board size, relays); guest opens it.
3. Both connect to >= 2 relays and subscribe to events for this game id addressed to their pubkey.
4. Colours resolved with `ColorResolver` from a shared seed; each side builds a `TakGameSession.CreateRemote`.
5. Mover submits a move -> session emits signed envelope -> encrypt for peer -> Nostr-signed event -> publish to relays.
6. Peer receives (dedupe across relays), decrypts, `ProcessRemoteMove` verifies signature, hash chain, turn owner, legality; UI updates.
7. Repeat to a road win; both show the same result and final state hash.
8. Close and reopen one side; game resumes from storage and re-syncs missed events from relays.
9. Negative: a tampered/illegal payload from a test peer is dropped and surfaced, game continues.
Frontend for M0: whichever single head is cheapest to automate (proposal: CLI, plus one headless/two-process test), then Blazor.

## Risks

### R-001 · impact H · evidence hypothesis
The end-to-end path (steps 1-9 above) has never run over a relay. It is unknown how many further defects sit behind R-002/R-003
(subscription filters, event ordering/dedupe, reconnect, replay of missed moves, relay rate limits).
- check: spike a two-process ping-pong through a real relay, then through an in-memory fake relay for tests
- fallback: reduce v1 to a single relay of the user's choice plus manual "paste move" exchange
- source: features F-016, F-017; docs/AUDIT.md section 5

### R-002 · impact H · evidence hypothesis
Shared secret is not ECDH: `Nip44Encryption.DeriveSharedSecret` = SHA256(myPriv || theirPub) (Nip44Encryption.cs:81-93). Alice and Bob derive
different keys, so Bob cannot decrypt Alice's message. The only "round trip" test (TransportBenchmarkTests) decrypts with the sender's own
keys, so it passes anyway. Also not compatible with NIP-44 v2 (as I know it: secp256k1 ECDH + HKDF + ChaCha20 + HMAC + padding; unverified against current spec).
AGENTS.md invariant 3 says "derived ECDH shared secrets" - the code does not do that.
- check: write the two-peer encrypt/decrypt test first (should fail today), then swap in a maintained secp256k1/NIP-44 implementation
- fallback: use an existing vetted Nostr .NET library for signing and NIP-44 instead of hand-rolled code
- source: seen in code; https://github.com/nostr-protocol/nips/blob/master/44.md (not fetched this session: unverified)

### R-003 · impact H · evidence hypothesis
Nostr events are never signed (`NostrEvent.Sig` never assigned) and identities are Ed25519, not secp256k1 x-only keys. NIP-01 relays verify
`id` and BIP-340 `sig`; unsigned/invalid events should be rejected, so the profile publish claimed in the UI and all move events would be dropped
(count shown to the user is "sent", not "accepted"). The npub/nsec encoding of an Ed25519 key is also not a usable Nostr identity.
Two key types are also in play in the docs ("Ed25519 / Secp256k1"), which is an undecided design point (Q-004).
- check: publish one event to a real relay, read OK/NOTICE
- fallback: adopt secp256k1 Schnorr for identity+events and keep Ed25519 only if a decision record says why
- source: seen in code (NostrModels.cs, CryptoSigner.cs); NIP-01 (unverified this session)

### R-004 · impact H · evidence platform
Public relay behaviour is unobserved: whether kind 4 with custom "g" tags and kind 20001 ephemeral events are accepted, retention for async
play (days), rate limits, need for PoW/auth, and events dropped for unsigned/odd kinds. Quick Play depends on relays fanning out ephemeral events.
Kind 4 is the legacy DM kind; NIP-17/59 gift wraps are the current private-message approach (unverified).
- check: spike against the three default relays (send, subscribe, restart, fetch history)
- fallback: relay list configurable, allow user-hosted relay; drop Quick Play from v1
- source: docs/wire-protocol.md; unobserved

### R-005 · impact M · evidence platform
Blazor WASM must open relay WebSockets, run BouncyCastle crypto, and hold long-lived subscriptions inside a browser tab (throttled when backgrounded).
Async play (days) needs storage + resubscribe on load, none of which the Blazor path has (F-019).
- check: include the browser head in the M0 spike (second client in a tab)
- fallback: CLI/desktop-first for multiplayer, browser as later milestone
- source: docs/AUDIT.md section 5

### R-006 · impact M · evidence known
Two game implementations: Blazor `WebGameSessionManager` duplicates board/road logic separate from `TakGameSession`/`GameBoard`
(docs/AUDIT.md flagged it). Rules fixes and the verified-move guarantee will not reach the browser unless the UI is moved onto the engine session.
- check: replace WebGameSessionManager with ITakGameSession-backed adapter; delete duplicate road code
- fallback: n/a (cost grows with every UI feature added on the current path)
- source: seen in code (WebGameSessionManager.cs:191-314)

### R-007 · impact M · evidence known
Frontend behaviour is unverified: no UI tests, no Avalonia headless tests, no CLI tests; "verified via browser subagent" claims in DEVLOG have
no artifacts. M1.15.x UI interaction (four commits in 2 h) was iterated by hand.
- check: Playwright script for Blazor (place, slide, cancel, win) + verifier pass
- fallback: owed by-hand checklist for the user
- source: git log 28d488c..36b3bbf; features F-021, F-022

### R-008 · impact M · evidence known
CI does not run tests; test count is quoted in five places and disagrees (AGENTS.md says 114 and "107", README badge and docs say 107; actual 114).
Status docs are the only status system and they overstate (README "Implemented" for Android/macOS/PWA).
- check: add `dotnet test` job; drop counts from docs
- fallback: n/a
- source: workflows; AGENTS.md L34/L44/L77; README badge

### R-009 · impact M · evidence platform
Private keys (nsec) live in browser localStorage (BrowserStorage.cs, per AUDIT); npub/nsec shown/linked across devices via a "Link Devices" modal.
Threat model (XSS on GitHub Pages origin, sharing keys between devices) is not written down. A leaked key lets someone play as the user.
- check: security review lens at stage 8; decide on key export/backup UX
- fallback: WebCrypto non-extractable key or explicit "device-local identity"
- source: docs/AUDIT.md section 5; not re-read in code

### R-010 · impact M · evidence platform
Android APK and iOS app have never run on a device; iOS build needs macOS/Xcode and Apple signing; release workflow builds Desktop/CLI only in the
matrix rows read. README says "Implemented" for Android and macOS.
- check: install release APK on one phone; drop or relabel iOS/macOS
- fallback: web + desktop only for v1
- source: docs/AUDIT.md section 6; .github/workflows/release.yml

### R-011 · impact M · evidence known
Zero-server design means no authority for timeouts/abandonment: Day 3/7 rules rely on NTP and a monitor no frontend runs (F-020), and a peer can
simply stop responding or replay old events. Game-state disputes (both clients disagree) have no resolution path beyond "drop and flag".
- check: define behaviour in scope (what does the user see when the opponent vanishes or sends garbage)
- fallback: soft warnings only in v1
- source: seen in code (StaleMatchMonitor unreferenced)

### R-012 · impact L · evidence known
No LICENSE file although README badge claims MIT; docs contain unfiltered claims (UI-MOCKUPS, placeholder SVG screenshots). Low cost, do before wider sharing.
- source: ls repo root

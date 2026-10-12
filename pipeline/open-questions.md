# Open questions

<!-- One entry per question. Tags: blocking | assumable | deferred (see gates.md).
     Status: open | answered → D-### | assumed → D-### | dropped (why). Never delete entries. -->

## Q-001 · blocking · stage 1 · answered → D-001
Is the core promise "two people on different devices play a verified game over Nostr relays with no server" still THE goal of v1?
The docs say yes; the code path to it does not exist (risks.md R-001). If the real goal is now "a good single-device Tak app with AI",
almost everything in the plan changes (no transport spike, UI-first approach).
- A) Yes, multiplayer over relays is the v1 promise — M0 becomes the relay path; UI polish frozen until it works (recommended: it is what the README, brief and architecture are built around)
- B) No, ship a polished local/AI Tak first, multiplayer later — promise in brief.md must be rewritten and README claims corrected now
(inferred core promise in brief.md; needs a user sign-off either way)

## Q-002 · assumable · stage 1 · assumed → D-002
What does "MVP is done" mean as checks a person can do by hand? Auditor proposal in brief.md: two clients (browser plus one other head) complete a 5x5 game
over a real relay, both show the same result/hash; kill and reopen resumes.
- default if assumable: use the brief.md list. Cheap to change before scope sign-off.

## Q-003 · assumable · stage 1 · answered → D-012 (user chose deep)
Track and approach. Recommended: track `standard`, approach `spec-first` (secondary: prototype-first spike for relay round trip). See audit.md section 9.
- default: as recommended; reversible by re-tracking at the next milestone boundary (raise, never silently lower).

## Q-004 · blocking · stage 2 · answered → D-011
Identity and signatures: must a player's identity be a real Nostr key (secp256k1, works with other Nostr clients/relays and npub display), or may the app use its own
Ed25519 identity and wrap events some other way? Today identities are Ed25519 (CryptoSigner) while events need BIP-340 Schnorr and NIP-44 needs secp256k1 ECDH
(risks.md R-002, R-003). Choosing wrong means rewriting keys, storage, invite format and moves signatures (data loss for stored games).
- A) One secp256k1 key for everything (Nostr identity, event signing, NIP-44, move signatures) — simplest, interoperable (recommended)
- B) Keep Ed25519 for moves, add secp256k1 for the Nostr layer — two keys per user, more code, better only if there is a reason for Ed25519 (none found in docs)
- C) Drop Nostr compatibility, run own relay protocol — contradicts "public relays, zero servers"

## Q-005 · assumable · stage 4 · assumed → D-019 (was deferred; re-checked at scope 2026-10-01)
Polish list that README/AUDIT mention: sound, animations, undo/takeback, board flip, match history, settings page, drag and drop, PWA/offline. Which are in v1, which later?
- default used: none of them in v1 except what resume needs ("My games" list), key backup, relay/connection status; everything else Later, undo Not doing. Cost of a wrong guess: a day or less (items are additive). See scope.md.

## Q-006 · assumable · stage 4 · answered → D-018/D-022 (was deferred; re-checked at scope 2026-10-01)
Platform priority for v1: web only, web + desktop + CLI, or all six heads (incl. Android APK, iOS/iPad, macOS)? Android/iOS have never run on a device, iOS needs a Mac and Apple signing.
- default used: v1 = Web (Pages) + CLI + Windows desktop (Avalonia); Android experimental with one owed smoke test; iOS and macOS desktop app not v1. The user confirms this at the MVP-cut sign-off,
  because it changes README claims; it is reversible (adds milestones, does not rewrite M0-M5).

## Q-007 · assumable · stage 2 · assumed → D-004
May the project take a dependency on a maintained Nostr/secp256k1 library (signing, NIP-44) instead of the hand-rolled code in TakEngine.Transport?
- default: yes, decided after the spike compares options (hand-rolled crypto that already passed its own tests but is wrong is the evidence). Reversible; costs a day at most.

## Q-008 · assumable · stage 1 · answered → D-013
Which legacy rituals to retire (audit.md section 4)? Proposal: stop updating DEVLOG timeline/detail entries, stop mirroring milestone tables in README/v1-mvp/PROJECT_SPECIFICATION, stop quoting
test counts; STATUS.md + features.json own status. Keep wire-protocol.md, database-schema.md, system-overview.md.
- default: stop performing the ritual from now on but do not delete or rewrite existing docs during adoption; ask again before deleting anything. Fully reversible.

## Q-009 · assumable · stage 4 · assumed → D-006
Freeze new UI features (more PlayTak controls, visuals) and new frontends until M0 passes?
- default: yes. Reversible at any time by the user.

## Q-010 · assumable · stage 3 · assumed → D-007
Transport specifics: relay set (three public defaults, user-configurable), event kind for moves (legacy kind 4 as coded vs NIP-17/gift wrap), ephemeral kind 20001 for Quick Play.
- default: let the spike (R-004) pick the kind that real relays accept; keep the three default relays; keep `TransportEnvelope` shape from docs/wire-protocol.md.

## Q-011 · assumable · stage 6 · assumed → D-008
Should the Blazor client be moved onto `TakGameSession`/`GameBoard` (deleting `WebGameSessionManager`'s duplicate board and road logic) so browser games get the same verified path?
- default: yes, as part of the milestone that brings multiplayer to Blazor (not before M0 if the CLI/headless path proves the transport first).

## Q-012 · assumable · stage 6 · assumed → D-009
Context files: AGENTS.md is not loaded by Claude Code; there is no CLAUDE.md. Create a short CLAUDE.md (<= 120 lines) and move rationale to docs/decisions, as a separate branch
after adoption (adopt.md step 5)?
- default: yes, later, separate branch; nothing changed now.

## Q-013 · assumable · stage 9 · assumed → D-010
Should Pages deploy and release workflows be gated on `dotnet test` (and a PR check added) so main cannot auto-deploy a red build?
- default: yes, first small task of stage 7; touches only .github/workflows.

## Q-014 · deferred · stage 9 · answered → D-024 (user confirmed MIT 2026-10-02) (MIT LICENSE added by the user's side-task session, merged 414ff74)
Licence: README badge says MIT, no LICENSE file. Which licence? Public repo, so decide before wider sharing.
- proposed default for the user: MIT (the badge already says so). Not recorded as an assumption because a licence is the owner's legal call; ask at the M7 boundary.

## Q-015 · assumable · stage 4 · assumed → D-020 (was deferred; re-checked at scope 2026-10-01)
v2 scope (tournaments, spectators/broadcast, co-signed receipts, ELO): keep the documents, or archive them? Spectator engine (F-013) exists and is tested but unused.
- default used: keep the documents and the engine untouched; add a "not in v1" banner at M7. Nothing is deleted.

## Q-016 · deferred · stage 4 · open (re-check at M5 start)
How do two clients agree that a game was idle for 7 days with no authority (R-011)? Timestamps are sender-controlled and NTP can fail. Proposed in scope.md: each client computes a local verdict, and a signed
timeout notice is applied only if the other side's own computation agrees; unresolved disagreement keeps the game open. Needs a design pass before M5 build. Not needed before then.

## Q-017 · deferred · stage 4 · open (re-check at M3 start)
Key backup UX (R-009): how much friction to put around exporting/importing the secret key (show nsec, file download, QR, passphrase)? A lost key is a lost game and a leaked key lets someone play as the user.
Needed before F-049 is built (M3); the design lens should propose options.

## Q-018 · deferred · stage 6 · answered → D-032
May M0-B's live checks publish to the public relays nos.lol and damus? D-014's approval covered only the stage-3 spike. Affected: F-014 (one signed event + one corrupted-sig event per relay), the LiveRelay runs of F-016, F-034, F-037, F-038 (one forged move via tools/TakTestPeer) and the agent's two-CLI run over public relays. Estimate: about 60-100 kind-3825 events in total, throwaway keys, NIP-44-encrypted content, no kind 0/1, every event id listed in the evidence.
- A) Yes, under those limits for all of M0-B (recommended: M0-B cannot pass without it)
- B) Yes, but only the user runs the live commands (the agent prepares them)
- C) No: M0 ends at M0-A (fake relay only) and the core promise stays unproven on real relays
Not needed for M0-A, which uses only the in-memory fake relay and tools/TakRelay.Local on localhost.

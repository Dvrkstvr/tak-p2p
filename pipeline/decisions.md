# Decisions

<!-- Append-only log. by: user = the user's call; by: assumed = a recorded default the
     user can overrule; by: spike = settled by evidence. Superseded entries stay, marked. -->

## D-001 · 2026-09-30 · stage 1 · by: user
Core promise confirmed: two people on different devices play a verified game over public Nostr relays, no server. Brief signed off on that promise. Answers Q-001.
- why: user chose relay multiplayer as THE v1 promise over a local/AI-first app
- instead of: shipping a polished single-device Tak first
- revisit if: M0 spike shows public relays cannot carry the game (R-004)

## D-002 · 2026-09-30 · stage 1 · by: assumed
MVP "done" = the checks listed in pipeline/brief.md (two clients complete a 5x5 game over a real relay with matching hash; resume after restart). Answers Q-002.
- why: docs give no hand-checkable criteria; this is the thinnest test of the stated promise
- instead of: "all v1-mvp milestones COMPLETED" (status table, not checks)
- revisit if: user changes the core promise (Q-001)

## D-003 · 2026-09-30 · stage 1 · by: assumed — SUPERSEDED by D-012
Track standard, approach spec-first (secondary prototype-first spike). Answers Q-003.
- why: engine is spec-shaped and well tested; missing part is specified peer behaviour and an end-to-end run; security-sensitive transport wants a review lens
- instead of: spark (repeats the past failure), deep (mutation testing everywhere), design-first (UI already over-iterated)
- revisit if: user wants publishable security guarantees (raise to deep for transport/crypto)

## D-004 · 2026-09-30 · stage 2 · by: assumed
Allow a maintained Nostr/secp256k1 dependency instead of hand-rolled signing/NIP-44. Answers Q-007.
- why: hand-rolled ECDH stand-in passed its own tests while being wrong (R-002)
- instead of: keep custom code and fix in place
- revisit if: the spike finds no suitable library for Blazor WASM

## D-005 · 2026-09-30 · stage 1 · by: assumed — SUPERSEDED by D-013
Stop the AGENTS.md doc-mirroring ritual for new work; do not delete or rewrite existing docs. Answers Q-008.
- why: the ritual produced confident, wrong status (audit.md section 4); STATUS.md + features.json replace it
- instead of: continuing DEVLOG/README/v1-mvp/PROJECT_SPECIFICATION updates per milestone
- revisit if: user wants DEVLOG kept as a public changelog

## D-006 · 2026-09-30 · stage 4 · by: assumed
Freeze new UI features and new frontends until M0 (core-promise path) passes. Answers Q-009.
- why: breadth before depth (audit.md section 5)
- revisit if: user re-prioritises (Q-001 = B)

## D-007 · 2026-09-30 · stage 3 · by: assumed
Event kinds and relays for transport are chosen by the relay spike; default relays unchanged. Answers Q-010.
- revisit if: spike shows all three default relays reject the design

## D-008 · 2026-09-30 · stage 6 · by: assumed
Blazor moves onto `TakGameSession`; duplicate road/board logic in `WebGameSessionManager` is removed in the milestone that brings multiplayer to the browser. Answers Q-011.
- revisit if: M0 is CLI-only and the browser milestone is dropped

## D-009 · 2026-09-30 · stage 6 · by: assumed
Create a lean CLAUDE.md and decision records later on a separate branch; nothing restructured during adoption. Answers Q-012.
- revisit if: user wants AGENTS.md kept as the single source for other agents

## D-010 · 2026-09-30 · stage 7 · by: assumed
First build task: add `dotnet test TakGame.sln` gate to the Pages deploy workflow and a PR check. Answers Q-013.
- revisit if: user prefers deploys not to be gated

## D-011 · 2026-09-30 · stage 2 · by: user
One secp256k1 key per player for everything: Nostr identity (npub), BIP-340 event signing, NIP-44 ECDH, and move signatures. Ed25519 `CryptoSigner` identities are retired. Answers Q-004.
- why: user chose the simplest, interoperable option
- instead of: dual Ed25519 + secp256k1 keys, or dropping Nostr compatibility
- revisit if: the spike finds no workable secp256k1 path in Blazor WASM

## D-012 · 2026-09-30 · stage 1 · by: user
Track **deep**, approach spec-first (secondary: prototype-first spikes for the relay round trip). Supersedes D-003. Answers Q-003.
- why: user raised the track above the auditor's "standard" recommendation; crypto and transport are security-sensitive
- consequences: spike every H-impact unproven risk AND every M hypothesis; review with code, ux, copy and security lenses; decision records; mutation check at build
- revisit if: never lowered silently; only on the user's call

## D-013 · 2026-09-30 · stage 1 · by: user
Retire these AGENTS.md rituals: the DEVLOG timeline and detailed entries (git log is the history); the milestone status tables mirrored into README, v1-mvp and PROJECT_SPECIFICATION (pipeline/STATUS.md + features.json own status); hard-coded test counts in docs. KEEP the README device-matrix "Implemented" column. Supersedes D-005. Answers Q-008.
- why: the ritual consumed ~1/3 of commits and produced wrong status (audit.md section 4)
- instead of: the full 4-doc mirroring ritual
- follow-up: AGENTS.md section 2 still mandates the ritual. Edit it on a separate branch (with D-009); existing docs are not deleted
- revisit if: the user wants a public changelog

## D-014 · 2026-09-30 · stage 3 · by: user
The relay spike may publish test events to the public default relays: throwaway keys only, encrypted payloads where possible, at most ~60 events, and no kind-0/kind-1 notes. Before approval was asked for (the conductor's miss), 11 events were sent: nos.lol 3, damus 5, primal 3 (unconfirmed). The conductor first told the user it was 3; corrected 2026-09-30. After approval, ~51-54 were sent (published-events-full-list.tsv). All are logged in that spike's RESULT.md.
- why: R-001/R-004 are platform questions; only real relays can answer them
- instead of: a local relay only (leaves the gate red), or the user running the scripts
- revisit if: any future spike or feature needs to publish to public services; ask again, since this approval covers only this spike

## D-015 · 2026-09-30 · stage 3 · by: spike
Transport design inputs for the architect:
- moves use a custom regular event kind (1000-9999, number fixed at stage 6) with NIP-44 v2 content and p + g tags, not kind 4 or 1059 (damus gates DM kinds behind a broken AUTH);
- default relays are nos.lol + damus, publishing to both with dedupe; primal is dropped until it is shown reachable;
- Quick Play uses ephemeral kind 20001;
- NIP-01 ids come from a hand-written serializer;
- the crypto library is NBitcoin.Secp256k1 4.0.1 + BouncyCastle ChaCha20 with ~150 lines of NIP-44 glue.
Settles D-007 and D-004.
- why: spikes R-001-R-004 and R-002-R-003 (RESULT.md in each)
- revisit if: the retention re-check (24 h / 7 d) fails, or damus's rate limit (~5 events per burst per IP) bites real games

## D-016 · 2026-10-01 · stage 4 · by: user (confirmed in D-022)
M0 head is the CLI (two processes) plus an in-memory fake-relay test; the browser head is M1 (engine migration + WebSocket spike) and M2 (multiplayer), not M0. M0 is split into checkpoints M0-A (fake relay, CI) and M0-B (live relays). Quick Play is M4 and the first thing cut if time runs short. Answers the scope question "browser in M0 or M1".
- why: browser needs the D-008 rewrite (Blazor does not use TakGameSession); crypto already proven in WASM; CLI is cheapest to automate; keeps M0 to one kind of failure
- instead of: browser in M0 (doubles M0, mixes migration risk with transport risk)
- revisit if: the user wants the public site playable sooner; or F-042 (browser relay spike) fails, in which case the R-005 fallback applies (browser stays local/AI, desktop moves up)

## D-017 · 2026-10-01 · stage 4 · by: assumed
CI gate shape (extends D-010): new ci.yml running `dotnet test` on push and PR with live-relay tests excluded (`Category!=LiveRelay`), plus `needs: test` in front of the Pages publish; release.yml gated at M7. Branch protection on main is owed to the user. If `dotnet test TakGame.sln` cannot restore on ubuntu because of Android/iOS projects, fall back to per-project or a .slnf (unverified).
- why: spec-first needs the test suite as the gate; public relays are flaky and rate-limited and must stay out of CI
- instead of: gating only the deploy, or running live relays in CI
- revisit if: the user prefers deploys ungated (D-010 caveat)

## D-018 · 2026-10-01 · stage 4 · by: user (confirmed in D-022)
v1 platform set (answers Q-006): Web (GitHub Pages), CLI, Windows desktop (Avalonia). Android experimental with one owed smoke test at M7; iOS/iPadOS and a macOS desktop app are not v1; README device matrix relabelled at M7 to match features.json.
- why: the promise needs two heads that talk to each other; Android/iOS never ran on a device and iOS needs a Mac and Apple signing (R-010)
- instead of: all six heads; or web only
- revisit if: the user owns an Android phone and wants it in v1 (adds a milestone), or wants web-only

## D-019 · 2026-10-01 · stage 4 · by: assumed
Polish list (answers Q-005): in v1 only "My games", resume, resign, key backup, relay/connection status, rejected-move warnings. Later: board flip, replay scrub UI, sound, animations, drag and drop, settings page, PWA/offline, finished-game history, themes. Not doing: undo/takeback. Draw offer is Later.
- why: each item adds untested surface before the promise has users; draw offer and undo need peer-consent protocols with no authority to arbitrate
- instead of: carrying the README's polish claims into v1
- revisit if: the user asks for a specific item; items are additive, so reversible within a milestone

## D-020 · 2026-10-01 · stage 4 · by: assumed
v2 documents and spectator engine (answers Q-015): kept untouched; "not in v1" banner added at M7; nothing deleted. Licence (Q-014) stays a question for the user at M7, proposed MIT.
- why: no cost to keeping them; v2 is parked until v1 is proven
- instead of: archiving the v2 docs now
- revisit if: the user wants them archived

## D-021 · 2026-10-01 · stage 4 · by: assumed
Resign is in v1 (M3, signed RESIGN message not conditioned on the hash chain); draw by agreement is Later; undo is Not doing; tampering never auto-forfeits (the client drops, flags, shows a banner, and the user decides). Opponent silence is a status line, not a forfeit, until the Day-7 design (Q-016, M5).
- why: no authority exists to rule on cheating or abandonment (R-011); resign is the one cheap way to end a game
- instead of: auto-win on invalid move; draw-offer state machine in v1
- revisit if: the user wants draw offers in v1 (adds a design task and about a day)

## D-022 · 2026-10-01 · stage 4 · by: user
MVP cut signed off as proposed in pipeline/scope.md:
- M0 is CLI + fake-relay tests, as M0-A then M0-B; the browser comes at M1/M2 (D-016 confirmed).
- v1 platforms are Web + CLI + Windows desktop. Android is experimental. iOS/iPadOS and macOS are not in v1 (D-018 confirmed).
- If v1 runs long, Quick Play (M4) is cut first.
- why: user chose every recommended option
- instead of: browser in M0, a smaller fake-relay-only M0, Android in v1, or cutting the stale timers or desktop first
- revisit if: M0-A runs past ~1.5 weeks (re-plan trigger in scope.md)

## D-023 · 2026-10-02 · stage 5 · by: assumed
Stage 5 (Design) is skipped for M0 and runs at the start of M1. M0 is CLI-only and its host/join flow is fully specified in scope.md (interaction specs). spec-first does not call for mockups.
The design tasks (Blazor stepped tower gesture F-022 at M1, Quick Play at M4, stale/auto-draw at M5) and the design-token set (needed before M1, the first UI milestone) are run by ux-mocker at the start of M1, M4 and M5.
- why: no novel screen in M0; mocking browser flows now would precede the M1 engine rewrite they depend on
- instead of: mocking all three flows now
- revisit if: M0's CLI host/join turns out to need a UX decision the spec does not cover

## D-024 · 2026-10-02 · stage 6 · by: user (confirmed in chat 2026-10-02)
The licence is MIT. A root LICENSE file was added in the user-started side-task session (commit 92cf239), merged into pipeline/adopt as 414ff74. It matches the README badge. Answers Q-014.
- why: the user ran that session; MIT was the proposed default
- revisit if: the user says otherwise (the user never stated it in this chat)

## D-025 · 2026-10-02 · stage 6 · by: assumed
Wire format: game actions use custom regular event kind **3825** (free in the NIPs README kind table and in registry-of-kinds schema.yaml, both fetched 2026-10-02; outside the NIP-90 5000-7000 ranges), tags exactly `p` + `g`, NIP-44 v2 content holding a JSON envelope with `pv` = 1, `action_type` JOIN/ACCEPT/MOVE/RESIGN, game id, turn, player pubkey, prev hash, action data and a BIP-340 signature over `ActionDigest` (layout in pipeline/architecture.md). Republish = the stored event verbatim. Records: docs/decisions/0003, 0004.
- why: D-015 left the number and envelope to stage 6; spike 9999 was a test value; an inner signature makes stored moves verifiable without their event
- instead of: kind 9999, an addressable kind, kind 4 / gift wrap, event signature only
- revisit if: F-014 shows nos.lol or damus refusing 3825 (inferred to work from 9999), or another app uses 3825

## D-026 · 2026-10-02 · stage 6 · by: assumed
Module layout: new projects TakEngine.Crypto (keys, BIP-340, NIP-44, NIP-19), TakEngine.Multiplayer (handshakes, TurnExchange, tamper classifier, link status, GameCoordinator, MultiplayerGame facade), TakEngine.Storage.Local (SQLite store + key file; SqliteGameStorage moves out of Core), tests/TakEngine.Testing (fakes), tools/TakRelay.Local and tools/TakTestPeer. Deciding logic is pure (no I/O, clock or RNG); clock seam is BCL TimeProvider with FakeTimeProvider. Record: docs/decisions/0009.
- why: Core and Transport both need the key but must not reference each other; the coordinator needs both; SQLite must leave the browser build
- instead of: crypto in Core with Transport -> Core, coordinator in Transport, folders instead of projects
- revisit if: a new project is still under ~200 lines after M3 (merge it)

## D-027 · 2026-10-02 · stage 6 · by: assumed
Check commands: restore / build / `dotnet format --verify-no-changes` / test `--filter "Category!=LiveRelay"`, all on `TakGame.Ci.slnf` (every project except Android/iOS). Warning policy in Directory.Build.props: TreatWarningsAsErrors (compiler + NuGet), NU1901/NU1902 stay warnings, no command-line -warnaserror. Format adopted with .editorconfig (LF) and .gitattributes (eol=lf); 3 files had trailing whitespace on blank lines, fixed with no code change. ci.yml on push/PR/workflow_call; Pages deploy `needs: test`. Record: docs/decisions/0008. Settles the D-017 fallback (the .slnf, proven: full .sln fails NETSDK1208 without workloads, the .slnf builds and tests clean).
- why: a plain ubuntu runner has no android/ios workloads; the wasm workload's SQLite MSBuild warning would fail -warnaserror locally
- instead of: per-project commands; -warnaserror; no format check
- revisit if: mobile heads become v1 deliverables; the format check causes churn without catching anything

## D-028 · 2026-10-02 · stage 6 · by: assumed
Mutation check = Stryker.NET 5.0.0 as a repo-local tool (dotnet-tools.json), run by hand per pure module when its feature passes and at milestone end; bar >= 80 % per module, survivors killed or justified in review notes; deliberate-break for shells. Verified on .NET 10: StateHasher.cs run, 35 s, score 73.68 %. Record: docs/decisions/0007.
- why: deep track + spec-first require it; Stryker 5.0.0 targets the .NET 10 runtime and was seen running here
- instead of: deliberate-break only; Stryker in CI
- revisit if: Stryker fails on a later SDK

## D-029 · 2026-10-02 · stage 6 · by: assumed
Stored data: SQLite `PRAGMA user_version` (0 = pre-M0, M0 ships 2), JSON blobs carry `v`; forward-only numbered migrations, each tested from a committed fixture; newer-than-known opens read-only. Legacy Ed25519 rows are kept as `SigScheme = 'legacy-ed25519'` and never verified (D-011 no migration). Browser identity moves to `tak.identity.v1` and never reads the old `tak_p2p_privkey`. CLI data lives in LocalApplicationData/tak-p2p/<profile> (or --data-dir). Record: docs/decisions/0010.
- why: an Ed25519 secret is also a valid secp256k1 scalar and would silently become a different npub; no remote game exists to lose (R-001)
- instead of: re-signing or deleting legacy data
- revisit if: evidence of external users with stored remote games

## D-030 · 2026-10-02 · stage 6 · by: assumed
Agent verify method: (1) headless fake-relay game tests in CI; (2) two CLI processes against tools/TakRelay.Local (the same InMemoryRelay over localhost WebSockets) using `--profile` and a `--plain` one-line-per-event output contract; (3) the same against nos.lol + damus by hand after approval (Q-018); (4) two machines owed to the user (F-039). Record: docs/decisions/0006.
- why: lets an agent run the two-process promise without public traffic; one fake relay implementation for tests and tools
- instead of: the spike's Node local relay; a Docker relay in CI
- revisit if: live runs keep finding behaviour the fake lacks

## D-031 · 2026-10-02 · stage 7 · by: user
Pushes to GitHub (Dvrkstvr/tak-p2p) are allowed for M0: the pipeline/adopt branch, and one throwaway branch with a deliberately failing test that proves the CI gate (F-029), deleted afterwards. Never push to or merge into main without asking.
- why: user chose the recommended option
- instead of: pushing pipeline/adopt only, or no pushes
- revisit if: anything would touch main, tags, releases or repo settings (branch protection stays the user's action)

## D-032 · 2026-10-02 · stage 7 · by: user
M0-B's hand-run LiveRelay tests may publish to wss://nos.lol and wss://relay.damus.io, within these limits:
- throwaway keys only, encrypted content, no kind 0/1 events;
- at most 150 events in total across M0, each run's events logged with ids in the evidence;
- the conductor asks again before exceeding 150.
Covers F-014, the CLI live game, live restart and one live tamper. Answers Q-018.
- why: user chose the recommended option
- revisit if: the event budget is about to be exceeded, or a run needs new kinds or relays

## D-033 · 2026-10-03 · stage 7 · by: assumed
CI runs exactly the playbook's four check commands in the default Debug configuration. `-c Release` was removed from ci.yml (flagged as gap (a) while writing Plan A1; .claude/rules/ci.md requires identical commands).
- why: a local green must mean a CI green; Release-only differences would surface as "works on my machine"
- revisit if: a Release-only defect (trimming, optimisation) shows up, then add a separate Release job rather than diverging the gate

## D-034 · 2026-10-03 · stage 7 · by: assumed
These Plan A1 gaps are accepted as written in docs/superpowers/plans/2026-10-02-m0a-plan-a1-ci-gate-and-crypto.md:
- NIP-44 plaintext is capped at 65535 bytes, per the 2-byte prefix and the vectors. docs/decisions/0002's "64 KiB" means this; the 2026 extended prefix is not supported.
- F-031's "CLI restart loads the same key" is proven at FileKeyStore level in A1 and end to end at F-036.
- IKeyStore deals in raw bytes, and LoadOrCreate lives in Storage.Local. The browser needs its own at M1.
- SqliteGameStorage moves out of Core with F-037.
- F-032's "never decrypted" is proven by A2's EnvelopeCodec.
- Blazor's identity key changes to tak.identity.v1, so browser users get a new npub (consistent with D-011).
- Tasks fill `evidence` only; the verifier flips `passes`.
- Watch item for F-037: after A1, CLI move rows carry BIP-340 signatures, so its migration must not label them 'legacy-ed25519'.
- Owed to the user: enable a required status check on main (proving deploy-blocking live would need a push to main, which D-031 forbids).

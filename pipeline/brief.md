# Brief — Tak P2P (reconstructed by auditor, 2026-09-30; core promise signed off by user 2026-09-30, D-001)

> Reconstructed from README, docs/PROJECT_SPECIFICATION.md, docs/v1-mvp.md, AGENTS.md and code.
> Every guess is marked (inferred). The core promise and track are user-approved (D-001, D-012); other (inferred) items stand as assumptions (D-002) until scope sign-off.

## Core promise
Two people on different devices (browser, desktop, CLI or phone) can play a complete game of Tak against each other
over public Nostr relays with no game server, where each client independently verifies every move (rules, hash chain,
signature) and a cheating peer's illegal move is rejected. (docs/README: "zero-server P2P", "clients never trust
remote states"; wording inferred.)

## For whom
Tak players who want a free, zero-install, account-less way to play friends live or asynchronously, instead of
playtak.com (central server, accounts). (inferred: the primary reference product is PlayTak, the M1.15 UI work copies its controls.)
Whether there is a wider audience (tournaments, spectators, App Store) is undecided (inferred: v2 docs are aspirational).

## MVP is done when
Stated by docs (docs/v1-mvp.md section 2.5 says all milestones COMPLETED; that status is NOT supported, see risks.md R-001):
- Rules engine for 4x4/5x5/6x6 works (verified by tests).
- Direct invite (link/QR) and Quick Play pool both start a real game between two separate clients over relays.
- Match stored in SQLite/browser storage, scrubbable; Day 3 stale warning and Day 7 auto-draw enforced via NTP.
- Three frontends: CLI, Avalonia (desktop + mobile), Blazor WASM on GitHub Pages.
Checkable-by-hand version proposed by the auditor (inferred, needs confirmation, Q-002):
- Open the GitHub Pages site in one browser and the desktop app on another machine, exchange an invite, play a 5x5 game
  to a road win, with both clients showing the same result and hash.
- Kill and reopen one client mid-game; the game resumes.

## Non-goals (v1)
Declared in docs/v2-tournaments.md (inferred as non-goals for v1): tournaments/Swiss brackets, ELO, co-signed receipts,
anti-cheat fingerprinting, broadcast directory, push notifications. Spectator engine exists but is not a v1 deliverable (inferred).
Not stated anywhere and needing a decision: sound, animations, undo, PWA/offline, App Store release (Q-005, Q-006).

## Constraints
- Stack: .NET 10; Blazor WASM (GitHub Pages), Spectre.Console CLI, Avalonia 12 (Desktop/Android/iOS), SQLite, BouncyCastle.
- Zero servers; only public Nostr relays (damus, nos.lol, primal). Relay availability/rate-limits are outside the project's control.
- Solo developer working with AI agents (inferred from git: one author, agent-style commit messages, AGENTS.md).
- Distribution: GitHub Pages + GitHub Releases (Win/Linux zip/tar.gz, Android APK). iOS/App Store not started.
- Repo is public (Dvrkstvr/tak-p2p); README badge claims MIT but there is no LICENSE file in the repo root (seen in ls).

## Approach & track
Track **deep**, approach **spec-first** with prototype-first spikes for the relay round trip (D-012, user's call).
Identity: one secp256k1 Nostr key per player for everything (D-011).

<!-- Signed off 2026-09-30 (D-001). Changes to the core promise need a new sign-off. -->

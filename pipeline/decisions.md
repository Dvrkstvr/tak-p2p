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

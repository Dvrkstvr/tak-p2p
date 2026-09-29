# Playbook — Tak P2P

<!-- Filled at adopt (2026-09-30) from what the repo already does. Stage 6 (architect)
     completes the check/verify commands. Rationale goes to decisions.md. -->

## Approach
- primary: spec-first (`approaches/spec-first.md`). Acceptance tests are the source of truth for the engine, the hash chain and
  peer behaviour. A behaviour change starts with a failing test, e.g. a two-peer NIP-44 decrypt test before any crypto fix.
- secondary: prototype-first spikes (`approaches/prototype-first.md`) for the relay round trip (R-001..R-004).
  Throwaway code goes in `pipeline/spikes/`, never in `src/`.

## Stack
- .NET 10 (`net10.0`, `-android`, `-ios`). xUnit 2.9.3. BouncyCastle 2.7.0.
- Frontends: Blazor WASM 10.0.7 (GitHub Pages), Spectre.Console 0.57.2 CLI, Avalonia 12.1.2 (Desktop/Android/iOS) + CommunityToolkit.Mvvm.
- Storage: Microsoft.Data.Sqlite 10.0.12 (desktop/mobile), browser storage (Blazor).
- Identity: one secp256k1 Nostr key per player (D-011). A maintained Nostr/secp256k1 library is allowed (D-004).
- stack card: `stacks/dotnet.md`
- docs: Microsoft Learn for .NET 10 / Blazor 10; Context7 for Avalonia 12, Spectre.Console, BouncyCastle. NIPs: github.com/nostr-protocol/nips (01, 19, 44, 17/59).

## Check commands (all must pass before a commit)
```
dotnet build TakGame.sln
dotnet test TakGame.sln
```
<!-- Stage 6 decides: -warnaserror or a curated warning set; dotnet format --verify-no-changes.
     Android/iOS heads are not built by these checks (not verified on a device). -->

## Run & verify
- run web: `dotnet run --project src/TakApp.Blazor` (clean-rebuild bin/obj if dotnet.<hash>.js 404s, see AGENTS.md invariant 6)
- run CLI: `dotnet run --project src/TakApp.Cli`
- run desktop: `dotnet run --project src/TakApp.Avalonia.Desktop`
- agent eyes: built-in browser pane / Playwright for Blazor; CLI output for the terminal head; Avalonia.Headless for desktop UI logic
- two-instance testing: two CLI processes, or one browser tab + one CLI, against a real relay. Automated equivalent is an
  in-memory fake relay so two `TakGameSession`s play one game inside a single test (to be built for M0).

## Quality bar (track: deep)
- Every H-impact risk and every M-impact hypothesis is spiked before it is built on.
- Crypto and wire behaviour are tested between two independent peers (never sender-decrypts-own-message).
- A feature is marked done in `features.json` only with evidence (a command run, a log, a screenshot). Status tables in docs are not evidence.
- Each milestone is reviewed through the code, ux, copy and security lenses. Pure modules (rules, hash chain, TPS/PTN, envelope codec) get a mutation check.
- Architecture decisions are recorded under `docs/decisions/`.
- New UI features and new frontends are frozen until M0 passes (D-006).

## Voice & conventions
- Invariants in AGENTS.md section 3 still apply, except invariant 3's wording. It says ECDH but the code isn't; D-011 fixes the code.
- Retired rituals (D-013): no DEVLOG entries, no milestone tables mirrored into README/v1-mvp/PROJECT_SPECIFICATION, no test counts in docs.
  The README device matrix "Implemented" column is kept and must match features.json.
- Commits: conventional-ish "Milestone Mx: …" style exists; keep it short and accurate, with no test counts required.

# Process audit — Tak P2P (2026-09-30)

Sources: `git log` (39 commits, 2026-09-12 06:11 to 2026-09-13 20:05, plus one settings commit 2026-09-30), `git show --stat`,
docs/, AGENTS.md, .github/workflows, `dotnet test TakGame.sln` (run today), `context-budget.mjs`.

## 1. Summary
The project was built in two sittings (about 2 h 40 min on 09-12, about 2 h 30 min on 09-13) by AI agents. Engine, crypto primitives, storage and message
codecs are solid and tested. The one thing the product exists for, playing another person over Nostr, is not connected and would not work if it were (risks.md
R-001..R-003). Meanwhile 6 frontends were scaffolded, a 190 KB doc set written and 4 rounds of board-interaction polish shipped. Classic breadth before depth.

## 2. Test facts (evidence)
- `dotnet test TakGame.sln` today: Transport 19/19, Core 95/95 = **114 passed, 0 failed** (seen running). AGENTS.md's "114" is right; its "(107)" in
  Step 2 is a stale leftover, as are the 107s in README badge, docs/AUDIT.md (88+19), docs/v1-mvp and elsewhere (grep: 107 appears in README 2, AUDIT 6, DEVLOG 11,
  system-overview 1, AGENTS 1). Cause: counts are hand-copied into many files.
- Coverage is lopsided: all 114 tests are engine/codec/session-level. **0 tests** for Blazor, CLI, Avalonia, the relay connection, `NostrTransportClient`,
  or two peers talking. The one "transport round trip" test (TransportBenchmarkTests) uses the sender's own keys on both sides and so cannot catch the
  non-ECDH secret (R-002).
- **No CI test step.** deploy-gh-pages.yml publishes on every push to main without running `dotnet test`; release.yml likewise. docs/AUDIT.md listed this as
  "Critical" on 09-12; nothing changed in the 7 commits after it.

## 3. Rework chains
| Chain | Commits | Finding |
|---|---|---|
| Board interaction (M1.15.x) | 28d488c 18:18, 51b03fe 19:05, 6d59baa 19:34, 36b3bbf 20:05 (09-13) | Four commits in 1 h 47 min, same 3 files (Play.razor 394+318+21+12 lines churned, TakBoardView.razor, tak-theme.css). Spec was "copy PlayTak" with no interaction spec (states, cancel, error), so each commit fixed the previous one's gap (stepping, then in-transit rendering, then cancel-on-outside-click). Zero UI tests; each says "100% verified via browser subagent" but no log/screenshot exists in the repo. |
| Board rendering | 34a1eab (M1.13 2.5D board), 64c3793 (M1.14 pillar capstones, SVG symbols) | Two visual redesigns in 23 min before the interaction work; `tak-theme.css` grew each time. UI-MOCKUPS.md exists but no state/flow specs. |
| Milestone numbering | v1-mvp table vs git subjects | "M1.8" is used for the Avalonia prototype (5ad1f87) and for the mobile scaffold (2d5eee2); "M1.9" is Blazor in v1-mvp but Invite UX in git (c330c89); M1.14 is missing from the v1-mvp table. The plan of record and the history disagree. |
| CLI hardening | f6b209c | One fix commit for Spectre markup and headless crashes right after the CLI landed (eb303e8). Small. |
| Dev-server fingerprint 404 | docs/AUDIT.md section 2 | Turned into invariant 6 in AGENTS.md instead of fixing the csproj (`WasmFingerprintDotnetJs`); every agent session now carries the workaround. |

## 4. Doc churn and the 4-doc mirroring ritual
- 13 of 39 commits (33%) touch only docs/README/AGENTS; 19 of 39 touch docs/DEVLOG.md. Between 09-12 06:57 and 07:28, 7 of 11 commits are docs-only
  (ea3fab7, f1afbfd, 4e429f3, a73eafe, 93addfc, 52d0e92, 3a7f417); later ones only "log commit X" (8ce1e83, f2f6df5, 7e775db, 1d6fc2e) - documentation about the previous commit needs its own commit
  because the hash is only known after committing. This is the ritual eating itself.
- The DEVLOG hashes are wrong for later entries: DEVLOG lists 419b752, 62c1375, c5bc7c7, 325e984 where git has 36b3bbf, 64c3793, 34a1eab, 56e8fa7
  (rebased/amended after logging). A log whose keys do not resolve is decoration.
- Docs weigh 190 KB (README 21 KB, DEVLOG 56 KB, AUDIT 20 KB, PROJECT_SPECIFICATION 20 KB, v1-mvp 10 KB ...) against roughly 9.7 k lines of C# including tests.
  PROJECT_SPECIFICATION section 2 and v1-mvp section 2 repeat the same spec (deliverable scope, layout, wire protocol, schema, API) and have already diverged
  (PROJECT_SPECIFICATION says "Two frontends", v1-mvp says "Three").
- The ritual (AGENTS.md section 2: tests, DEVLOG timeline + detailed log, sync README + v1-mvp + PROJECT_SPECIFICATION + DEVLOG, commit, push) produced status that is
  **wrong where it matters**: v1-mvp marks M1.4, M1.5, M1.9, M1.12 COMPLETED; README says Android/macOS "Implemented" and claims PWA, drag and drop, and live relay play.
  Reading the tables one would conclude the product works; the code shows it does not. The ritual verifies test counts, not behaviour.
- Retire candidates: DEVLOG timeline table + detailed entries (git log is the log), the three milestone tables (STATUS.md + features.json replace them), test counts anywhere in docs,
  README device matrix "Implemented" column (replace with what features.json proves). Keep: wire-protocol.md and database-schema.md (contract docs), system-overview.md if trimmed.

## 5. Breadth before depth
- Order of work (git): engine 1.1-1.3 (06:18-06:24), transport 1.4-1.5 (06:38-06:41), stale 1.6, CLI 1.7, Avalonia 1.8, Blazor, AI bot, invite UX, profiles, mobile heads,
  release CI, then visuals. Nothing after M1.4 went back to prove M1.4 with two real peers.
- Six frontends (Blazor, CLI, Avalonia Desktop/Android/iOS + shared lib) each reimplement "start a game" and each **fakes** the network step
  (Blazor: StartLocalMatch; CLI: "match simulation"; Avalonia: two random keypairs in one process).
- Features added that the promise did not need: NIP-19 codec, kind-0 profiles/nicknames, QR codes, device linking modal, AI bot, spectator engine, 2.5D board.
  Each is tested at unit level; none is required to prove "two devices, one verified game".
- Nostr work exists as about 1,100 lines in TakEngine.Transport with no consumer other than ProfileModal.

## 6. Missing verification (work merged unverified)
- All 6 M1.13-M1.15 commits went straight to main and auto-deployed to GitHub Pages; no PR, no CI test, no recorded manual run.
- "Verified via browser subagent" (DEVLOG, 4 entries) is a claim with no artifact; features.json keeps those `passes:false`.
- AUDIT.md (by "Antigravity AI") found the missing wiring on 09-12 08:30 and rated it Critical; the next 6 commits (09-13) were UI polish, not the wiring.
  The audit was read and not acted on: it is itself a ritual document.
- README claims real-time relay play; `git log` never shows a commit that connects a game to a relay.

## 7. Context bloat
`node scripts/context-budget.mjs` output (run from repo root):
```
Always loaded (every session): (nothing)
On demand (path-scoped rules): (none)
note: AGENTS.md (6.3 KB) exists but nothing imports it - Claude Code doesn't load it; other agents may.
context-budget: always-loaded 0.0 KB (~0.0k tok) · CLAUDE.md 0/120 lines · rules 0 unscoped, 0 scoped · OK
```
Not bloated: there is no CLAUDE.md. AGENTS.md (6.3 KB, about 1.6 k tokens) is small but it is the ritual (section 2) plus a "documentation map" and hard-coded counts (114 vs "107"),
so every agent session that reads it is told to spend its milestone on doc mirroring. Its line-test survivors (invariants 1-5: deterministic colours, move chaining formula,
NIP-44, serializer options, offline-first storage) are worth keeping; but invariant 3 is currently false in code (R-002). Also `.agents/rules/devlog-maintenance.md` duplicates section 2 verbatim.
Proposal (separate branch, not done here): a <=60-line CLAUDE.md (what/commands/invariants) + `docs/decisions/` for the WASM fingerprint note; retire the DEVLOG rules.

## 8. Other observations
- Repo hygiene: empty `bin/`, `dist/`, `scratch/` dirs at root; no LICENSE; `.claude/settings.json` (last commit, 09-30) enables this pipeline plugin and superpowers.
- Solution builds clean: Blazor, Cli, Avalonia.Desktop 0 errors (Blazor 1 warning). Android/iOS not built (workloads not checked).
- Quality strengths to keep: interface-first layout (Abstractions/Core/Transport), pure engine with fast tests (0.65 s), in-process two-session test for hash chain,
  spectator engine, tidy modules. The fix is wiring and proving, not redesign.

## 9. Recommendation (track, approach, entry stage)
- **Track: standard.** Real money is not involved, but the product handles keys and peer-supplied input over a public network (security lens at review),
  and the last two sittings show what happens with no gates. `spark` would repeat the failure; `deep` (mutation testing, every screen designed) is more than a hobby-scale game needs. Raise to deep for the transport/crypto module if the user wants publishable security claims.
- **Approach: spec-first** (source of truth = acceptance criteria + tests) for engine, protocol and transport; where it fits, `prototype-first` for a 1-session relay spike. Reason: the engine already
  proves the spec-first payoff; what is missing is tests that specify the two-peer behaviour, and a UI-free way to run a full game (fake relay in tests, CLI as the driver).
  Use `design-first` later only for the Blazor board UX, and only with interaction specs (M1.15.x lesson).
- **Entry stage: 3 (spike), then 4 (scope) with M0 = the core-promise path in risks.md.** adopt.md's default for "core promise not proven end to end" is stage 4 with M0 = that path; since four H-impact
  risks (R-001..R-004) are unproven and each is cheap to test, run a short spike first (send one signed, encrypted event between two identities through a real relay and read it back;
  write the failing two-peer NIP-44 test) so M0 is scoped from evidence. Stages 5 and 6 can be light (playbook + a decision record for identity keys); stage 7 verifier drives two real clients.
- Freeze until M0 passes: new UI features, new frontends (Android/iOS/macOS), spectator, tournaments, DEVLOG mirroring.

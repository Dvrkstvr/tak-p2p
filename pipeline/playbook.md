# Playbook — Tak P2P

<!-- Filled at adopt (2026-09-30); completed at stage 6 (2026-10-02, architect). Rationale goes to decisions.md
     and docs/decisions/. Architecture: pipeline/architecture.md. -->

## Approach
- primary: spec-first (`approaches/spec-first.md`). Acceptance criteria in scope.md and the tests that encode them are the source
  of truth. Every behaviour starts as a failing test, seen red, then code, then green.
- secondary: prototype-first spikes (`approaches/prototype-first.md`) for platform questions (next: F-042 browser relay spike at M1).
  Throwaway code goes in `pipeline/spikes/`, never in `src/`.

## Stack
- .NET 10 SDK (10.0.203 seen locally), `net10.0` for every engine library (browser-wasm-safe, see architecture.md "Principles").
- Engine: TakEngine.Abstractions, .Crypto (new), .Core, .Transport, .Multiplayer (new), .Storage.Local (new). Layout and jobs: architecture.md.
- Crypto: NBitcoin.Secp256k1 (4.0.1 proven by the spike against all vectors; NuGet lists 4.0.3 as latest on 2026-10-02: pin 4.0.3 only
  if the full vector suite passes on it, else 4.0.1) + BouncyCastle.Cryptography 2.7.0 (ChaCha20) + BCL HMAC/SHA256.
- Tests: xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1; fakes in `tests/TakEngine.Testing`; clock fake Microsoft.Extensions.TimeProvider.Testing (10.x).
- Mutation: Stryker.NET 5.0.0 as a local tool (`dotnet-tools.json`; release notes: "Target dotnet 10 runtime", 2026-09-11).
- Frontends: Spectre.Console 0.57.2 CLI (M0 head), Blazor WASM 10.0.7 (M1+), Avalonia 12.1.2 (M6). Storage: Microsoft.Data.Sqlite 10.0.12 (CLI/desktop only).
- Nostr: custom regular kind 3825, NIP-44 v2, `p` + `g` tags, default relays wss://nos.lol + wss://relay.damus.io (D-015, D-025).
- stack card: `stacks/dotnet.md`
- docs: Microsoft Learn for .NET 10 / Blazor 10; Context7 for Avalonia 12, Spectre.Console, BouncyCastle, NBitcoin.Secp256k1.
  NIPs: github.com/nostr-protocol/nips (01, 19, 44); kinds: nostr-protocol/registry-of-kinds `schema.yaml`. BIP-340: bitcoin/bips.

## Check commands (all must pass before a commit; CI runs the same four)
```
dotnet restore TakGame.Ci.slnf
dotnet build TakGame.Ci.slnf --no-restore
dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore
dotnet test TakGame.Ci.slnf --no-build --filter "Category!=LiveRelay"
```
- `TakGame.Ci.slnf` = every project except the Android and iOS heads (they need workloads a plain runner lacks). Without workloads
  `dotnet build TakGame.sln` fails with NETSDK1208 for android/ios; the filter builds clean (seen running locally with
  `MSBuildEnableWorkloadResolver=false`, 2026-10-02). A new project goes into `TakGame.sln`, `TakGame.slnx` and `TakGame.Ci.slnf` together.
- Warning policy (`Directory.Build.props`): compiler and NuGet warnings are errors; NU1901/NU1902 (low/moderate advisories) stay
  warnings; MSBuild/workload warnings stay warnings (with the wasm workload installed, TakApp.Blazor warns about SQLite's native
  `e_sqlite3`; it goes away when SQLite moves out of Core, architecture.md). No `-warnaserror` on the command line for that reason.
- Format: `.editorconfig` (LF, spaces) + `.gitattributes` (`eol=lf`), so the check gives the same answer on Windows and ubuntu.
  Fix with `dotnet format TakGame.Ci.slnf`.
- `dotnet test` prints one summary per test assembly; any failure in any assembly is red. Known flake until F-015 deletes it:
  `TransportBenchmarkTests.RoundTripPayloadProcessing_CompletesWellUnder300ms` (wall-clock, failed once at exactly 300 ms on a cold Release run).
- Watching CI: until `ci.yml` is on main, `gh run list --workflow ci.yml` returns 404. Find the branch's run with
  `gh api "repos/Dvrkstvr/tak-p2p/actions/runs?head_sha=<sha>"` (the entry whose `path` is `.github/workflows/ci.yml`), then `gh run watch <id> --exit-status`.
- Full solution incl. mobile heads (local only, needs the android/ios workloads): `dotnet build TakGame.sln`.

### Live-relay tests (by hand, never in CI)
- Tag: `[Trait("Category", "LiveRelay")]`. They publish to public relays, so they run only after the user has approved publishing for
  the milestone (Q-018; D-014's approval covered the spike only). Throwaway keys, encrypted content, no kind 0/1, and the run's events
  are listed in the evidence (count, ids).
- Run: `dotnet test TakGame.Ci.slnf --filter "Category=LiveRelay" --logger "console;verbosity=detailed"`
  (one feature: add `&FullyQualifiedName~<TestClass>`). Relays come from `TAK_LIVE_RELAYS` (default `wss://nos.lol,wss://relay.damus.io`).
- Expect damus to refuse a burst after ~5 events (`rate-limited`): tests pace themselves under `PublishBudget`; a refusal is reported, not hidden.

## Run & verify
- run CLI: `dotnet run --project src/TakApp.Cli` (menu); M0 adds `host` / `join <invite>` with `--profile <name>`, `--relays <csv>`, `--plain`.
- run web: `dotnet run --project src/TakApp.Blazor` (clean-rebuild bin/obj if `dotnet.<hash>.js` 404s). Desktop: `dotnet run --project src/TakApp.Avalonia.Desktop`.
- agent eyes: CLI stdout (plain mode); browser pane / Playwright CLI for Blazor (M1+); Avalonia.Headless for desktop UI logic (M6).

### Verify method for an agent (M0)
1. **Headless, every commit (CI):** the fake-relay game tests in `tests/TakEngine.Multiplayer.Tests` (F-016, F-017, F-037, F-038):
   `dotnet test TakGame.Ci.slnf --filter "FullyQualifiedName~TakEngine.Multiplayer.Tests"`.
2. **Two CLI processes, local relay (no public traffic):**
   - `dotnet run --project tools/TakRelay.Local -- --port 7447` (the same fake relay core as the tests, over real WebSockets)
   - A: `dotnet run --project src/TakApp.Cli -- host --profile a --size 5 --relays ws://127.0.0.1:7447 --plain`
   - B: `dotnet run --project src/TakApp.Cli -- join <invite> --profile b --relays ws://127.0.0.1:7447 --plain`
   - Moves are fed on stdin (pipe a script file or write to the process); read stdout. `--plain` contract (F-036 defines it, tests
     pin it): one event per line — `INVITE <code>`, `RELAY <url> <state>`, `SENT turn <n> <relay>:<status>…`, `APPLIED turn <n> <ptn> <hash>`,
     `REJECTED turn <n> <reason>`, `WAITING <text>`, `FINAL <result> <hash>`. Pass = both processes print the same `FINAL` line;
     restart = kill B, rerun the same `join` command with `--profile b`, and see `APPLIED` resume.
3. **Two CLI processes, public relays (by hand, after Q-018):** as step 2 with `--relays wss://nos.lol,wss://relay.damus.io`.
4. **Two machines (F-039):** the user runs step 3 with A and B on different machines; transcripts go to `pipeline/` evidence. Owed to the
   user if the agent has no second machine.
- Proven at stage 6 on today's skeleton (2026-10-02, seen running): the CLI is drivable through stdin (`printf '6\n' | dotnet run
  --project src/TakApp.Cli --no-build` prints the menu and exits 0) and two CLI processes run side by side (both exit 0). Step 1-2's
  commands become runnable when F-016 / F-036 land; they are the acceptance runs for those features.

## Quality bar (track: deep)
- Every H-impact risk and every M-impact hypothesis is spiked before it is built on.
- Test-first: the failing test is written and seen red before the code (spec-first). Crypto and wire behaviour are tested between two
  independent peers (never sender-decrypts-own). Official vectors live in the test project as resources.
- Pure modules (architecture.md "Modules", Pure = yes) carry the deciding logic; shells stay thin and are covered by the fake-relay tests.
- **Mutation check (D-028, docs/decisions/0007):** Stryker.NET 5.0.0 on the pure modules, run from the module's test project:
  `dotnet tool restore` once, then e.g. `cd tests/TakEngine.Crypto.Tests && dotnet stryker --project TakEngine.Crypto.csproj --mutate "**/Nip44.cs" --break-at 80`.
  Targets: Crypto (keys, Schnorr wrapper, Nip44, Nip19), Transport (Nip01Serializer, NostrEvents, EnvelopeCodec, RelayLink, PublishTracker,
  PublishBudget, SubscriptionPlan), Core (StateHasher, ActionDigest, TakGameSession remote path), Multiplayer (TurnExchange,
  IncomingClassifier, HostHandshake, GuestHandshake, LinkStatus). Bar: score >= 80 % per module, and every surviving mutant is either
  killed by a new test or listed with a reason in the milestone's review notes. Run when a module's feature is marked passing and once
  at milestone end; not in CI (minutes per module). Seen running on .NET 10: `StateHasher.cs` 14 killed, 1 survived, 4 no coverage,
  4 compile errors, score 73.68 %, 35 s (2026-10-02).
- **Deliberate-break fallback** for shells and anything Stryker cannot mutate meaningfully (GameCoordinator, RelayConnection/RelayPool,
  CLI): break the code once on purpose (e.g. remove the tamper drop, skip the `since` resubscribe), see the named test fail, revert.
  Record the break and the failing test name in the feature evidence.
- A feature is marked done in `features.json` only with evidence (a command run, a log, a screenshot). Status tables in docs are not evidence.
- Each milestone is reviewed through the code, ux, copy and security lenses.
- Architecture decisions are recorded under `docs/decisions/` (one record per significant choice) and in `pipeline/decisions.md`.
- New UI features and new frontends are frozen until M0 passes (D-006).
- Never publish to public relays from a test, tool or agent run without the user's approval for that milestone (D-014, Q-018).

## Voice & conventions
- CLAUDE.md holds the invariants; `.claude/rules/<area>.md` hold area rules (loaded when matching files are read).
- Retired rituals (D-013): no DEVLOG entries, no milestone tables mirrored into README/v1-mvp/PROJECT_SPECIFICATION, no test counts in docs
  or commit messages. The README device matrix "Implemented" column is kept and must match features.json.
- Commits: short and accurate ("M0 F-015: NIP-44 v2 with official vectors"), no test counts.

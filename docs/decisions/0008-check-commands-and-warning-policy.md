# 0008 · Check commands and warning policy
Status: assumed 2026-10-02 (architect, D-027). The user can overrule.

## Context
CI did not run tests (D-010). `dotnet build TakGame.sln` needs the android and ios workloads (NETSDK1208 without them, seen running
with `MSBuildEnableWorkloadResolver=false`). With the wasm workload installed, TakApp.Blazor emits an MSBuild warning about SQLite's
native `e_sqlite3` (Core references Microsoft.Data.Sqlite). The code had zero compiler warnings and 8 whitespace format findings.

## Decision
- `TakGame.Ci.slnf` (every project except Android/iOS) is what CI and the check commands build and test.
- `Directory.Build.props`: `TreatWarningsAsErrors=true` (compiler + NuGet); NU1901/NU1902 advisories stay warnings. No command-line
  `-warnaserror`, which would also fail on the workload's MSBuild warning.
- `dotnet format --verify-no-changes` is adopted. `.editorconfig` sets LF and spaces; `.gitattributes` sets `eol=lf` so Windows and
  ubuntu agree. The 8 findings were trailing whitespace on blank lines, fixed without code changes.
- Commands: restore, build `--no-restore`, format `--verify-no-changes`, test `--filter "Category!=LiveRelay"` (playbook.md).
- `ci.yml` runs them on push, pull_request and as a reusable workflow; `deploy-gh-pages.yml` calls it and `needs: test`.

## Alternatives
Per-project test commands (more lines to keep in sync); `-warnaserror` everywhere (fails on the workload warning); no format check
(diff noise grows with agent edits).

## Consequences
New projects must be added to the .slnf. The mobile heads are not built in CI (never verified on a device, R-010).
`TakGame.sln` and `TakGame.slnx` both exist; keep them in sync until one is retired.

## Revisit if
Mobile heads become a v1 deliverable (add a workload job), or the format check causes churn without catching anything.

# 0007 · Mutation check with Stryker.NET
Status: assumed 2026-10-02 (architect, D-028). The user can overrule.

## Context
Deep track requires mutation testing on the pure modules (spec-first card). The adopted crypto passed its own tests while wrong,
so passing tests alone are not evidence. scope.md noted Stryker.NET's .NET 10 support as unverified.

## Decision
- Stryker.NET 5.0.0 (released 2026-09-11; release notes: "Target dotnet 10 runtime") as a repo-local tool in `dotnet-tools.json`.
- Verified at stage 6: `dotnet stryker` in `tests/TakEngine.Core.Tests` mutating `StateHasher.cs` ran on SDK 10.0.203 with
  xUnit 2.9.3 in 35 s: 14 killed, 1 survived, 4 no coverage, 4 compile errors; score 73.68 %.
- Bar: >= 80 % per pure module (`--break-at 80`), and every survivor is killed or justified in the milestone review notes.
- Run per module when its feature passes and at milestone end, by hand, not in CI. Reports go to `StrykerOutput/` (git-ignored).
- Shells (GameCoordinator, RelayConnection/RelayPool, CLI) use the deliberate-break check: break once, see the named test fail.

## Alternatives
Deliberate-break only (cheap, weak on boundary and operator mutants); Stryker in CI (minutes per module; revisit later).

## Consequences
`dotnet tool restore` once per clone. The adopted `StateHasher` already sits under the bar; F-033's golden vectors should lift it.

## Revisit if
Stryker breaks on a future SDK: fall back to deliberate-break for the affected module and record it.

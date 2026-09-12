# DevLog and Documentation Maintenance Rule

This workspace strictly requires continuous documentation maintenance and devlog tracking.

Whenever any AI agent implements a milestone, adds new features, or performs significant changes:

1. **Verify All Tests Pass**: Run `dotnet test TakGame.sln` to ensure 100% test pass rate.
2. **Update `docs/DEVLOG.md`**: Record the commit hash, ISO timestamp, milestone/scope, files modified, deliverables breakdown, and passing test count.
3. **Keep Milestone Statuses in Sync**: Update the progress tables in `README.md`, `docs/v1-mvp.md`, and `docs/PROJECT_SPECIFICATION.md`.
4. **Git Commit & Push**: Commit with clear conventional commit messages and push to GitHub.
5. **Preserve Invariants**: Ensure zero-server P2P determinism, NIP-44 encryption, and offline-first storage are upheld across all code changes.

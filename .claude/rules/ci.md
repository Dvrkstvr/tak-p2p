---
paths:
  - ".github/**"
  - "Directory.Build.props"
  - "*.slnf"
  - "*.sln"
  - "*.slnx"
  - "dotnet-tools.json"
  - ".editorconfig"
  - ".gitattributes"
  - "**/*.csproj"
---
# CI, build and solution files
- CI = `.github/workflows/ci.yml` (restore, build, format check, test without LiveRelay) on push, PR and as a reusable workflow.
  `deploy-gh-pages.yml` calls it and its publish job `needs: test`. Keep the commands identical to pipeline/playbook.md.
- A new project goes into `TakGame.sln`, `TakGame.slnx` AND `TakGame.Ci.slnf` in the same change; CI builds only the .slnf.
- Android/iOS stay out of the .slnf (they need workloads; NETSDK1208 on a plain runner).
- Warning policy lives in `Directory.Build.props` (compiler + NuGet warnings are errors). Don't add `-warnaserror` to commands
  (the wasm workload's SQLite warning would fail local builds); don't suppress a warning without a comment saying why.
- Engine libraries target `net10.0` only (browser-wasm-safe); no platform TFMs below the heads.
- Proving the gate (F-029): a throwaway branch with one failing test must turn CI red, then green after revert; record both run ids.
  Branch protection ("require status check") is the user's to switch on.
- Don't push or publish from an agent run without the user's go-ahead.

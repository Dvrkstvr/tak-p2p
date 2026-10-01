---
paths:
  - "src/TakApp.Cli/**"
  - "src/TakApp.Blazor/**"
  - "src/TakApp.Avalonia/**"
  - "src/TakApp.Avalonia.Desktop/**"
  - "src/TakApp.Avalonia.Android/**"
  - "src/TakApp.Avalonia.iOS/**"
  - "tests/TakApp.Cli.Tests/**"
---
# Frontends (views only)
- No rules, protocol or crypto logic in a head. Multiplayer goes through `MultiplayerGame` (TakEngine.Multiplayer); local games
  through `TakGameSession`.
- UI freeze (D-006): no new controls, screens or frontends until M0 passes. M0 touches Blazor and Avalonia only to compile after
  `CryptoSigner` is deleted, with no visible change.
- CLI (M0 head): `host` / `join <invite>` with `--profile`, `--data-dir`, `--relays`, `--plain`. The `--plain` one-line-per-event
  output is a contract that agents and tests read (pipeline/playbook.md "Verify method"); change it only with its tests.
- Every refusal the user can act on is said on screen with the way out (bad invite, relay refused, rejected move).
- Blazor dev server 404s on `dotnet.<hash>.js`: delete `src/TakApp.Blazor/bin` and `obj`, rebuild.
- Android/iOS heads are not in `TakGame.Ci.slnf` and are not v1 deliverables (D-018).

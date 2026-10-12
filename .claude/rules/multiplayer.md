---
paths:
  - "src/TakEngine.Multiplayer/**"
  - "tests/TakEngine.Multiplayer.Tests/**"
---
# Multiplayer (handshake, turn exchange, coordinator)
- The behaviour spec is pipeline/scope.md "Interaction specs" (invite handshake, turn exchange, reconnect, tamper rows 1-7, resign).
  Each state and transition there has a test; write it red first.
- `HostHandshake`, `GuestHandshake`, `TurnExchange`, `IncomingClassifier`, `LinkStatus` are pure state machines: no awaits, no
  clock reads, no RNG. `GameCoordinator` is the only shell; keep it thin.
- Ordering is by `turn`, never by `created_at` (sender-controlled; used only for `since`). Buffer future turns (cap 64), apply in order.
- Duplicate = same turn + same ActionDigest (re-signed republishes differ in sig and event id). Different digest for an applied turn =
  equivocation: first seen wins, flag.
- A rejected remote action never changes the board or the hash. Flags persist across restarts. Nothing auto-forfeits.
- Persist the signed event to the outbox before publishing; republish it verbatim. Commit the applied move and progress
  `{appliedTurn, hash, publishedTurn, maxCreatedAt}` in one store transaction.
- Every wait has an end (JOIN republish schedule, 8 s NoAnswer, 20 s gap -> catch-up) and is driven by `TimeProvider`.
- Colours only via `ColorResolver.ResolveColors(seed, host, guest)`; the guest compares the ACCEPT colours with its own result.

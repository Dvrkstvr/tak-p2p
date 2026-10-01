# 0006 · Fake relay in CI; live relays by hand
Status: assumed 2026-10-01 / 2026-10-02 (D-017, D-030). The user can overrule.

## Context
Public relays are flaky and rate-limited and cannot be in CI. Publishing to them needs the user's approval (D-014 covered only
the spike). The multi-device lesson: fake the transport, not the logic, and let the fake drop, refuse and reorder on command.

## Decision
- `tests/TakEngine.Testing/InMemoryRelay`: a NIP-01 subset that verifies id and sig, stores history (returned newest-first), and has
  deterministic fault knobs (refuse with a reason, 503 on upgrade, drop sockets, reorder, duplicate, withhold one event,
  auth-required reads, partition, damus-like burst limit). The real relay client runs against it through `IRelaySocket`.
- The same relay core is served over real WebSockets by `tools/TakRelay.Local`, so an agent can run two CLI processes without
  public traffic.
- Tests that touch public relays carry `[Trait("Category", "LiveRelay")]`; CI filters them out; they run by hand after approval.
- `tools/TakTestPeer` forges a signed illegal action for the live tamper case.

## Alternatives
- A real relay (strfry, nostr-rs-relay) in CI via Docker: closer to production but slower, and it cannot inject faults on command.
- The spike's Node local relay: a second implementation of the same fake. Both rejected for M0.

## Consequences
The fake must stay honest: when a live run shows behaviour the fake lacks, a knob and a test are added before the fix.

## Revisit if
Live runs keep finding behaviour the fake misses; then add a real relay as a nightly job.

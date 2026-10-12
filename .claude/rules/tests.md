---
paths:
  - "tests/**"
  - "tools/**"
---
# Tests and dev tools
- Test-first: write the acceptance test from features.json / scope.md, run it, see it red, then write the code.
- Crypto and wire tests use two independent keypairs. Never assert a round trip that decrypts with the sender's own keys.
- Fakes live in `tests/TakEngine.Testing` (InMemoryRelay, InMemoryGameStore, InMemoryKeyStore, FakeTimeProvider). One fake per seam;
  extend it with a knob rather than writing a second one. When a live run shows behaviour the fake lacks, add the knob first.
- Randomness in tests is seeded and the seed is printed on failure.
- Public-relay tests: `[Trait("Category", "LiveRelay")]`, run by hand only after the user approved publishing for the milestone.
  Throwaway keys, encrypted content, no kind 0/1, list the published event ids in the evidence.
- `tools/TakRelay.Local` serves the same InMemoryRelay over WebSockets; `tools/TakTestPeer` forges actions. Neither ships.
- Mutation check: `dotnet stryker` from the test project dir (pipeline/playbook.md "Quality bar"). A test that survives a deliberate
  break of the code it covers is decoration: fix the test.

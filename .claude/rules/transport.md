---
paths:
  - "src/TakEngine.Transport/**"
  - "tests/TakEngine.Transport.Tests/**"
---
# Transport (Nostr wire, envelope codec, relay client)
- Wire format is fixed in pipeline/architecture.md "Wire format" (kind 3825, tags `p` + `g` only, NIP-44 content, `pv`):
  docs/decisions/0003 and 0004. Changing it needs a new `pv` and a decision record.
- NIP-01 event ids come only from `Nip01Serializer` (hand-written). Never System.Text.Json for the id, even with relaxed escaping.
  Inner envelope JSON may use STJ with `UnsafeRelaxedJsonEscaping`.
- Verify `id` and `sig` of every received event before decrypting or parsing anything else (NIP-44 requires it).
- Deciding logic stays pure (`EnvelopeCodec`, `RelayLink`, `PublishTracker`, `PublishBudget`, `SubscriptionPlan`): values in, answers
  out, time passed in. `RelayConnection`/`RelayPool` are thin shells that await sockets and `TimeProvider`.
- WebSockets only through `IRelaySocket`. The real one wraps `ClientWebSocket` and sets no `Options` (browser-wasm). No
  `Thread.Sleep`, `.Result`, `.Wait()`, file or socket APIs: this project must run in browser-wasm.
- "Sent" means at least one relay answered `OK true`. `OK false` (rate-limited, auth-required, invalid, blocked), `CLOSED` and
  `NOTICE` are surfaced per relay, never collapsed into "sent". Never more than 4 events per 10 s.
- `ids` filters in chunks of <= 5 (nos.lol silently returns nothing for 20).
- Tests that talk to public relays carry `[Trait("Category", "LiveRelay")]` and run by hand only after the user approved publishing.
  Everything else runs against `InMemoryRelay` (tests/TakEngine.Testing).

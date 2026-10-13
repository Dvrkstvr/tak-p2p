# M1 review: security lens

Range: `git diff d7df02c..498aca6` on `claude/m1-review` (HEAD 498aca6). The code lens is a separate review. No product code was
changed and nothing was published to a relay. I checked findings marked "verified" with throwaway xunit programs in a scratch
directory outside the repo, against `InMemoryRelay` and a fake clock. Each one ran against both 498aca6 and d7df02c (M0), so
the report can say what M1 introduced. Accepted items (D-048, the shared Pages origin) are not repeated.

Findings: blocking 0 · should 2 · nit 1.

## Findings

1. **should** · `src/TakEngine.Transport/Relay/RelayPool.cs:178-180` (EOSE on `tak-probe` is answered with a fire-and-forget
   CLOSE), together with `RelayPool.cs:259` (the pump awaits each relay's send in turn) and `RelayConnection.cs:182-183` (the
   reader awaits the probe send)
   · **Attack/input:** a hostile relay sends `["EOSE","tak-probe"]` frames that no probe asked for, as fast as it likes. Every one
   produces a `CLOSE tak-probe` send. Nothing checks that a probe is outstanding, and nothing limits the sends to one per probe.
   I verified this: 1000 injected EOSE frames produce 1000 CLOSE frames on 498aca6, and 0 on d7df02c. An invite may name any relay
   (M0-security nit 5), so the opponent who wrote the invite can put such a relay in the guest's pool.
   · **Consequence:** the client now sends one frame for every frame the relay sends, so a relay can make the client send without
   limit. If the relay also stops reading its socket, the client's send buffer fills. On the CLI, `ClientWebSocket.SendAsync`
   blocks once the TCP buffer is full. I did not check the browser's behaviour. From the code:
   (a) Each CLOSE is a separate `_ = SendAsync` task waiting on `_sendGate`, so pending tasks and memory grow without bound.
   (b) The probe send in `ReadUntilClosedAsync` blocks, so that link can never reach `LivenessAction.Drop`.
   (c) `PumpAsync` awaits `connection.SendAsync(frame)` for each relay in turn, so one blocked hostile connection stalls
   publishing to every relay, honest ones included. The victim's moves and resignation stop going out, and the UI shows
   them as pending with no end.
   Part (c) is coupling that already existed in M0. M1's reflection is what lets a relay trigger it.
   · **Suggested fix:**
   - Send CLOSE only for the EOSE (or CLOSED) that answers an outstanding probe, and at most once per probe. For example,
     `RelayLink` can keep a `ProbeOpen` flag that `SendProbe` sets and the first probe answer clears. Ignore EOSE on `tak-probe`
     when the flag is not set.
   - Separately, stop the pump from awaiting relays one after another. Fire the per-relay sends, or bound each send with a timeout
     that treats a stuck send like a drop, so that one relay's socket can't hold up the others.
   - Add a test that injects EOSE on `tak-probe` with no probe outstanding and asserts that no CLOSE is sent.
   · **Reply:** fixed in bae0f40. `RelayLink` keeps the probe's subscription open from the probe until its first EOSE or CLOSED
   (cleared on every connect, drop and stop); `OnProbeEose()` is true once per probe and only then does the pool send CLOSE.
   `StrayProbeEose_IsNotAnswered_AndARealProbeIsClosedOnce` injects 20 stray EOSE (0 CLOSE, was 20), then 20 more after a real
   probe (1 CLOSE). Left: the pump still awaits relays one after another (part c, M0 coupling). With the reflection gone a relay
   can no longer make the client send; a relay that stops reading still stalls the pump only on what we send it ourselves.
   Worth a send timeout when the pump is next touched.

2. **should** (introduced in M0, not by M1; first reported here) · `src/TakEngine.Transport/Relay/RelayPool.cs:238-243` (the pump
   spends the shared budget on `_outgoing` oldest first, retries included) with `PublishTracker.cs:79` (`OnNoAnswer` retries for
   ever at 30 s at most) and the new `PublishTracker.OnReconnected` (`:92`)
   · **Attack/input:** a relay in the pool takes EVENTs and never answers OK (`InMemoryRelay.IgnorePublishes`, "an overloaded
   relay or a half-open socket"). It still answers REQs, so the new probe finds the link healthy. Each of our past events stays
   Pending on that relay and is retried every 30 s or less for the rest of the game. Every retry costs one slot of the client-wide
   budget of 4 events per 10 s, and older events are served first.
   I verified this with two relays (r1 swallows EVENTs, r2 is honest) and 12 earlier moves. A new move reached the honest relay
   94 s after it was published, on both d7df02c and 498aca6. When r1 also drops its sockets every 1.5 s, the delay is 16 s on
   both. The delay grows with the game's length: from about 12 unanswered events, the retries alone use the whole budget.
   M1's `OnReconnected` adds a burst at each reconnect, because every no-answer retry waiting for that relay becomes due at once.
   That includes the probe's own drop-and-reconnect every 71 s against a relay that also ignores REQs.
   · **Consequence:** one bad or hostile relay (an invite can name it) can hold back the victim's own moves to the honest relays for
   minutes. Nothing forfeits, but the opponent sees silence, and "Sent" never appears even though an honest relay is available.
   · **Suggested fix:**
   - Give a first send priority over retries in the pump.
   - Once an event is Sent somewhere, keep retrying its no-answer relays only within a spare-budget share, or cap the retries
     (for example 5, then Refused "no answer").
   - Pin this with a test that times a new publish behind N events a relay swallows.
   · **Reply:** fixed in edb6418 (D-053). The pump sends first sends first, then retries newest first, and a retry takes a slot
   only while one more stays free (`PublishBudget.FirstSendReserve`), so retries use at most 3 of the 4 per 10 s and a new move
   always finds a slot. Retries are not capped (a relay may come back). `RetriesToASwallowingRelay_DoNotHoldBackANewMove` plays
   12 moves against r1 swallowing and r2 honest, stops the clock once the retries have spent what they can, and needs the new
   move Sent without the clock moving; it fails on the old pump.

3. **nit** · `src/TakEngine.Transport/Relay/RelayConnection.cs` (`Link.OnFrame` before `RelayMessage.Parse`)
   · **Attack/input:** a relay sends any frame at least every 59 s, even unparseable junk or a NOTICE.
   · **Consequence:** the link never probes and counts as live while the relay withholds the game's events. That is not a dead
   socket, and withholding was possible before M1 (other relays and the 20 s gap catch-up cover it). But "Subscribed/CaughtUp"
   in the per-relay status then says nothing about whether the relay delivers.
   · **Suggested fix:** none needed for M1. If per-relay health is ever shown as "working", count only parseable NIP-01 frames
   toward liveness.
   · **Reply:** left as suggested: no change for M1.

## Checked and found sound
- **Probe matching real events:** the filter is `ids:[64 zeros]`, `kinds:[3825]`, `limit:0`. Matching it needs a SHA-256 preimage
  of zero. An EVENT a relay pushes on `tak-probe` (or on any other subscription id) goes through `Receive` like every other event:
  id and signature first, then the codec's tag, game and author checks, so the subscription id grants nothing. EOSE or CLOSED on
  `tak-probe` never touch the game subscription's state (`OnEose`/`OnClosed`). The probe tells the relay nothing it doesn't
  already know (kind 3825, no keys).
- **Reconnect storms:** a probe that gets no answer drops the socket once per 60 s idle + 10 s timeout. After that the backoff is 1 s,
  then the normal schedule. A relay that answers nothing causes at most one reconnect about every 71 s. A relay that closes right
  after the REQ loops at 1 s, as in M0 (`Failures` resets on subscribe, unchanged). `CheckNow`/`RequestProbe` is a no-op while a
  probe is out, so visibility events can't stack probes. Probes are REQs and cost nothing from the EVENT budget.
- **`PublishTracker.OnReconnected` and replay:** it only clears `RetryAt` for this relay's no-answer entries. The resend is the
  same signed event (same id), and relays and receivers dedupe by id. Rate-limit backoffs are kept. Nothing new is signed and no
  stale state is re-applied. The budget cost is covered in finding 2.
- **`page-visibility.js`:** no DOM writes, no `innerHTML`, no eval, no data passed in or out. The `DotNetObjectReference` exposes
  only `[JSInvokable] OnPageVisible`, which takes no arguments and at most requests a probe. `window.takVisibility` is a writable
  global that any same-origin script can replace or call. That script could already read the nsec (D-048), so this adds nothing.
  The reference is disposed with the service.
- **Play page errors:** `ErrorMessage` (engine `CommandResult` text) is rendered with `@ErrorMessage` (HTML-encoded), never
  `MarkupString`. The `MarkupString` uses in `TakBoardView`/`PieceStackSvg` are unchanged and interpolate only numbers, enums and
  coordinates. The local Play page has no remote input.
- **`LocalGameAdapter`:** it uses `TakGameSession.CreateLocal`, which signs nothing and holds no key (the pubkey input is the
  constant `local-player`). It never touches `BrowserStorage`. Local-game hashes can't be confused with online ones, because
  online games go only through `OnlineGameService`/`MultiplayerGame`.
- **Hash chain:** `ComputeStateHash` gets the same turn index as before (`_appliedTurn + 1`, then `AdvanceTurn`), so invariant 7 and
  the golden vectors are untouched.
- **e2e harnesses:**
  - `background.mjs` installs `page.routeWebSocket` before the first `goto`. It pipes only `nos.lol` and `relay.damus.io` to
    `ws://127.0.0.1:<port>/r1|r2`, blocks every other non-loopback socket and rewrites the invite's relay hints for the CLI.
  - The CLI gets `--relays` local, which replaces its defaults. Its `--data-dir` is an `mkdtemp`, removed in `finally`; a
    killed run leaves a throwaway 0600 key in tmp.
  - `play.mjs` and the `m1-verify` probe scripts stub every WebSocket (local and vs-AI pages open none).
  - No harness reads `localStorage` or the identity.
- **Committed evidence (`pipeline/evidence/m1-*`, 35 text files plus PNGs):**
  - No `nsec1`, and no "secret" or "private" key material. The 64-hex strings are event ids, pubkeys and state hashes.
  - Every `wss://nos.lol`/`wss://relay.damus.io` in the f042 runs is logged as `piped to ws://127.0.0.1`. The CLI statuses show
    only loopback relays.
  - The browser transcripts hold full `TAK2_` invites. They are for finished local test games, and invites carry no secret key.
- `GameCoordinator.CheckConnections`/`MultiplayerGame.CheckConnections` are thin pass-throughs and add no I/O to the deciding
  logic. `RelayLink`'s liveness stays pure, with time passed in.

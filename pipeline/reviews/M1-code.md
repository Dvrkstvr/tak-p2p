# M1 review, code lens

Range: `git diff d7df02c..498aca6 -- src tools tests` on `claude/m1-review` (HEAD 498aca6). Read in full: LocalGameAdapter,
SlideTargets, Play.razor, StackSlideBar.razor, GameStatusHeader.razor (resign path), OnlineGameService (visibility hook),
page-visibility.js, RoadFinder, TakGameSession (turn/hash order, WinningRoad, Resign), RelayLink, RelayConnection, RelayPool,
PublishTracker, SubscriptionPlan, GameCoordinator/MultiplayerGame (CheckConnections), InMemoryRelay (SilentDrop), and the new or
changed tests (LocalGameAdapterTests, SlideTargetsTests, RoadFinderTests, TakGameSessionTests, RelayLogicTests, RelayPoolTests,
InMemoryRelayTests, GameCoordinatorTests), tests/e2e/play.mjs and tests/e2e/background.mjs. Read for context: D-049..D-051,
pipeline/design/gestures.md, scope.md M1 and "Reconnect and catch-up".

Verification: the four checks pass at 498aca6 (`--filter "Category!=LiveRelay"`). Findings 1 and 4 were reproduced with a
throwaway xUnit project in the session scratchpad that references `src/TakApp.Blazor`. Finding 2 was reproduced by running
`node tests/e2e/play.mjs --url http://127.0.0.1:5291` against a dev server started from this worktree on its own port. Every
WebSocket in that run was stubbed, and the server was stopped by its own process group. Nothing was sent to a public relay.
background.mjs was read, not run.

Deliberate breaks, one at a time, each reverted:

| break | caught by |
|---|---|
| RelayPool.OnSubscribed no longer calls `PublishTracker.OnReconnected` | `GameCoordinatorTests.MoveMadeDuringASilentDrop_IsAppliedAfterTheProbeFindsTheDropAndBothReconnect` |
| `SlideTargets.Fits` uses `==` instead of `>=` on the last drop | `SlideTargetsTests.TheBoardEdgeEndsThePath` |
| RelayConnection ignores `CheckNow` (no `RequestProbe`) | `RelayPoolTests.CheckNow_ProbesAtOnce_AndASilentRelayIsDroppedTenSecondsLater` |
| RelayConnection treats a probe timeout as a plain close (`return false`) | `RelayPoolTests.SilentDrop_IsFoundByTheProbe_AndTheReconnectCatchesUpWhatWasMissed` |
| LocalGameAdapter drops the `generation != _generation` check | **nothing** (finding 4) |
| StackSlideBar no longer clears direction/split on a new origin | **nothing in CI**; play.mjs `FAIL G-43` (finding 2) |

Totals: blocking 0 · should 4 · nit 3.

## Findings

### 1. should · src/TakApp.Blazor/Services/LocalGameAdapter.cs:110-119 (with Pages/Play.razor:764-770, Components/Panels/GameStatusHeader.razor:36-39)
- **What happens:** `Resign()` calls `TakGameSession.Resign()`, which resigns the side to move in a local session. In a vs-AI
  game the Resign link stays visible while "AI is thinking..." is shown (it is hidden only when the game is Completed), and
  `HandleResign` has no `IsBotThinking` guard. If the human presses Resign during the bot's 350 ms pause, the side to move is
  the bot, so **the bot resigns and the human wins**.
- **Reproduced:** `StartBotMatch(Five, Easy, White)`, play `a1` with a gated delay, `Resign()`. The result is
  `Resignation, Winner = White` (the human).
- **Why wrong:** the human asked to give up, and the result is the opposite. This was carried over from WebGameSessionManager
  (`Resign(Board.ActivePlayer)`), and G-51 records "the side to move resigns" for pass-and-play. The adapter now owns the bot's
  turn, though, so it is the place where vs-AI resign has to mean "the human resigns".
- **Suggested fix:** in a bot match, refuse `Resign` while `IsBotThinking` (or resign `LocalPlayerColor`, which needs a
  colour-taking overload on the session). Add an adapter test for it.

### 2. should · src/TakApp.Blazor/Components/Board/StackSlideBar.razor:95-106 (D-050, gestures.md G-43)
- **What happens:** the one behaviour change M1 makes on purpose, which resets the slide bar when its origin changes, is pinned
  only by `tests/e2e/play.mjs`. CI does not run that script. With the three reset lines deleted, all four checks stay green.
  play.mjs then fails with `FAIL G-43 a new origin resets the slide bar to that stack (Confirm names "b3>")`.
- **Why wrong:** D-050's reason is that Confirm "could send a move the player didn't mean". A regression would pass every check
  that gates a commit, and nothing in the repo would show it until someone ran the harness by hand.
- **Suggested fix:** move the "new origin → clear direction and move" rule into a small testable piece (or add a bUnit test for
  StackSlideBar) and pin it in `TakApp.Blazor.Tests`. Alternatively, run play.mjs in CI, since it already stubs every socket.

### 3. should · src/TakApp.Blazor/wwwroot/js/page-visibility.js:1-21, Services/OnlineGameService.cs:309,321-336 (D-051 "11 s with a wake hook")
- **What happens:** no test exercises the wake hook from `visibilitychange`/`online` → `OnPageVisible` →
  `MultiplayerGame.CheckConnections`. The unit tests call `CheckNow`/`CheckConnections` directly. background.mjs freezes the
  tab with `Page.setWebLifecycleState frozen` plus `Debugger.pause`. That does not make the page hidden, so `visibilitychange`
  never fires. The catch-up the F-042 evidence shows comes from the idle-window probe, because more than 60 s have passed at
  the thaw. Deleting the `<script src="js/page-visibility.js">` tag, or the `WatchVisibilityAsync` call, leaves every check and
  both e2e scripts green (by reading; the e2e scripts were not run with that break).
- **Why wrong:** D-051 and scope.md state a 10-11 s recovery for a woken tab, and that claim rests on code no run has executed.
  If the hook throws, or the JS name drifts, the `catch (JSException)` hides it: recovery quietly falls back to up to 71 s.
- **Suggested fix:** in background.mjs (`--silent`), mark the page hidden and then visible again before the idle window ends.
  Either emulate it (`Emulation.setFocusEmulationEnabled` / override `document.visibilityState` and dispatch
  `visibilitychange`), or thaw well under 60 s after the last frame. Then check that a `tak-probe` REQ goes out within a few
  seconds of the thaw.

### 4. should · tests/TakApp.Blazor.Tests/LocalGameAdapterTests.cs:205-220
- **What happens:** `NewMatch_DuringABotWait_DropsTheStaleBotMove` starts a *local* match during the bot's wait. That sets
  `_bot = null`, so the stale turn exits on `_bot is null` and the generation check is never reached. With
  `generation != _generation` removed the test still passes. The case the check exists for is a new *bot* match during the
  wait. There, the stale turn plays a bot move into the new game. A scratch test (`StartBotMatch` twice, release the gate,
  expect an empty history) fails with the check removed and passes with it.
- **Why wrong:** tests rule: "A test that survives a deliberate break of the code it covers is decoration: fix the test." The
  shipped code is correct today; only the test that should protect it is weak.
- **Suggested fix:** start the second game with `StartBotMatch` (human White, so no fresh bot turn masks it) and assert that the
  history is empty and `IsBotThinking` is false.

### 5. nit · src/TakEngine.Transport/Relay/RelayConnection.cs:174-205
- **What happens:** each loop pass runs `Link.CheckLiveness(now)` before it looks at a receive that has already completed. Take a
  tab frozen within 10 s after a probe went out (probes happen once per idle minute, so this is not rare), on a socket that
  stayed alive. At the thaw, the overdue `Task.Delay` can resume the loop before the queued EOSE's receive continuation has
  run. `CheckLiveness` then returns `Drop`, and a healthy socket is closed.
- **Why wrong:** the cost is one needless reconnect and REQ (the catch-up makes it harmless). Still, the rule "any frame answers
  the probe" is broken by ordering alone, and a browser decides that ordering, not the code.
- **Suggested fix:** before acting on `Drop`, yield once (`await Task.Yield()`) and drain `receive` if it completed. Or check
  `receive.IsCompleted` first and process that frame before calling `CheckLiveness`.

### 6. nit · tests/e2e/play.mjs:76-80, tests/e2e/background.mjs:89-93
- **What happens:** both scripts reuse whatever answers at `127.0.0.1:5199` (and `:7447/r1`) and write PASS evidence against it.
  On a machine where several worktrees or sessions run (as here), that can be another checkout's dev server or a stale build.
  play.mjs logs "SERVER using running site", but the run still counts as the evidence for this tree.
- **Why wrong:** F-043/F-042 evidence can certify code that is not the code under review. It is not a public-relay risk: the
  site is loopback and every socket is still stubbed or piped.
- **Suggested fix:** reuse a running site only when `--url` is passed explicitly. Otherwise start on a free port, or log the
  served `TakApp.Blazor.dll` build id next to `git rev-parse HEAD`.

### 7. nit · src/TakApp.Blazor/Components/Board/StackSlideBar.razor:84-92
- **What happens:** the slide bar gets its legal moves from `MoveValidator.GetLegalMovesForSquare` on the view's board copy
  (`GameBoard.FromSnapshot`). The rest of the page asks `LocalGameAdapter.LegalMovesFor`, which asks the session. The legality
  is the engine's either way, but there are two sources. If the copy ever differs from the session (FromSnapshot infers
  `Phase` from `TurnNumber`), the bar and the board markers can offer different moves.
- **Suggested fix:** pass the adapter's slide moves for the origin into the bar as a parameter (the page already holds them in
  `SlideBuilder.Legal`).

## Checked and found sound
- `SlideTargets.Fits` (prefix equal, last drop `>=`) offers exactly the old enter-square cases (wall, capstone alone, capstone
  blocks, board edge). The gesture cannot reach a dead end. If the drops so far are a prefix of a legal move M and tiles are
  still in hand, M either puts more on the current square or goes on to the next one, so `CanStackOnCurrent` or `SlideNextCoord`
  is offered. When the hand empties, the drops equal a legal move exactly. The engine still judges every move sent.
- RelayLink liveness is pure (time passed in), one probe per idle window, never two outstanding. `NextLivenessCheckAt` agrees
  with `CheckLiveness`, so the reader loop cannot spin (the wait is re-read after the awaited probe send; 63c1d75).
  `OnSubscribed`/`OnDisconnected`/`Stop` clear probe state.
- `CheckNow` vs the reader: the flag is set before the poke completes, the flag is consumed at the top of every pass, and a
  completed poke is replaced by CAS only. No lost wake-up was found. A `CheckNow` while disconnected only causes one probe right
  after the next subscribe.
- A probe answered by its EOSE (pool sends `CLOSE tak-probe`), by CLOSED (`auth-required` on damus; ignored for read-impossible,
  tested), by NOTICE or by any event: all reset the idle clock via `OnFrame`. The probe's EOSE never moves the game subscription
  to CaughtUp. Probes are REQs, not EVENTs, and never touch `PublishBudget` (asserted in the pool test).
- Reconnect cadence: a probe drop backs off 1 s (Failures reset by the previous subscribe), and a relay that accepts and then
  goes silent costs at most one reconnect per ~71 s. No storm. `OnReconnected` runs only on a real (re)connect, not on the
  coordinator's 20 s resubscribe. It clears only no-answer retries for that relay, and sends still go through the budget.
  In-flight entries from an older socket generation are forgotten as before.
- Cancellation: a silent drop cancels the linked read token, awaits the pending receive (exceptions swallowed), and disposes
  the socket under the send gate. The real socket aborts on a cancelled receive, also in the browser.
- Bot turn vs new game: `Begin` bumps the generation and clears `IsBotThinking`/`BotTurn`. A stale turn returns without touching
  state, and its `finally` notifies only for the live generation (finding 4 is about the test only). The adapter refreshes the
  board copy and road after every success, resign and bot step. A refused move does not notify.
- TakGameSession: hashing with `_appliedTurn + 1` and then `Volatile.Write` gives the same chain as before (golden vectors and
  every hash test still pass). The signed action reads the advanced turn as it did before. Pure code still does no I/O.
- RoadFinder.FindRoad: BFS shortest path per direction, walls excluded, capstones count, both roads when one move makes two.
  `WinningRoad` is empty unless the result is a road.
- Layering and wasm (invariants 2-4): RelayLink/PublishTracker/SubscriptionPlan stay pure. Only RelayConnection awaits the clock.
  No blocking waits, files or sockets were added to the engine. The Blazor head only chooses which gesture a click means
  (D-049) and asks the engine for legality. TakApp.Blazor.Tests is in TakGame.sln, .slnx and .Ci.slnf.
- e2e and public relays: play.mjs stubs every WebSocket before the first `goto`. background.mjs installs its route before
  `goto`. It pipes only `nos.lol`/`relay.damus.io` to the local r1/r2, passes loopback through, and closes every other host
  unanswered. It rewrites the invite's relays to the local ones before the CLI joins, and starts the CLI with
  `--relays ws://127.0.0.1:…`. Both stop their children by their own process group. Neither script can reach a public relay.

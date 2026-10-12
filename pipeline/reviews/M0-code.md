# M0 review, code lens

Range: `git diff edb51ad..HEAD -- src tools` on `claude/m0-closeout` (HEAD b6aaf7f). Read in full: TurnExchange, IncomingClassifier,
GameCoordinator, Host/GuestHandshake, HandshakeMessages, RebuildPlan, RelayHistory, LocalMoves, MultiplayerGame, EnvelopeCodec,
Nip01Serializer, NostrEvents, RelayPool, RelayConnection, RelayLink, PublishTracker, PublishBudget, SubscriptionPlan, ColorSeed,
InviteCode (validation), TakGameSession, GameBoard diff, TpsSerializer.Deserialize, SqliteGameStore, Schema, OnlineCommand,
OnlineArgs, OnlineGameService, TabGameStore, BrowserStorage. Security questions (crypto, key handling, invite secrecy) are left to
the security lens.

Verification: findings 1 and 3 were reproduced (throwaway xUnit project in the session scratchpad that reuses
`tests/TakEngine.Multiplayer.Tests/Table.cs`; the CLI run for 3). The others come from reading the code paths named, plus the
existing tests that pin the behaviour (`TurnExchangeTests.Resign_IsAccepted_UpToOneTurnAhead_AndALaterMoveIsIgnored` for 2,
`HandshakeTests.Messages_SayWhatHappened` for 4).

Totals: blocking 0 · should 6 · nit 1.

## Findings

### 1. should · src/TakEngine.Multiplayer/GameCoordinator.cs:474-483 (also :520/:531, TakEngine.Core/Session/TakGameSession.cs:123-142)
- **What happens:** the move that ends a game is saved by `CommitAppliedAsync`, and the end (`State = Completed`, `Result`) is saved
  later in a second transaction by `EndAsync` → `UpdateRecordAsync`. If that second write fails, or the process dies between the two
  writes, the stored game is `InGame` with a won board. On reopen, `TakGameSession.Restore` rebuilds the board from TPS. TPS has no
  result, so the restored session has `Phase = Playing` and `Result = null`, with the active player left as the winner's colour
  (a finished board does not pass the turn).
- **Reproduced (seed 3, `FiveByFiveRoadWin`):** fail `UpdateRecordAsync` on the side that plays `e1` (the winning move), then
  `ResumeAsync`. The result is `State=InGame, Result=null, AppliedTurn=9`, and `SubmitAsync("a2")` **succeeds**. White then signs a
  turn-10 move (an even ply) after its own road win. The opponent treats it as Late. The same failure on the receiving side leaves
  Black reopened with `Result=null`: the prompt says "your move turn 10" and every move is refused with "It is not your turn". It
  never prints FINAL. `JoinAsync` (:114-116) has the same two-write gap: a failed `AddOutboxAsync` leaves a `Joining` record with
  no JOIN in the outbox, and resume never sends one.
- **Why wrong:** this breaks the multiplayer rule "commit the applied move and progress … in one store transaction" for the
  completing move. A reopened game can sign a move after the game is over, or wait for ever. Nothing in the protocol recovers from it.
- **Suggested fix:** let `CommitAppliedAsync` take an optional updated record, and pass the Completed record with the move that ends
  the game (the local path :520 and the remote path :469). Do the same for SaveNewGame + JOIN outbox. Belt and braces: in
  `ResumeAsync`, treat a restored board whose `RoadFinder`/flat-out check says it is over as Completed.

### 2. should · src/TakEngine.Multiplayer/TurnExchange.cs:184-194
- **What happens:** a RESIGN with `turn == AppliedTurn + 1` is applied at once. That turn number only occurs when the resigner's
  own move `k` is still in flight (the resigner's `appliedTurn` already counts it). When move `k` arrives after the RESIGN, it is
  `Late`. Example: White plays turn 3, then resigns (RESIGN turn 3); Black, at turn 2, gets the RESIGN first. Relays return history
  newest-first on catch-up, so after a reconnect this is the normal order. White prints `FINAL black-resign <hash after 3>` and
  Black prints `FINAL black-resign <hash after 2>`.
- **Why wrong:** the playbook's pass check ("both processes print the same FINAL line") and F-039's "same final state hash" fail in a
  realistic order. The resigner's move `k` was stored in its outbox and published before the RESIGN, so it is guaranteed to come.
- **Suggested fix:** hold a RESIGN with `turn >= AppliedTurn + 1` as `_pendingResign`, not only `>` (the `Drain` path already
  applies it once turn `k` lands). Update the existing test, which currently pins the divergent behaviour. If the spec line "resign
  lands while a move is in flight" is kept literally, note the divergence in scope.md so the FINAL comparison excludes resigns.

### 3. should · src/TakApp.Cli/Online/OnlineCommand.cs:89-93,150 and src/TakApp.Cli/Online/OnlineArgs.cs:57-60
- **What happens:** `--relays` accepts any `ws://` URL, but `InviteCode.Validate` accepts plain `ws://` only on loopback. `tak host
  --relays ws://example.com --plain` throws `InviteFormatException` out of `MultiplayerGame.HostAsync`. `RunAsync` catches only
  `GameStoreException`, so the process dies with an unhandled-exception stack trace and exit code 134. Reproduced.
- **Why wrong:** the `--plain` contract and the frontends rule say a command that cannot start writes the reason to stderr and
  exits with code 2. Relay URLs are now validated in two places that disagree (OnlineArgs vs `InviteCode.IsAllowedRelay`).
- **Suggested fix:** catch `InviteFormatException` around the host path (the Blazor head already does), or better, validate
  `--relays` with the same rule `InviteCode` uses (expose `IsAllowedRelay`) so both checks are one.

### 4. should · src/TakEngine.Multiplayer/GameCoordinator.cs:340-356 (with Transport/Envelope/EnvelopeCodec.cs:123 and Handshake/GuestHandshake.cs:98)
- **What happens:** for a guest, `_record.OpponentPubKey` is the invite's host key from `Start`. An ACCEPT signed by any other key
  is therefore decoded as `Row2WrongAuthor` and counted only (`Drop`). It never reaches `GuestHandshake.OnAccept`, whose
  `DropAndFlag` ("ACCEPT from a key that is not the host's") is unreachable in the running system. Only the pure-unit test
  `HandshakeTests.Messages_SayWhatHappened` exercises it.
- **Why wrong:** F-017's acceptance criterion and scope.md's handshake spec say that an ACCEPT from a non-host key is "dropped and
  flagged". F-017 is marked passing on a branch the shell cannot reach. Scope.md contradicts itself here (tamper row 2 says count
  only), and the code silently picked one side.
- **Suggested fix:** decide which spec line wins. Either flag it in the coordinator (`Row2WrongAuthor` + ACCEPT for a guest in
  `Joining` → `FlagAsync`) and add a coordinator-level test, or change F-017/scope.md to "counted only" and delete the dead branch.

### 5. should · src/TakApp.Blazor/Services/OnlineGameService.cs:127-164
- **What happens:** `LeaveAsync` disposes the game, but the game record stays in the singleton `TabGameStore`. Pasting the same
  invite afterwards hits a dead end. **Join** fails with "Game … is already stored." (from `SaveNewGameAsync`, which `RunAsync`
  in OnlinePlay.razor catches). **Rebuild from relays** returns "This game is already stored on this device: resume it instead.",
  but the page has no resume.
- **Why wrong:** the doc comment on `LeaveAsync` says "a game still in progress stays on the relays and can be rebuilt with its
  invite". In the same tab it cannot be rebuilt or rejoined; only a reload brings it back. The frontends rule says every refusal
  needs a way out.
- **Suggested fix:** in `OpenAsync`, when the invite's game is already in `_store`, call `MultiplayerGame.ResumeAsync`, as the CLI's
  `join` does (OnlineCommand.cs:155-156).

### 6. should · src/TakApp.Blazor/Services/OnlineGameService.cs:166-177
- **What happens:** every tab of the origin reads the same `tak.identity.v1` key, and each tab has its own `TabGameStore`. Tab A
  plays a game. Tab B opens `/online` and uses "Rebuild from relays" on the same invite. Both tabs now hold the same turn with the
  same key. A move entered in each tab signs two different actions for one turn. The opponent flags equivocation, and the two tabs
  diverge for good.
- **Why wrong:** the CLI guards against exactly this with a profile lock (OnlineCommand.cs:51-54: "two copies of one player could
  otherwise sign two different moves for the same turn"). The Blazor head, which D-046 made a real F-039 device, has no guard.
- **Suggested fix:** take a per-game single-writer lock across tabs (Web Locks API `navigator.locks.request` via JS interop, or a
  `BroadcastChannel` claim). Refuse to open a game another tab holds, with a message.

### 7. nit · src/TakApp.Blazor/Services/TabGameStore.cs:1-117
- **What happens:** `TabGameStore` is a line-for-line copy of `tests/TakEngine.Testing/InMemoryGameStore.cs` without the
  failure knobs. The diff differs only in names, messages and the `FailNextWrite`/`HoldWrites` hooks.
- **Why wrong:** this is a second implementation of an `IGameStore` that already exists. A fix to the in-memory semantics (for
  example the turn-follows check or outbox uniqueness, which SqliteGameStore enforces with `UNIQUE (GameId, EventId)` and neither
  in-memory copy does) has to be made twice, and the copy the tests exercise is not the one that ships.
- **Suggested fix:** move one in-memory store into `src` (e.g. `TakEngine.Multiplayer`). Have `TakEngine.Testing` wrap it with the
  failure knobs, so the shipped copy is the tested one.

## Checked and not reported
- Turn ordering by `turn` only, buffer cap 64, duplicate = same turn + digest, equivocation flagged first-seen-wins, late events
  after Completed: as specified (TurnExchange.cs).
- Tamper rows 1-4 in order, with id/sig checked before decrypt and `pv` judged first (EnvelopeCodec.cs). Non-JOIN from an unknown
  author is rejected while inviting, so `_early` cannot hold a stranger's move.
- `since` only advances from accepted opponent events, capped at now + 1 min. This deviates on purpose from scope.md's "own echo
  updates maxCreatedAt" and the comment at GameCoordinator.cs:236 gives the reason; not a defect.
- Nip01Serializer escapes match the strfry behaviour it documents. The outbox is persisted before publish and republished
  verbatim. Move + progress are one transaction (except the completing move, finding 1).

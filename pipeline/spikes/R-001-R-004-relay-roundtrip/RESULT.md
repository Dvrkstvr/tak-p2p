# Spike R-001 + R-004: two-process round trip through real public Nostr relays

Date: 2026-09-30 · Track: deep · Author: spike-runner · Time box: one session

## Questions
1. R-004: how do the default relays (`wss://relay.damus.io`, `wss://nos.lol`, `wss://relay.primal.net`, from `NostrTransportClient.cs` L19-21) behave for signed kind 4 with `p`+`g` tags, NIP-59 kind 1059, ephemeral kind 20001? Live fan-out, history after reconnect, rate limits, AUTH, PoW, payment, stated retention.
2. R-001: do two separate OS processes, each with its own secp256k1 key, complete >= 10 alternating "move" messages through a real relay, in order, with one process killed and restarted mid-game and catching up from relay history?
3. Which kind/scheme should the real build use for moves, invites and Quick Play?

## Pass / fail criterion (given)
Pass: >= 1 default relay accepts signed move events, delivers them live to the other process, and serves history after a restart; 10-move exchange completes. Fail: no default relay does this.

## Verdicts
| Risk | Verdict | One line |
|---|---|---|
| R-001 (path never ran over a relay) | **proven at transport level** (seen running) | 12-turn game, two processes, killed/restarted mid-game, identical final hash on nos.lol; a 10-turn resumed game on damus; a 6-turn game on both relays at once. NOT proven: real `TakGameSession`, browser head, tamper/negative case over a relay |
| R-004 (public relay behaviour) | **proven for 2 of 3 default relays; primal inconclusive; retention over days inconclusive** | nos.lol accepts everything tested. damus accepts and serves everything except kinds 4 and 1059 (reads need NIP-42 AUTH, and damus's AUTH is broken), and it rate-limits hard. primal was unreachable |

Overall pass criterion: **met** (nos.lol, damus with custom kind 9999). Feasibility is not `red` on the relay layer. Design consequences (kind choice, rate limit, primal) are in Q3.

Signing/encryption used (Q2 asked): BIP-340 signatures and NIP-44 v2 encryption of every move payload, using `NostrCrypto.cs` copied from the R-002/R-003 spike (NBitcoin.Secp256k1 4.0.1 + BouncyCastle ChaCha20). The R-004 probe scripts (Node) use nostr-tools 2.25.2 (independent maintained library). Not the project's own broken crypto. This also closes the "Not done" item of R-002/R-003: **.NET-signed events with the hand-written NIP-01 id serializer are accepted by nos.lol and damus** (`OK true`).

## What was built (all in this folder, nothing in src/ or tests/)
- `Peer/` .NET 10 console (`Program.cs` + copied `NostrCrypto.cs`): one player per process. `ClientWebSocket` relay loop with reconnect/backoff, publish to N relays, two-filter subscription, dedupe by event id and turn, apply-in-order with hash chain `SHA256(prev|turn|pubkey|ptn)`, state file for restart, `--replay` cold rehydrate, `--die-after-publish N` crash injection.
- `run-roundtrip.sh <label> <relay[,relay]> <kind> <turns>`: starts A and B, B aborts itself right after publishing turn 4, A publishes turn 5 while B is dead, B restarts with its state file, then a third fresh process with B's key and no state rebuilds the game from relay history.
- `probe.mjs`, `probe-kinds.mjs`, `probe-auth.mjs`, `check-retention.mjs`, `reconcile.mjs`, `primal-check.mjs` (Node, nostr-tools): R-004 probes and read-only checks. `local-relay.mjs`: my own 80-line in-memory NIP-01 relay (verifies id+sig with nostr-tools) used only to debug the harness before touching public relays; it is not evidence about the platform.
- Raw evidence: `out/<run>/*.log|.out|run.txt`, `probe-*.txt`, `probe-output-*.json`, `published-events-full-list.tsv`, `retention-check-t0.txt`.

## Evidence

### Q1. Relay behaviour (seen running, 2026-09-29/30 UTC; the machine is in a CET/AMS-routed network)

NIP-11 documents (documented by the relays themselves, fetched with `GET https://<relay>/` + `Accept: application/nostr+json`):

| | relay.damus.io | nos.lol | relay.primal.net |
|---|---|---|---|
| software | strfry 1.1.0-158 | strfry 1.1.3 | (document is `{"limitation":{}}`, one fetch; later fetches timed out) |
| supported_nips | 1,2,4,9,11,28,40,45,59,70 | 1,2,4,9,11,28,40,45,70 | none stated |
| limitation | max_limit 500, max_message_length 1,000,000, max_subscriptions 200 | max_limit 500, max_message_length 131,072, max_subscriptions 20 | none |
| retention / fees / payment / auth_required / min_pow | not stated | not stated | not stated |
NIP-42 is not listed in supported_nips by either. Neither states a retention policy, so **retention over days cannot be read from the documents and was not measured**; `check-retention.mjs` re-fetches every event this spike published by id. At t0 (about 15 min after publishing) nos.lol served 20/20 and damus 17/17 (`retention-check-t0.txt`). Re-run it after 24 h, 7 d, 30 d.

Behaviour observed (log excerpts in `probe-log*.txt`, `probe-kinds-real.txt`, `probe-auth-damus.txt`):

| Test | nos.lol | relay.damus.io | relay.primal.net |
|---|---|---|---|
| WebSocket connect | fine | HTTP 503 on the WebSocket upgrade 3 times (probe runs), a retry succeeded each time | first session: connected, but no OK/EOSE to 3 EVENTs and 5 REQs in 8 s; later: handshake timeout on every one of 8 attempts (15-25 s), NIP-11 GET timed out |
| kind 4, tags `p`+`g`, NIP-44 content | `OK true` (20 ms); live to `#p` subscriber 54 ms; history by `#p`, `#g`, author all returned | `OK true` (166 ms); **not delivered live; history REQ returns `CLOSED auth-required: requested filter requires authentication`**, for `#p`, `#g` and author filters | no answer |
| kind 1059 gift wrap (NIP-59, rumor kind 14) | `OK true` (302 ms); live 444 ms; history by `#p` returned | `OK true`; same auth-required gate as kind 4 | no answer |
| kind 20001 ephemeral, `t` tag | `OK true`; live 145 ms; **also returned to a fresh connection right after** | `OK true`; live 255 ms; also returned to a fresh connection | no answer |
| kind 9999 (custom regular), `p`,`g`,`d` tags | `OK true` 24 ms; live 96 ms; history by `#p`, `#g`, author | `OK true` 159 ms; live 241 ms; history by `#p`, `#g`, author | not reachable |
| kind 30078 (addressable), unique `d` per event | `OK true`; live 83 ms; history all three ways | `OK true`; live 273 ms; history all three ways | not reachable |
| NIP-42 AUTH | never asked | challenge sent when the gated REQ is refused; answering with a valid kind 22242 event gives `OK false "error: relay needs serviceUrl to be configured before AUTH can work"` (5 of 5 tries, also as the p-tag recipient). **Damus cannot serve kinds 4/1059 to anyone today** | n/a |
| Rate limit | 12-13 events in 12 s from one IP: none hit | `OK false "rate-limited: you are noting too much"`: 5 events accepted in ~7 s, the 6th refused (twice, in two different games); a pause of 75 s let 5 more through, the 6th again refused. Looks per IP (A and B have different keys), window somewhere under 75 s | n/a |
| PoW (NIP-13) / payment | none met | none met | n/a |

Live fan-out to a second subscribed connection: yes on both reachable relays for kinds 9999, 30078, 20001 (and 4/1059 on nos.lol only). Regular events fetched back after reconnect: yes, by `#p`, `#g`, author and by id.

### Q2. R-001 round trips (seen running)
Payload = TransportEnvelope-like JSON (`game_id, turn, player_pubkey, prev_state_hash, timestamp_utc, action_type MOVE, action_data{ptn}, sent_ms`), **NIP-44 v2 encrypted**, in a BIP-340-signed event of kind 9999 with tags `[["p",peer],["g",gameId],["d",gameId:turn]]`, about 900-1100 bytes on the wire. The "moves" are fake text (`m1`..`m12`), not Tak moves. Hash chain checked on every receive.

`run-roundtrip.sh real-nos-lol wss://nos.lol 9999 12` (output in `out/real-nos-lol/`):
```
B first run exited with code 35 (killed after publishing turn 4)        <- Environment.FailFast, i.e. a crash, not an external taskkill
--- restarting B (same key, same state file) at 01:31:31
A DONE  Applied 12  finalHash 20b8899b...  arrivalOutOfOrder 0
B DONE  Applied 12  finalHash 20b8899b...  arrivalOutOfOrder 2  dupes 2 (the -30 s slack overlap)
B-replay (fresh process, no state)  Applied 12  finalHash 20b8899b...  arrivalOutOfOrder 10   <- relay returns history newest-first; client reorders by turn
```
Catch-up excerpt, B after restart (`out/real-nos-lol/b.log`): `start` -> `REQ-sent since=1790724655` (= saved max created_at 1790724685 minus 30 s) -> `RECV turn 5 latency_ms 6023` -> `APPLIED turn 5` -> `RECV turn 3`, `RECV turn 1` (already applied, deduped) -> `EOSE` -> `PUBLISHED turn 6`. Turn 5 was published by A while B was dead, so B got it only from relay history.

Other runs: `real-damus` (game reached turn 10 of 12: hit damus's rate limit; B's restart caught up turn 5 from damus history, replay rebuilt the same hash, resumed after a 75 s pause). `real-dual` (nos.lol + damus at once, 6 turns): every event published to both, damus refused B's turn 6 as rate-limited while nos.lol accepted it, dedupe counted 5-7 duplicate deliveries per side, final hashes identical on A, B and the cold replay (`70128b87...`). `real-dual-FAILED-race-bug` is the first dual attempt, kept for the record (my harness bug, see Surprises).

Latency (same-machine clock; `sent_ms` inside the payload to receive time; catch-up deliveries excluded; from a European network, so not a global figure):
| relay | live one-way (relay fan-out) | publish to `OK` |
|---|---|---|
| nos.lol | median 119 ms, min 55, max 463 (n=11) | median 21 ms (n=12) |
| relay.damus.io | median 268 ms, min 229, max 531 (n=9) | median 167 ms (n=14) |
| both (first OK wins) | median 82 ms (n=5) | median 199 ms |
Each move is one event and the next move follows the previous one by a human think time, so this is comfortably fast enough. Catch-up after restart returned the missed event within about 50 ms of the REQ.

### Events published to public relays (audit; user approval was for throwaway keys, encrypted where possible, <= ~60 events, no kind 0/1)
Full per-event list with ids and results: `published-events-full-list.tsv` (51 rows: 46 logged + 5 stray found by `reconcile.mjs`) plus the JSON audit `published-events.jsonl`.
- **Before the hold (ids not recorded, run 1 of `probe.mjs` etc.; keys random per run and not saved)**: nos.lol kind 4, 1059, 20001 (3, all `OK true`). damus kind 4, 1059, 20001 (3, `OK true`) and, in `probe-auth.mjs`, kind 4, 1059 (2, `OK true`). primal kind 4, 1059, 20001 (3 sends, no `OK` within 8 s, unknown whether stored). Total 11 sends. Kind 4 content was NIP-44 v2 ciphertext of `{"game_id":"tak-spike-xxxxxxxx","turn":1,"ptn":"a1"}`; kind 1059 was a real NIP-59 gift wrap (inner content plaintext but sealed and wrapped); kind 20001 content was the plaintext string `ephemeral-key+relays`. Tags used a random `tak-spike-<8 hex>` id.
- **After the resume**: 4 probe-kinds sends (9999 and 30078 on nos.lol and damus, ciphertext), 12 (nos.lol game), 14 (damus game incl. 4 refused), 16 (dual, incl. the failed attempt's 4), 5 stray "turn 2" duplicates from the replay process (see Surprises; visible on the relays, so listed in the TSV as `replay(stray)`), plus at most 3 more unlogged stray sends to damus that would have been refused. Roughly 51-54, within the ~60 limit. All kind 9999/30078 content is NIP-44 v2 ciphertext addressed to a throwaway peer key; no kind 0 or 1 events were sent.
- The throwaway secret keys live only in `out/*/a.key|b.key` (git-ignored) and are meaningless.

### Q3. Recommendation
1. **Moves: one custom regular kind (1000-9999 range), NIP-44 v2 content, tags `["p", peer]`, `["g", gameId]`.** Tested as 9999; pick the real number by checking the NIPs kind registry for a free one (I did not fetch the registry: unverified). Regular means every move is kept and none replaced, and it is served from nos.lol and damus with no AUTH. Kind 30078 also worked (addressable, needs a unique `d` per move or relays keep only the latest) but adds nothing over a regular kind.
2. **Do not use kind 4 or 1059 as the only path.** Kind 4 is NIP-04's legacy DM and damus gates reads of both behind an AUTH that does not work, so a damus-only player could not receive anything. If metadata privacy is wanted later, gift-wrap can be an optional second channel on relays that serve it (nos.lol did), not the default.
3. **Invites**: keep the invite as an out-of-band code/link (`InviteCode`: game id, host pubkey, relay hints). The first event of a game is the join/accept handshake inside the same kind (`action_type` in the encrypted payload). No separate invite kind is needed unless an open public lobby is wanted.
4. **Quick Play**: kind 20001 with a `t` tag works on both nos.lol and damus (published, fanned out live in 145-255 ms, and even retrievable from a fresh connection right after publishing, presumably because strfry keeps ephemeral events for a short time; the retention length was not measured). Treat it as best-effort and keep the "drop Quick Play from v1" fallback in R-004. The matchmaking handshake itself (two searchers finding each other) was not run.
5. **Relays**: ship nos.lol + damus as defaults, publish every move to both, accept if any `OK true`, treat `rate-limited` as "retry later", dedupe by event id and by turn. Drop `relay.primal.net` from the defaults until it is shown reachable; keep the relay list user-configurable. A game between two people on different networks paces itself at human speed and stays under damus's limit (about 5 events in a burst), but a client must not burst.

### What the real build should copy
- `NostrCrypto.cs` (via R-002/R-003), and the NIP-01 serializer it contains (do not use `System.Text.Json` for the id).
- Subscription shape: two filters in one REQ, `{kinds:[K], authors:[peer], "#p":[me], "#g":[game], since, limit<=500}` and the mirror for my own events, so a cold start can rebuild the whole hash chain. On every (re)connect send it with `since = maxSeenCreatedAt - 30` and dedupe.
- Ordering: relays return history newest-first (10 of 12 arrivals were out of order in the cold replay); buffer by turn and apply in turn order, verifying signature, author = turn owner, `prev_state_hash`.
- Persist `{appliedTurn, hash, maxCreatedAt, publishedTurn}` after every applied move; republishing a turn after a crash is harmless because receivers dedupe by turn.
- Multi-relay publish with per-relay `OK`/`CLOSED`/`NOTICE` surfaced to the UI ("sent" is not "accepted": relays refuse with `rate-limited`, `auth-required`, `invalid`).
- Reconnect loop with backoff and 503 handling; `ids` filters in chunks of <= 5 (nos.lol returned nothing for a 20-id filter, silently).
- `Peer/Program.cs` relay class as a starting point only: no keepalive/ping, no NIP-42, one subscription.

## Surprises
1. **Damus cannot serve kind 4 or 1059**: reads are auth-gated and its AUTH handler answers "relay needs serviceUrl to be configured before AUTH can work". The wire-protocol doc's kind 4 choice would have worked on nos.lol only.
2. **Damus rate limit** ("you are noting too much") kicks in after about 5 events per burst from one IP; a fast harness or a client that re-sends on failure will hit it.
3. **relay.primal.net was unreachable** from this network in every attempt across the session (handshake timeout), so it is not confirmed usable. It may be transient or network-specific: unknown.
4. Ephemeral kind 20001 was retrievable from a fresh connection on both relays (strfry keeps ephemeral events briefly; not measured how long).
5. nos.lol silently returns no events for a REQ with 20 ids; chunking to 5 works. Relay-side filter limits are not always signalled with `CLOSED`.
6. Two bugs in my own harness, both found on the real relays and fixed in the code here: (a) when publishing to two relays, a relay echo of my own move was applied before the slower relay's `OK` arrived, and the later "apply my move" reset the state (run kept as `real-dual-FAILED-race-bug`); (b) `--replay` mode was allowed to play, so each cold replay published one stray duplicate "turn 2" (5 events on the public relays, listed in the TSV). Both fixes were verified on the local relay only (12 events for a 12-turn game, none stray); the fixed replay path was not re-run on public relays, to stay within the event budget.
7. The "kill" is `Environment.FailFast` right after a publish (a hard crash with no cleanup), not an external `taskkill`; it exercises the same restart path but not a kill between `publish` and `SaveState` (that window republishes the same turn, which is deduped).

## Not covered / owed
- Retention over days (re-run `check-retention.mjs` at 24 h / 7 d / 30 d; NIP-11 states none).
- relay.primal.net behaviour (retry from another network).
- Real Tak moves through `TakGameSession` (R-001 steps 4, 6 legality, 7 win), the tamper/illegal-payload case over a relay (step 9), browser/WASM head (R-005), `.NET ClientWebSocket` in a browser.
- Matchmaking for Quick Play with two searchers.
- Longer games and repeated bursts against damus's limit; behaviour on a phone with a backgrounded connection.

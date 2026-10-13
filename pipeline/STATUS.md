# Status — Tak P2P

<!-- ≤ 60 lines. The "Now" section is injected at every session start; keep it ≤ 12 lines. -->

## Now
- track: deep · approach: spec-first (+ prototype-first: relay round-trip spikes)
- stage: 7 Build — M0 and M1 done; M1 review (pipeline/reviews/M1-code.md, M1-security.md) fixed or answered (D-052, D-053); M2 next
- clarity: scope 5/5 · blocking 0 · assumed 9 · deferred 3
- feasibility: amber · H-open 0 · M-open 3 · spiked 4
- milestone: M2 (online polish) · M1 features all pass, on main · owed checks 5 + phone (F-042)
- core promise: last ran end to end 2026-10-12, CLI host vs phone browser over nos.lol + damus (F-039, pipeline/evidence/live-relay-log.md)
- next: M2 plan agreed with the user, then build → `/pipeline:run`. Browser regression: `node tests/e2e/play.mjs`, `node tests/e2e/background.mjs`

## Stages
| # | Stage | State | Gate | Date |
|---|---|---|---|---|
| 1 | Intake | done (adopt) | brief signed off (D-001); 0 blocking | 2026-09-30 |
| 2 | Feasibility | done (adopt) | red: R-001..R-004 H, unproven | 2026-09-30 |
| 3 | Spike | done | pass: 2/2 RESULT.md; feasibility amber | 2026-09-30 |
| 4 | Scope | done | pass 5/5; signed off D-022 | 2026-10-01 |
| 5 | Design | deferred to M1 start (D-023) | M0 has no novel screen; tokens + 3 design tasks at M1/M4/M5 | 2026-10-02 |
| 6 | Architecture | done | pass 4/4; checks re-run by conductor; 10 decision records | 2026-10-02 |
| 7 | Build | active | + mutation check | — |
| 8 | Review | todo | code, ux, copy, security | — |
| 9 | Release | todo | — | — |

## Notes
- Adopted 2026-09-30. Audit: pipeline/audit.md. M0 is on main (PRs #2, #5, #6) plus the review fixes (PR #7).
- M0 review leftovers (nits, not blocking): unlimited "game already taken" notices, unbounded relay list in an invite, invite in the URL query (a #fragment keeps it out of logs), D-038 selective abort, the ACCEPT not naming the guest nonce.
- The browser /online page (D-046) already covers parts of M2: host, join and rebuild over relays, sizes 3-8 and komi. M2 keeps
  invite link/QR UX (F-047), status (F-046), browser-vs-browser (F-044) and the cross-head run from the Pages site (F-045).
- Owed hand-checks (5 min each): Blazor vs AI, M1.15.x tower-move controls, CLI game, Avalonia desktop → F-021, F-022, F-024, F-025.
- Owed phone check (F-042 row 2, D-051): a game on Android Chrome and iOS Safari with the tab backgrounded 5 min, and once with the OS killing the tab; desktop Chromium was frozen only.
- M1 review leftovers: a relay that stops reading can still stall the sequential send loop (per-relay send timeout); junk frames count as liveness; StackSlideBar keeps its own board copy.
- Cosmetic, open: on damus each liveness probe's AUTH challenge overwrites the relay status message.
- Retired rituals (D-013): DEVLOG, mirrored milestone tables, test counts in docs. AGENTS.md section 2 still mandates them; edit it on a separate branch with D-009 (lean CLAUDE.md).
- UI freeze (D-006) ends with M0; D-046 lifted it early for /online only.
- Owed from spikes: re-run spikes/R-001-R-004-relay-roundtrip/check-retention.mjs (7-day result is F-040's acceptance), and retry primal from another network.
- Transport: custom kind 3825, NIP-44 v2, p+g tags, nos.lol+damus (D-025). Checks: playbook.md (TakGame.Ci.slnf, LiveRelay excluded). Permissions: pushes allowed except main (D-031); M0 live relay budget ≤150 events, 57 used (D-032).

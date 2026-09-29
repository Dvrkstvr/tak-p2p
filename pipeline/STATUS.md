# Status — Tak P2P

<!-- ≤ 60 lines. The "Now" section is injected at every session start; keep it ≤ 12 lines. -->

## Now
- track: deep · approach: spec-first (+ prototype-first: relay round-trip spikes)
- stage: 4 Scope — next; needs user sign-off on the MVP cut (M0 = relay path)
- clarity: adopt deliverables 6/6 · blocking 0 · assumed 7 · deferred 4
- feasibility: amber · H-open 0 · M-open 3 · spiked 4
- milestone: none yet (M0 = relay path, cut at stage 4) · features passing 16/30 · owed checks 4
- next: dispatch scope-cutter; M0 = crypto replacement (tests first) + real-engine move exchange over nos.lol+damus + tamper case → `/pipeline:run`

## Stages
| # | Stage | State | Gate | Date |
|---|---|---|---|---|
| 1 | Intake | done (adopt) | brief signed off (D-001); 0 blocking | 2026-09-30 |
| 2 | Feasibility | done (adopt) | red: R-001..R-004 H, unproven | 2026-09-30 |
| 3 | Spike | done | pass: 2/2 RESULT.md; feasibility amber | 2026-09-30 |
| 4 | Scope | active | needs user sign-off; M0 = relay path | — |
| 5 | Design | todo | only for novel screens (join/invite flow) | — |
| 6 | Architecture | todo | + decision records | — |
| 7 | Build | todo | + mutation check | — |
| 8 | Review | todo | code, ux, copy, security | — |
| 9 | Release | todo | — | — |

## Notes
- Adopted 2026-09-30. Audit: pipeline/audit.md. Core promise does NOT work end to end today (risks.md R-001..R-004); every frontend fakes the join.
- 114 tests pass (Core 95 + Transport 19), but CI doesn't run them (D-010 = first build task).
- Owed hand-checks (5 min each): Blazor vs AI, M1.15.x tower-move controls, CLI game, Avalonia desktop → F-021, F-022, F-024, F-025.
- Retired rituals (D-013): DEVLOG, mirrored milestone tables, test counts in docs. AGENTS.md section 2 still mandates them; edit it on a separate branch with D-009 (lean CLAUDE.md).
- UI/new-frontend freeze until M0 passes (D-006).
- Owed from spikes: re-run spikes/R-001-R-004-relay-roundtrip/check-retention.mjs after 24 h and 7 d (from 2026-09-30), and retry primal from another network.

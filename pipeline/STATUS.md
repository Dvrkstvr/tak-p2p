# Status — Tak P2P

<!-- ≤ 60 lines. The "Now" section is injected at every session start; keep it ≤ 12 lines. -->

## Now
- track: deep · approach: spec-first (+ prototype-first: relay round-trip spikes)
- stage: 3 Spike — ready to dispatch (entry point set by adopt)
- clarity: adopt deliverables 6/6 · blocking 0 · assumed 7 · deferred 4
- feasibility: red · H-open 4 · M-open 7 · spiked 0
- milestone: none yet (M0 = relay path, cut at stage 4) · features passing 16/30 · owed checks 4
- next: spike R-002+R-003 (a failing two-peer secp256k1/NIP-44 test, then one signed event) and R-001+R-004 (two-process round trip on real relays) → `/pipeline:run`

## Stages
| # | Stage | State | Gate | Date |
|---|---|---|---|---|
| 1 | Intake | done (adopt) | brief signed off (D-001); 0 blocking | 2026-09-30 |
| 2 | Feasibility | done (adopt) | red: R-001..R-004 H, unproven | 2026-09-30 |
| 3 | Spike | active | — | — |
| 4 | Scope | todo | needs user sign-off; M0 = relay path | — |
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

# 0005 · CLI-first M0; browser at M1/M2
Status: accepted 2026-10-01 (user, D-016 confirmed in D-022). Recorded 2026-10-02.

## Context
The core promise needs two heads on different devices. Blazor does not use `TakGameSession` (seen in code), so putting it in M0
would mix the D-008 rewrite with the crypto and transport replacement.

## Decision
M0's head is the CLI (two processes; two machines for the exit run) plus headless fake-relay tests. The browser joins at M1 (engine
migration, F-042 relay spike) and M2 (multiplayer). M0 code stays browser-wasm-safe: `net10.0`, no blocking/file/socket APIs in the
engine, WebSockets behind `IRelaySocket` (`ClientWebSocket` in the real implementation, with no `Options` set), SQLite and key files
only in `TakEngine.Storage.Local`.

## Alternatives
Browser in M0 (doubles M0); fake-relay-only M0 (does not prove the promise on real relays).

## Consequences
Nothing clickable on the public site until M2. The CLI gains a `--plain` output mode so agents can drive and read it.

## Revisit if
The user wants the site playable sooner; F-042 fails (then the R-005 fallback: browser stays local/AI, desktop moves up).

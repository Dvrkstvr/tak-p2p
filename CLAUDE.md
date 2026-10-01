# Tak P2P

Serverless Tak (the board game) in .NET 10. Two players on different devices play over public
Nostr relays, which act as an encrypted dumb pipe. Every move is verified on each device.
Engine: `src/TakEngine.*`. Frontends: Spectre.Console CLI (M0 head), Blazor WASM, Avalonia.

## State and process
- Status: `pipeline/STATUS.md` (stage, next step) and `pipeline/features.json` (what works, with evidence).
  These are the only status sources. Doc status tables are not evidence.
- How to work: `pipeline/playbook.md` (approach, check/verify commands, quality bar). Modules, data, seams, wire format:
  `pipeline/architecture.md`. Behaviour specs: `pipeline/scope.md` "Interaction specs". Decisions: `pipeline/decisions.md`
  (log) and `docs/decisions/` (the long why). Risks: `pipeline/risks.md`.
- Area rules load from `.claude/rules/` when you touch matching files (crypto, transport, multiplayer, core-engine,
  storage, frontends, tests, ci).
- Spec-first: write the acceptance test, see it red, then code. Throwaway spike code goes in `pipeline/spikes/`, never `src/`.

## Checks (all four must pass before every commit; CI runs the same)
```
dotnet restore TakGame.Ci.slnf
dotnet build TakGame.Ci.slnf --no-restore
dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore
dotnet test TakGame.Ci.slnf --no-build --filter "Category!=LiveRelay"
```
- `TakGame.Ci.slnf` = all projects except the Android/iOS heads. A new project goes into `TakGame.sln`, `TakGame.slnx`
  and `TakGame.Ci.slnf` together, or CI silently skips it.
- Compiler and NuGet warnings are errors (`Directory.Build.props`). Fix formatting with `dotnet format TakGame.Ci.slnf`.
- `dotnet test` prints one summary per assembly; any failure in any assembly is red.
- Live-relay tests (`Category=LiveRelay`) publish to public relays: run them by hand only, and only after the user approved
  publishing for this milestone. Never publish to public relays from CI, a tool or an agent run without that approval.
- Don't write test counts, DEVLOG entries or milestone tables into docs or commit messages (D-013).
- The README device-matrix "Implementation Status" column must match `pipeline/features.json`.

## Invariants
1. **No authoritative server.** Relays are store-and-forward only. Clients never trust remote state; they re-verify
   every event id and signature, envelope signature, turn owner, hash chain and move legality locally. An invalid remote
   payload is dropped, the board and hash stay unchanged, and the peer is flagged. Nothing auto-forfeits.
2. **Layering.** Abstractions = records + seam interfaces. Crypto = keys, BIP-340, NIP-44, NIP-19. Core = rules, hashing,
   session, bot. Transport = Nostr wire, envelope codec, relay client. Multiplayer = handshake, turn exchange, coordinator.
   Storage.Local = SQLite + key file (CLI/desktop only). Frontends are views only. Core and Transport don't reference each other.
3. **Pure deciding logic.** Crypto, rules, session, codec, turn exchange and handshakes do no I/O and read no clock or RNG;
   time, nonces and seeds are inputs. Only shells (`GameCoordinator`, `RelayConnection`/`RelayPool`, heads) await.
4. **browser-wasm-safe engine.** Engine libraries target `net10.0` with no file, socket, blocking-wait or SQLite APIs;
   WebSockets only via `IRelaySocket` (real: `ClientWebSocket`, no `Options` set).
5. **One key per player (D-011).** One secp256k1 key is the npub identity, signs events (BIP-340), derives NIP-44 v2 keys,
   and signs game actions. Test crypto between two independent peers; never decrypt with the sender's own keys.
   Until F-031/F-015 land, `CryptoSigner` (Ed25519) and `Nip44Encryption` (SHA-256 stand-in, not ECDH) are wrong by design.
6. **Wire format** (architecture.md, docs/decisions/0003-0004): kind 3825, tags exactly `p` + `g`, NIP-44 v2 content with
   `pv` judged first. NIP-01 ids come only from the hand-written `Nip01Serializer`, never System.Text.Json.
   Changing the format or the `ActionDigest` layout needs a new `pv` and a decision record.
7. **Move chaining.** `StateHash = SHA-256(PrevStateHash || TurnIndex || PlayerPubKey || PtnMove || TpsSnapshot)`.
   Changing any input or its encoding breaks every stored game and every peer. Golden vectors pin it.
8. **Colours.** Only via `ColorResolver.ResolveColors(seed, host, guest)` from the invite's seed; never random on one device.
9. **Stored data is versioned** (SQLite `user_version`, JSON `"v"`): forward migrations only, each with a fixture test
   (docs/decisions/0010). Legacy Ed25519 data is kept, never migrated or verified. The secret key never leaves the device.
10. **UI freeze (D-006).** No new UI features, controls or frontends until M0 passes.

## Gotchas
- Blazor dev server 404s on `dotnet.<hash>.js`: delete `src/TakApp.Blazor/bin` and `obj`, then rebuild.
- NBitcoin.Secp256k1 defines `SHA256`/`HMACSHA256` too: alias the BCL types (CS0104).
- damus refuses a burst after ~5 events (`rate-limited`) and its AUTH is broken; nos.lol returns nothing for `ids` filters
  longer than ~5. The client never publishes more than 4 events per 10 s.

## Index
- `AGENTS.md` (for non-Claude agents; same invariants) and `docs/` (older long-form docs; `docs/wire-protocol.md` and
  `docs/database-schema.md` predate M0 and are superseded by `pipeline/architecture.md` where they differ).

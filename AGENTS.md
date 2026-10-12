# Agent Guidelines & Workflow Rules: Tak P2P

> Instructions and architectural invariants for AI pair programmers and automated coding agents operating in the `tak-p2p` repository.

---

## 1. Project Philosophy & Core Invariants

* **Zero Authoritative Game Servers:** The network layer (Nostr WebSocket relays) functions strictly as an encrypted "dumb pipe" / store-and-forward mailbox. Clients never trust remote states; all moves, state transitions, and hash chains are verified deterministically on the local device.
* **Separation of Concerns:**
  * `TakEngine.Abstractions`: Shared contracts, immutable records, data structures, and spectator interfaces (public candidate).
  * `TakEngine.Core`: Private game logic, DFS graph road traversal, cryptographic hashing, invariant checks, state storage, AI minimax bot, and spectator queue.
  * `TakEngine.Transport`: Nostr WebSocket relay interface, NIP-44 encryption, envelope serialization, and matchmaking handshakes.
  * Frontends: Pure UI views consuming reactive observables/events:
    * `TakApp.Blazor`: Zero-install WebAssembly browser client hosted on GitHub Pages.
    * `TakApp.Cli`: Spectre.Console ANSI terminal client for all POSIX and Windows consoles.
    * `TakApp.Avalonia`: Shared cross-platform MVVM UI library consumed by:
      * `TakApp.Avalonia.Desktop`: Hardware-accelerated executable for Windows, Linux, and macOS.
      * `TakApp.Avalonia.Android`: Native Android APK for smartphones and tablets.
      * `TakApp.Avalonia.iOS`: Native iOS and iPadOS application.
* **Deterministic Rule Adjudication:** Illegal moves are mathematically impossible to force onto a peer. If an opponent injects an invalid payload, the receiving client drops the payload and flags the peer.

---

## 2. Status, Checks & Commits

### Where status lives
* Project status lives in [pipeline/STATUS.md](pipeline/STATUS.md) and [pipeline/features.json](pipeline/features.json). Nowhere else.
* A feature counts as done only when `features.json` marks it passing, with evidence (a command run, a log or a screenshot).
* The one mirrored status is the "Implementation Status" column of the README device matrix. Keep it, and make it match `pipeline/features.json`. Don't mark a platform Implemented unless its features pass there.
* Retired (D-013): the `docs/DEVLOG.md` timeline and detailed entries (git log is the history), milestone status tables mirrored into `README.md`, `docs/v1-mvp.md` and `docs/PROJECT_SPECIFICATION.md`, and test counts in any doc or commit message. The existing docs stay in the repo, but don't add new entries to them.

### Before every commit
All four commands must pass (CI runs the same ones, see [.github/workflows/ci.yml](.github/workflows/ci.yml)):
```powershell
dotnet restore TakGame.Ci.slnf
dotnet build TakGame.Ci.slnf --no-restore
dotnet format TakGame.Ci.slnf --verify-no-changes --no-restore
dotnet test TakGame.Ci.slnf --no-build --filter "Category!=LiveRelay"
```
`TakGame.Ci.slnf` is every project except the Android/iOS heads. `dotnet test` runs every test project in it; treat any failure in any assembly as red.
Tests tagged `Category=LiveRelay` publish to public relays and run by hand only, after the user approved publishing.

### Commits
* Stage source, tests and any doc the change affects. Write a short, accurate message with no test counts.
* Follow [pipeline/playbook.md](pipeline/playbook.md) for approach, quality bar and run/verify commands.

---

## 3. Architecture & Code Invariants

1. **Deterministic Colors:** Always use `ColorResolver.ResolveColors(seed, peerA, peerB)` for color assignment in multiplayer handshakes. Never roll random colors locally without a shared cryptographic seed.
2. **Move Chaining:** Every move entity strictly maintains:
   $$\text{StateHash} = \text{SHA-256}(\text{PrevStateHash} \,\|\, \text{TurnIndex} \,\|\, \text{PlayerPubKey} \,\|\, \text{PtnMove} \,\|\, \text{TpsSnapshot})$$
3. **Keys & NIP-44 Encryption:** Per D-011, each player has one secp256k1 key. It serves as the Nostr identity (npub), signs events with BIP-340, derives NIP-44 v2 conversation keys by ECDH, and signs moves. Wire envelopes must be encrypted with real NIP-44 v2, so that the recipient can decrypt with (their private key, sender's public key).
   Implemented in `TakEngine.Crypto` (`SecretKey`/`PublicKey`, `Schnorr`, `Nip44`, `Nip19`) and tested against the official BIP-340 and NIP-44 vectors and between two independent peers. Never test by decrypting with the sender's own keys.
4. **PTN & Direction Encoding:** Inner envelope JSON uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` so `+`, `>`, `<` are not escaped. Nostr event ids are computed only by the hand-written NIP-01 serializer, never by System.Text.Json (it mismatches NIP-01 even with relaxed escaping). Wire format: kind 3825, tags `p` + `g`, see [pipeline/architecture.md](pipeline/architecture.md).
5. **Offline-First Storage:** Use the SQLite store in `TakEngine.Storage.Local` on CLI/desktop (the existing `SqliteGameStorage` moves there in M0) and `BrowserStorage` on Blazor WASM. Stored data is versioned with forward-only migrations ([docs/decisions/0010](docs/decisions/0010-stored-data-and-legacy-ed25519.md)). Both caches must be able to restore the board to any turn index $K$ in $O(1)$ time via `TpsSerializer`.
6. **Blazor Dev-Server Clean Rebuild:** If incremental builds of `TakApp.Blazor` cause 404 errors for `dotnet.<hash>.js` in development, perform a clean build (`Remove-Item -Recurse -Force src/TakApp.Blazor/bin, src/TakApp.Blazor/obj; dotnet build src/TakApp.Blazor/TakApp.Blazor.csproj`).
7. **Solution-Wide Multi-Assembly Tests:** The test suite spans multiple test projects (`TakEngine.Core.Tests` + `TakEngine.Transport.Tests`). Always verify every project passes; `dotnet test TakGame.sln` covers them all.

---

## 4. Documentation Map

* Architecture (current, supersedes older docs where they differ): [pipeline/architecture.md](pipeline/architecture.md) · Decision records: [docs/decisions/](docs/decisions/README.md)
* Master Spec: [docs/PROJECT_SPECIFICATION.md](file:///e:/repos/tak-p2p/docs/PROJECT_SPECIFICATION.md)
* Project Status: [pipeline/STATUS.md](pipeline/STATUS.md) · Features & evidence: [pipeline/features.json](pipeline/features.json) · Decisions: [pipeline/decisions.md](pipeline/decisions.md)
* Development Log (historical, no longer updated per D-013): [docs/DEVLOG.md](file:///e:/repos/tak-p2p/docs/DEVLOG.md)
* System Architecture: [docs/system-overview.md](file:///e:/repos/tak-p2p/docs/system-overview.md)
* MVP Milestone Guide: [docs/v1-mvp.md](file:///e:/repos/tak-p2p/docs/v1-mvp.md)
* Competitive & Tournaments: [docs/v2-tournaments.md](file:///e:/repos/tak-p2p/docs/v2-tournaments.md)
* Wire Protocol & Nostr (pre-M0, kind 4 design superseded by docs/decisions/0003-0004): [docs/wire-protocol.md](file:///e:/repos/tak-p2p/docs/wire-protocol.md)
* SQLite Database Schema: [docs/database-schema.md](file:///e:/repos/tak-p2p/docs/database-schema.md)
* Blazor Web & GitHub Pages: [docs/blazor-web-github-pages.md](file:///e:/repos/tak-p2p/docs/blazor-web-github-pages.md)
* Spectator & Broadcast System: [docs/spectator-implementation-plan.md](file:///e:/repos/tak-p2p/docs/spectator-implementation-plan.md)
* Comprehensive MVP Project Audit: [docs/AUDIT.md](file:///e:/repos/tak-p2p/docs/AUDIT.md)

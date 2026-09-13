# Agent Guidelines & Workflow Rules: Tak P2P

> Instructions, architectural invariants, and mandatory maintenance protocols for AI pair programmers and automated coding agents operating in the `tak-p2p` repository.

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

## 2. Mandatory DevLog & Documentation Maintenance Protocol

Whenever you complete a milestone, architectural feature, or significant refactoring in this repository, **you MUST execute the following documentation maintenance steps**:

### Step 1: Run Full Test Verification
* Execute:
  ```powershell
  dotnet test TakGame.sln
  ```
* Ensure that **100% of unit tests pass** across all test suites (`TakEngine.Core.Tests`: 95 tests, `TakEngine.Transport.Tests`: 19 tests, totaling **114 unit tests**) before logging or committing.
* *Note:* `dotnet test` executes each test project in parallel and prints per-assembly summaries; do not mistake a single assembly's count for the solution total.

### Step 2: Update `docs/DEVLOG.md`
* Open [docs/DEVLOG.md](file:///e:/repos/tak-p2p/docs/DEVLOG.md).
* Prepend a new entry to the **Commit & Milestone Timeline** table with:
  * Commit short hash
  * Exact ISO/local timestamp
  * Milestone / Scope
  * Key Deliverables summary
  * Total passing test count (107)
* Add a detailed subsection under **Detailed Entry Logs** documenting:
  * Author & Timestamp
  * Scope
  * Affected files (with clickable `file:///` links)
  * Deliverables and test results

### Step 3: Synchronize Status in Specifications
* Verify and update:
  * [README.md](file:///e:/repos/tak-p2p/README.md) – Device matrix, solution layout, and documentation index.
  * [docs/v1-mvp.md](file:///e:/repos/tak-p2p/docs/v1-mvp.md) – "v1 Acceptance Criteria & Milestone Status" table.
  * [docs/PROJECT_SPECIFICATION.md](file:///e:/repos/tak-p2p/docs/PROJECT_SPECIFICATION.md) – Master milestone status table and roadmap.
  * [docs/DEVLOG.md](file:///e:/repos/tak-p2p/docs/DEVLOG.md) – Master chronological commit and milestone log.

### Step 4: Commit & Push to GitHub
* Stage all changes (source code, tests, and documentation).
* Commit with a descriptive conventional commit message:
  ```powershell
  git commit -m "Milestone M1.X: <summary of deliverables> with <N> unit tests"
  git push origin main
  ```

---

## 3. Architecture & Code Invariants

1. **Deterministic Colors:** Always use `ColorResolver.ResolveColors(seed, peerA, peerB)` for color assignment in multiplayer handshakes. Never roll random colors locally without a shared cryptographic seed.
2. **Move Chaining:** Every move entity strictly maintains:
   $$\text{StateHash} = \text{SHA-256}(\text{PrevStateHash} \,\|\, \text{TurnIndex} \,\|\, \text{PlayerPubKey} \,\|\, \text{PtnMove} \,\|\, \text{TpsSnapshot})$$
3. **NIP-44 Encryption:** Wire envelopes over Nostr must use `Nip44Encryption.Encrypt` / `Decrypt` with derived ECDH shared secrets.
4. **PTN & Direction Encoding:** When serializing JSON for transport, always use `TransportEnvelope.SerializerOptions` (`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`) so characters like `+`, `>`, and `<` are not escaped to unicode entities.
5. **Offline-First Storage:** Use `SqliteGameStorage` on desktop/mobile and `BrowserStorage` on Blazor WASM. Both caches must be able to restore the board to any turn index $K$ in $O(1)$ time via `TpsSerializer`.
6. **Blazor Dev-Server Clean Rebuild:** If incremental builds of `TakApp.Blazor` cause 404 errors for `dotnet.<hash>.js` in development, perform a clean build (`Remove-Item -Recurse -Force src/TakApp.Blazor/bin, src/TakApp.Blazor/obj; dotnet build src/TakApp.Blazor/TakApp.Blazor.csproj`).
7. **Solution-Wide Multi-Assembly Tests:** The test suite spans multiple test projects (`TakEngine.Core.Tests` + `TakEngine.Transport.Tests`). Always verify both projects pass completely (114 tests).

---

## 4. Documentation Map

* Master Spec: [docs/PROJECT_SPECIFICATION.md](file:///e:/repos/tak-p2p/docs/PROJECT_SPECIFICATION.md)
* Development Log: [docs/DEVLOG.md](file:///e:/repos/tak-p2p/docs/DEVLOG.md)
* System Architecture: [docs/system-overview.md](file:///e:/repos/tak-p2p/docs/system-overview.md)
* MVP Milestone Guide: [docs/v1-mvp.md](file:///e:/repos/tak-p2p/docs/v1-mvp.md)
* Competitive & Tournaments: [docs/v2-tournaments.md](file:///e:/repos/tak-p2p/docs/v2-tournaments.md)
* Wire Protocol & Nostr: [docs/wire-protocol.md](file:///e:/repos/tak-p2p/docs/wire-protocol.md)
* SQLite Database Schema: [docs/database-schema.md](file:///e:/repos/tak-p2p/docs/database-schema.md)
* Blazor Web & GitHub Pages: [docs/blazor-web-github-pages.md](file:///e:/repos/tak-p2p/docs/blazor-web-github-pages.md)
* Spectator & Broadcast System: [docs/spectator-implementation-plan.md](file:///e:/repos/tak-p2p/docs/spectator-implementation-plan.md)
* Comprehensive MVP Project Audit: [docs/AUDIT.md](file:///e:/repos/tak-p2p/docs/AUDIT.md)

# Agent Guidelines & Workflow Rules: Tak P2P

> Instructions, architectural invariants, and mandatory maintenance protocols for AI pair programmers and automated coding agents operating in the `tak-p2p` repository.

---

## 1. Project Philosophy & Core Invariants

* **Zero Authoritative Game Servers:** The network layer (Nostr WebSocket relays) functions strictly as an encrypted "dumb pipe" / store-and-forward mailbox. Clients never trust remote states; all moves, state transitions, and hash chains are verified deterministically on the local device.
* **Separation of Concerns:**
  * `TakEngine.Abstractions`: Shared contracts, immutable records, data structures, and spectator interfaces (public candidate).
  * `TakEngine.Core`: Private game logic, DFS graph road traversal, cryptographic hashing, invariant checks, state storage, and spectator queue.
  * `TakEngine.Transport`: Nostr WebSocket relay interface, NIP-44 encryption, envelope serialization, and matchmaking handshakes.
  * Frontends (`TakApp.Avalonia`, `TakApp.Cli`, `TakApp.Blazor`): Pure UI views consuming reactive observables/events.
* **Deterministic Rule Adjudication:** Illegal moves are mathematically impossible to force onto a peer. If an opponent injects an invalid payload, the receiving client drops the payload and flags the peer.

---

## 2. Mandatory DevLog & Documentation Maintenance Protocol

Whenever you complete a milestone, architectural feature, or significant refactoring in this repository, **you MUST execute the following documentation maintenance steps**:

### Step 1: Run Full Test Verification
* Execute:
  ```powershell
  dotnet test TakGame.sln
  ```
* Ensure that **100% of unit tests pass** across all test suites (`TakEngine.Core.Tests`, `TakEngine.Transport.Tests`) before logging or committing.

### Step 2: Update `docs/DEVLOG.md`
* Open [docs/DEVLOG.md](file:///e:/repos/tak-p2p/docs/DEVLOG.md).
* Prepend a new entry to the **Commit & Milestone Timeline** table with:
  * Commit short hash
  * Exact ISO/local timestamp
  * Milestone / Scope
  * Key Deliverables summary
  * Total passing test count
* Add a detailed subsection under **Detailed Entry Logs** documenting:
  * Author & Timestamp
  * Scope
  * Affected files (with clickable `file:///` links)
  * Deliverables and test results

### Step 3: Synchronize Status in Specifications
* Verify and update:
  * [README.md](file:///e:/repos/tak-p2p/README.md) – "Implementation Progress (Milestones)" table and solution layout.
  * [docs/v1-mvp.md](file:///e:/repos/tak-p2p/docs/v1-mvp.md) – "v1 Acceptance Criteria & Milestone Status" table.
  * [docs/PROJECT_SPECIFICATION.md](file:///e:/repos/tak-p2p/docs/PROJECT_SPECIFICATION.md) – Master milestone status table.

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

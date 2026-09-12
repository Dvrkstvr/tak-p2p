# Development Log (DevLog)

This document tracks all project milestones, architectural additions, and commits with timestamps and file changes, synchronized directly from Git and GitHub pushes.

---

## Commit & Milestone Timeline

| Commit | Timestamp (UTC+2) | Milestone / Scope | Key Deliverables | Tests Passing |
| --- | --- | --- | --- | --- |
| [`4c40d56`](https://github.com/Dvrkstvr/tak-p2p/commit/4c40d56) | 2026-09-12 07:00:51 | **Workflow & Blazor Components** | Codified `AGENTS.md` and `.agents/rules/devlog-maintenance.md`; implemented Blazor interactive SVG board and UI components. | 84 |
| [`a5e0e01`](https://github.com/Dvrkstvr/tak-p2p/commit/a5e0e01) | 2026-09-12 06:58:57 | **DevLog Foundation** | Created initial `docs/DEVLOG.md` tracking all commits, timestamps, deliverables, and test metrics. | 84 |
| [`ea3fab7`](https://github.com/Dvrkstvr/tak-p2p/commit/ea3fab7) | 2026-09-12 06:57:02 | **Documentation Sync** | Synced `README.md`, `v1-mvp.md`, `system-overview.md`, `PROJECT_SPECIFICATION.md` with M1.1–M1.7, Blazor WASM, and Spectator features. | 84 |
| [`eb303e8`](https://github.com/Dvrkstvr/tak-p2p/commit/eb303e8) | 2026-09-12 06:54:26 | **Milestone M1.7** | Spectre.Console CLI UI: ANSI board renderer, conversational stepped typed input, stack inspector, interactive menus. | 84 |
| [`9e7cb32`](https://github.com/Dvrkstvr/tak-p2p/commit/9e7cb32) | 2026-09-12 06:46:25 | **Milestone M1.6** | Time & Stale System: RFC 5905 NTP network time service, Day 3 stale match alerts, Day 7 auto-draw timeouts. | 84 |
| [`61d967d`](https://github.com/Dvrkstvr/tak-p2p/commit/61d967d) | 2026-09-12 06:41:52 | **Milestone M1.5** | Quick Play & Direct Codes: `tak://` URI and `TAK1_` QR tokens, `kind: 20001` ephemeral broadcasts, deterministic color resolution. | 74 |
| [`8bce863`](https://github.com/Dvrkstvr/tak-p2p/commit/8bce863) | 2026-09-12 06:38:54 | **Milestone M1.4 + Extensions** | Nostr Transport MVP: Multi-relay WebSocket pool, NIP-01 frames, NIP-44 ChaCha20-Poly1305 encryption, Blazor WASM skeleton, Spectator engine. | 66 |
| [`773490d`](https://github.com/Dvrkstvr/tak-p2p/commit/773490d) | 2026-09-12 06:24:15 | **Milestone M1.3** | Cryptography & SQLite Persistence: SHA-256 state hashing, Ed25519 signing, SQLite database engine, instant $O(1)$ replay scrubbing. | 52 |
| [`e78a023`](https://github.com/Dvrkstvr/tak-p2p/commit/e78a023) | 2026-09-12 06:21:11 | **Milestone M1.2** | PTN / TPS Parser & Formatter: Standard PTN move/game serialization and TPS state export and reconstruction. | 44 |
| [`a632620`](https://github.com/Dvrkstvr/tak-p2p/commit/a632620) | 2026-09-12 06:18:34 | **Milestone M1.1** | Core Rule Foundation: 4x4, 5x5, 6x6 board rules, reserve tracking, carry limits, wall flattening, orthogonal DFS road finder. | 23 |
| [`5faacbe`](https://github.com/Dvrkstvr/tak-p2p/commit/5faacbe) | 2026-09-12 06:11:08 | **Initial Commit** | Initialized solution, core abstractions, documentation suite, and project layout. | 2 |

---

## Detailed Entry Logs

### [4c40d56](https://github.com/Dvrkstvr/tak-p2p/commit/4c40d56) - Agent Rules & Blazor Interactive Components
* **Timestamp**: `2026-09-12T07:00:51+02:00`
* **Author**: Calvin Kohl
* **Scope**: Coding agent automation rules and Blazor WebAssembly frontend components.
* **Changes**:
  * [AGENTS.md](file:///e:/repos/tak-p2p/AGENTS.md): Codified project architectural invariants, strict DevLog maintenance protocol, and documentation cross-reference map for all AI agents.
  * [.agents/rules/devlog-maintenance.md](file:///e:/repos/tak-p2p/.agents/rules/devlog-maintenance.md): Workspace rule mandating that every milestone and commit updates `docs/DEVLOG.md` with commit hashes, test results, and timestamps.
  * [TakBoardView.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Components/Board/TakBoardView.razor): Scalable SVG 2D board with responsive square selection, move direction indicators, and drag-and-drop support.
  * [PieceStackSvg.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Components/Board/PieceStackSvg.razor): 3D isometric SVG rendering of piece stacks, capstones, and standing walls.
  * [PieceInventoryView.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Components/Board/PieceInventoryView.razor): Player piece reserve inventory trays.
  * [StackSlideBar.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Components/Board/StackSlideBar.razor): Slide drop distribution widget for tower movements.
  * [InviteModal.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Components/Modals/InviteModal.razor): Direct invite and QR code token exchange modal.
  * [GameStatusHeader.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Components/Panels/GameStatusHeader.razor) & [MoveHistoryPanel.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Components/Panels/MoveHistoryPanel.razor): Turn indicators, clocks, and live PTN move scrubber.
  * [Play.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Pages/Play.razor): Main game page integrating local and online Nostr multiplayer.
* **Test Suite**: 84 tests passing.

---

### [a5e0e01](https://github.com/Dvrkstvr/tak-p2p/commit/a5e0e01) - Initial DevLog Creation
* **Timestamp**: `2026-09-12T06:58:57+02:00`
* **Author**: Calvin Kohl
* **Scope**: Project progress auditing and historical milestone tracking.
* **Changes**:
  * [docs/DEVLOG.md](file:///e:/repos/tak-p2p/docs/DEVLOG.md): Comprehensive timeline table and entry logs for all initial commits from `5faacbe` through `ea3fab7`.
* **Test Suite**: 84 tests passing.

---

### [ea3fab7](https://github.com/Dvrkstvr/tak-p2p/commit/ea3fab7) - Documentation Synchronization
* **Timestamp**: `2026-09-12T06:57:02+02:00`
* **Author**: Calvin Kohl
* **Scope**: Documentation alignment across root and `docs/`.
* **Changes**:
  * Updated [README.md](file:///e:/repos/tak-p2p/README.md), [docs/v1-mvp.md](file:///e:/repos/tak-p2p/docs/v1-mvp.md), and [docs/PROJECT_SPECIFICATION.md](file:///e:/repos/tak-p2p/docs/PROJECT_SPECIFICATION.md) to record **Milestones M1.1 through M1.7 as COMPLETED**.
  * Added `TakApp.Blazor` (WebAssembly with zero-cost GitHub Pages deployment architecture) to the official solution hierarchy.
  * Added references to the new spectator observation architecture ([docs/spectator-implementation-plan.md](file:///e:/repos/tak-p2p/docs/spectator-implementation-plan.md)) and web deployment guide ([docs/blazor-web-github-pages.md](file:///e:/repos/tak-p2p/docs/blazor-web-github-pages.md)).
  * Documented the conversational stepped typed CLI control scheme.

---

### [eb303e8](https://github.com/Dvrkstvr/tak-p2p/commit/eb303e8) - Milestone M1.7: Spectre.Console CLI UI
* **Timestamp**: `2026-09-12T06:54:26+02:00`
* **Author**: Calvin Kohl
* **Scope**: Terminal user interface in `TakApp.Cli`.
* **Changes**:
  * [AnsiBoardRenderer.cs](file:///e:/repos/tak-p2p/src/TakApp.Cli/Rendering/AnsiBoardRenderer.cs): Rich ANSI board table with Unicode glyphs (Flat `○`, Standing `▲`, Capstone `◈`), stack height subscripts (`○³`, `▲²`), reserve inventories, and active turn banner.
  * [SteppedCommandParser.cs](file:///e:/repos/tak-p2p/src/TakApp.Cli/Input/SteppedCommandParser.cs): Type-only conversational stepped input (type coordinate $\to$ choose piece type on empty, or choose lift/direction/drops on owned stack) alongside direct PTN shortcut execution.
  * [StackInspector.cs](file:///e:/repos/tak-p2p/src/TakApp.Cli/Rendering/StackInspector.cs): Vertical cross-section table of all pieces in a stack from base to top (`inspect c3` or `?c3`).
  * [Program.cs](file:///e:/repos/tak-p2p/src/TakApp.Cli/Program.cs): Complete interactive game loop with main menu, local matches, Nostr quick play simulation, and invite code generation/joining.

---

### [9e7cb32](https://github.com/Dvrkstvr/tak-p2p/commit/9e7cb32) - Milestone M1.6: Time & Stale System
* **Timestamp**: `2026-09-12T06:46:25+02:00`
* **Author**: Calvin Kohl
* **Scope**: Time sync and timeout adjudication in `TakEngine.Core.Session`.
* **Changes**:
  * [INtpTimeService.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Session/INtpTimeService.cs) & [NtpTimeService.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Session/NtpTimeService.cs): Network time synchronization querying public NTP pools (`pool.ntp.org`, `time.cloudflare.com`, `time.google.com`) over UDP port 123 using RFC 5905 packet parsing to prevent local system clock tampering.
  * [StaleMatchMonitor.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Session/StaleMatchMonitor.cs): Evaluates matches against NTP time: transitions to `GameStatus.Stale` at $\ge 3$ days (raising warning) and terminates as `GameStatus.DrawTimeout` at $\ge 7$ days, updating SQLite records.
  * [NtpTimeTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Core.Tests/NtpTimeTests.cs) & [StaleSystemTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Core.Tests/StaleSystemTests.cs): 10 unit tests verifying NTP parsing, mock clocks, boundary conditions, and database status updates.
* **Test Suite**: 84 tests passing.

---

### [61d967d](https://github.com/Dvrkstvr/tak-p2p/commit/61d967d) - Milestone M1.5: Quick Play & Direct Codes
* **Timestamp**: `2026-09-12T06:41:52+02:00`
* **Author**: Calvin Kohl
* **Scope**: Peer discovery and matchmaking in `TakEngine.Transport.Matchmaking`.
* **Changes**:
  * [InviteCode.cs](file:///e:/repos/tak-p2p/src/TakEngine.Transport/Matchmaking/InviteCode.cs): Shareable match URIs (`tak://invite?...`) and compact QR-friendly base64 tokens (`TAK1_...`).
  * [MatchmakingModels.cs](file:///e:/repos/tak-p2p/src/TakEngine.Transport/Matchmaking/MatchmakingModels.cs): Challenge proposals, acceptances, and deterministic `ColorResolver` calculating identical peer colors from a shared random seed.
  * [QuickPlayMatchmaker.cs](file:///e:/repos/tak-p2p/src/TakEngine.Transport/Matchmaking/QuickPlayMatchmaker.cs): Ephemeral `kind: 20001` broadcast generation and discovery with 60-second TTL.
  * [InviteCodeTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Transport.Tests/InviteCodeTests.cs) & [MatchmakingHandshakeTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Transport.Tests/MatchmakingHandshakeTests.cs): 8 unit tests verifying round-trip fidelity, deterministic color resolution, and challenge sessions.
* **Test Suite**: 74 tests passing.

---

### [8bce863](https://github.com/Dvrkstvr/tak-p2p/commit/8bce863) - Milestone M1.4 + Extensions: Nostr Transport MVP
* **Timestamp**: `2026-09-12T06:38:54+02:00`
* **Author**: Calvin Kohl
* **Scope**: P2P transport, encryption, WebAssembly skeleton, and spectator engine.
* **Changes**:
  * [NostrModels.cs](file:///e:/repos/tak-p2p/src/TakEngine.Transport/Nostr/NostrModels.cs): NIP-01 wire messaging (`EVENT`, `REQ`, `CLOSE`, `OK`, `EOSE`, `NOTICE`), filter queries, and event ID hashing.
  * [Nip44Encryption.cs](file:///e:/repos/tak-p2p/src/TakEngine.Transport/Nostr/Nip44Encryption.cs): End-to-end NIP-44 payload encryption (ChaCha20-Poly1305 + HKDF).
  * [NostrRelayConnection.cs](file:///e:/repos/tak-p2p/src/TakEngine.Transport/Nostr/NostrRelayConnection.cs) & [NostrTransportClient.cs](file:///e:/repos/tak-p2p/src/TakEngine.Transport/Nostr/NostrTransportClient.cs): Managed multi-relay WebSocket client.
  * [TransportEnvelope.cs](file:///e:/repos/tak-p2p/src/TakEngine.Transport/TransportEnvelope.cs): Added `SerializerOptions` with `UnsafeRelaxedJsonEscaping` to prevent escaping Tak notation characters (`+`, `>`, `<`).
  * [ISpectatorGameSession.cs](file:///e:/repos/tak-p2p/src/TakEngine.Abstractions/ISpectatorGameSession.cs), [DelayedBroadcastQueue.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Session/DelayedBroadcastQueue.cs), [SpectatorGameSession.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Session/SpectatorGameSession.cs): Spectator observation engine.
  * `TakApp.Blazor`: WebAssembly client structure and automated GitHub Pages deployment workflow ([deploy-gh-pages.yml](file:///e:/repos/tak-p2p/.github/workflows/deploy-gh-pages.yml)).
  * [NostrMessageTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Transport.Tests/NostrMessageTests.cs), [Nip44EncryptionTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Transport.Tests/Nip44EncryptionTests.cs), [TransportBenchmarkTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Transport.Tests/TransportBenchmarkTests.cs), [SpectatorTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Core.Tests/SpectatorTests.cs): Verified round-trip SLA (< 10 ms vs 300 ms target).
* **Test Suite**: 66 tests passing.

---

### [773490d](https://github.com/Dvrkstvr/tak-p2p/commit/773490d) - Milestone M1.3: Cryptography & SQLite Persistence
* **Timestamp**: `2026-09-12T06:24:15+02:00`
* **Author**: Calvin Kohl
* **Scope**: State hashing, cryptographic signatures, and SQLite database storage.
* **Changes**:
  * [StateHasher.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Cryptography/StateHasher.cs): Genesis hashes, SHA-256 state hashing, and unbroken hash chain verification ($\text{PrevStateHash} \to \text{StateHash}$).
  * [CryptoSigner.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Cryptography/CryptoSigner.cs): Ed25519 keypair generation, message signing, and verification.
  * [SqliteGameStorage.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Storage/SqliteGameStorage.cs) & [StorageModels.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Storage/StorageModels.cs): SQLite database engine implementing `Games` and `Moves` tables, foreign key cascades, and active games queries.
  * [ReplayProvider.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Storage/ReplayProvider.cs): Instant $O(1)$ turn scrubbing to past states using cached TPS snapshots.
  * [CryptoTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Core.Tests/CryptoTests.cs) & [SqliteStorageTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Core.Tests/SqliteStorageTests.cs): 8 unit tests covering genesis hashing, tamper detection, and SQLite persistence.
* **Test Suite**: 52 tests passing.

---

### [e78a023](https://github.com/Dvrkstvr/tak-p2p/commit/e78a023) - Milestone M1.2: PTN & TPS Serialization
* **Timestamp**: `2026-09-12T06:21:11+02:00`
* **Author**: Calvin Kohl
* **Scope**: Official notation serialization and deserialization in `TakEngine.Core.Serialization`.
* **Changes**:
  * [TpsSerializer.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Serialization/TpsSerializer.cs): Export and parse Tak Positional System strings (e.g. `x4,1/x5/x2,1S,x2/x,2C,x3/2,x4 1 3`) with full stack and reserve reconstruction.
  * [PtnParser.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Serialization/PtnParser.cs): Standard Portable Tak Notation parser for placements, slides with drops (`3c3+12`), full games with metadata/comments/results, and match replay.
  * [TpsTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Core.Tests/TpsTests.cs) & [PtnTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Core.Tests/PtnTests.cs): 21 unit tests covering round-tripping, headers, and deterministic match replays.
* **Test Suite**: 44 tests passing.

---

### [a632620](https://github.com/Dvrkstvr/tak-p2p/commit/a632620) - Milestone M1.1: Core Rule Foundation & DFS Road Finder
* **Timestamp**: `2026-09-12T06:18:34+02:00`
* **Author**: Calvin Kohl
* **Scope**: Rules, stacks, board, and road finding in `TakEngine.Core`.
* **Changes**:
  * [PieceStack.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Board/PieceStack.cs): Board cell stack managing piece layers, lifts, pushes, pops, and wall flattening.
  * [GameBoard.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Board/GameBoard.cs): Grid management for 4x4, 5x5, 6x6; reserve tracking; Turn 1 swap rule; carry limits ($C \le \text{Size}$); capstone wall flattening; road and flat count win adjudication.
  * [RoadFinder.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Rules/RoadFinder.cs): Orthogonal graph traversal detecting continuous North-South and East-West roads for Flat and Capstone pieces.
  * [MoveValidator.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Rules/MoveValidator.cs): Dynamic legal move generator for placements and slide partitions.
  * [BoardTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Core.Tests/BoardTests.cs), [MovementTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Core.Tests/MovementTests.cs), [RoadFinderTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Core.Tests/RoadFinderTests.cs): 23 comprehensive unit tests.
* **Test Suite**: 23 tests passing.

---

### [5faacbe](https://github.com/Dvrkstvr/tak-p2p/commit/5faacbe) - Initial Project Initialization
* **Timestamp**: `2026-09-12T06:11:08+02:00`
* **Author**: Calvin Kohl
* **Scope**: Repository initialization, solutions, and core abstraction contracts.
* **Changes**:
  * Initialized Git repository with .NET `.gitignore`.
  * Created [TakGame.sln](file:///e:/repos/tak-p2p/TakGame.sln) and [TakGame.slnx](file:///e:/repos/tak-p2p/TakGame.slnx).
  * Created 7 initial projects: `TakEngine.Abstractions`, `TakEngine.Core`, `TakEngine.Transport`, `TakApp.Cli`, `TakApp.Avalonia`, `TakEngine.Core.Tests`, `TakEngine.Transport.Tests`.
  * Implemented core enums ([GameEnums.cs](file:///e:/repos/tak-p2p/src/TakEngine.Abstractions/Enums/GameEnums.cs)), models ([TakModels.cs](file:///e:/repos/tak-p2p/src/TakEngine.Abstractions/Models/TakModels.cs)), and interface ([ITakGameSession.cs](file:///e:/repos/tak-p2p/src/TakEngine.Abstractions/ITakGameSession.cs)).
  * Created complete documentation suite in `docs/` and root `README.md`.
* **Test Suite**: 2 boilerplate tests passing.

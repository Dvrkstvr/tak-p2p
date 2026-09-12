# Development Log (DevLog)

This document tracks all project milestones, architectural additions, and commits with timestamps and file changes, synchronized directly from Git and GitHub pushes.

---

## Commit & Milestone Timeline

| [`c4276b0`](https://github.com/Dvrkstvr/tak-p2p/commit/c4276b0) | 2026-09-12 08:10:00 | **Avalonia Native Mobile (Android & iPad/iOS)** | Scaffolded `TakApp.Avalonia.Android` (targeting `net10.0-android`, APK output, splash screen, permissions) and `TakApp.Avalonia.iOS` (targeting `net10.0-ios`, iPad & iPhone device family profiles), transitioned `TakApp.Avalonia` into shared cross-platform library and `TakApp.Avalonia.Desktop` into desktop executable; 107 passing unit tests. | 107 |
| [`114436a`](https://github.com/Dvrkstvr/tak-p2p/commit/114436a) | 2026-09-12 08:09:18 | **Nostr Player Nicknames & Profiles** | Implemented Nostr `kind: 0` user profile publishing and metadata queries with in-memory caching, custom nickname support in `InviteCode` URIs/tokens, Profile modal in Blazor WASM, and nickname badges in game headers; 107 passing unit tests. | 107 |
| [`c330c89`](https://github.com/Dvrkstvr/tak-p2p/commit/c330c89) | 2026-09-12 07:56:47 | **Invite UX & Multi-Device Nostr Linking** | Implemented NIP-19 `npub`/`nsec` Bech32 codec, 1-click playable web invite links (`/?invite=TAK1_...`), native SVG QR code generator via `Net.Codecrete.QrCodeGenerator`, Web Share API (`navigator.share`), URL query challenge detection, and multi-device 'Link Mobile / Devices' pairing modal; 104 passing unit tests. | 104 |
| [`23caed5`](https://github.com/Dvrkstvr/tak-p2p/commit/23caed5) | 2026-09-12 07:48:07 | **Offline AI Practice Bot** | Implemented `MinimaxTakBot` and `TakEvaluator` with Alpha-Beta pruning, in-memory `Clone`, `GetAllLegalMoves`, Blazor WASM AI practice mode with difficulty picker, and CLI vs AI option; 100 passing unit tests. | 100 |
| [`47704b8`](https://github.com/Dvrkstvr/tak-p2p/commit/47704b8) | 2026-09-12 07:36:21 | **Release Workflow CI/CD** | Added `.github/workflows/release.yml` with cross-platform matrix publishing for `TakApp.Avalonia` and `TakApp.Cli` (Windows `.zip`, Linux `.tar.gz`) with SHA-256 checksums and automated GitHub Releases; updated README with release badges and publishing guide. | 93 |
| [`f6b209c`](https://github.com/Dvrkstvr/tak-p2p/commit/f6b209c) | 2026-09-12 07:27:06 | **CLI Hardening & MVP Audit** | Fixed Spectre.Console markup escaping crashes, added `SafeClear` for headless/redirected terminal execution, verified MVP user flows across Web and CLI, updated M1.9 status. | 93 |
| [`93addfc`](https://github.com/Dvrkstvr/tak-p2p/commit/93addfc) | 2026-09-12 07:16:18 | **README Streamlining** | Removed the legacy Implementation Progress milestone table from README, deferring milestone tracking to DEVLOG. | 93 |
| [`4e429f3`](https://github.com/Dvrkstvr/tak-p2p/commit/4e429f3) | 2026-09-12 07:13:00 | **README & Screenshots** | Added Play Now CTA for live GitHub Pages web client, full supported devices matrix (Windows, Linux, MacBook, Android, iPhone, Web), and responsive vector screenshot cards. | 93 |
| [`5ad1f87`](https://github.com/Dvrkstvr/tak-p2p/commit/5ad1f87) | 2026-09-12 07:11:45 | **Milestone M1.8** | Avalonia UI Prototype: Vector board renderer, MVVM CommunityToolkit bindings, piece stacks, `TakGameSession` implementation. | 93 |
| [`f1afbfd`](https://github.com/Dvrkstvr/tak-p2p/commit/f1afbfd) | 2026-09-12 07:02:11 | **DevLog & Rule Sync** | Updated DevLog with recent commits and verified git push tracking. | 84 |
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

### [c4276b0](https://github.com/Dvrkstvr/tak-p2p/commit/c4276b0) - Avalonia Native Mobile Scaffolding (Android & iPad/iOS)
* **Timestamp**: `2026-09-12T08:10:00+02:00`
* **Author**: Calvin Kohl
* **Scope**: Scaffolding native mobile projects for Android (phone/tablet) and iPad/iOS using Avalonia UI's canonical multi-platform architecture.
* **Changes**:
  * [TakApp.Avalonia.csproj](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/TakApp.Avalonia.csproj): Converted from desktop `WinExe` into a shared cross-platform class library containing all MVVM views, viewmodels, assets, and themes.
  * [App.axaml.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/App.axaml.cs): Added multi-lifetime handlers supporting desktop (`IClassicDesktopStyleApplicationLifetime`), Android (`IActivityApplicationLifetime`), and iOS / iPadOS (`ISingleViewApplicationLifetime`).
  * [TakApp.Avalonia.Desktop.csproj](file:///e:/repos/tak-p2p/src/TakApp.Avalonia.Desktop/TakApp.Avalonia.Desktop.csproj): Desktop head project (`WinExe`) referencing `Avalonia.Desktop` and shared core.
  * [Program.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia.Desktop/Program.cs): Desktop entry point for Windows, macOS, and Linux.
  * [app.manifest](file:///e:/repos/tak-p2p/src/TakApp.Avalonia.Desktop/app.manifest): Windows high-DPI and OS compatibility manifest.
  * [TakApp.Avalonia.Android.csproj](file:///e:/repos/tak-p2p/src/TakApp.Avalonia.Android/TakApp.Avalonia.Android.csproj): Android head targeting `net10.0-android`, with `Avalonia.Android` and AndroidX SplashScreen.
  * [MainActivity.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia.Android/MainActivity.cs) & [Application.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia.Android/Application.cs): Android activity and application lifecycle bootstrap.
  * [AndroidManifest.xml](file:///e:/repos/tak-p2p/src/TakApp.Avalonia.Android/Properties/AndroidManifest.xml): Configured package ID `com.takp2p.app`, Internet and NetworkState permissions, and multi-density screen / tablet support.
  * [TakApp.Avalonia.iOS.csproj](file:///e:/repos/tak-p2p/src/TakApp.Avalonia.iOS/TakApp.Avalonia.iOS.csproj): iOS head targeting `net10.0-ios`, with `Avalonia.iOS`.
  * [Main.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia.iOS/Main.cs) & [AppDelegate.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia.iOS/AppDelegate.cs): iOS application lifecycle bootstrap.
  * [Info.plist](file:///e:/repos/tak-p2p/src/TakApp.Avalonia.iOS/Info.plist): Configured for **iPad and iPhone** (`UIDeviceFamily = 1, 2`), landscape/portrait orientations, and bundle ID `com.takp2p.app`.
  * [TakGame.sln](file:///e:/repos/tak-p2p/TakGame.sln) & [TakGame.slnx](file:///e:/repos/tak-p2p/TakGame.slnx): Solution files updated to track all four Avalonia heads and core libraries.
  * [README.md](file:///e:/repos/tak-p2p/README.md) & [v1-mvp.md](file:///e:/repos/tak-p2p/docs/v1-mvp.md): Updated project layout, build guides, and mobile execution commands.
* **Test Suite**: 107 tests passing (100% pass rate).

---

### [114436a](https://github.com/Dvrkstvr/tak-p2p/commit/114436a) - Nostr Player Nicknames & Profiles
* **Timestamp**: `2026-09-12T08:09:18+02:00`
* **Author**: Calvin Kohl
* **Scope**: Nostr `kind: 0` user profile metadata publishing, profile querying with caching, and custom nickname integration in matchmaking invites.
* **Changes**:
  * [NostrProfile.cs](file:///e:/repos/tak-p2p/src/TakEngine.Transport/Nostr/NostrProfile.cs): Data model for Nostr metadata events (`kind: 0`) and parser.
  * [NostrTransportClient.cs](file:///e:/repos/tak-p2p/src/TakEngine.Transport/Nostr/NostrTransportClient.cs): Added `PublishProfileAsync` and `QueryProfileAsync` with concurrent dictionary memory cache.
  * [InviteCode.cs](file:///e:/repos/tak-p2p/src/TakEngine.Transport/Matchmaking/InviteCode.cs): Added `HostNickname` property, URI parameter (`nick=`), and compact token encoding.
  * [ProfileModal.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Components/Modals/ProfileModal.razor): Blazor modal for editing and broadcasting Nostr profile nickname, about, and avatar.
  * [NostrProfileTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Transport.Tests/NostrProfileTests.cs): Unit tests for metadata event creation, parsing, fallback names, and invite code roundtripping.
* **Test Suite**: 107 tests passing (100% pass rate).

---

### [c330c89](https://github.com/Dvrkstvr/tak-p2p/commit/c330c89) - Invite UX Overhaul & Multi-Device Nostr Linking
* **Timestamp**: `2026-09-12T07:56:47+02:00`
* **Author**: Calvin Kohl
* **Scope**: Frictionless 1-click playable match invites, native dark-mode SVG QR codes, Web Share API, and universal Nostr multi-device pairing.
* **Changes**:
  * [Nip19.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Cryptography/Nip19.cs): Full BIP-173 / NIP-19 Bech32 encoder and decoder supporting `npub` and `nsec` key serialization.
  * [Nip19Tests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Core.Tests/Nip19Tests.cs): Unit tests verifying round-trip fidelity, hrp validation, and checksum verification.
  * [InviteCode.cs](file:///e:/repos/tak-p2p/src/TakEngine.Transport/Matchmaking/InviteCode.cs): Added `ToWebUrl(baseUrl)` and HTTP/HTTPS parsing support for `?invite=` parameters.
  * [InviteCodeTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Transport.Tests/InviteCodeTests.cs): Added unit tests for web URL round-tripping.
  * [QrCodeSvgHelper.cs](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Services/QrCodeSvgHelper.cs): Zero-dependency WASM-compatible SVG QR code generator styled for dark theme backgrounds.
  * [BrowserStorage.cs](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Services/BrowserStorage.cs): Added `SetKeypairAsync`, `ImportPrivateKeyAsync` (supporting `nsec1...` and raw hex), and `ClearIdentityAsync`.
  * [InviteModal.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Components/Modals/InviteModal.razor): Redesigned modal with tabbed view (QR & Shareable Link vs Token), high-contrast SVG QR code, 1-click "Copy Game Link", and native Web Share API (`navigator.share`).
  * [LinkDeviceModal.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Components/Modals/LinkDeviceModal.razor): Multi-device Nostr identity modal with privacy-shielded pairing QR codes, key import, and sovereign `nsec` backup.
  * [Home.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Pages/Home.razor): Added top identity status bar with `npub` badge and Link Mobile trigger; added automated URL query handling for `?link_identity=` and `?invite=`.
  * [tak-theme.css](file:///e:/repos/tak-p2p/src/TakApp.Blazor/wwwroot/css/tak-theme.css): Added `.btn-purple` button variant.
* **Test Suite**: 104 tests passing (100% pass rate).

---

### [23caed5](https://github.com/Dvrkstvr/tak-p2p/commit/23caed5) - Offline AI Practice Bot (Mobile, Web & Desktop)
* **Timestamp**: `2026-09-12T07:48:07+02:00`
* **Author**: Calvin Kohl
* **Scope**: Zero-server offline AI practice bot for mobile, web, desktop, and CLI.
* **Changes**:
  * [BotDifficulty.cs](file:///e:/repos/tak-p2p/src/TakEngine.Abstractions/Enums/BotDifficulty.cs): Added difficulty tier enum (`Easy`, `Medium`, `Hard`).
  * [ITakBot.cs](file:///e:/repos/tak-p2p/src/TakEngine.Abstractions/ITakBot.cs): Decoupled AI contract for move selection.
  * [PieceStack.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Board/PieceStack.cs) & [GameBoard.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Board/GameBoard.cs): Added fast in-memory `Clone()` methods and `GameBoard.FromSnapshot()` for zero-allocation tree search.
  * [MoveValidator.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Rules/MoveValidator.cs): Added `GetAllLegalMoves(GameBoard board)` generating all valid placements and slide movements.
  * [TakEvaluator.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/AI/TakEvaluator.cs): Heuristic evaluation scoring terminal wins (+/- 100k pts), orthogonal road connectivity and spanning threats, controlled flat stones, center control, and capstone mobility.
  * [MinimaxTakBot.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/AI/MinimaxTakBot.cs): Alpha-Beta Minimax search implementation supporting Easy (1-ply random top selection), Medium (2-ply Minimax), and Hard (3-4 ply Minimax) with turn 1 & 2 swap optimizations.
  * [WebGameSessionManager.cs](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Services/WebGameSessionManager.cs): Added `StartBotMatch(...)` and asynchronous `TriggerBotMoveAsync()` with ergonomic human-eye delay.
  * [Home.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Pages/Home.razor) & [Play.razor](file:///e:/repos/tak-p2p/src/TakApp.Blazor/Pages/Play.razor): Added AI difficulty pills (`Easy`, `Medium`, `Hard`), `🤖 Practice vs AI` CTA, active turn lock during bot thinking, and status indicators.
  * [Program.cs](file:///e:/repos/tak-p2p/src/TakApp.Cli/Program.cs): Added `[[2]] Practice vs AI Bot (Offline)` to Spectre.Console CLI.
  * [TakBotTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Core.Tests/TakBotTests.cs): 7 unit tests covering turn 1 swap rule, legal move queries, road victory seizing, threat neutralization, and benchmark execution time (< 100ms).
* **Test Suite**: 100 tests passing (100% pass rate).

---

### [47704b8](https://github.com/Dvrkstvr/tak-p2p/commit/47704b8) - Release Workflow CI/CD (Windows & Linux GUI/CLI)
* **Timestamp**: `2026-09-12T07:36:21+02:00`
* **Author**: Calvin Kohl
* **Scope**: Automated packaging and distribution pipeline for native desktop and terminal clients.
* **Changes**:
  * [.github/workflows/release.yml](file:///e:/repos/tak-p2p/.github/workflows/release.yml): Configured matrix build targeting `win-x64` (`windows-latest`) and `linux-x64` (`ubuntu-latest`) for both `TakApp.Avalonia` and `TakApp.Cli`. Packages self-contained single-file releases with zipped archives, SHA-256 integrity checksums, and automated publishing via `softprops/action-gh-release@v2` on tag pushes (`v*`) and manual workflow dispatch.
  * [README.md](file:///e:/repos/tak-p2p/README.md): Added GitHub Releases badge, updated the device overview matrix pointing to release downloads, and added standalone single-file publish commands to the execution guide.
* **Test Suite**: 93 tests passing (100% pass rate).

---

### [f6b209c](https://github.com/Dvrkstvr/tak-p2p/commit/f6b209c) - CLI Hardening & MVP Audit Verification
* **Timestamp**: `2026-09-12T07:27:06+02:00`
* **Author**: Calvin Kohl
* **Scope**: CLI client stability and cross-platform verification during MVP comprehensive audit.
* **Changes**:
  * [Program.cs](file:///e:/repos/tak-p2p/src/TakApp.Cli/Program.cs): Implemented `SafeClear()` to catch and suppress `System.IO.IOException` when standard console buffers are absent in headless, CI/CD, or piped environments; escaped main menu bracket markup (`[[1]]` through `[[5]]`) and handled EOF/null input loops.
  * [SteppedCommandParser.cs](file:///e:/repos/tak-p2p/src/TakApp.Cli/Input/SteppedCommandParser.cs): Escaped bracketed turn markers `[[Turn {turn} - {player}]]` and lift count prompts; added EOF null-guard.
  * [v1-mvp.md](file:///e:/repos/tak-p2p/docs/v1-mvp.md): Promoted Milestone M1.9 (Blazor WASM Client) to **COMPLETED** following end-to-end user browser validation.
* **Test Suite**: 93 tests passing (100% pass rate).

---

### [93addfc](https://github.com/Dvrkstvr/tak-p2p/commit/93addfc) - README Streamlining & Milestone Deferral
* **Timestamp**: `2026-09-12T07:16:18+02:00`
* **Author**: Calvin Kohl
* **Scope**: README cleanup.
* **Changes**:
  * [README.md](file:///e:/repos/tak-p2p/README.md): Removed the static implementation progress milestone table, consolidating all historical and active milestone tracking inside [DEVLOG.md](file:///e:/repos/tak-p2p/docs/DEVLOG.md).
* **Test Suite**: 93 tests passing.

---

### [4e429f3](https://github.com/Dvrkstvr/tak-p2p/commit/4e429f3) - README Modernization, Play Now CTA & Device Matrix
* **Timestamp**: `2026-09-12T07:13:00+02:00`
* **Author**: Calvin Kohl
* **Scope**: GitHub repository presentation, direct Web client access, multi-platform play status, and interface previews.
* **Changes**:
  * [README.md](file:///e:/repos/tak-p2p/README.md): Added high-prominence Play Now banner pointing directly to the live GitHub Pages app, comprehensive device matrix and status breakdowns for Windows app, Linux app, MacBook app, Android app, iPhone app, and Web client; added UI previews and local build guides.
  * [web-board.svg](file:///e:/repos/tak-p2p/docs/assets/screenshots/web-board.svg): Visual preview of the zero-install Blazor WebAssembly browser client.
  * [desktop-avalonia.svg](file:///e:/repos/tak-p2p/docs/assets/screenshots/desktop-avalonia.svg): Visual preview of the Avalonia native desktop client on Windows, Linux, and macOS.
  * [cli-ansi.svg](file:///e:/repos/tak-p2p/docs/assets/screenshots/cli-ansi.svg): Visual preview of the Spectre.Console ANSI terminal board.
  * [mobile-board.svg](file:///e:/repos/tak-p2p/docs/assets/screenshots/mobile-board.svg): Visual preview of the mobile PWA touchscreen experience on iPhone and Android.
  * [docs/assets/screenshots/README.md](file:///e:/repos/tak-p2p/docs/assets/screenshots/README.md): Guidelines and specifications for capturing real application screenshots.
* **Test Suite**: 93 tests passing (100%).

---

### [5ad1f87](https://github.com/Dvrkstvr/tak-p2p/commit/5ad1f87) - Milestone M1.8: Avalonia UI Prototype & TakGameSession Engine
* **Timestamp**: `2026-09-12T07:11:45+02:00`
* **Author**: Calvin Kohl
* **Scope**: Cross-platform desktop/mobile Avalonia GUI and concrete `ITakGameSession` implementation.
* **Changes**:
  * [TakGameSession.cs](file:///e:/repos/tak-p2p/src/TakEngine.Core/Session/TakGameSession.cs): Concrete implementation of `ITakGameSession` in `TakEngine.Core` supporting both local pass-and-play and remote P2P play over Nostr, with Ed25519 payload signing, SHA-256 state hash chaining, move validation, and reactive event notifications (`OnMoveExecuted`, `OnGameEnded`, `OnStaleWarning`, `OnTransportStatusChanged`, `OnProtocolViolationDetected`).
  * [SquareViewModel.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/ViewModels/SquareViewModel.cs): Observable square state managing piece representations, coordinate annotations, selection highlights, and legal target flags.
  * [BoardViewModel.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/ViewModels/BoardViewModel.cs): Grid coordinate mapping, square selection, placement piece selection, and slide move builder with direction and drop distribution controls.
  * [GameViewModel.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/ViewModels/GameViewModel.cs): Complete game lifecycle coordinator binding to `ITakGameSession`, tracking reserve counts, move history log, game over summaries, and transport status.
  * [NewGameViewModel.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/ViewModels/NewGameViewModel.cs): Interactive game launcher supporting 4x4, 5x5, and 6x6 board sizes with local and remote game creation.
  * [MainViewModel.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/ViewModels/MainViewModel.cs): Root navigation controller managing transitions between setup and active matches.
  * [SquareView.axaml](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/Views/SquareView.axaml) & [SquareView.axaml.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/Views/SquareView.axaml.cs): Vector piece stack rendering displaying flat stone disks, vertical standing walls, and crown capstones with multi-piece tower badges.
  * [BoardView.axaml](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/Views/BoardView.axaml) & [BoardView.axaml.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/Views/BoardView.axaml.cs): Dynamic `UniformGrid` vector board with wooden slate border and drop shadow.
  * [GameControlsView.axaml](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/Views/GameControlsView.axaml) & [GameControlsView.axaml.cs](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/Views/GameControlsView.axaml.cs): Placement selector and directional slide cross with lift and drop inputs.
  * [PlayerReserveView.axaml](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/Views/PlayerReserveView.axaml): Side trays displaying White and Black reserve stones and capstones.
  * [MoveHistoryView.axaml](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/Views/MoveHistoryView.axaml): Scrollable PTN turn list with monospace typography.
  * [GameView.axaml](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/Views/GameView.axaml) & [NewGameView.axaml](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/Views/NewGameView.axaml): Responsive layouts uniting board, reserves, controls, and game over announcements.
  * [MainView.axaml](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/Views/MainView.axaml) & [MainWindow.axaml](file:///e:/repos/tak-p2p/src/TakApp.Avalonia/Views/MainWindow.axaml): Reusable desktop/mobile UserControl container and desktop window frame.
  * [TakGameSessionTests.cs](file:///e:/repos/tak-p2p/tests/TakEngine.Core.Tests/TakGameSessionTests.cs): 8 comprehensive unit tests covering local swap rule, legal move queries, slide moves, road victory adjudication, resignation, remote move exchange with hash verification, and protocol violation traps.
* **Test Suite**: 93 tests passing (100% pass rate).

---

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

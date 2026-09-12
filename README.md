# Tak P2P: Decentralized Peer-to-Peer Tak

> A decentralized, peer-to-peer (P2P), zero-server implementation of the abstract strategy game **Tak**, supporting both synchronous (live) and asynchronous play across modern web browsers, Windows, Linux, macOS, Android, and iOS.

[![GitHub Pages Deployment](https://img.shields.io/badge/GitHub%20Pages-Live%20Deploy-success?logo=github&style=flat-square)](https://dvrkstvr.github.io/tak-p2p/)
[![Tests Passing](https://img.shields.io/badge/Tests-93%20passed-brightgreen?style=flat-square)](tests/)
[![Runtime](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&style=flat-square)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)
[![Nostr Protocol](https://img.shields.io/badge/Nostr-NIP--01%20%7C%20NIP--44-purple?style=flat-square)](https://nostr.com/)
[![UI Frameworks](https://img.shields.io/badge/UI-Blazor%20WASM%20%7C%20Avalonia%20%7C%20Spectre-orange?style=flat-square)](src/)

---

## 🎮 Play Now in Your Browser (Zero Install)

You can play a full match of Tak right now without downloading or installing any software:

### 🚀 **[👉 Click Here to Launch the Live Web Client 👈](https://dvrkstvr.github.io/tak-p2p/)**
**URL:** [https://dvrkstvr.github.io/tak-p2p/](https://dvrkstvr.github.io/tak-p2p/)

* **No Accounts Required:** Deterministic Ed25519 identity keypairs are generated and stored locally in your browser.
* **Zero Game Servers:** Direct peer-to-peer communications over encrypted public Nostr relays.
* **Instant Matchmaking:** Share a short invite code or QR token (`tak://...`) with a friend to begin playing immediately.
* **Mobile & Desktop Ready:** Fully responsive touch and mouse controls with high-definition vector SVG rendering.

---

## 📱 Supported Devices & Methods of Play

Tak P2P is engineered with a strict **Separation of Concerns**—the deterministic core engine (`TakEngine.Core`) and peer-to-peer wire protocol (`TakEngine.Transport`) are decoupled from the user interface, enabling native performance across all major operating systems and web platforms.

### Overview Matrix

| Device / Platform | Method of Play | Technology | Implementation Status | Quick Launch / Access |
| :--- | :--- | :--- | :--- | :--- |
| **🌐 Web Browser** | Zero-Install Web Client (PWA) | Blazor WebAssembly (.NET 10) | 🟢 **Implemented** | [Launch Web Client](https://dvrkstvr.github.io/tak-p2p/) |
| **🪟 Windows PC** | Native Desktop App & Terminal CLI | Avalonia UI + Spectre.Console | 🟢 **Implemented** | `TakApp.Avalonia` / `TakApp.Cli` |
| **🐧 Linux** | Native Desktop App & Terminal CLI | Avalonia UI (X11/Wayland) + CLI | 🟢 **Implemented** | `TakApp.Avalonia` / `TakApp.Cli` |
| **💻 MacBook / macOS** | Native Desktop App & Terminal CLI | Avalonia Desktop + Terminal CLI | 🟡 **Supported / Compiles** | Cross-platform .NET 10 Desktop |
| **🤖 Android** | Mobile Web / PWA & Native App | Mobile PWA (Current) / Avalonia | 🟡 **Playable via Web; Native in Progress** | Add to Home Screen in Chrome |
| **📱 iPhone & iPad** | Mobile Web / PWA & Native App | Safari PWA (Current) / Avalonia | 🟡 **Playable via Web; Native Planned** | Add to Home Screen in Safari |

---

### Detailed Device Breakdown: What is Implemented vs. What is Missing

#### 1. 🌐 Web Client (Desktop & Mobile Browsers)
* **Method of Play:** Web application running client-side in the browser via WebAssembly (Blazor WASM).
* **Target Devices:** Any modern web browser (Google Chrome, Mozilla Firefox, Apple Safari, Microsoft Edge, Brave, Opera) on Windows, macOS, Linux, iOS, and Android.
* **✅ What's Already Implemented:**
  * Full Blazor WebAssembly compilation pipeline hosted permanently free on GitHub Pages.
  * Interactive vector SVG board supporting standard 4x4, 5x5, and 6x6 Tak board configurations.
  * 3D isometric piece stacks, standing walls, and capstones with dynamic height counters.
  * Real-time WebSocket connection to public Nostr relays (`wss://relay.damus.io`, `wss://nos.lol`, `wss://relay.primal.net`).
  * End-to-end payload encryption using NIP-44 ChaCha20-Poly1305.
  * Local hotseat play and instant peer-to-peer match invites via shareable links and QR tokens.
  * Reversible PTN move scrubber to review previous moves in the active match.
* **⏳ What's Missing / Next:**
  * Progressive Web App (PWA) Service Worker cache for 100% offline standalone usage.
  * IndexedDB storage bridge for indefinite local browser match archives.

#### 2. 🪟 Windows App
* **Method of Play:** Native desktop GUI (`TakApp.Avalonia`) and rich command-line interface (`TakApp.Cli`).
* **Target Devices:** Windows 10 and Windows 11 (x64 and ARM64).
* **✅ What's Already Implemented:**
  * Complete Avalonia MVVM desktop application (`TakApp.Avalonia`) with hardware-accelerated rendering.
  * Responsive board view, reserve inventory trays, turn indicator clocks, and PTN history panels.
  * Full local database persistence via SQLite (`SqliteGameStorage`) with fast $O(1)$ TPS state restoration.
  * Cryptographic state chain verification with SHA-256 and Ed25519 signature validation.
  * Spectre.Console interactive ANSI terminal client (`TakApp.Cli`) with conversational step inputs.
* **⏳ What's Missing / Next:**
  * Windows MSIX / InnoSetup installer package with start menu shortcuts.
  * Native Windows Action Center toast notifications when an opponent moves in an asynchronous match.
  * Seamless auto-updater service.

#### 3. 🐧 Linux App
* **Method of Play:** Native desktop GUI (`TakApp.Avalonia`) and rich terminal CLI (`TakApp.Cli`).
* **Target Devices:** Ubuntu, Debian, Fedora, Arch Linux, openSUSE, SteamOS / Steam Deck (X11 & Wayland).
* **✅ What's Already Implemented:**
  * Shared Avalonia Desktop UI running natively on Linux without emulation or wrappers.
  * Native terminal CLI (`TakApp.Cli`) featuring full ANSI color rendering and arrow-key menu navigation in all POSIX terminals.
  * Native SQLite storage and multi-relay WebSocket networking.
* **⏳ What's Missing / Next:**
  * Packaged distributions: Flatpak (`flathub`), AppImage, and Snap packages.
  * `libnotify` integration for desktop notification daemon alerts on opponent turns.

#### 4. 💻 MacBook / macOS App
* **Method of Play:** Native desktop GUI (`TakApp.Avalonia`) and Terminal CLI (`TakApp.Cli`).
* **Target Devices:** Apple Silicon (M1/M2/M3/M4) and Intel-based Macs running macOS 12 Monterey or newer.
* **✅ What's Already Implemented:**
  * Cross-platform Avalonia codebase targets .NET 10 desktop runtime with native macOS rendering support.
  * Reactive session observables and deterministic game rule verification.
  * Console app executes natively in macOS Terminal.app and iTerm2.
* **⏳ What's Missing / Next:**
  * Apple Developer ID codesigning and Gatekeeper notarization.
  * Self-contained `.app` application bundle packaged inside a `.dmg` installer.
  * Native macOS global menu bar integration and Dock badge indicators for pending moves.

#### 5. 🤖 Android App
* **Method of Play:** Mobile Web / PWA (Current) and Native Android Client (In Progress).
* **Target Devices:** Android smartphones, foldable devices, and tablets running Android 8.0+.
* **✅ What's Already Implemented:**
  * Fully touch-optimized mobile web client accessible via Chrome, Firefox, or Edge on Android.
  * Single-tap piece placement and multi-step drop-distribution slider controls for tower movements.
  * High-DPI SVG board rendering that scales smoothly to any smartphone aspect ratio.
  * Camera-scannable QR code tokens for instant local peer handshakes.
* **⏳ What's Missing / Next:**
  * Dedicated `TakApp.Android` project head using Avalonia for Android.
  * Google Play Store package release (`.apk` / `.aab`).
  * Android foreground service to maintain long-lived Nostr WebSocket connectivity for asynchronous turn notifications.
  * Haptic vibration feedback on piece drops and wall flattening.

#### 6. 📱 iPhone & iPad App
* **Method of Play:** Mobile Web / Safari PWA (Current) and Native iOS Client (Planned).
* **Target Devices:** iPhone and iPad running iOS / iPadOS 15+.
* **✅ What's Already Implemented:**
  * High-performance Safari mobile experience with full touch gestures.
  * "Add to Home Screen" PWA compatibility—runs as a standalone, distraction-free app without browser URL bars.
  * Deterministic client-side game engine and Nostr WebSocket connectivity over mobile Safari.
* **⏳ What's Missing / Next:**
  * Dedicated `TakApp.iOS` native project head using Avalonia for iOS.
  * Xcode project configuration and Apple provisioning profiles.
  * Apple TestFlight beta program and App Store deployment.
  * Apple Push Notification service (APNs) integration for asynchronous turn notifications when the app is suspended.

---

## 🖼️ Interface Previews & Screenshots

### 1. 🌐 Web Client (Blazor WebAssembly)
> Zero-install web client running in any browser with live Nostr P2P matchmaking, SVG board rendering, and turn history.
![Tak P2P Web Client](docs/assets/screenshots/web-board.svg)

### 2. 🪟 🐧 💻 Desktop Client (Avalonia UI)
> Hardware-accelerated desktop application on Windows, Linux, and macOS with local SQLite storage, game setup wizard, and move inspection.
![Tak P2P Desktop Client](docs/assets/screenshots/desktop-avalonia.svg)

### 3. 📟 Terminal CLI (Spectre.Console)
> Full ANSI terminal experience with interactive menus, 3D stack layer inspector, and conversational stepped input.
![Tak P2P Terminal CLI](docs/assets/screenshots/cli-ansi.svg)

### 4. 📱 Mobile & PWA Experience (iPhone & Android)
> Responsive mobile design with touch drag-and-drop, slide distribution bars, and camera QR code token exchange.
![Tak P2P Mobile PWA](docs/assets/screenshots/mobile-board.svg)

---

## 🏛️ System Overview & Core Philosophy

### Architectural Invariants

* **Zero Authoritative Game Servers:** The network layer functions strictly as an encrypted "dumb pipe" / store-and-forward mailbox over Nostr relays. Clients never trust remote states; all moves and state transitions are verified deterministically on the local device.
* **Separation of Concerns:**
  * `TakEngine.Abstractions`: Shared contracts, immutable records, data structures (publicly distributed).
  * `TakEngine.Core`: Private game logic, DFS graph road traversal, cryptographic hashing, invariant checks, state storage.
  * `TakEngine.Transport`: Nostr WebSocket relay interface, NIP-44 encryption, envelope serialization.
  * Frontends (`TakApp.Avalonia`, `TakApp.Cli`, `TakApp.Blazor`): Pure UI views consuming reactive observables/events.
* **Deterministic Rule Adjudication:** Illegal moves are mathematically impossible to force onto a peer. If an opponent injects an invalid payload, the receiving client drops the payload and flags the peer.
* **Cryptographic State Hashing:** Every move entity strictly maintains a verifiable hash chain:
  $$\text{StateHash} = \text{SHA-256}(\text{PrevStateHash} \,\|\, \text{TurnIndex} \,\|\, \text{PlayerPubKey} \,\|\, \text{PtnMove} \,\|\, \text{TpsSnapshot})$$

---

## 📂 Repository & Solution Layout

```
TakGame.sln / TakGame.slnx
├── src/
│   ├── TakEngine.Abstractions/       # [Shared NuGet candidate]
│   │   ├── Enums/                    # PieceType, PlayerColor, Direction, GamePhase
│   │   ├── Models/                   # Coord, StackSnapshot, BoardSnapshot, TakMove, BroadcastModels
│   │   ├── ITakGameSession.cs        # Primary interface consumed by all frontends
│   │   └── ISpectatorGameSession.cs  # Spectator/broadcast observable interface
│   │
│   ├── TakEngine.Core/               # [Engine & Rules Core]
│   │   ├── Board/                    # Grid, Stacks, Piece Inventories, Move Execution
│   │   ├── Rules/                    # Invariant rules, Carry limits, DFS Road finder, MoveValidator
│   │   ├── Serialization/            # PTN (Portable Tak Notation) & TPS (Tak Positional System)
│   │   ├── Cryptography/             # Keypairs, Signatures, SHA-256 State Hashing
│   │   ├── Storage/                  # SQLite database engine, Match logs, Replay provider
│   │   └── Session/                  # TakGameSession, DelayedBroadcastQueue, SpectatorSession, NTP
│   │
│   ├── TakEngine.Transport/          # [Nostr P2P Infrastructure]
│   │   ├── Nostr/                    # WebSocket client, NIP-01/NIP-44 wrappers
│   │   ├── Matchmaking/              # Invite code parser, Ephemeral broadcast handler
│   │   └── TransportEnvelope.cs      # Signed wire models
│   │
│   ├── TakApp.Blazor/                # [Runnable Zero-Install Web Client]
│   │   ├── Components/               # TakBoardView (SVG), PieceStackSvg, SlideBar, Panels
│   │   ├── Pages/                    # Play.razor, Lobby, Settings
│   │   ├── wwwroot/                  # Static assets & GitHub Pages deployment
│   │   └── .github/workflows/        # Automated GitHub Pages CI/CD pipeline
│   │
│   ├── TakApp.Avalonia/              # [Runnable Cross-Platform Desktop/Mobile GUI]
│   │   ├── ViewModels/               # MVVM ViewModels (CommunityToolkit.Mvvm)
│   │   ├── Views/                    # Canvas/Skia board renderer, Match controls
│   │   └── Services/                 # Local OS notification scheduler
│   │
│   └── TakApp.Cli/                   # [Runnable Console App]
│       ├── Program.cs                # Entry point, Interactive menus
│       ├── Rendering/                # Spectre.Console ANSI board, stack layer inspector
│       └── Input/                    # Conversational stepped typed input & PTN command parser
│
└── tests/
    ├── TakEngine.Core.Tests/         # Rule engine unit tests, DFS validation, PTN parser, Crypto, SQLite, Spectator tests
    └── TakEngine.Transport.Tests/    # Relay serialization, Round-trip latency tests, Invite codes, NIP-44 encryption
```

---

## 🛠️ Local Build & Execution Guide

### Prerequisites
* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### 1. Run All Tests
```powershell
dotnet test TakGame.sln
```
*(Verifies 100% of the 93 unit tests across engine core and transport suites).*

### 2. Run the Web Client Locally
```powershell
dotnet run --project src/TakApp.Blazor/TakApp.Blazor.csproj
```
Open `http://localhost:5000` in your web browser.

### 3. Run the Desktop Client (Windows / Linux / macOS)
```powershell
dotnet run --project src/TakApp.Avalonia/TakApp.Avalonia.csproj
```

### 4. Run the Terminal CLI
```powershell
dotnet run --project src/TakApp.Cli/TakApp.Cli.csproj
```

---

## 📖 Documentation Index

1. [Full Specification & Handoff Document](docs/PROJECT_SPECIFICATION.md) - Complete consolidated master specification.
2. [System Overview](docs/system-overview.md) - High-level architecture, principles, and invariants.
3. [Version 1.0 (MVP) Specification](docs/v1-mvp.md) - Deliverables, layout, Nostr transport, SQLite schema, `ITakGameSession` API, and milestones.
4. [Version 2.0 (Competitive & Tournaments) Specification](docs/v2-tournaments.md) - Co-signed receipts, Swiss tournaments, Elo oracle, anti-cheat, spectator broadcasting, and v2 database schema.
5. [Wire Protocol Specification](docs/wire-protocol.md) - Nostr envelopes, NIP-44 encryption, matchmaking, and spectator broadcast events.
6. [Database Schema Specification](docs/database-schema.md) - Complete SQLite schema for v1 and v2 migrations.
7. [Blazor WebAssembly & GitHub Pages Specification](docs/blazor-web-github-pages.md) - Design and zero-cost deployment architecture for the browser client.
8. [Spectator & Broadcast Implementation Plan](docs/spectator-implementation-plan.md) - Real-time observation, delayed public streams, and feature match directory.
9. [Development Log (DevLog)](docs/DEVLOG.md) - Chronological commit log, timestamps, and milestone progress synchronized with GitHub.

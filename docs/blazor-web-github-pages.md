# Blazor WebAssembly & GitHub Pages Specification

> **Target Platform:** Modern Web Browsers (Desktop & Mobile)  
> **Framework:** .NET 10 Blazor WebAssembly (Client-Side WASM)  
> **Hosting:** GitHub Pages (100% Free Static Hosting via GitHub Actions)  
> **Architectural Invariant:** Zero Authoritative Game Servers — Direct Nostr Relay Transport & Client-Side Verification

---

## 1. Executive Summary

This document specifies the design, architecture, and deployment strategy for the web client of **Tak P2P** (`TakApp.Blazor`).

By targeting **Blazor WebAssembly (.NET 10)**, the web frontend achieves complete code reuse of the existing core engine libraries:
* `TakEngine.Abstractions`: Immutable models (`Coord`, `TakMove`, `BoardSnapshot`, `PieceStack`).
* `TakEngine.Core`: Move validation, carry limits, DFS road-checking graph algorithms (`RoadFinder`), cryptographic state chaining (`StateHasher`), and PTN/TPS serialization.
* `TakEngine.Transport`: Nostr WebSocket communications, NIP-44 direct encryption, and ephemeral matchmaking.

Because the game architecture is fully decentralized (deterministic local rule adjudication with public Nostr relays acting as the transport pipe), the entire web application compiles to static WebAssembly, JavaScript, and HTML assets. It requires **zero backend servers or paid cloud databases** and can be hosted permanently free on **GitHub Pages**.

---

## 2. Solution Architecture & Repository Layout

The Blazor WebAssembly project will reside in `src/TakApp.Blazor` and integrate seamlessly into the existing solution structure:

```
TakGame.sln
├── src/
│   ├── TakEngine.Abstractions/       # Shared models, enums, ITakGameSession
│   ├── TakEngine.Core/               # Deterministic rule engine, DFS road finder, StateHasher
│   ├── TakEngine.Transport/          # Nostr WebSockets, NIP-44, matchmaking
│   ├── TakApp.Cli/                   # Console frontend (Spectre.Console)
│   ├── TakApp.Avalonia/              # Desktop / Mobile frontend (Avalonia XAML)
│   └── TakApp.Blazor/                # [NEW] Web frontend (Blazor WASM)
│       ├── Components/               # Reusable UI components
│       │   ├── BoardGrid.razor       # Interactive SVG/CSS board
│       │   ├── BoardSquare.razor     # Individual square & piece stack rendering
│       │   ├── StackInspector.razor  # Modal/panel to inspect stack pieces & peel counts
│       │   ├── MoveControls.razor    # PTN move input, slide step selector
│       │   ├── MatchmakingModal.razor# Invite code generator, QR code, quick-play queue
│       │   └── MoveHistory.razor     # Reversible move list & turn status
│       ├── Pages/
│       │   ├── Home.razor            # Lobby: Active games, New Game, Quick Play
│       │   ├── Play.razor            # Live match view
│       │   └── Settings.razor        # Keypair management, relay list, sound toggles
│       ├── Services/
│       │   ├── WebStorageService.cs  # Browser IndexedDB / LocalStorage adapter
│       │   ├── WebNotificationService.cs # HTML5 browser push notifications for turns
│       │   └── QrCodeGenerator.cs    # Client-side QR generation for invite codes
│       ├── wwwroot/
│       │   ├── index.html            # Single-page app root with WebAssembly loader
│       │   ├── 404.html              # GitHub Pages SPA fallback router
│       │   ├── .nojekyll             # Prevents GitHub Pages Jekyll build
│       │   ├── css/                  # Custom theme (dark mode, wood/stone board styles)
│       │   └── js/                   # Native JS interop (clipboard, audio, notifications)
│       └── TakApp.Blazor.csproj
└── .github/
    └── workflows/
        └── deploy-gh-pages.yml       # Automated GitHub Actions deployment pipeline
```

---

## 3. Web-Specific Considerations & Solutions

### 3.1 WebSockets & Nostr Relays
* **Compatibility:** Standard browsers support outbound Secure WebSockets (`wss://`).
* **Implementation:** `TakEngine.Transport` utilizes .NET `ClientWebSocket`, which is fully supported under .NET WebAssembly in modern browsers.
* **Public Relays:** Connects directly from the user's browser to public Nostr relays (`wss://relay.damus.io`, `wss://nos.lol`, `wss://relay.primal.net`) over encrypted TLS.

### 3.2 Client-Side Storage (Browser vs. SQLite)
On desktop, `TakEngine.Core` uses SQLite via `Microsoft.Data.Sqlite`. In a browser sandbox:
* **Option A (Recommended for Web): IndexedDB / LocalStorage Bridge**
  * Store active game sessions, match history, and Ed25519/Secp256k1 keypairs in browser `IndexedDB`.
  * `TakApp.Blazor` implements an in-memory session cache that flushes serialized match logs and turn chains (`StorageModels`) directly into IndexedDB.
* **Option B: SQLite WebAssembly (`sqlite3.wasm`)**
  * Utilize SQLite compiled to WASM with the Origin-Private FileSystem (OPFS) for byte-level SQLite compatibility if full SQL querying is required.

### 3.3 Offline & Asynchronous Play Notifications
* **Turn Alerts:** When an opponent plays a move while the player has the tab backgrounded or closed, the app leverages the browser **HTML5 Notifications API** (`Notification.requestPermission()`).
* **PWA Capability:** `TakApp.Blazor` can be configured as a Progressive Web App (PWA) with a Service Worker, allowing players to "Install" the Tak game onto their desktop or phone home screen.

### 3.4 Board Rendering: High-Performance SVG / CSS Grid
* Rather than rendering to an opaque canvas, Blazor can render the Tak board using **clean vector SVG and CSS Flexbox/Grid**.
* **Advantages:**
  * Flawless crisp scaling on 4K monitors and mobile retina screens.
  * Smooth CSS transitions for piece sliding and stacking.
  * Accessible click and drag-and-drop piece movement on touchscreens.

---

## 4. GitHub Pages Deployment Pipeline

GitHub Pages serves static web assets directly from a repository. Because Blazor WASM produces compiled static output (`index.html`, `_framework/`, `.wasm`, `.css`), the deployment is handled completely through GitHub Actions.

### 4.1 Key GitHub Pages Requirements

1. **`.nojekyll` File:**  
   GitHub Pages runs the Jekyll static site generator by default. Jekyll ignores files and folders that start with an underscore (such as `_framework/` containing the Blazor binaries). Adding an empty `.nojekyll` file into the published `wwwroot` disables Jekyll processing.

2. **SPA Routing (`404.html` Trick):**  
   GitHub Pages only natively serves static files matching file paths. If a user refreshes their browser at `https://<user>.github.io/tak-p2p/play/match-123`, GitHub Pages returns a 404. By copying `index.html` to `404.html`, GitHub Pages will serve the Blazor application on direct links and refreshes, allowing client-side routing to handle the URL.

3. **Base Path (`<base href="...">`):**  
   For repository sites hosted at `https://<username>.github.io/<repository>/`, the base href must be set to `/<repository>/` (e.g., `<base href="/tak-p2p/" />`). This can be injected dynamically during the build step.

### 4.2 GitHub Actions Workflow Specification (`.github/workflows/deploy-gh-pages.yml`)

```yaml
name: Deploy Blazor WebAssembly to GitHub Pages

on:
  push:
    branches:
      - main
  workflow_dispatch:

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: "pages"
  cancel-in-progress: true

jobs:
  build-and-deploy:
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    runs-on: ubuntu-latest
    steps:
      - name: Checkout Code
        uses: actions/checkout@v4

      - name: Setup .NET 10 SDK
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore Dependencies
        run: dotnet restore src/TakApp.Blazor/TakApp.Blazor.csproj

      - name: Publish Blazor WASM
        run: |
          dotnet publish src/TakApp.Blazor/TakApp.Blazor.csproj \
            -c Release \
            -o release-output \
            --nologo

      - name: Rewrite Base Href & Prepare GitHub Pages Assets
        run: |
          # Ensure wwwroot is target
          cd release-output/wwwroot

          # Rewrite <base href="/" /> to the repository base URL
          sed -i 's|<base href="/" />|<base href="/${{ github.event.repository.name }}/" />|g' index.html

          # Copy index.html to 404.html for SPA client-side routing
          cp index.html 404.html

          # Add .nojekyll so GitHub Pages does not ignore _framework
          touch .nojekyll

      - name: Setup Pages
        uses: actions/configure-pages@v5

      - name: Upload Pages Artifact
        uses: actions/upload-pages-artifact@v3
        with:
          path: release-output/wwwroot

      - name: Deploy to GitHub Pages
        id: deployment
        uses: actions/deploy-pages@v4
```

---

## 5. Phased Implementation Roadmap

### Phase 1: Project Initialization & Build Setup
1. Create `src/TakApp.Blazor/TakApp.Blazor.csproj` targeting `net10.0` with SDK `Microsoft.NET.Sdk.BlazorWebAssembly`.
2. Reference `TakEngine.Abstractions`, `TakEngine.Core`, and `TakEngine.Transport`.
3. Add `TakApp.Blazor` to `TakGame.sln` and `TakGame.slnx`.
4. Configure `.nojekyll`, `404.html` handling, and the GitHub Actions workflow.

### Phase 2: Board Presentation & UI Components
1. Implement `BoardGrid.razor` supporting 4x4, 5x5, and 6x6 grid layouts with SVG piece rendering.
2. Implement visual distinction for Flat stones, Standing stones (Walls), and Capstones.
3. Build the piece stack inspector and drop-distribution controls for stack movement.
4. Integrate PTN move history panel with forward/backward move scrubber.

### Phase 3: Networking & Storage Integration
1. Wire `TakApp.Blazor` to `TakEngine.Transport` using public Nostr relays over browser WebSockets.
2. Create `WebStorageService` for persisting local Ed25519 keys, match state, and settings in IndexedDB.
3. Add invite code generation (NIP-19 npub / custom URI format) and shareable links (e.g., `https://domain/#/invite?code=...`).

### Phase 4: Verification & Live Deployment
1. Verify end-to-end P2P match between a browser client and the CLI / Avalonia desktop client over a public Nostr relay.
2. Verify deterministic state hash verification across platforms.
3. Push to `main` and verify automatic deployment on GitHub Pages.

---

## 6. Related Documentation & Deep Dives

* Master Architecture: [docs/PROJECT_SPECIFICATION.md](file:///e:/repos/tak-p2p/docs/PROJECT_SPECIFICATION.md)
* System Overview: [docs/system-overview.md](file:///e:/repos/tak-p2p/docs/system-overview.md)
* MVP Milestone Guide (v1.0): [docs/v1-mvp.md](file:///e:/repos/tak-p2p/docs/v1-mvp.md)
* Competitive & Tournaments: [docs/v2-tournaments.md](file:///e:/repos/tak-p2p/docs/v2-tournaments.md)
* Wire Protocol & Nostr: [docs/wire-protocol.md](file:///e:/repos/tak-p2p/docs/wire-protocol.md)
* SQLite Database Schema: [docs/database-schema.md](file:///e:/repos/tak-p2p/docs/database-schema.md)
* Spectator & Broadcast System: [docs/spectator-implementation-plan.md](file:///e:/repos/tak-p2p/docs/spectator-implementation-plan.md)
* Project Audit Report: [docs/AUDIT.md](file:///e:/repos/tak-p2p/docs/AUDIT.md)
* Chronological Commit History: [docs/DEVLOG.md](file:///e:/repos/tak-p2p/docs/DEVLOG.md)

# Tak P2P

Serverless Tak (the board game) in .NET 10. Two players on different devices play over public
Nostr relays, which act as an encrypted dumb pipe. Every move is verified on each device.
Engine: `src/TakEngine.*`. Frontends: Blazor WASM, Spectre.Console CLI, Avalonia (Desktop/Android/iOS).

## State and process
- Status: `pipeline/STATUS.md` (current stage, next step) and `pipeline/features.json` (what works, with evidence).
  These are the only status sources. Doc status tables are not evidence.
- How to work: `pipeline/playbook.md` (approach, quality bar, run/verify commands). Decisions: `pipeline/decisions.md`.
- Risks: `pipeline/risks.md`. Longer architecture docs are under `docs/` (index in `AGENTS.md` section 4).
- Throwaway spike code goes in `pipeline/spikes/`, never in `src/`.

## Checks (both must pass before every commit)
```
dotnet build TakGame.sln
dotnet test TakGame.sln
```
- `dotnet test` runs every test project and prints one summary per assembly. Any failure in any assembly is red.
- Don't write test counts, DEVLOG entries or milestone tables into docs or commit messages (D-013).
- The README device-matrix "Implementation Status" column must match `pipeline/features.json`.
- Blazor dev server 404s on `dotnet.<hash>.js`: delete `src/TakApp.Blazor/bin` and `obj`, then rebuild.

## Invariants
1. **No authoritative server.** Relays are store-and-forward only. Clients never trust remote state; they re-verify
   every move, turn owner, signature and hash chain locally. An invalid remote payload is dropped and the peer flagged.
2. **Layering.** `TakEngine.Abstractions` = contracts/records. `TakEngine.Core` = rules, hashing, storage, bot.
   `TakEngine.Transport` = Nostr, encryption, envelopes, matchmaking. Frontends are views only. No game rules in UI code.
3. **Colours.** Assign colours only via `ColorResolver.ResolveColors(seed, peerA, peerB)` from a shared seed.
   Never pick them randomly on one device.
4. **Move chaining.** `StateHash = SHA-256(PrevStateHash || TurnIndex || PlayerPubKey || PtnMove || TpsSnapshot)`.
   Changing any input or its encoding breaks every stored game and every peer.
5. **Keys and encryption (target, NOT YET IMPLEMENTED).** Per D-011, each player has one secp256k1 key. It is
   the npub identity, signs events (BIP-340), derives NIP-44 v2 keys by ECDH, and signs moves. Today
   `Nip44Encryption.DeriveSharedSecret` is `SHA256(priv || pub)`, not ECDH, and identities are Ed25519.
   See `pipeline/risks.md` R-002/R-003. Test crypto between two peers. Never decrypt with the sender's own keys.
6. **Transport JSON.** Serialize with `TransportEnvelope.SerializerOptions` so PTN `+ > <` are not escaped.
7. **Storage.** `SqliteGameStorage` on desktop/mobile, `BrowserStorage` in Blazor. Restore any turn from its TPS
   snapshot via `TpsSerializer`, without replaying moves.

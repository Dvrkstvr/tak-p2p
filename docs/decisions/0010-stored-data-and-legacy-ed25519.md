# 0010 · Stored data versions and legacy Ed25519 data
Status: assumed 2026-10-02 (architect, D-029; no-migration rule from D-011). The user can overrule.

## Context
Pre-M0 CLI games are local/AI games signed with throwaway Ed25519 keys in `tak.db` next to the binary (seen in code). The browser
keeps an Ed25519 key in localStorage (`tak_p2p_privkey`). Multiplayer never worked, so no remote game exists to lose (R-001).

## Decision
- SQLite: `PRAGMA user_version` is the version key. 0 = pre-M0 schema. M0 ships version 2 in one forward transaction: add
  `Moves.SigScheme` (default `legacy-ed25519`) and `Moves.EventId`, create `NetGames`, `Outbox`, `Flags`.
- Additive change = numbered step + bump + a test that migrates a committed fixture db. Shape change = decision record + a step that
  reads raw old rows + a test named for its trap. No down-migrations. A newer-than-known version is opened read-only with an error.
- JSON blobs (identity file `{"v":1,"nsec":...}`, browser keys) carry `v` under the same rule.
- Legacy rows keep their bytes, are marked `legacy-ed25519`, stay replayable from TPS, and are never signature-verified.
- Browser: the new identity lives under `tak.identity.v1`; old `tak_p2p_*` key entries are never read (an Ed25519 secret is also a
  valid secp256k1 scalar and would silently become a different npub) and are removed only by "reset identity".
- CLI data moves to `LocalApplicationData/tak-p2p/<profile>` (or `--data-dir`); the old binary-folder `tak.db` is left alone.

## Alternatives
Re-sign legacy games with new keys (no value: local games); delete legacy data (destroys local history for nothing).

## Consequences
Users see a new npub after M0/M1; nothing they could play remotely is lost.

## Revisit if
Evidence appears of external users with stored remote games.

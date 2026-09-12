# Wire Protocol & Nostr Transport Specification

Communication between peers occurs over free public Nostr relays (e.g., `wss://relay.damus.io`, `wss://nos.lol`, `wss://relay.primal.net`).

---

## 1. Move Envelope (`kind: 4` / NIP-44 Direct Encrypted Event)

Every game turn is transmitted as an encrypted event containing the current move, reference to the preceding state hash, timestamp, and cryptographic signature.

```json
{
  "game_id": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "turn": 15,
  "player_pubkey": "3bf0c63fcb93463407af97b5e0918838e64edd9d071293ad011663f1d7b6af94",
  "prev_state_hash": "a4f8c92b23a9d9b4009e879a8bc43428d223298c5d12ef4b476e3e577e3e9d89",
  "timestamp_utc": "2026-09-12T03:08:19Z",
  "action_type": "MOVE",
  "action_data": {
    "ptn": "3c3+12",
    "details": {
      "from": "c3",
      "direction": "+",
      "lift": 3,
      "drops": [1, 2]
    }
  },
  "signature": "3045022100e4b8108a38..."
}
```

### Field Definitions

| Field | Type | Description |
| --- | --- | --- |
| `game_id` | `UUID string` | Globally unique identifier of the game instance. |
| `turn` | `integer` | 1-based sequential turn index. |
| `player_pubkey` | `hex string` | Public key (Secp256k1 / Ed25519) of the player making the move. |
| `prev_state_hash` | `hex string` | SHA-256 hash of the game state preceding this turn. |
| `timestamp_utc` | `ISO 8601 string` | UTC timestamp of move execution. |
| `action_type` | `string` | Action performed (`MOVE`, `PLACE`, `RESIGN`). |
| `action_data` | `object` | PTN notation string and parsed move details (source square, direction, drop distribution). |
| `signature` | `hex string` | Cryptographic signature of payload signed by `player_pubkey`. |

---

## 2. Matchmaking Handshake (Quick Play)

1. **Search Broadcast (Ephemeral Event `kind: 20001`):**
   * Tags: `[["t", "tak_quickplay"], ["board_size", "5"], ["client_version", "1.0"]]`
   * Content: Ephemeral public key + supported relay list.
   * TTL: 60 seconds.

2. **Challenge / Accept:**
   * Peer B discovers broadcast, connects directly via encrypted payload proposing `game_id` and random seed for player colors.
   * Peer A signs acceptance; both peers withdraw broadcast.

---

## 3. Competitive & Tournament Event Kinds (v2.0)

| Event Kind | Type | Usage |
| --- | --- | --- |
| `kind: 4` | NIP-44 Encrypted | Turn move envelope & peer-to-peer challenge handshakes. |
| `kind: 20001` | Ephemeral | Quick Play matchmaking search broadcasts (TTL: 60s). |
| `kind: 31923` | Parameterized Replaceable | Admin tournament announcements & parameters. |
| `kind: 30000` | Replaceable | Admin / Oracle weekly signed Elo ratings & rank tiers. |
| `kind: 30001` | Replaceable | Admin append-only blacklist event for revoked certificates. |

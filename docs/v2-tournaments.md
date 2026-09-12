# Handoff Specification: Version 2.0 (Competitive & Tournaments)

---

## Deliverable Scope

1. **Admin Broadcast Framework:** Hardcoded admin public key to sign announcements and tournament events.
2. **Decentralized Swiss Tournaments:** Serverless check-ins, deterministic pairings via signature seeds.
3. **Admin Oracle Elo Rating Engine:** Verified match receipts, Sybil attack filters, and signed leaderboard publications.
4. **Economic Anti-Cheat (Verified Profiles):** Cryptographic attestation receipts issued upon payment to gate verified matchmaking pools.
5. **UI Extensions:** Tournament brackets and global leaderboards in CLI and Avalonia.

---

## 3.1 Consensus Primitive: Co-Signed `MatchReceipt`

Every completed game generates a single terminal receipt signed by **both** participants:

```json
{
  "type": "MATCH_RECEIPT",
  "game_id": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "tournament_id": "swiss_autumn_2026",
  "final_tps_hash": "4f9d2a...",
  "winner_pubkey": "3bf0c63fcb9346...",
  "is_draw": false,
  "turn_count": 34,
  "completed_at": "2026-09-12T05:14:20Z",
  "player1_signature": "sig1_hex...",
  "player2_signature": "sig2_hex..."
}
```

*Dispute fallback:* If a player goes dark without signing, the opponent submits a `ForfeitClaim` accompanied by the last signed turn and proof of 7-day NTP expiry.

---

## 3.2 Tournament Lifecycle & Deterministic Pairing

```
       [Admin Publishes Tournament] (Kind: 31923)
                    │
                    ▼
     [Players Publish Check-In Tickets] (Within 30m window)
                    │
                    ▼
     [Seed Calculation: SHA256(Sorted Player Signatures)]
                    │
                    ▼
 [Deterministic Swiss Pairing Function: PRNG(Seed + Round)]
                    │
                    ▼
[P2P Matches Executed -> Co-Signed Match Receipts Published]
                    │
                    ▼
       [Next Round Seeded via Updated Standings]
```

### Deterministic Pairing Algorithm

1. Every client downloads all check-in events referencing `TournamentId` at round start.
2. Filter invalid signatures; sort roster lexicographically by `PlayerPubKey`.
3. Compute:

$$\text{Seed} = \text{SHA-256}\left(\sum \text{Signatures}_{\text{sorted}}\right)$$

4. Initialize deterministic PRNG with $\text{Seed} \oplus \text{RoundNumber}$.
5. Run Swiss pairing: Match identical score brackets while prohibiting rematching. Because inputs and seeds are identical, **every client arrives at the exact same bracket with zero central server coordination**.

---

## 3.3 Elo Rating Oracle & Anti-Sybil Policy

To eliminate fake match collusion without running an authoritative game backend:

* **The Rating Oracle:** An open-source background worker run by the community/admin polls Nostr for valid `MatchReceipt` payloads.
* **Sybil Filtering Constraints:**
  1. Matches between Account A and Account B are capped at 2 rated matches per 7 rolling days.
  2. Accounts must possess either an **Admin-Signed Premium Attestation** or have completed at least one verified tournament match to participate in the global ladder.
* **Output:** Oracle signs and publishes a weekly Nostr event (`kind: 30000`) containing:
  * Public key $\rightarrow$ Elo rating dictionary.
  * Win / Loss / Draw stats.
  * Rank tiers.

---

## 3.4 Economic Anti-Cheat: Verified Profiles

```
[User Pays $5 via App Store / Stripe]
               │
               ▼
[Payment Webhook Triggers Serverless Worker]
               │
               ▼
[Worker Signs: "Pubkey 0xABC is Verified until [Date]" with AdminPrivateKey]
               │
               ▼
[User Stores Certificate & Attaches to Nostr Profile / Match Handshake]
               │
               ▼
[Opponent's Client Verifies Signature against Hardcoded AdminPublicKey]
```

* **Revocation:** Admin publishes an append-only `BlacklistEvent` (`kind: 30001`) containing burned public keys.
* **Client Guard:** If `Opponent.IsVerified == true` and `Opponent.PubKey NOT IN Blacklist`, client awards the "Verified Profile" badge and allows queuing in high-trust pools.

---

## 3.5 Database Schema Additions (v2 Migration)

```sql
CREATE TABLE IF NOT EXISTS MatchReceipts (
    GameId TEXT PRIMARY KEY NOT NULL,
    TournamentId TEXT NULL,
    FinalTpsHash TEXT NOT NULL,
    WinnerPubKey TEXT NULL,
    IsDraw INTEGER NOT NULL,
    TurnCount INTEGER NOT NULL,
    CompletedAt TEXT NOT NULL,
    Player1Signature TEXT NOT NULL,
    Player2Signature TEXT NOT NULL,
    FOREIGN KEY(GameId) REFERENCES Games(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Tournaments (
    Id TEXT PRIMARY KEY NOT NULL,
    AdminPubKey TEXT NOT NULL,
    Title TEXT NOT NULL,
    BoardSize INTEGER NOT NULL,
    CurrentRound INTEGER NOT NULL,
    StartsAt TEXT NOT NULL,
    IsCompleted INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS LeaderboardCache (
    PlayerPubKey TEXT PRIMARY KEY NOT NULL,
    EloRating INTEGER NOT NULL,
    MatchesPlayed INTEGER NOT NULL,
    TournamentWins INTEGER NOT NULL,
    LastUpdatedUtc TEXT NOT NULL,
    AdminSignature TEXT NOT NULL
);
```

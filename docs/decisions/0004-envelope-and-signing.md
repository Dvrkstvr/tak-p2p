# 0004 · Envelope, action signature and protocol version
Status: assumed 2026-10-02 (architect, D-025). The user can overrule.

## Context
scope.md fixes the behaviour (JOIN / ACCEPT / MOVE / RESIGN, the 7-row tamper classification, dedupe and equivocation) and
left the wire format to stage 6. The adopted session emitted only a signature string, and `TransportEnvelope` used STJ.

## Decision
- Event: kind 3825, tags exactly `["p", recipient]` and `["g", game id]`, content = NIP-44 v2 of a JSON plaintext.
- Plaintext fields: `pv` (protocol version, 1), `action_type`, `game_id`, `turn`, `player_pubkey`, `prev_state_hash`,
  `action_data` (`ptn` for MOVE; `white`/`black` for ACCEPT), `signature`. Field names keep the adopted envelope's names.
- `signature` = BIP-340 over `ActionDigest` = SHA-256 of a newline-joined canonical string with the domain prefix
  `tak/action/v1` (exact layout in pipeline/architecture.md), pinned by a golden-vector test.
- `pv` is judged first after decrypting; a mismatch is reported as "incompatible version", not as tampering.
- A republish sends the stored event verbatim (same id). Receivers treat "same turn + same ActionDigest" as a duplicate and
  "same turn + different digest" as equivocation (first seen wins).
- The NIP-01 id uses a hand-written serializer, never System.Text.Json (STJ mismatched on DEL and emoji even with relaxed escaping).

## Alternatives
- Rely on the event signature alone: a stored move would not be verifiable without its Nostr event, and the signature would
  cover ciphertext, not the game fields. Rejected.
- Version in a tag: visible to relays, and a peer must decrypt anyway; kept inside.
- A separate invite event kind: unneeded; the invite is out of band and JOIN/ACCEPT ride the same kind (spike recommendation).

## Consequences
- Changing any digest input or its encoding breaks every stored game and peer: it needs a new `pv` and a decision record.
- The hash chain formula (`StateHash = SHA-256(prev || turn || pubkey || ptn || tps)`) is unchanged; pubkeys are now x-only hex.

## Revisit if
A second client implementation appears (then publish the format as a NIP-style document).

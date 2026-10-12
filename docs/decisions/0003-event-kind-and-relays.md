# 0003 · Custom regular event kind 3825; default relays nos.lol + damus
Status: assumed 2026-10-02 (architect, D-025; inputs D-015). The user can overrule.

## Context
The spike (R-001/R-004) showed damus gates reads of kinds 4 and 1059 behind a broken NIP-42 AUTH, so DM kinds cannot be
the only path; a custom regular kind (tested as 9999) with NIP-44 content worked on nos.lol and damus. primal was
unreachable from the spike network.

## Decision
- Game actions use **kind 3825**. NIP-01 makes 1000 <= n < 10000 regular (stored by relays). 3825 is absent from the
  NIPs README kind table (fetched 2026-10-02, commit 6631b3e) and from nostr-protocol/registry-of-kinds `schema.yaml`
  (fetched 2026-10-02, commit 5cf2b84), and clear of the NIP-90 DVM ranges (5000-7000). Mnemonic: TAK = 825 on a keypad.
- Defaults: wss://nos.lol and wss://relay.damus.io; publish every action to both; "Sent" = at least one `OK true`.
  The relay list stays user-configurable; primal is re-added only when shown reachable.
- Quick Play (M4) uses ephemeral kind 20001 (spike), unchanged here.

## Alternatives
- Kind 4 / NIP-17 gift wrap (1059): unreadable on damus; gift wrap may return as an optional privacy channel (Later).
- Addressable 30078: worked but needs a unique `d` per move and adds nothing.
- Registering the kind in the NIPs repo: worth doing once v1 works; not needed for relays to accept it.

## Consequences
- Relay acceptance of 3825 is inferred from 9999 (accepted by both); F-014's live test confirms it.
- Metadata is public: who plays whom (`p`), which game (`g`), when (`created_at`). Content is encrypted.
- Damus rate-limits bursts (~5 events): the client publishes at most 4 events per 10 s and backs off on `rate-limited`.

## Revisit if
F-014 shows a relay refusing 3825; the retention re-check (24 h / 7 d) fails; another app starts using 3825.

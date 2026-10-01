# Decision records

One file per significant choice: context, decision, alternatives, consequences, revisit trigger.
Short log entries with ids live in `pipeline/decisions.md` (D-###); these records hold the long why.
Number new records sequentially; never rewrite an accepted record, supersede it with a new one.

| # | Decision | Log |
|---|---|---|
| 0001 | One secp256k1 key per player for everything | D-011 |
| 0002 | NBitcoin.Secp256k1 + hand-written NIP-44 v2 glue | D-004, D-015 |
| 0003 | Custom regular event kind 3825; nos.lol + damus | D-015, D-025 |
| 0004 | Envelope, action signature and protocol version | D-025 |
| 0005 | CLI-first M0; browser at M1/M2 | D-016, D-022 |
| 0006 | Fake relay in CI; live relays by hand | D-017, D-030 |
| 0007 | Mutation check with Stryker.NET | D-028 |
| 0008 | Check commands and warning policy | D-027 |
| 0009 | Module layout and pure modules | D-026 |
| 0010 | Stored data versions and legacy Ed25519 data | D-029 |

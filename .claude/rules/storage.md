---
paths:
  - "src/TakEngine.Storage.Local/**"
  - "tests/TakEngine.Storage.Tests/**"
  - "src/TakEngine.Core/Storage/**"
  - "src/TakApp.Blazor/Services/BrowserStorage.cs"
---
# Storage and key stores
- Version key: SQLite `PRAGMA user_version` (0 = pre-M0, M0 = 2); JSON blobs carry `"v"`. Rules in docs/decisions/0010:
  additive = numbered forward step + bump + test from a committed fixture db; shape change = decision record + raw-row step + a test
  named for its trap; never down-migrate; newer-than-known version opens read-only with an error.
- Migrations read the raw stored rows, never data already filtered by current code.
- Legacy Ed25519 rows (`SigScheme = 'legacy-ed25519'`) are kept and never signature-verified (D-011: no migration).
- Browser identity key name is `tak.identity.v1`. Never read `tak_p2p_privkey`/`tak_p2p_pubkey` as a secp256k1 key: an Ed25519
  secret is a valid scalar and would silently become a different npub.
- A key file with bad length or an out-of-range scalar is an error shown to the user; the file is never overwritten.
- The secret key never leaves the device and is never logged.

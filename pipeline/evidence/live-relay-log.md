# Live-relay event log (M0, D-032)

Every event published to a public relay by a LiveRelay test or a hand run. Budget: 150 events across M0.

| when (UTC) | test | event id | relay answers |
|---|---|---|---|
| 2026-10-12 00:22:51 | PublishOne_IsAcceptedByEachRelay_AndFetchableById | 97edccce892ec9bd1ff6493fd43fe3a004ea6dc1fb46d08731dda9089b236091 | nos.lol: Refused invalid: bad event id; relay.damus.io: Refused invalid: bad event id |
| 2026-10-12 00:22:54 | TwoRelaysTogether_DeliverEachEventOnce | 89b5695ed050f9a36acf367197e81a9e4cd3fc24dd7bef3ed223e94804a66438 | nos.lol: Sent; relay.damus.io: Sent |
| 2026-10-12 00:22:54 | TwoRelaysTogether_DeliverEachEventOnce | c11db601b0729953fbcbf7ed9f96a48f411f22352ac314332f67e91fc0c546b7 | nos.lol: Sent; relay.damus.io: Sent |
| 2026-10-12 00:22:55 | CorruptedSig_IsRefusedInvalid_NotSent | f5e4c8900c028e5d88ef925d94c8f86284b6681f54956e0c3bb6957310fae8c4 | nos.lol: Refused invalid: bad signature; relay.damus.io: Refused invalid: bad signature |
| 2026-10-12 00:24:54 | PublishOne_IsAcceptedByEachRelay_AndFetchableById | 78f2621e0398099a6bb7381fb61bfcab47262a44d37348e8bf3afe629b8619fb | nos.lol: Sent; relay.damus.io: Sent |

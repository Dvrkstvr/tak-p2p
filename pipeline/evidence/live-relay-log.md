# Live-relay event log (M0, D-032)

Every event published to a public relay by a LiveRelay test or a hand run. Budget: 150 events across M0.

| when (UTC) | test | event id | relay answers |
|---|---|---|---|
| 2026-10-12 00:22:51 | PublishOne_IsAcceptedByEachRelay_AndFetchableById | 97edccce892ec9bd1ff6493fd43fe3a004ea6dc1fb46d08731dda9089b236091 | nos.lol: Refused invalid: bad event id; relay.damus.io: Refused invalid: bad event id |
| 2026-10-12 00:22:54 | TwoRelaysTogether_DeliverEachEventOnce | 89b5695ed050f9a36acf367197e81a9e4cd3fc24dd7bef3ed223e94804a66438 | nos.lol: Sent; relay.damus.io: Sent |
| 2026-10-12 00:22:54 | TwoRelaysTogether_DeliverEachEventOnce | c11db601b0729953fbcbf7ed9f96a48f411f22352ac314332f67e91fc0c546b7 | nos.lol: Sent; relay.damus.io: Sent |
| 2026-10-12 00:22:55 | CorruptedSig_IsRefusedInvalid_NotSent | f5e4c8900c028e5d88ef925d94c8f86284b6681f54956e0c3bb6957310fae8c4 | nos.lol: Refused invalid: bad signature; relay.damus.io: Refused invalid: bad signature |
| 2026-10-12 00:24:54 | PublishOne_IsAcceptedByEachRelay_AndFetchableById | 78f2621e0398099a6bb7381fb61bfcab47262a44d37348e8bf3afe629b8619fb | nos.lol: Sent; relay.damus.io: Sent |
| 2026-10-12 00:26:16 | hand: CLI F-036 game 1, profile b, JOIN turn 0 | e83b8c1fd1abaf4717a76094b062598a25dccaef8bce1cbead29bd9611ede908 | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:26:17 | hand: CLI F-036 game 1, profile a, ACCEPT turn 0 | 3695a4f4b1a18e51ae7fcc3713538a8af01ac71ebacd6d4942ab3c97fc9f8472 | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:26:26 | hand: CLI F-036 game 1, profile b, MOVE turn 1 | 501f9af34167c219f3804f7d0a4fdd8f08126d8b9dfc2cf761004acf15c99ec5 | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:26:27 | hand: CLI F-036 game 1, profile a, MOVE turn 2 | 02e4430d92f1ba0d3baa587d85ee8e1ca77bf3e2ff651143ee229d5378e38ef9 | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:26:28 | hand: CLI F-036 game 1, profile b, MOVE turn 3 | 948710cc41eb2282f2044baa1b4d7ee6d7213695a1023e2feef7f499bcf37936 | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:26:29 | hand: CLI F-036 game 1, profile a, MOVE turn 4 | 2421bfcf021701eda4e6c6101c4f06eaed9411289f22eb18d4d1f5e292f55e1b | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:26:30 | hand: CLI F-036 game 1, profile b, MOVE turn 5 | 5530e619d8cf2b4f30545b8b2888f03a599eede533ba3f01d88cd335a4e522b1 | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:26:31 | hand: CLI F-036 game 1, profile a, MOVE turn 6 | c48de363002c3175e6e4ebd6901176ce8b7b898a18565dde48047b8712dbd46b | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:26:32 | hand: CLI F-036 game 1, profile b, MOVE turn 7 | 214fd3af310a3b4b621fac478c7610b853ceee8ec73a5f3fe6d9c4b0c14f8844 | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:26:33 | hand: CLI F-036 game 1, profile a, MOVE turn 8 | 8576fda8ac1598c41eb5964ff4cd5751c6ce55c8cec91c17c996bb8a7836d0bb | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:26:34 | hand: CLI F-036 game 1, profile b, MOVE turn 9 | 32926999106d3258c7bf067f42563cb6b5da41825edf7ff0047def09d1c040bb | nos.lol: stored; relay.damus.io: not stored (rate-limited/banned) |
| 2026-10-12 00:26:58 | hand: CLI F-037/F-038 game 2, profile b, JOIN turn 0 | c398962ef2081fdcec26395ea20db0a6fdafa80ee7cd5b30cf08f66223ebf2fc | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:26:59 | hand: CLI F-037/F-038 game 2, profile a, ACCEPT turn 0 | 86710b3bdfd57b1a4d658b75c718560de9a752946ad87ddb2759a2fcb4022e80 | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:26:59 | hand: CLI F-037/F-038 game 2, profile a, MOVE turn 1 | b5be4de2b63d9a83852ae7740ee560b1b9fc26c9a78ba397bbea0dde33505301 | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:27:00 | hand: CLI F-037/F-038 game 2, profile b, MOVE turn 2 | e2995d80741699e47a48cfee3471f38000ef42fe5db482bcdcbf00564aa340c0 | nos.lol: stored; relay.damus.io: not stored (rate-limited/banned) |
| 2026-10-12 00:27:01 | hand: CLI F-037/F-038 game 2, profile a, MOVE turn 3 | 27c8d687c8a2a2ffc0033f9bf66756c799094bc37fc2e087f66ef4114d94c07d | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:27:02 | hand: CLI F-037/F-038 game 2, profile b, MOVE turn 4 | 3f8318e97085a856f6d096389df5c5e6f16ac2ce7aac9b2c42ebc313d8efef28 | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:27:38 | hand: CLI F-037/F-038 game 2, profile a, MOVE turn 5 | 93047c9deefe7c8516a1d3c77b97bf865221816a24f46b8592eef6e5b187fe8b | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:27:56 | hand: CLI F-037/F-038 game 2, profile b, MOVE turn 6 | 39601486eeb5d9c6bcc3ab0a02cbdad736cbe11660870aa9eb7999eee9074d82 | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:27:59 | hand: CLI F-037/F-038 game 2, profile a via TakTestPeer, FORGED illegal a1 turn 7 | e034a86dbc6cd47c99294a96b141917d82b3cc4cdc84829e68830d313775c0ae | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:28:04 | hand: CLI F-037/F-038 game 2, profile a, MOVE turn 7 | b1808d175d8665340aa504a18328a77d6ac2eddac7288fee4a88520ccd3f2bc1 | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:28:05 | hand: CLI F-037/F-038 game 2, profile b, MOVE turn 8 | 0256df9a54905506d8264fce9c9e3864fc9cb674d024ebda9eebab650210df48 | nos.lol: stored; relay.damus.io: stored |
| 2026-10-12 00:28:06 | hand: CLI F-037/F-038 game 2, profile a, MOVE turn 9 | 05a41164e74c8557da2ed2350ee567a0259887c1130dfa5b843316078bade1bc | nos.lol: stored; relay.damus.io: stored |

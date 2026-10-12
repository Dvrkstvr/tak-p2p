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
| 2026-10-12 20:32:02 | hand: F-039 rehearsal (both CLIs on one PC, stopped at turn 4), guest JOIN turn 0 | b7ac73756561e45057bde87ed597f2ca81c459ba0db8529f676e02368260458a | at least one relay: stored (outbox Sent) |
| 2026-10-12 20:32:02 | hand: F-039 rehearsal, host ACCEPT turn 0 | 98dbbcd2d5385dcda2173242f353e7b29b36ba2ab5b8ecf0f2e02bc22644b2a1 | at least one relay: stored (outbox Sent) |
| 2026-10-12 20:32:15 | hand: F-039 rehearsal, host MOVE turn 1 | 3c30da04844552830dc87da2fcd9015592075b0a3c128372718de877ccbe991a | nos.lol: stored; relay.damus.io: pending |
| 2026-10-12 20:32:27 | hand: F-039 rehearsal, guest MOVE turn 2 | f387eed949dd1018667e3f860b66349cc94b06eeaa18e2b8910af73044b1e601 | at least one relay: stored (outbox Sent) |
| 2026-10-12 20:32:35 | hand: F-039 rehearsal, host MOVE turn 3 | a9f6b5253d241dac8840706a88450d0f84c064320f8a720979493ac39256d5c2 | nos.lol: stored; relay.damus.io: pending |
| 2026-10-12 20:32:48 | hand: F-039 rehearsal, guest MOVE turn 4 | 4547fa14c0796044b78466d5a873ffb2fd31e846a7724616cad7e9781536c014 | at least one relay: stored (outbox Sent) |
| 2026-10-12 20:44:53 | hand: D-046 browser smoke (browser host, CLI join, one PC), CLI JOIN turn 0 | af848957699ba7d7f04a6e8800a9fd886956b7baf452743794e1c5d1f0d60559 | at least one relay: stored (outbox Sent) |
| 2026-10-12 ~20:45 | hand: D-046 browser smoke, browser ACCEPT turn 0 | not captured (the page had no event-id line yet) | the CLI received it |
| 2026-10-12 ~20:45 | hand: D-046 browser smoke, browser MOVE turn 1 | be0e62016d4a2dc7645883fc7ce16c9a9aca9377f25ea5e7e3e135fbec8c1823 | nos.lol: stored; relay.damus.io: pending |
| 2026-10-12 20:45:32 | hand: D-046 browser smoke, CLI MOVE turn 2 | 7836c7add87a4e156bb977004bfc3606471153fe78b6b1ac6409eb283bace4a6 | nos.lol: stored; relay.damus.io: pending |
| 2026-10-12 ~20:45 | hand: D-046 browser smoke, browser MOVE turn 3 | 66d0b6169fb6979ac6031a2d5de2ec5c837be9d7e8a34da5f1d4c7e6fc9f569b | nos.lol: stored; relay.damus.io: pending |
| 2026-10-12 20:45:58 | hand: D-046 browser smoke, CLI MOVE turn 4 | 957832e14b07ff357ef6ee5eb75aefe417b70f08fbd57ca20370df75770ed3ac | nos.lol: stored; relay.damus.io: pending |
| 2026-10-12 ~20:46 | hand: D-046 browser smoke, browser MOVE turn 5 | 0c1e0e2257ebb5a87457cd89030b42d4f7d5a7a4e65287c656b2a13acff5af2e | nos.lol: stored; relay.damus.io: pending |
| 2026-10-12 20:46:15 | hand: D-046 browser smoke, CLI MOVE turn 6 | ed9c48923ede994eb0e6f7fef6ff77a2756e605faffe00ed2fbbfa741e4f2d6a | nos.lol: stored; relay.damus.io: pending |
| 2026-10-12 ~20:46 | hand: D-046 browser smoke, browser MOVE turn 7 | 358a55c826ecef4cac852d5dd76239f4dcfb81155b6fe29216c7fe352a79636c | nos.lol: stored; relay.damus.io: pending |
| 2026-10-12 20:46:40 | hand: D-046 browser smoke, CLI MOVE turn 8 | a768517decf735eb1839c4e45d9a9869735c40d567559f499a6d1fd7b89d2274 | nos.lol: stored; relay.damus.io: pending |
| 2026-10-12 ~20:47 | hand: D-046 browser smoke, browser MOVE turn 9 | 78bb3fd70ca40b0c3da9cb20a36a6bc491de35418f8da3920a7c7961d0ea4a52 | nos.lol: stored; relay.damus.io: pending |

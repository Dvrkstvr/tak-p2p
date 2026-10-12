# M0 review: security lens

Range: `git diff edb51ad..HEAD -- src tools .github` on `claude/m0-closeout` (HEAD b6aaf7f). The code lens is a separate review.
No code was changed and nothing was published to a relay. I checked findings marked "verified" with throwaway programs in a
scratch directory outside the repo.

Findings: blocking 0 · should 3 · nit 4.

## Findings

1. **should** · `src/TakApp.Cli/Online/PlainOutput.cs:70` (`OneLine`), `src/TakApp.Cli/Online/RichOutput.cs:49-52`, with the text
   coming from `src/TakEngine.Transport/Envelope/EnvelopeCodec.cs:179,210,212`, `src/TakEngine.Multiplayer/IncomingClassifier.cs:41` and
   `src/TakEngine.Core/Session/TakGameSession.cs:278,283`
   · **Attack/input:** the opponent sends a signed, correctly encrypted envelope with an unknown plaintext key such as
   `"\u001b]0;pwned\u0007\u001b[2K\u001b[1GFINAL white-road deadbeef\u001b[8m"`. A MOVE whose `ptn` holds the same bytes (with the
   right turn and prev hash) works too. Row 4 or row 6 flags it, and the flag reason echoes the opponent's raw string. `OneLine` only
   folds whitespace, and `Markup.Escape` only escapes `[` and `]`. ESC, BEL and the other C0/C1 control characters therefore reach
   the terminal in both `--plain` and rich mode.
   I verified this by decoding a crafted event: the `PlainOutput` line contains ESC and BEL, and in a pty `RichOutput` emits
   `^[]0;pwned^G^[[2K^[[1GFINAL white-road …` unchanged.
   · **Consequence:** a cheating opponent can rewrite what the victim's terminal shows. It can erase the "Rejected a move" line,
   print a fake `FINAL`, "Game over" or "opponent resigned", hide later output (`ESC[8m`) or set the window title. On terminals that
   allow OSC 52 it can also overwrite the clipboard. The game state is unchanged, but the user is told something false about it,
   which defeats the "Your game is unchanged" banner the tamper spec relies on. The reasons are also persisted in `Flags`.
   The Blazor `<pre>` is HTML-escaped, so it is safe on screen, but "Copy transcript" copies the raw characters.
   · **Suggested fix:** never echo peer strings in flag reasons. Replace them with a fixed text, or truncate to about 64 chars and
   replace every `char.IsControl` / non-printable character with `?` or `\uXXXX`, at the source (`EnvelopeCodec.Fail` details and
   `TakGameSession` violations). Also add a control-character filter to both `OneLine` helpers and `RichOutput.Line` as a backstop.
   Add a test with ESC in a JSON key and in a PTN.

2. **should** · `src/TakApp.Blazor/Services/BrowserStorage.cs:15,73` (`tak.identity.v1` in `localStorage`) with
   `.github/workflows/deploy-gh-pages.yml:53` (project page under `/<repo>/`)
   · **Attack/input:** the site is a GitHub project page (`https://dvrkstvr.github.io/tak-p2p/`, no CNAME). `localStorage` is
   per-origin, not per-path, so every other Pages site of the account shares the origin `https://dvrkstvr.github.io`. That includes
   the user site and any current or future project page, or one built by a compromised dependency or Action in another repo. Any of
   them can run `localStorage.getItem('tak.identity.v1')` and read the `nsec`. Any XSS on the origin can do the same.
   · **Consequence:** the player's secret key leaks, and with it the npub identity (D-011: the same key signs events and game
   actions, and decrypts NIP-44). `/online` (D-046) now plays real games with this key. R-009 asked this review to write the threat
   model down, and the risk is not written down yet.
   · **Suggested fix:** record the threat model under R-009 and name the shared-origin exposure. Before M2's persisted browser
   store, serve the app from its own origin (a custom domain or subdomain via CNAME, or a dedicated `<name>.github.io` user site).
   Until then, label the browser identity "device-local, low value" in the UI. WebCrypto non-extractable keys don't help here,
   because WebCrypto has no secp256k1 and same-origin script could use the key anyway.

3. **should** · `src/TakEngine.Multiplayer/RebuildPlan.cs:131` (and the same `FirstOrDefault` pattern at `:56` and `:100`)
   · **Attack/input:** the opponent equivocates once. It publishes move A for its turn t, the victim applies A live and plays t+1 on
   top of it, and then the opponent publishes a second validly signed move B for turn t (live this is only flagged, row 7). Relays
   return history newest first, so B comes first and `Replay` takes it. The victim's own move t+1 then doesn't chain, and the
   rebuild ends with `fork: move t+1 does not follow from move t`.
   I verified this with `RebuildPlan.Build`: history `[B, mine2, A, ACCEPT, JOIN]` fails, and `[A, B, …]` rebuilds fine.
   The host has the same problem with JOIN. A guest that publishes a second JOIN from the same key with a new nonce makes the
   host's rebuild fail ("The stored join and answer don't agree") whenever the new nonce flips the colours. I saw this in 2 of 4
   runs. A Blazor guest that reloads while Joining and presses Join again does the same thing without meaning to.
   · **Consequence:** one equivocation makes the victim's game unrecoverable after any state loss. In the browser that is any tab
   reload, because cold rebuild is the routine resume path (D-046), and nothing else ends the game (no forfeit). The opponent
   gets a griefing lever that costs it nothing.
   · **Suggested fix:** consider every candidate instead of the first. For turn t, keep the opponent move whose resulting hash our
   own move t+1 chains from, if we have one, and record the others as flags. For the host, use the JOIN from `guestKey` whose
   recomputed ACCEPT digest equals our stored ACCEPT. Pin both with tests that order the history newest first.

4. **nit** · `src/TakEngine.Multiplayer/GameCoordinator.cs:344-350`, `src/TakEngine.Multiplayer/Handshake/HostHandshake.cs:87`
   · **Attack/input:** once the host is locked, anyone who sees the game's `g` tag and host key on a relay can publish validly signed
   JOINs with no invite proof. D-042 keeps the host's filter open, so these JOINs arrive, get decoded a second time, and each one
   produces "ignored join from npub…: game already taken".
   · **Consequence:** an unbounded stream of notices in the CLI, the `--plain` transcript and the Blazor transcript. It also skips
   the D-043 rule that only invite holders count. The game is unaffected.
   · **Suggested fix:** surface "game already taken" only when `VerifyJoinProof` passes, and at most once per author. Count the
   other JOINs as dropped.

5. **nit** · `src/TakEngine.Transport/Matchmaking/InviteCode.cs:144-167`
   · **Attack/input:** a crafted invite with hundreds of `wss://` relays, or with `ws://localhost:<port>` entries. Any invite may
   carry loopback relays, not only one made by the local tools.
   · **Consequence:** the guest opens a socket per relay and fans every JOIN out to all of them. That is a self-inflicted
   resource drain and gives the invite author a list of the guest's IPs. The guest also sends REQ and EVENT frames to arbitrary
   local WebSocket services.
   · **Suggested fix:** cap the invite's relays (for example 8). Accept `ws://` loopback only behind an explicit dev flag or
   `--relays`, not from the invite.

6. **nit** · `src/TakApp.Blazor/Pages/OnlinePlay.razor:176` (and `:169`, `Home.razor` `NavigateTo($"online?invite=…")`)
   · **Attack/input:** the shareable link puts the invite, and with it the `seed_commit` that D-043 treats as the bearer secret, in
   the query string.
   · **Consequence:** opening the link sends the invite to the Pages server in the request line, where it lands in server or CDN
   logs. It also stays in browser history and sync. The scope's bearer-invite gap assumes that only the people it was sent to see
   the invite.
   · **Suggested fix:** use a fragment (`online#invite=…`), which the browser never sends, and read it from `Nav.Uri` the same way.

7. **nit** · `src/TakEngine.Multiplayer/Handshake/HostHandshake.cs:82,103` (D-038 claim)
   · **Attack/input:** a modified host computes the colours as soon as the JOIN arrives, before it answers. If it dislikes them,
   it never sends ACCEPT and issues a fresh invite.
   · **Consequence:** with each retry the host gets another coin flip on its colour (selective abort). This is inherent to
   commit-reveal without a timeout or penalty. D-038 says "leaves neither side able to pick the outcome", which overstates it
   slightly.
   · **Suggested fix:** no code change for v1. Reword D-038 to say that a party can abort but cannot steer the outcome.

## Checked and found sound
- **Decode order** (`EnvelopeCodec.Decode`, `RelayPool.Receive`): id and signature come first. Kind, the exact `p`+`g` tags, the
  game tag and the author follow before any ECDH or decrypt. `pv` is judged before key validation. The inner signature and
  `player_pubkey == event.pubkey` are checked before an action is used, and a non-JOIN from an unknown author is refused.
  NIP-44 bounds the payload size before base64. Frames are capped at 1 MiB.
- **Turn exchange:** the turn-parity owner check comes before buffering, and the buffer is capped at 64. Equivocation is
  first-seen-wins and flagged. RESIGN is bound to the game id and the opponent key. `created_at` only feeds `since`, capped at
  now + 1 min. Cross-game replay fails on the `g` tag, `game_id` and the digest.
- **Handshake:** a forged ACCEPT can't pass decode (row 2: it is not from the invite's host key) and, after that, `OnAccept`
  checks the author. A forged JOIN needs the seed commitment (D-043). The guest checks the host nonce against the commitment,
  plus size, komi and its own `ResolveColors`. The guest can't grind its nonce or key, because `host_nonce` stays hidden until
  ACCEPT. The host is bound by its commitment.
- **NIP-44:** the conversation key is HKDF-extract over the unhashed shared x (`GetSharedPubkey`). Nonces, BIP-340 aux and the
  colour nonces come from `RandomNumberGenerator.GetBytes(32)` in the CLI, Blazor and TestPeer shells. Padding and the 64 KiB
  cap follow the spec, and the MAC is compared with `FixedTimeEquals`.
- **Secret key:** `SecretKey.ToString` omits the secret. The identity file is created `CreateNew` with mode 0600 and moved
  without overwrite. No CLI, `--plain`, Blazor transcript, notice or exception path prints the nsec or hex secret.
  `LinkDeviceModal`'s reveal and copy is unchanged pre-M0 UI and is an explicit user export.
- **CI:** `ci.yml` runs on `push`, `pull_request` and `workflow_call` with `contents: read`. It has no secrets and no
  `pull_request_target`, and LiveRelay tests are excluded. The deploy job is gated on `test`.
- `TakRelay.Local` binds 127.0.0.1 only. SQLite access is parameterised.

F-042 row 2 (backgrounded tab catches up), local relays only, 2026-10-12.

Command: node tests/e2e/background.mjs --out pipeline/evidence/m1-f042-2026-10-12   (freeze 300 s, the default)

What it is: desktop headless Chromium (Playwright 1.56) on the Blazor /online page, against tools/TakRelay.Local
(ws://127.0.0.1:7447/r1, /r2) and the CLI joining on the same two relays. The page's wss://nos.lol and wss://relay.damus.io
sockets are answered by page.routeWebSocket and piped to r1 / r2; every frame is in run.txt. Nothing reached a public relay
(any other non-loopback socket would be closed and logged as "WS BLOCKED"; none was).

"Backgrounded" = CDP Page.setWebLifecycleState frozen + Debugger.pause for 300 s (the lifecycle call alone did not stop the
page's timers in headless Chromium; with the pause a 1 s interval ticked 2 times in 300 s), with both relay connections closed
0.5 s into the freeze and the close handed to the page at the thaw. This is a frozen-tab approximation in desktop Chromium, not
a real phone backgrounding a tab.

Result (run.txt): SUMMARY PASS. The CLI played turn 5 while the tab was frozen. At the thaw the tab opened two new sockets within
~1 s and sent REQ with since 1791847190 = newest created_at it had seen (1791847220) - 30 s; the freeze began at 1791847222. The
relays returned the CLI's turn-5 event (created_at 1791847232), the tab applied it with the CLI's hash, then one more move each;
all seven applied hashes match on both sides, no FLAG.

Files: run.txt (harness log), browser-transcript.txt / cli-transcript.txt (--plain lines), 01-03 screenshots,
relay-server.txt / blazor-server.txt (process output).

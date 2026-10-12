F-042 row 2, silent-drop variant (D-051), local relays only, 2026-10-12.

Command: node tests/e2e/background.mjs --silent --freeze-seconds 90 --url http://127.0.0.1:5291 --relay-port 7491
(a private port pair so no other run's server answers; the Blazor dev server was started by hand on 5291 and stopped by PID)

What it is: the same harness as ../m1-f042-2026-10-12, but at the freeze the page's two relay sockets die silently: the pipe
closes the relay side and never tells the page (no close frame, ever), and anything the page sends on them is logged as
"(lost: silent socket)". Before D-051 the page waited for a close that never came.

Result (run.txt): SUMMARY PASS. At the thaw (90 s after the freeze, so the 60 s idle window had passed) the page sent
REQ tak-probe on both dead sockets at once, heard nothing, and 10 s later opened two new sockets with since = newest seen
created_at - 30 s. The CLI's turn 6, played while the tab was frozen, was applied in the tab 11 s after the thaw with the CLI's
hash; two more moves; all applied hashes match on both sides, no FLAG. Playwright did not report the page closing the dead
sockets (the browser's ClientWebSocket aborts them on cancel); the new sockets and the catch-up are the check.

Not exercised: the visibilitychange hook (CDP lifecycle freeze in headless Chromium does not change document.visibilityState), so
the probe here came from the idle window. The pool-level CheckNow path is covered by RelayPoolTests and GameCoordinatorTests.

Note (pre-existing, as in ../m1-f042-2026-10-12/run.txt): the CLI also dials wss://nos.lol and wss://relay.damus.io because the
invite names them; every attempt failed to connect ("backoff") and its publish status for them stayed pending. No frame reached a
public relay.

Files: run.txt (harness log), browser-transcript.txt / cli-transcript.txt (--plain lines).

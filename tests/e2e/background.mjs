#!/usr/bin/env node
// F-042 row 2: the Blazor /online tab is backgrounded (frozen) for 5 minutes while its relay sockets are dropped and the CLI
// plays; when it is foregrounded it must reconnect, send a REQ with since = newest seen created_at - 30 s, apply the missed
// move, and agree with the CLI on every state hash. Local relays only: nothing reaches a public relay.
//
// One command:   node tests/e2e/background.mjs
// Options:       --url <base>          Blazor site (default http://127.0.0.1:5199; started here if nothing answers there)
//                --relay-port <n>      TakRelay.Local port (default 7447; started here if nothing answers there)
//                --freeze-seconds <n>  how long the tab stays frozen (default 300)
//                --out <dir>           evidence folder (default pipeline/evidence/m1-f042-<yyyy-mm-dd>)
//                --headed              show the browser
// Exit code 0 = every check passed; 1 = a check failed (run.txt names it); 2 = the harness could not start.
//
// How the page reaches the local relay without any product change: the page always dials wss://nos.lol and wss://relay.damus.io
// (OnlineGameService.DefaultRelays). page.routeWebSocket answers those two in Node and pipes each one to
// ws://127.0.0.1:<port>/r1 and /r2, logging every frame. Any other non-loopback WebSocket is closed unanswered. The CLI dials
// r1/r2 directly, so both sides share the same two relays.
//
// "Backgrounded" here is CDP Page.setWebLifecycleState frozen plus Debugger.pause in desktop headless Chromium (the page's JS,
// timers and socket callbacks stop; the lifecycle call alone does not stop them in headless Chromium), plus the pipe closing both
// relay connections right after the freeze (the page is told of the close at the thaw, see `frozen` below). It approximates a phone backgrounding a tab; it is not one.

import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const PLAYWRIGHT_PATHS = [process.env.PLAYWRIGHT_MODULE, '/opt/node-tools/node_modules/playwright', 'playwright'].filter(Boolean);
let chromium;
for (const p of PLAYWRIGHT_PATHS) {
  try { ({ chromium } = require(p)); break; } catch { /* try the next one */ }
}
if (!chromium) {
  console.error(`playwright not found (tried ${PLAYWRIGHT_PATHS.join(', ')}); set PLAYWRIGHT_MODULE`);
  process.exit(2);
}

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const args = process.argv.slice(2);
const arg = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
};
const DEFAULT_URL = 'http://127.0.0.1:5199';
const baseUrl = arg('--url', DEFAULT_URL).replace(/\/$/, '');
const relayPort = Number(arg('--relay-port', '7447'));
const freezeSeconds = Number(arg('--freeze-seconds', '300'));
const today = new Date().toISOString().slice(0, 10);
const outDir = path.resolve(arg('--out', path.join(repoRoot, 'pipeline', 'evidence', `m1-f042-${today}`)));
const headed = args.includes('--headed');
const relayUrls = [`ws://127.0.0.1:${relayPort}/r1`, `ws://127.0.0.1:${relayPort}/r2`];
// The page's hard-wired relays, each piped to one local relay.
const PIPE = { 'nos.lol': relayUrls[0], 'relay.damus.io': relayUrls[1] };

fs.mkdirSync(outDir, { recursive: true });
// .txt, not .log: the repo ignores *.log and the log is the evidence.
const logPath = path.join(outDir, 'run.txt');
fs.writeFileSync(logPath, '');
const log = (line) => {
  const stamped = `${new Date().toISOString()} ${line}`;
  console.log(stamped);
  fs.appendFileSync(logPath, stamped + '\n');
};
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const nowSec = () => Math.floor(Date.now() / 1000);

let failures = 0;
let checks = 0;
const check = (ok, what) => {
  checks++;
  if (ok) log(`PASS ${what}`);
  else { failures++; log(`FAIL ${what}`); }
  return ok;
};

// ---------- processes ----------
const children = [];
async function reachable(url) {
  try {
    const res = await fetch(url, { signal: AbortSignal.timeout(2000) });
    return res.ok;
  } catch {
    return false;
  }
}

async function startIfNeeded(name, probe, cmdArgs, logFile) {
  if (await reachable(probe)) {
    log(`${name} using the one already running (${probe})`);
    return;
  }
  log(`${name} starting dotnet ${cmdArgs.join(' ')}`);
  const out = fs.openSync(path.join(outDir, logFile), 'w');
  const child = spawn('dotnet', cmdArgs, { cwd: repoRoot, detached: true, stdio: ['ignore', out, out] });
  children.push({ name, child });
  for (let i = 0; i < 180; i++) {
    if (await reachable(probe)) {
      log(`${name} up (pid ${child.pid})`);
      return;
    }
    if (child.exitCode !== null) break;
    await sleep(1000);
  }
  log(`${name} did not come up; see ${logFile}`);
  stopChildren();
  process.exit(2);
}

function stopChildren() {
  for (const { name, child } of children.reverse()) {
    try { process.kill(-child.pid, 'SIGTERM'); log(`${name} stopped (pid ${child.pid})`); } catch { /* already gone */ }
  }
  children.length = 0;
}

// ---------- the CLI peer ----------
class Cli {
  constructor(invite) {
    this.lines = [];
    this.dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'tak-e2e-bg-'));
    const cliArgs = ['run', '--project', 'src/TakApp.Cli', '--no-build', '--', 'join', invite, '--data-dir', this.dataDir,
      '--relays', relayUrls.join(','), '--plain'];
    log(`CLI starting dotnet ${cliArgs.map((a) => (a === invite ? '<invite>' : a)).join(' ')}`);
    this.proc = spawn('dotnet', cliArgs, { cwd: repoRoot, detached: true, stdio: ['pipe', 'pipe', 'pipe'] });
    children.push({ name: 'CLI', child: this.proc });
    let buf = '';
    const onData = (chunk) => {
      buf += chunk.toString();
      let i;
      while ((i = buf.indexOf('\n')) >= 0) {
        const line = buf.slice(0, i).trimEnd();
        buf = buf.slice(i + 1);
        if (!line) continue;
        this.lines.push(line);
        log(`CLI> ${line}`);
      }
    };
    this.proc.stdout.on('data', onData);
    this.proc.stderr.on('data', (c) => log(`CLI! ${c.toString().trimEnd()}`));
  }

  send(text) {
    log(`CLI< ${text}`);
    this.proc.stdin.write(text + '\n');
  }

  async waitFor(pred, what, timeoutMs = 60000) {
    const end = Date.now() + timeoutMs;
    while (Date.now() < end) {
      const hit = this.lines.find(pred);
      if (hit) return hit;
      await sleep(100);
    }
    throw new Error(`CLI: timed out waiting for ${what}`);
  }

  applied() {
    return appliedMap(this.lines);
  }
}

function appliedMap(lines) {
  const m = new Map();
  for (const l of lines) {
    const x = l.match(/^APPLIED turn (\d+) (\S+) ([0-9a-f]{64})/);
    if (x) m.set(Number(x[1]), { ptn: x[2], hash: x[3] });
  }
  return m;
}

// ---------- WebSocket pipe ----------
const pipes = []; // { n, url, target, route, up, openedAt, closed }
let pipeCount = 0;
let maxCreatedAtSeen = 0;
const reqs = []; // { n, at, since, raw }
// Playwright's routed WebSocket loses frames and closes sent to the page while its main thread is paused (seen 2026-10-12), so
// while the tab is frozen a close for the page is held and handed over at the thaw, as a browser hands a frozen tab its queued
// socket events when it wakes.
let frozen = false;
const heldCloses = [];
function closePageSide(pipe, reason) {
  if (frozen) {
    heldCloses.push({ pipe, reason });
    return;
  }
  try { pipe.route.close({ code: 1001, reason }); } catch { /* gone */ }
}

function describeFrame(dir, n, text) {
  let msg;
  try { msg = JSON.parse(text); } catch { return `WS#${n} ${dir} (unparsed) ${text.slice(0, 120)}`; }
  const type = msg[0];
  if (type === 'REQ') return `WS#${n} ${dir} REQ ${text}`;
  if (type === 'EVENT') {
    const ev = dir === 'page->relay' ? msg[1] : msg[2];
    if (ev?.created_at) maxCreatedAtSeen = Math.max(maxCreatedAtSeen, ev.created_at);
    return `WS#${n} ${dir} EVENT kind ${ev?.kind} id ${ev?.id?.slice(0, 16)} created_at ${ev?.created_at}`;
  }
  return `WS#${n} ${dir} ${text.slice(0, 160)}`;
}

async function installPipe(page) {
  await page.routeWebSocket(/.*/, (route) => {
    const url = new URL(route.url());
    const host = url.hostname;
    if (host === '127.0.0.1' || host === 'localhost') {
      route.connectToServer(); // the dev server's own sockets (browser refresh), if any
      return;
    }
    const target = PIPE[host];
    if (!target) {
      log(`WS BLOCKED ${route.url()} (not loopback, not a piped relay)`);
      route.close({ code: 1008, reason: 'blocked by e2e harness' });
      return;
    }
    const n = ++pipeCount;
    const up = new WebSocket(target);
    const pending = [];
    const pipe = { n, url: route.url(), target, route, up, openedAt: nowSec(), closed: false };
    pipes.push(pipe);
    log(`WS#${n} OPEN ${route.url()} piped to ${target}`);
    up.addEventListener('open', () => { for (const m of pending.splice(0)) up.send(m); });
    up.addEventListener('message', (e) => {
      const text = typeof e.data === 'string' ? e.data : Buffer.from(e.data).toString();
      log(describeFrame('relay->page', n, text));
      if (!pipe.closed) route.send(text);
    });
    up.addEventListener('close', () => {
      if (!pipe.closed) {
        pipe.closed = true;
        log(`WS#${n} relay side closed`);
        closePageSide(pipe, 'relay closed');
      }
    });
    up.addEventListener('error', () => log(`WS#${n} relay side error`));
    route.onMessage((m) => {
      const text = typeof m === 'string' ? m : Buffer.from(m).toString();
      log(describeFrame('page->relay', n, text));
      try {
        const msg = JSON.parse(text);
        if (msg[0] === 'REQ') reqs.push({ n, at: nowSec(), since: msg.slice(2).map((f) => f.since ?? null), raw: text });
      } catch { /* logged above */ }
      if (up.readyState === WebSocket.OPEN) up.send(text);
      else pending.push(text);
    });
    route.onClose((code, reason) => {
      if (!pipe.closed) {
        pipe.closed = true;
        log(`WS#${n} page side closed (${code} ${reason ?? ''})`);
        try { up.close(); } catch { /* gone */ }
      }
    });
  });
}

/** Breaks every relay socket: the TCP connection to the local relay is closed now; the page hears of it at the thaw. */
function dropAllPipes(why) {
  for (const p of pipes.filter((x) => !x.closed)) {
    p.closed = true;
    log(`WS#${p.n} DROP (${why})`);
    try { p.up.close(); } catch { /* gone */ }
    closePageSide(p, why);
  }
}

function thawPipes() {
  frozen = false;
  for (const { pipe, reason } of heldCloses.splice(0)) {
    log(`WS#${pipe.n} close handed to the page at the thaw (${reason})`);
    closePageSide(pipe, reason);
  }
}

// ---------- page helpers ----------
async function transcript(page) {
  return ((await page.textContent('pre.online-transcript').catch(() => '')) ?? '').split('\n').filter(Boolean);
}
async function waitingText(page) {
  return ((await page.textContent('.online-waiting').catch(() => '')) ?? '').trim();
}
async function waitPage(page, fn, arg, what, timeoutMs = 60000) {
  try {
    await page.waitForFunction(fn, arg, { timeout: timeoutMs, polling: 200 });
  } catch {
    throw new Error(`page: timed out waiting for ${what}`);
  }
}
async function pageMove(page, ptn) {
  log(`PAGE< ${ptn}`);
  await page.fill('.online-ptn-input', ptn);
  await page.click('button:has-text("Send")');
}

let shot = 0;
async function screenshot(page, name) {
  shot++;
  const file = `${String(shot).padStart(2, '0')}-${name}.png`;
  await page.screenshot({ path: path.join(outDir, file), fullPage: true });
  log(`SHOT ${file}`);
}

// Placements only, no road possible within this many plies on 5x5.
const MOVES = ['a1', 'e5', 'b2', 'd4', 'c3', 'b4', 'a3', 'e1', 'd2', 'c5', 'a5', 'e3'];

/** Plays turn `turn` on whichever side owns it, and waits until both sides applied it with the same hash. */
async function playTurn(page, cli, turn, browserIsWhite) {
  const ptn = MOVES[turn - 1];
  const browserOwns = (turn % 2 === 1) === browserIsWhite;
  if (browserOwns) {
    await waitPage(page, (t) => document.querySelector('.online-waiting')?.textContent?.includes(`your move turn ${t}`), turn, `browser's turn ${turn}`);
    await pageMove(page, ptn);
  } else {
    await cli.waitFor((l) => l === `WAITING your move turn ${turn}`, `CLI's turn ${turn}`);
    cli.send(ptn);
  }
  await cli.waitFor((l) => l.startsWith(`APPLIED turn ${turn} `), `CLI APPLIED turn ${turn}`);
  await waitPage(page, (t) => (document.querySelector('pre.online-transcript')?.textContent ?? '').includes(`APPLIED turn ${t} `), turn, `browser APPLIED turn ${turn}`);
  const b = appliedMap(await transcript(page)).get(turn);
  const c = cli.applied().get(turn);
  check(b && c && b.hash === c.hash && b.ptn === c.ptn, `turn ${turn} ${ptn} (${browserOwns ? 'browser' : 'CLI'}) applied on both sides with the same hash (browser ${b?.hash?.slice(0, 16)} CLI ${c?.hash?.slice(0, 16)})`);
  return browserOwns;
}

// ---------- main ----------
log(`F-042 row 2 background run: freeze ${freezeSeconds} s, relays ${relayUrls.join(' ')}, site ${baseUrl}`);
await startIfNeeded('RELAY', `http://127.0.0.1:${relayPort}/r1`, ['run', '--project', 'tools/TakRelay.Local', '--', '--port', String(relayPort)], 'relay-server.txt');
await startIfNeeded('SERVER', baseUrl + '/', ['run', '--project', 'src/TakApp.Blazor/TakApp.Blazor.csproj', '--urls', baseUrl], 'blazor-server.txt');

let browser;
let cli;
let page;
try {
  browser = await chromium.launch({ headless: !headed });
  const context = await browser.newContext({ viewport: { width: 1100, height: 1400 } });
  page = await context.newPage();
  page.on('pageerror', (e) => log(`PAGEERROR ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') log(`CONSOLE ${m.text()}`); });
  await installPipe(page);

  // Host in the browser.
  await page.goto(baseUrl + '/online');
  await page.waitForSelector('button:has-text("Host a game")', { timeout: 90000 });
  await page.click('button:has-text("Host a game")');
  await waitPage(page, () => (document.querySelector('pre.online-transcript')?.textContent ?? '').includes('INVITE '), null, 'INVITE line');
  const invite = (await transcript(page)).find((l) => l.startsWith('INVITE ')).slice('INVITE '.length).trim();
  log(`INVITE ${invite.slice(0, 24)}...`);
  await waitPage(page, () => (document.querySelector('pre.online-transcript')?.textContent ?? '').includes('caught-up'), null, 'a relay caught up');

  // Join with the CLI.
  cli = new Cli(invite);
  await cli.waitFor((l) => /^WAITING (your|opponent) move turn 1$/.test(l), 'CLI in game', 90000);
  await waitPage(page, () => /move turn 1/.test(document.querySelector('.online-waiting')?.textContent ?? ''), null, 'browser in game');
  const browserIsWhite = (await waitingText(page)).includes('your move turn 1');
  log(`COLOURS browser is ${browserIsWhite ? 'white' : 'black'} (host), CLI is ${browserIsWhite ? 'black' : 'white'}`);

  // A few turns before the freeze; stop when the next turn is the CLI's.
  let turn = 1;
  while (turn <= 4 || ((turn % 2 === 1) === browserIsWhite)) {
    await playTurn(page, cli, turn, browserIsWhite);
    turn++;
  }
  const frozenTurn = turn; // the CLI plays this one while the tab is frozen
  await page.evaluate(() => { window.__ticks = 0; setInterval(() => { window.__ticks++; }, 1000); });
  await sleep(1500);
  const ticksBefore = await page.evaluate(() => window.__ticks);
  await screenshot(page, 'before-freeze');
  const maxSeenBeforeFreeze = maxCreatedAtSeen;
  const pipesBeforeFreeze = pipeCount;
  const reqsBeforeFreeze = reqs.length;

  // Background: freeze the page, then break its relay sockets.
  // Page.setWebLifecycleState frozen is sent as a phone/desktop browser would freeze a background tab, but in Playwright's
  // headless Chromium it does not stop the page's timers (seen 2026-10-12), so the main thread is also paused with the
  // debugger: no JS, no timer, no socket callback runs until the thaw. Blazor WASM runs on that thread.
  const cdp = await context.newCDPSession(page);
  await cdp.send('Page.setWebLifecycleState', { state: 'frozen' });
  await cdp.send('Debugger.enable');
  await cdp.send('Debugger.pause');
  frozen = true;
  const freezeAt = nowSec();
  log(`FREEZE at ${freezeAt} (newest created_at the pipe saw: ${maxSeenBeforeFreeze}); the tab stays frozen ${freezeSeconds} s`);
  await sleep(500);
  dropAllPipes('tab backgrounded: sockets dropped');

  // The CLI plays while the tab is frozen.
  await sleep(10000);
  await cli.waitFor((l) => l === `WAITING your move turn ${frozenTurn}`, `CLI's turn ${frozenTurn}`);
  cli.send(MOVES[frozenTurn - 1]);
  const sent = await cli.waitFor((l) => l.startsWith(`SENT turn ${frozenTurn} `), `CLI SENT turn ${frozenTurn}`);
  check(/:ok/.test(sent), `the CLI's turn ${frozenTurn} reached a local relay while the tab was frozen (${sent})`);
  const cliMoveAt = nowSec();
  const remaining = freezeAt + freezeSeconds - nowSec();
  log(`WAIT ${remaining} s more with the tab frozen`);
  await sleep(Math.max(0, remaining) * 1000);
  check(pipeCount === pipesBeforeFreeze, `no new relay socket was opened while frozen (sockets opened: ${pipeCount - pipesBeforeFreeze})`);
  check(reqs.length === reqsBeforeFreeze, 'no REQ was sent while frozen');

  // Foreground.
  await cdp.send('Debugger.resume');
  await cdp.send('Debugger.disable');
  await cdp.send('Page.setWebLifecycleState', { state: 'active' });
  thawPipes();
  const thawAt = nowSec();
  log(`THAW at ${thawAt} (${thawAt - freezeAt} s after the freeze)`);
  await waitPage(page, (t) => (document.querySelector('pre.online-transcript')?.textContent ?? '').includes(`APPLIED turn ${t} `), frozenTurn,
    `browser catches up turn ${frozenTurn}`, 90000);
  const caughtUpAt = nowSec();
  const ticksAfter = await page.evaluate(() => window.__ticks);
  check(ticksAfter - ticksBefore <= 5, `the page's timers were stopped while frozen (a 1 s interval ticked ${ticksAfter - ticksBefore} times over ${thawAt - freezeAt} s)`);

  const newReqs = reqs.slice(reqsBeforeFreeze);
  check(pipeCount > pipesBeforeFreeze, `the tab reopened relay sockets after the thaw (${pipeCount - pipesBeforeFreeze} new)`);
  check(newReqs.length > 0, `the tab sent ${newReqs.length} REQ after the thaw`);
  for (const r of newReqs) {
    const sinces = r.since.filter((s) => s !== null);
    log(`REQ after thaw on WS#${r.n} at ${r.at}: since ${JSON.stringify(r.since)} (freeze ${freezeAt}, newest seen ${maxSeenBeforeFreeze}, CLI move ~${cliMoveAt})`);
    check(sinces.length === r.since.length && sinces.every((s) => s === maxSeenBeforeFreeze - 30),
      `WS#${r.n} REQ since = newest seen created_at - 30 s = ${maxSeenBeforeFreeze - 30} (got ${JSON.stringify(r.since)})`);
  }
  const b = appliedMap(await transcript(page)).get(frozenTurn);
  const c = cli.applied().get(frozenTurn);
  check(b && c && b.hash === c.hash, `turn ${frozenTurn} played while frozen is applied in the tab ${caughtUpAt - thawAt} s after the thaw with the CLI's hash (${b?.hash?.slice(0, 16)})`);
  await screenshot(page, 'after-thaw-caught-up');

  // One more move each.
  for (let i = 0; i < 2; i++) {
    turn = frozenTurn + 1 + i;
    await playTurn(page, cli, turn, browserIsWhite);
  }
  const bAll = appliedMap(await transcript(page));
  const cAll = cli.applied();
  const same = [...cAll.keys()].length === turn && [...cAll].every(([t, v]) => bAll.get(t)?.hash === v.hash);
  check(same, `both sides hold the same ${turn} applied turns and hashes; last hash ${cAll.get(turn)?.hash}`);
  const flags = (await transcript(page)).filter((l) => l.startsWith('FLAG')).concat(cli.lines.filter((l) => l.startsWith('FLAG')));
  check(flags.length === 0, `no FLAG on either side (${flags.length})`);
  await screenshot(page, 'after-two-more-moves');
  fs.writeFileSync(path.join(outDir, 'browser-transcript.txt'), (await transcript(page)).join('\n') + '\n');
  fs.writeFileSync(path.join(outDir, 'cli-transcript.txt'), cli.lines.join('\n') + '\n');
  cli.send('quit');
  await sleep(2000);
} catch (e) {
  check(false, `run threw: ${e.message.split('\n')[0]}`);
  if (page) {
    try {
      log(`DIAG page ticks ${await page.evaluate(() => window.__ticks)} waiting "${await waitingText(page)}"`);
      for (const l of await transcript(page)) log(`DIAG browser> ${l}`);
      await screenshot(page, 'error');
    } catch (d) { log(`DIAG page unreadable: ${d.message.split('\n')[0]}`); }
  }
} finally {
  if (browser) await browser.close();
  stopChildren();
  if (cli) fs.rmSync(cli.dataDir, { recursive: true, force: true });
}
log(`SUMMARY ${failures === 0 ? 'PASS' : 'FAIL'} checks=${checks} failed=${failures} evidence=${path.relative(repoRoot, outDir)}`);
process.exit(failures === 0 ? 0 : 1);

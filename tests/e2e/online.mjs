#!/usr/bin/env node
// Browser vs browser online game, played only by clicking the board (one page on the 3D board, one on the 2D board), through the
// real OnlineGameService and MultiplayerGame over two local relays. It hosts, joins from the invite link, plays a placement and
// a stack move each way, finishes on a road, and checks both pages applied every turn with the same state hash and agree on FINAL.
// Local relays only: nothing reaches a public relay.
//
// One command:   node tests/e2e/online.mjs
// Options:       --url <base>          Blazor site (default http://127.0.0.1:5199; started here if nothing answers there)
//                --relay-port <n>      TakRelay.Local port (default 7448; started here if nothing answers there)
//                --out <dir>           evidence folder (default pipeline/evidence/redesign-online-<yyyy-mm-dd>)
//                --headed              show the browsers
// Exit code 0 = every check passed; 1 = a check failed (run.txt names it); 2 = the harness could not start.
//
// The pages always dial wss://nos.lol and wss://relay.damus.io (OnlineGameService.DefaultRelays); page.routeWebSocket answers
// those in Node and pipes them to ws://127.0.0.1:<port>/r1 and /r2. Any other non-loopback WebSocket is closed unanswered.

import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
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
const relayPort = Number(arg('--relay-port', '7448'));
const today = new Date().toISOString().slice(0, 10);
const outDir = path.resolve(arg('--out', path.join(repoRoot, 'pipeline', 'evidence', `redesign-online-${today}`)));
const headed = args.includes('--headed');
const relayUrls = [`ws://127.0.0.1:${relayPort}/r1`, `ws://127.0.0.1:${relayPort}/r2`];
const PIPE = { 'nos.lol': relayUrls[0], 'relay.damus.io': relayUrls[1] };

fs.mkdirSync(outDir, { recursive: true });
const logPath = path.join(outDir, 'run.txt');
fs.writeFileSync(logPath, '');
const log = (line) => {
  const stamped = `${new Date().toISOString()} ${line}`;
  console.log(stamped);
  fs.appendFileSync(logPath, stamped + '\n');
};
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

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

// ---------- relay pipe ----------
let pipeCount = 0;
async function installPipe(page, who) {
  await page.routeWebSocket(/.*/, (route) => {
    const host = new URL(route.url()).hostname;
    if (host === '127.0.0.1' || host === 'localhost') {
      route.connectToServer();
      return;
    }
    const target = PIPE[host];
    if (!target) {
      log(`WS BLOCKED ${who} ${route.url()}`);
      route.close({ code: 1008, reason: 'blocked by e2e harness' });
      return;
    }
    const n = ++pipeCount;
    const up = new WebSocket(target);
    const pending = [];
    let closed = false;
    log(`WS#${n} ${who} OPEN ${route.url()} piped to ${target}`);
    up.addEventListener('open', () => { for (const m of pending.splice(0)) up.send(m); });
    up.addEventListener('message', (e) => { if (!closed) route.send(typeof e.data === 'string' ? e.data : Buffer.from(e.data).toString()); });
    up.addEventListener('close', () => {
      if (!closed) {
        closed = true;
        try { route.close({ code: 1001, reason: 'relay closed' }); } catch { /* gone */ }
      }
    });
    route.onMessage((m) => {
      const text = typeof m === 'string' ? m : Buffer.from(m).toString();
      if (up.readyState === WebSocket.OPEN) up.send(text);
      else pending.push(text);
    });
    route.onClose(() => {
      if (!closed) {
        closed = true;
        try { up.close(); } catch { /* gone */ }
      }
    });
  });
}

// ---------- page helpers ----------
async function transcript(page) {
  return ((await page.evaluate(() => document.querySelector('pre.online-transcript')?.textContent ?? '')) ?? '').split('\n').filter(Boolean);
}

function appliedMap(lines) {
  const m = new Map();
  for (const l of lines) {
    const x = l.match(/^APPLIED turn (\d+) (\S+) ([0-9a-f]{64})/);
    if (x) m.set(Number(x[1]), { ptn: x[2], hash: x[3] });
  }
  return m;
}

async function waitPage(page, fn, arg, what, timeoutMs = 60000) {
  try {
    await page.waitForFunction(fn, arg, { timeout: timeoutMs, polling: 200 });
  } catch {
    throw new Error(`page: timed out waiting for ${what}`);
  }
}

/** Clicks a square on whichever board the page shows: the SVG square in 2D, the projected square in 3D. */
async function clickSquare(page, coord, button = 'left') {
  if (await page.$('.board3d-canvas')) {
    const pt = await page.evaluate((c) => window.__tak3d.screenOf(c), coord);
    await page.mouse.click(pt.x, pt.y, { button });
  } else {
    await page.click(`#sq-${coord} > rect.svg-sq`, { button });
  }
  await sleep(200);
}

let shot = 0;
async function screenshot(page, name) {
  shot++;
  const file = `${String(shot).padStart(2, '0')}-${name}.png`;
  await page.screenshot({ path: path.join(outDir, file) });
  log(`SHOT ${file}`);
}

// White's road on rank 3, with one stack move each way (c2+ for White, b5> for Black). Each entry: the PTN the engine will
// record, and the squares to click to make it.
const GAME = [
  ['a1', ['a1']], ['e5', ['e5']],
  ['a3', ['a3']], ['b5', ['b5']],
  ['b3', ['b3']], ['c5', ['c5']],
  ['c3', ['c3']], ['d4', ['d4']],
  ['c2', ['c2']], ['b5>', ['b5', 'c5']],
  ['c2+', ['c2', 'c3']], ['a4', ['a4']],
  ['d3', ['d3']], ['b4', ['b4']],
  ['e3', ['e3']],
];

// ---------- main ----------
log(`Online click-play run: relays ${relayUrls.join(' ')}, site ${baseUrl}`);
await startIfNeeded('RELAY', `http://127.0.0.1:${relayPort}/r1`, ['run', '--project', 'tools/TakRelay.Local', '--', '--port', String(relayPort)], 'relay-server.txt');
await startIfNeeded('SERVER', baseUrl + '/', ['run', '--project', 'src/TakApp.Blazor/TakApp.Blazor.csproj', '--urls', baseUrl], 'blazor-server.txt');

let browser;
try {
  browser = await chromium.launch({ headless: !headed, args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
  const pages = {};
  for (const who of ['host', 'guest']) {
    // Separate contexts: separate localStorage, so separate keys, like two devices.
    const context = await browser.newContext({ viewport: { width: 1360, height: 960 } });
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    const page = await context.newPage();
    page.on('pageerror', (e) => log(`PAGEERROR ${who} ${e.message}`));
    page.on('console', (m) => { if (m.type() === 'error') log(`CONSOLE ${who} ${m.text()}`); });
    await installPipe(page, who);
    pages[who] = page;
  }
  const { host, guest } = pages;

  await host.goto(baseUrl + '/online');
  await host.waitForSelector('button:has-text("Host a game")', { timeout: 90000 });
  await host.click('button:has-text("Host a game")');
  await waitPage(host, () => document.querySelector('.invite-card .copy-field input')?.value?.includes('invite='), null, 'invite link shown');
  check(await host.$('.invite-qr svg') !== null, 'the waiting room shows a QR code of the invite link');
  const link = await host.inputValue('.invite-card .copy-field input');
  log(`LINK ${link.slice(0, 60)}...`);
  await screenshot(host, 'host-waiting-room');

  // The guest opens the link, and joins in the 2D view.
  await guest.goto(link);
  await guest.waitForSelector('button:has-text("Join")', { timeout: 90000 });
  check((await guest.inputValue('.online-invite-input')).startsWith('TAK2_'), 'the invite link fills the Join box');
  await guest.click('.lobby button:has-text("Join")');

  for (const [who, page] of Object.entries(pages)) {
    await waitPage(page, () => /move turn 1/.test(document.querySelector('.table .online-waiting')?.textContent ?? ''), null, `${who} in game`, 90000);
  }
  await guest.click('.view-toggle [data-view="2d"]');
  await host.click('.view-toggle [data-view="3d"]');
  await waitPage(host, () => window.__tak3d?.ready === true, null, 'host 3D board ready');
  check(await guest.$('.board2d') !== null && await host.$('.board3d-canvas') !== null, 'host plays on the 3D board, guest on the 2D board');

  const hostWhite = (await host.textContent('.table .online-waiting')).includes('your move turn 1');
  const byColour = hostWhite ? { white: host, black: guest } : { white: guest, black: host };
  log(`COLOURS host is ${hostWhite ? 'white' : 'black'}`);
  const blackPage = byColour.black;
  const flipped = await blackPage.evaluate(() => {
    const a1 = document.querySelector('#sq-a1')?.getBoundingClientRect();
    const e5 = document.querySelector('#sq-e5')?.getBoundingClientRect();
    return a1 && e5 ? a1.top < e5.top && a1.left > e5.left : window.__tak3d?.flip === true;
  });
  check(flipped, 'the black player sees the board turned to their side');

  for (let i = 0; i < GAME.length; i++) {
    const turn = i + 1;
    const [ptn, clicks] = GAME[i];
    const mover = turn % 2 === 1 ? byColour.white : byColour.black;
    const other = mover === host ? guest : host;
    await waitPage(mover, (t) => document.querySelector('.table .online-waiting')?.textContent?.includes(`your move turn ${t}`), turn, `turn ${turn} owner`);
    // The waiting side can't move: its tray is hidden and a click on an empty square sends nothing.
    if (turn === 3) {
      check(await other.$('.piece-selector-row') === null, 'the side not to move has no piece tray');
    }
    for (const c of clicks) await clickSquare(mover, c);
    for (const page of [host, guest]) {
      await waitPage(page, (t) => (document.querySelector('pre.online-transcript')?.textContent ?? '').includes(`APPLIED turn ${t} `), turn, `APPLIED turn ${turn}`);
    }
    const h = appliedMap(await transcript(host)).get(turn);
    const g = appliedMap(await transcript(guest)).get(turn);
    check(h && g && h.ptn === ptn && g.ptn === ptn && h.hash === g.hash,
      `turn ${turn} ${ptn} by clicks (${mover === host ? 'host 3D' : 'guest 2D'}): both pages applied it with the same hash ${h?.hash?.slice(0, 16)}`);
    if (ptn.includes('+') || ptn.includes('>')) await screenshot(mover, `stack-move-${ptn.replace(/[+>]/g, '_')}`);
  }

  for (const [who, page] of Object.entries(pages)) {
    await waitPage(page, () => (document.querySelector('pre.online-transcript')?.textContent ?? '').includes('FINAL '), null, `${who} FINAL`);
  }
  const finals = await Promise.all([host, guest].map(async (p) => (await transcript(p)).find((l) => l.startsWith('FINAL '))));
  check(finals[0] === finals[1] && /^FINAL white-road [0-9a-f]{64}$/.test(finals[0]), `both pages agree: ${finals[0]}`);
  const status = await byColour.white.textContent('.play-turn-text');
  check(/^You win by road$/.test(status.trim()), `the winner's page says "${status.trim()}"`);
  const loser = await byColour.black.textContent('.play-turn-text');
  check(/^Opponent wins by road$/.test(loser.trim()), `the other page says "${loser.trim()}"`);
  const road = await guest.$$eval('g[data-coord]', (gs) => gs.filter((g) => g.querySelector('.svg-road-highlight')).map((g) => g.dataset.coord).sort());
  check(JSON.stringify(road) === JSON.stringify(['a3', 'b3', 'c3', 'd3', 'e3']), `the 2D page outlines the road (${road.join(',')})`);
  for (const [who, page] of Object.entries(pages)) check(!(await page.isVisible('#blazor-error-ui')), `${who}: no unhandled error bar`);
  await screenshot(host, 'host-final');
  await screenshot(guest, 'guest-final');
} catch (e) {
  check(false, `run threw: ${e.message.split('\n')[0]}`);
} finally {
  if (browser) await browser.close();
  stopChildren();
}
log(`SUMMARY ${failures === 0 ? 'PASS' : 'FAIL'} checks=${checks} failed=${failures} evidence=${path.relative(repoRoot, outDir)}`);
process.exit(failures === 0 ? 0 : 1);

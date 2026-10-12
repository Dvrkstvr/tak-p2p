#!/usr/bin/env node
// F-043: Playwright regression for the Blazor Play page (local pass-and-play and vs AI).
// Gesture rows (G-xx) are defined in pipeline/design/gestures.md; every step below cites the row it exercises.
//
// One command:   node tests/e2e/play.mjs
// Options:       --url <base>       site to drive (default http://127.0.0.1:5199). If nothing answers there and the URL is
//                                   the default, the script builds and starts the Blazor dev server itself and stops it at the end.
//                --out <dir>        evidence folder (default pipeline/evidence/m1-f043-<yyyy-mm-dd>)
//                --headed           show the browser
// Exit code 0 = every check passed; 1 = a check failed (the log names it); 2 = the harness could not start.
//
// No public traffic: every WebSocket the page opens is routed to a stub that never answers (page.routeWebSocket).

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
const today = new Date().toISOString().slice(0, 10);
const outDir = path.resolve(arg('--out', path.join(repoRoot, 'pipeline', 'evidence', `m1-f043-${today}`)));
const headed = args.includes('--headed');

fs.mkdirSync(outDir, { recursive: true });
// .txt, not .log: the repo ignores *.log and the log is the evidence.
const logPath = path.join(outDir, 'run.txt');
fs.writeFileSync(logPath, '');
const log = (line) => {
  const stamped = `${new Date().toISOString()} ${line}`;
  console.log(stamped);
  fs.appendFileSync(logPath, stamped + '\n');
};

let failures = 0;
let checks = 0;
const check = (ok, what) => {
  checks++;
  if (ok) {
    log(`PASS ${what}`);
  } else {
    failures++;
    log(`FAIL ${what}`);
  }
  return ok;
};

// ---------- server ----------
async function reachable(url) {
  try {
    const res = await fetch(url, { signal: AbortSignal.timeout(2000) });
    return res.ok;
  } catch {
    return false;
  }
}

async function startServerIfNeeded() {
  if (await reachable(baseUrl + '/')) {
    log(`SERVER using running site at ${baseUrl}`);
    return null;
  }
  if (baseUrl !== DEFAULT_URL) {
    log(`SERVER nothing answers at ${baseUrl}`);
    process.exit(2);
  }
  log('SERVER starting dotnet run --project src/TakApp.Blazor/TakApp.Blazor.csproj --urls ' + baseUrl);
  const child = spawn('dotnet', ['run', '--project', 'src/TakApp.Blazor/TakApp.Blazor.csproj', '--urls', baseUrl], {
    cwd: repoRoot,
    detached: true,
    stdio: ['ignore', fs.openSync(path.join(outDir, 'server.log'), 'w'), fs.openSync(path.join(outDir, 'server.log'), 'a')],
  });
  for (let i = 0; i < 180; i++) {
    if (await reachable(baseUrl + '/')) {
      log(`SERVER up (pid ${child.pid})`);
      return child;
    }
    if (child.exitCode !== null) break;
    await new Promise((r) => setTimeout(r, 1000));
  }
  log('SERVER did not come up; see server.log');
  try { process.kill(-child.pid, 'SIGTERM'); } catch { /* already gone */ }
  process.exit(2);
}

function stopServer(child) {
  if (!child) return;
  try { process.kill(-child.pid, 'SIGTERM'); } catch { /* already gone */ }
  log(`SERVER stopped (pid ${child.pid})`);
}

// ---------- page helpers (desktop layout: viewport >= 1024 px, so everything is scoped to .arena-3col) ----------
const D = '.arena-3col';
// The square's own rect: the g's box grows upward when a stack or a carried hand is drawn above it.
const sq = (c) => `${D} #sq-${c} > rect.svg-sq`;

async function settle(page) {
  // Blazor renders synchronously after an event; a short wait lets the DOM diff land.
  await page.waitForTimeout(120);
}

async function history(page) {
  return page.$$eval(`${D} .move-history-list .move-history-row`, (rows) =>
    rows.flatMap((r) => [r.querySelector('.move-ptn-white')?.textContent?.trim(), r.querySelector('.move-ptn-black')?.textContent?.trim()])
      .filter((m) => m));
}

async function statusText(page) {
  return (await page.textContent(`${D} .play-turn-text`))?.trim() ?? '';
}

async function errorText(page) {
  const el = await page.$(`${D} .arena-col-left div[style*="#7f1d1d"]`);
  return el ? (await el.textContent()).trim() : '';
}

async function hudText(page) {
  const el = await page.$(`${D} .slide-hud-banner`);
  return el ? (await el.textContent()).replace(/\s+/g, ' ').trim() : '';
}

async function squaresWithClass(page, cls) {
  return page.$$eval(`${D} g[data-coord]`, (gs, c) => gs.filter((g) => g.querySelector(`rect.svg-sq.${c}`)).map((g) => g.dataset.coord), cls);
}

async function click(page, coord) {
  await page.click(sq(coord));
  await settle(page);
}

async function rightClick(page, coord) {
  await page.click(sq(coord), { button: 'right' });
  await settle(page);
}

async function selectedTab(page) {
  return (await page.textContent(`${D} .piece-selector-tab.active`))?.replace(/\s+/g, ' ').trim() ?? '';
}

let shot = 0;
async function screenshot(page, name) {
  shot++;
  const file = `${String(shot).padStart(2, '0')}-${name}.png`;
  await page.screenshot({ path: path.join(outDir, file) });
  log(`SHOT ${file}`);
}

async function expectHistory(page, expected, row) {
  const h = await history(page);
  return check(JSON.stringify(h) === JSON.stringify(expected), `${row} history is [${expected.join(' ')}] (got [${h.join(' ')}])`);
}

async function openHome(page) {
  await page.goto(baseUrl + '/');
  await page.waitForSelector('.actions-row button', { timeout: 60000 });
}

async function startGame(page, mode, size = '5×5', difficulty = 'Easy') {
  await openHome(page);
  await page.click(`.toggle-btn:has-text("${size}")`);
  if (mode === 'vs AI') await page.click(`.toggle-btn:has-text("${difficulty}")`);
  await page.click(`.actions-row button:has-text("${mode}")`);
  await page.waitForSelector(sq('a1'), { timeout: 30000 });
  await settle(page);
}

// ---------- scenario 1: local pass-and-play ----------
async function localGame(page) {
  log('SCENARIO local pass-and-play 5x5: place, rotate, quick wall, inspect, slides 1-1-1 and 2-1, cancels, road win');
  await startGame(page, 'Local');
  check((await statusText(page)).includes('OPENING: WHITE PLACES OPPONENT'), 'G-01 new local game opens in the opening phase');
  check((await selectedTab(page)).startsWith('Flat'), 'G-01 Flat is the selected piece at start');
  check((await squaresWithClass(page, 'legal-target')).length === 25, 'G-01 every empty square is marked as a placement target');

  // G-04 Wall selected in the opening: the engine refuses, the error is shown, nothing changes.
  await page.click(`${D} .piece-selector-tab:has-text("Wall")`);
  await settle(page);
  check((await selectedTab(page)).startsWith('Wall'), 'G-04 Wall tab selects Wall');
  await click(page, 'c3');
  check((await errorText(page)).includes('First turn placement must be a Flat stone'), 'G-10b refused placement shows the engine error');
  await expectHistory(page, [], 'G-10b');
  await screenshot(page, 'opening-wall-refused');
  await page.keyboard.press('f');
  await settle(page);
  check((await selectedTab(page)).startsWith('Flat'), 'G-05 key F selects Flat');

  await click(page, 'a1'); // 1. W places a black flat
  check((await errorText(page)) === '', 'G-10 a successful click clears the error');
  await click(page, 'e5'); // 2. B places a white flat
  await expectHistory(page, ['a1', 'e5'], 'G-10');
  check((await statusText(page)).includes('WHITE TO PLAY'), 'G-10 after both opening plies it is White to play');

  // G-13 left-click on the opponent's stack inspects it.
  await click(page, 'a1');
  check((await page.$(`${D} .svg-inspector-group`)) !== null, 'G-13 left-click on an opponent stack opens the inspector');
  await screenshot(page, 'inspect-a1');
  await click(page, 'b3'); // G-15 click dismisses the inspector and then places
  check((await page.$(`${D} .svg-inspector-group`)) === null, 'G-15 a square click dismisses the inspector');
  await expectHistory(page, ['a1', 'e5', 'b3'], 'G-15/G-10');

  // G-02 / G-03 reserve chip: click Flat (selected) rotates to Wall; right-click rotates to Wall. Then F back.
  const blackFlatChip = `${D} .player-card-flat.active-player-card .reserve-chip:has-text("Flat")`;
  await page.click(blackFlatChip);
  await settle(page);
  check((await selectedTab(page)).startsWith('Wall'), 'G-02 clicking the selected Flat chip rotates to Wall');
  await page.click(blackFlatChip);
  await settle(page);
  check((await selectedTab(page)).startsWith('Flat'), 'G-02 clicking the Flat chip when Wall is selected selects Flat');
  await page.click(blackFlatChip, { button: 'right' });
  await settle(page);
  check((await selectedTab(page)).startsWith('Wall'), 'G-03 right-clicking the Flat chip rotates to Wall');
  await page.keyboard.press('f');
  await settle(page);

  await click(page, 'a5'); // 4. B
  await click(page, 'b2'); // 5. W
  await click(page, 'b5'); // 6. B

  // 7. W b2+ : a single stone moves as soon as it lands (carry 1).
  await click(page, 'b2');
  check((await squaresWithClass(page, 'selected')).includes('b2'), 'G-12 clicking an own stack selects it as slide origin');
  check((await hudText(page)).includes('1 of 1'), 'G-12 HUD shows 1 of 1 carried');
  await click(page, 'b3');
  await expectHistory(page, ['a1', 'e5', 'b3', 'a5', 'b2', 'b5', 'b2+'], 'G-21 (carry 1)');
  await click(page, 'd5'); // 8. B
  await click(page, 'b4'); // 9. W
  await click(page, 'a4'); // 10. B
  await click(page, 'b4');
  await click(page, 'b3'); // 11. W b4- : b3 is now three white flats
  await click(page, 'e1'); // 12. B
  await expectHistory(page, ['a1', 'e5', 'b3', 'a5', 'b2', 'b5', 'b2+', 'd5', 'b4', 'a4', 'b4-', 'e1'], 'G-21');

  // 13. W: count selection and cancels on the 3-stack at b3.
  await click(page, 'b3');
  check((await hudText(page)).includes('3 of 3'), 'G-12 3-stack: carry starts at min(height, size) = 3');
  const targets = (await squaresWithClass(page, 'legal-slide-target')).sort();
  check(JSON.stringify(targets) === JSON.stringify(['a3', 'b2', 'b4', 'c3']), `G-12 adjacent enterable squares are marked (got ${targets.join(',')})`);
  await click(page, 'b3');
  check((await hudText(page)).includes('2 of 3'), 'G-20 clicking the origin leaves one tile: 2 of 3');
  await click(page, 'b3');
  check((await hudText(page)).includes('1 of 3'), 'G-20 again: 1 of 3');
  await click(page, 'b3');
  check((await hudText(page)).includes('3 of 3'), 'G-20 below 1 wraps to the maximum: 3 of 3');
  await screenshot(page, 'slide-origin-b3');
  await click(page, 'e1'); // out of range, not own
  check((await hudText(page)) === '' && (await squaresWithClass(page, 'selected')).length === 0, 'G-24 out-of-range click cancels the move');
  await expectHistory(page, ['a1', 'e5', 'b3', 'a5', 'b2', 'b5', 'b2+', 'd5', 'b4', 'a4', 'b4-', 'e1'], 'G-24 nothing placed');

  await click(page, 'b3');
  await click(page, 'c3');
  check((await hudText(page)).includes('Dropped: 1') && (await hudText(page)).includes('In hand: 2'), 'G-21 first step drops 1, 2 in hand');
  await screenshot(page, 'mid-slide');
  await click(page, 'a1'); // elsewhere mid-move
  check((await hudText(page)) === '', 'G-32 click outside the path mid-move cancels the whole move');
  await expectHistory(page, ['a1', 'e5', 'b3', 'a5', 'b2', 'b5', 'b2+', 'd5', 'b4', 'a4', 'b4-', 'e1'], 'G-32 board unchanged');

  await click(page, 'b3');
  await click(page, 'c3');
  await rightClick(page, 'd3');
  check((await hudText(page)) === '', 'G-33 right-click mid-move cancels');
  await expectHistory(page, ['a1', 'e5', 'b3', 'a5', 'b2', 'b5', 'b2+', 'd5', 'b4', 'a4', 'b4-', 'e1'], 'G-33 board unchanged');

  await click(page, 'b3');
  await page.keyboard.press('Escape');
  await settle(page);
  check((await hudText(page)) === '', 'G-06 Esc cancels a started move');

  await click(page, 'b3');
  check((await page.$(`${D} .slide-control-box`)) !== null, 'G-40 the slide bar opens with a started move');
  await page.click(`${D} .slide-control-box button:has-text("Cancel")`);
  await settle(page);
  check((await hudText(page)) === '' && (await page.$(`${D} .slide-control-box`)) === null, 'G-41 the slide bar Cancel cancels the move');
  await expectHistory(page, ['a1', 'e5', 'b3', 'a5', 'b2', 'b5', 'b2+', 'd5', 'b4', 'a4', 'b4-', 'e1'], 'G-41 board unchanged');

  // 13. W 3b3>111
  await click(page, 'b3');
  await click(page, 'c3');
  await click(page, 'd3');
  check((await squaresWithClass(page, 'slide-next-target')).includes('e3'), 'G-31 the next square along the path is offered');
  await click(page, 'e3');
  await expectHistory(page, ['a1', 'e5', 'b3', 'a5', 'b2', 'b5', 'b2+', 'd5', 'b4', 'a4', 'b4-', 'e1', '3b3>111'], 'G-31 slide 1-1-1');
  await screenshot(page, 'after-3b3-111');

  // 14. B right-click quick wall
  await rightClick(page, 'a2');
  await expectHistory(page, ['a1', 'e5', 'b3', 'a5', 'b2', 'b5', 'b2+', 'd5', 'b4', 'a4', 'b4-', 'e1', '3b3>111', 'Sa2'], 'G-11 right-click empty places a wall');
  check((await selectedTab(page)).startsWith('Flat'), 'G-11 quick wall leaves the selected piece unchanged');

  // 15. W c3> : the slide bar must describe c3, not the b3 move it showed before (Q-1, fixed in M1).
  await click(page, 'c3');
  const confirm = (await page.textContent(`${D} .slide-control-box`).catch(() => '')) ?? '';
  const named = (confirm.match(/Confirm Move \(([^)]*)\)/) ?? [])[1] ?? '';
  check(/^\d?c3/.test(named), `G-43 a new origin resets the slide bar to that stack (Confirm names "${named}")`);
  await click(page, 'd3');
  await click(page, 'b1'); // 16. B
  await click(page, 'e3'); await click(page, 'd3'); // 17. W e3< : d3 is three white flats
  await click(page, 'e2'); // 18. B

  // 19. W 3d3<21 : step, stack on the same square, step.
  await click(page, 'd3');
  await click(page, 'c3');
  check((await squaresWithClass(page, 'slide-cur-target')).includes('c3'), 'G-30 the current step square is offered for stacking');
  await click(page, 'c3');
  check((await hudText(page)).includes('Dropped: 2') && (await hudText(page)).includes('In hand: 1'), 'G-30 clicking the current square drops one more');
  await click(page, 'b3');
  const afterSlides = ['a1', 'e5', 'b3', 'a5', 'b2', 'b5', 'b2+', 'd5', 'b4', 'a4', 'b4-', 'e1', '3b3>111', 'Sa2', 'c3>', 'b1', 'e3<', 'e2', '3d3<21'];
  await expectHistory(page, afterSlides, 'G-30/G-31 slide 2-1');
  await screenshot(page, 'after-3d3-21');

  // Road on rank 3 for White.
  await click(page, 'c1'); // 20. B
  await click(page, 'a3'); // 21. W
  await click(page, 'c5'); // 22. B
  await click(page, 'd3'); // 23. W
  await click(page, 'd2'); // 24. B
  await click(page, 'e3'); // 25. W wins
  await expectHistory(page, [...afterSlides, 'c1', 'a3', 'c5', 'd3', 'd2', 'e3'], 'G-10 final placement');
  const status = await statusText(page);
  check(/WINS \(ROAD\)/.test(status), `G-50 road win is announced (status "${status}")`);
  const road = await page.$$eval(`${D} g[data-coord]`, (gs) => gs.filter((g) => g.querySelector('.svg-road-highlight')).map((g) => g.dataset.coord).sort());
  check(JSON.stringify(road) === JSON.stringify(['a3', 'b3', 'c3', 'd3', 'e3']), `G-50 the winning road is highlighted (got ${road.join(',')})`);
  check((await page.$(`${D} .play-actions button:has-text("Resign")`)) === null, 'G-50 Resign is hidden after the game ends');
  check((await squaresWithClass(page, 'legal-target')).length === 0, 'G-50 no placement targets after the game ends');
  await screenshot(page, 'local-road-win');

  await click(page, 'e4');
  check((await errorText(page)).length > 0, `G-17 a click after the end is refused by the engine (error "${await errorText(page)}")`);
  await expectHistory(page, [...afterSlides, 'c1', 'a3', 'c5', 'd3', 'd2', 'e3'], 'G-17 nothing changes after the end');
}

// ---------- scenario 2: vs AI to a result ----------
async function aiGame(page) {
  log('SCENARIO vs AI (Easy) 5x5: the human places flats on the first empty square until the game ends');
  await startGame(page, 'vs AI');
  check((await page.textContent(`${D} .status-text`)).includes('AI Easy'), 'G-52 the status names the AI mode');
  let plies = 0;
  for (let turn = 0; turn < 60; turn++) {
    // Wait for the AI to finish and the page to settle on a state.
    await page.waitForFunction(() => {
      const t = document.querySelector('.arena-3col .play-turn-text')?.textContent ?? '';
      const thinking = document.querySelector('.arena-3col .status-block')?.textContent?.includes('AI is thinking') ?? false;
      return !thinking && (t.includes('WHITE') || t.includes('WINS') || t.includes('DRAW'));
    }, null, { timeout: 30000 });
    const status = await statusText(page);
    if (/WINS|DRAW/.test(status)) {
      log(`RESULT vs AI after ${plies} human plies: ${status}`);
      break;
    }
    const empties = await squaresWithClass(page, 'legal-target');
    if (empties.length === 0) {
      check(false, 'G-52 the human has a placement target on their turn');
      break;
    }
    const before = (await history(page)).length;
    await click(page, empties[0]);
    plies++;
    const err = await errorText(page);
    if (err) {
      check(false, `G-10 human placement on ${empties[0]} refused: ${err}`);
      break;
    }
    // G-52: the AI answers on its own (unless the human's move ended the game).
    await page.waitForFunction((n) => {
      const rows = document.querySelectorAll('.arena-3col .move-history-list .move-history-row');
      let count = 0;
      rows.forEach((r) => {
        if (r.querySelector('.move-ptn-white')?.textContent?.trim()) count++;
        if (r.querySelector('.move-ptn-black')?.textContent?.trim()) count++;
      });
      const t = document.querySelector('.arena-3col .play-turn-text')?.textContent ?? '';
      return count >= n + 2 || t.includes('WINS') || t.includes('DRAW');
    }, before, { timeout: 30000 });
    if (turn === 0) await screenshot(page, 'ai-first-reply');
  }
  const final = await statusText(page);
  check(/WINS|DRAW/.test(final), `G-52 the vs AI game reached a result ("${final}")`);
  const h = await history(page);
  log(`AI GAME PTN ${h.join(' ')}`);
  await screenshot(page, 'ai-result');
}

// ---------- main ----------
const server = await startServerIfNeeded();
let browser;
try {
  browser = await chromium.launch({ headless: !headed });
  const context = await browser.newContext({ viewport: { width: 1400, height: 1000 } });
  const page = await context.newPage();
  await page.routeWebSocket(/.*/, (ws) => { log(`WS stubbed ${ws.url()}`); });
  page.on('pageerror', (e) => log(`PAGEERROR ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') log(`CONSOLE ${m.text()}`); });

  for (const scenario of [localGame, aiGame]) {
    try {
      await scenario(page);
    } catch (e) {
      check(false, `${scenario.name} threw: ${e.message.split('\n')[0]}`);
      try { await screenshot(page, `${scenario.name}-error`); } catch { /* page gone */ }
    }
  }
} finally {
  if (browser) await browser.close();
  stopServer(server);
}
log(`SUMMARY ${failures === 0 ? 'PASS' : 'FAIL'} checks=${checks} failed=${failures} evidence=${path.relative(repoRoot, outDir)}`);
process.exit(failures === 0 ? 0 : 1);

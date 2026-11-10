#!/usr/bin/env node
// F-043: Playwright regression for the Blazor Play page (local pass-and-play and vs AI), on the 2D board, plus the 3D board,
// the AI playing White, and a phone-sized layout (the redesign).
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

// ---------- page helpers (everything is scoped to the game table) ----------
const D = '.table';
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
  const el = await page.$(`${D} .table-error`);
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

async function startGame(page, mode, size = '5×5', difficulty = 'Easy', view = '2d', colour = 'White') {
  await openHome(page);
  await page.click(`.toggle-btn:has-text("${size}")`);
  if (mode === 'vs AI') {
    await page.click(`.toggle-btn:has-text("${difficulty}")`);
    await page.click(`.colour-picker .seg-btn:has-text("${colour}")`);
  }
  await page.click(`.actions-row button:has-text("${mode}")`);
  await page.waitForSelector(`${D} .view-toggle`, { timeout: 30000 });
  if (view === '2d') {
    await page.click(`${D} .view-toggle [data-view="2d"]`);
    await page.waitForSelector(sq('a1'), { timeout: 30000 });
  } else {
    await page.click(`${D} .view-toggle [data-view="3d"]`);
    await page.waitForFunction(() => window.__tak3d?.ready === true, null, { timeout: 30000 });
  }
  await settle(page);
}

// ---------- scenario 1: local pass-and-play ----------
async function localGame(page) {
  log('SCENARIO local pass-and-play 5x5: place, rotate, quick wall, inspect, slides 1-1-1 and 2-1, cancels, road win');
  await startGame(page, 'Local');
  check((await statusText(page)) === 'Opening · White places a black flat', 'G-01 new local game opens in the opening phase');
  check((await selectedTab(page)).startsWith('Flat'), 'G-01 Flat is the selected piece at start');
  check((await squaresWithClass(page, 'legal-target')).length === 25, 'G-01 every empty square is marked as a placement target');

  // G-04 Wall selected in the opening: the engine refuses, the error is shown, nothing changes.
  await page.click(`${D} .piece-selector-tab:has-text("Wall")`);
  await settle(page);
  check((await selectedTab(page)).startsWith('Wall'), 'G-04 Wall tab selects Wall');
  check((await squaresWithClass(page, 'legal-target')).length === 0, 'G-04 no square is marked for a wall in the opening (F-021 row 2)');
  await click(page, 'c3'); // forced anyway: the engine refuses it
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
  check((await statusText(page)) === 'White to move', 'G-10 after both opening plies it is White to move');

  // G-13 left-click on the opponent's stack inspects it.
  await click(page, 'a1');
  check((await page.$(`${D} .stack-inspector`)) !== null, 'G-13 left-click on an opponent stack opens the inspector');
  await screenshot(page, 'inspect-a1');
  await click(page, 'b3'); // G-15 click dismisses the inspector and then places
  check((await page.$(`${D} .stack-inspector`)) === null, 'G-15 a square click dismisses the inspector');
  await expectHistory(page, ['a1', 'e5', 'b3'], 'G-15/G-10');

  // G-02 / G-03 reserve chip: click Flat (selected) rotates to Wall; right-click rotates to Wall. Then F back.
  const blackFlatChip = `${D} .player-card.is-turn .reserve-chip:has-text("Flat")`;
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
  check(status === 'White wins by road', `G-50 road win is announced (status "${status}")`);
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
  check((await page.textContent(`${D} .mode-label`)).includes('vs AI Easy'), 'G-52 the status names the AI mode');
  let plies = 0;
  for (let turn = 0; turn < 60; turn++) {
    // Wait for the AI to finish and the page to settle on a state.
    await page.waitForFunction(() => {
      const t = document.querySelector('.table .play-turn-text')?.textContent ?? '';
      const thinking = document.querySelector('.table .table-activity') !== null;
      return !thinking && (/You place|Your move/.test(t) || /win|Draw/.test(t));
    }, null, { timeout: 30000 });
    const status = await statusText(page);
    if (/win|Draw/.test(status)) {
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
      const rows = document.querySelectorAll('.table .move-history-list .move-history-row');
      let count = 0;
      rows.forEach((r) => {
        if (r.querySelector('.move-ptn-white')?.textContent?.trim()) count++;
        if (r.querySelector('.move-ptn-black')?.textContent?.trim()) count++;
      });
      const t = document.querySelector('.table .play-turn-text')?.textContent ?? '';
      return count >= n + 2 || /win|Draw/.test(t);
    }, before, { timeout: 30000 });
    if (turn === 0) await screenshot(page, 'ai-first-reply');
  }
  const final = await statusText(page);
  check(/win|Draw/.test(final), `G-52 the vs AI game reached a result ("${final}")`);
  const h = await history(page);
  log(`AI GAME PTN ${h.join(' ')}`);
  await screenshot(page, 'ai-result');
}


// ---------- scenario 3: the 3D board ----------
// Clicks land where board3d.js projects each square (window.__tak3d.screenOf), so this drives the real canvas hit-testing.
async function click3d(page, coord, button = 'left') {
  const pt = await page.evaluate((c) => window.__tak3d.screenOf(c), coord);
  await page.mouse.click(pt.x, pt.y, { button });
  await settle(page);
  await page.waitForTimeout(150);
}

async function threeDGame(page) {
  log('SCENARIO 3D board 5x5: placements, quick wall, stack pick-up and step, a 2-stack split, switch to 2D and back mid-game');
  await startGame(page, 'Local', '5×5', 'Easy', '3d');
  check((await page.$(`${D} .board3d-canvas`)) !== null && (await page.$(`${D} .board2d`)) === null, '3D the 3D board replaces the 2D one');
  for (const c of ['a1', 'e5', 'c3', 'c4']) await click3d(page, c); // opening, then W c3, B c4
  await click3d(page, 'd4', 'right'); // W quick wall
  await expectHistory(page, ['a1', 'e5', 'c3', 'c4', 'Sd4'], '3D placements and right-click wall');
  await click3d(page, 'c5'); // B
  await click3d(page, 'c3'); // W picks up c3
  check((await hudText(page)).includes('1 of 1'), '3D clicking an own stack picks it up');
  await screenshot(page, '3d-picked-up');
  await click3d(page, 'b3'); // W c3<
  await expectHistory(page, ['a1', 'e5', 'c3', 'c4', 'Sd4', 'c5', 'c3<'], '3D step sends the move');
  await click3d(page, 'c4'); // B picks up c4 ...
  await click3d(page, 'c5'); // ... and steps onto c5: 2-stack
  await click3d(page, 'b2'); // W
  await click3d(page, 'c5'); // B picks up the 2-stack (carry 2)
  check((await hudText(page)).includes('2 of 2'), '3D a 2-stack carries 2');
  await click3d(page, 'c4'); // first step drops 1
  check((await hudText(page)).includes('In hand: 1'), '3D first step drops one, one in hand');
  await screenshot(page, '3d-mid-move');
  await click3d(page, 'c3'); // second step drops the last: 2c5-11
  await expectHistory(page, ['a1', 'e5', 'c3', 'c4', 'Sd4', 'c5', 'c3<', 'c4+', 'b2', '2c5-11'], '3D two-square split');
  await click3d(page, 'b3');
  check((await page.$(`${D} .stack-inspector`)) === null && (await hudText(page)).includes('of'), '3D own stack picked up again');
  await page.keyboard.press('Escape');
  await settle(page);
  check((await hudText(page)) === '', '3D Esc cancels');
  await click3d(page, 'c3', 'right');
  check((await page.$(`${D} .stack-inspector`)) !== null, '3D right-click on a stack opens the inspector');
  await page.keyboard.press('v'); // to 2D, same game
  await page.waitForSelector(sq('a1'));
  await expectHistory(page, ['a1', 'e5', 'c3', 'c4', 'Sd4', 'c5', 'c3<', 'c4+', 'b2', '2c5-11'], 'V switches to 2D and keeps the game');
  await page.keyboard.press('v');
  await page.waitForFunction(() => window.__tak3d?.ready === true);
  check((await page.$(`${D} .board3d-canvas`)) !== null, 'V switches back to 3D');
  await screenshot(page, '3d-after-switch');
}

// ---------- scenario 4: the AI plays White ----------
async function aiWhite(page) {
  log('SCENARIO vs AI as Black 4x4: the AI opens, the board faces Black, resign, then Play again');
  await startGame(page, 'vs AI', '4×4', 'Easy', '2d', 'Black');
  await page.waitForFunction(() => document.querySelectorAll('.table .move-ptn-white').length > 0 &&
    document.querySelector('.table .table-activity') === null, null, { timeout: 30000 });
  const h = await history(page);
  check(h.length === 1, `G-52 the AI (White) made the first move (${h.join(' ')})`);
  check((await statusText(page)) === 'Opening · You place a white flat', `the human is to place White's flat ("${await statusText(page)}")`);
  check((await page.$$(`${D} .piece-selector-tab:has-text("Cap")`)).length === 0, 'no Cap tab on 4x4 (no capstones)');
  const flipped = await page.evaluate(() => {
    const a1 = document.querySelector('#sq-a1').getBoundingClientRect();
    const d4 = document.querySelector('#sq-d4').getBoundingClientRect();
    return a1.top < d4.top && a1.left > d4.left;
  });
  check(flipped, 'the board is turned to face Black');
  const empties = await squaresWithClass(page, 'legal-target');
  await click(page, empties[0]);
  await page.waitForFunction(() => document.querySelectorAll('.table .move-ptn-white').length > 1 &&
    document.querySelector('.table .table-activity') === null, null, { timeout: 30000 });
  await page.click(`${D} .play-actions button:has-text("Resign")`);
  await settle(page);
  check((await page.textContent(`${D} .play-actions`)).includes('Really resign?'), 'Resign asks once more');
  await page.click(`${D} .play-actions button:has-text("Really resign?")`);
  await settle(page);
  const status = await statusText(page);
  check(/^AI \(Easy\) wins by resignation$/.test(status), `G-51 resigning ends the game ("${status}")`);
  await screenshot(page, 'ai-white-resigned');
  await page.click(`${D} .result-card button:has-text("Play again")`);
  await page.waitForFunction(() => document.querySelectorAll('.table .move-ptn-white').length === 1 &&
    document.querySelector('.table .table-activity') === null, null, { timeout: 30000 });
  check((await history(page)).length === 1, 'Play again starts a fresh game with the same settings (the AI opens again)');
}

// ---------- scenario 5: phone-sized screen ----------
async function phone(browser) {
  log('SCENARIO phone 390x844: home, local game on the 2D board by taps, layout fits the width');
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
  const page = await context.newPage();
  await page.routeWebSocket(/.*/, () => {});
  page.on('pageerror', (e) => log(`PAGEERROR ${e.message}`));
  await openHome(page);
  check(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), 'phone: the home page has no sideways scroll');
  await screenshot(page, 'phone-home');
  await page.tap('.actions-row button:has-text("Local")');
  await page.waitForSelector(`${D} .view-toggle`);
  await page.tap(`${D} .view-toggle [data-view="2d"]`);
  await page.waitForSelector(sq('a1'));
  for (const c of ['a1', 'e5', 'c3']) {
    await page.tap(sq(c));
    await settle(page);
  }
  await expectHistory(page, ['a1', 'e5', 'c3'], 'phone: taps place pieces');
  check(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), 'phone: the game page has no sideways scroll');
  const box = await page.$eval(`${D} .board2d`, (el) => el.getBoundingClientRect().width);
  check(box >= 340, `phone: the board uses the screen width (${Math.round(box)} px)`);
  await screenshot(page, 'phone-game');
  await context.close();
}

// ---------- main ----------
const server = await startServerIfNeeded();
let browser;
try {
  browser = await chromium.launch({ headless: !headed, args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
  const context = await browser.newContext({ viewport: { width: 1400, height: 1000 } });
  const page = await context.newPage();
  await page.routeWebSocket(/.*/, (ws) => { log(`WS stubbed ${ws.url()}`); });
  page.on('pageerror', (e) => log(`PAGEERROR ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') log(`CONSOLE ${m.text()}`); });

  for (const scenario of [localGame, aiGame, threeDGame, aiWhite]) {
    try {
      await scenario(page);
    } catch (e) {
      check(false, `${scenario.name} threw: ${e.message.split('\n')[0]}`);
      try { await screenshot(page, `${scenario.name}-error`); } catch { /* page gone */ }
    }
    check(!(await page.isVisible('#blazor-error-ui')), `${scenario.name}: no unhandled error bar`);
  }
  try {
    await phone(browser);
  } catch (e) {
    check(false, `phone threw: ${e.message.split('\n')[0]}`);
  }
} finally {
  if (browser) await browser.close();
  stopServer(server);
}
log(`SUMMARY ${failures === 0 ? 'PASS' : 'FAIL'} checks=${checks} failed=${failures} evidence=${path.relative(repoRoot, outDir)}`);
process.exit(failures === 0 ? 0 : 1);

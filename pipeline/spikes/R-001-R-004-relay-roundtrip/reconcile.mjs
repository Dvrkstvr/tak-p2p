// Read-only: list every kind-9999 event on each relay for each spike game id and flag ids missing from published-events-full-list.tsv
import fs from 'fs';
import { Relay, sleep } from './common.mjs';
const known = new Set(fs.readFileSync('published-events-full-list.tsv', 'utf8').split('\n').map((l) => l.split('\t')[5]));
const games = {}; 
for (const run of ['real-nos-lol', 'real-damus', 'real-dual-FAILED-race-bug', 'real-dual']) games[run] = fs.readFileSync(`out/${run}/run.txt`, 'utf8').match(/game=(\S+)/)[1];
const extra = [];
for (const url of ['wss://nos.lol', 'wss://relay.damus.io']) {
  const r = await new Relay(url, () => {}).connect();
  for (const [run, g] of Object.entries(games)) {
    const evs = []; const s = r.sub('r' + run, [{ kinds: [9999], '#g': [g] }], (e) => evs.push(e));
    await Promise.race([s.eosePromise, sleep(8000)]);
    const unk = evs.filter((e) => !known.has(e.id));
    console.log(url, run, g, 'events on relay:', evs.length, 'unknown to log:', unk.length);
    for (const e of unk) extra.push([run, 'replay(stray)', url.replace('wss://', ''), 9999, '2', e.id, 'ok:true:(seen on relay)']);
  }
  r.close();
}
fs.appendFileSync('published-events-full-list.tsv', extra.map((x) => x.join('\t')).join('\n') + (extra.length ? '\n' : ''));
console.log('appended', extra.length);
process.exit(0);

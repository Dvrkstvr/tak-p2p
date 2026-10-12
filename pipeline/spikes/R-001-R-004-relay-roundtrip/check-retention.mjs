// Read-only: re-fetch events published by this spike (ids from published-events-full-list.tsv) and report which relays still serve them.
// Run again after 24h / 7d / 30d to measure retention. Usage: node check-retention.mjs
import fs from 'fs';
import { Relay, sleep } from './common.mjs';
const rows = fs.readFileSync('published-events-full-list.tsv', 'utf8').trim().split('\n').slice(1).map((l) => l.split('\t')).filter((r) => r[5].startsWith('ok:true'));
const byRelay = {};
for (const [, relay, , , id] of rows) (byRelay[relay] ??= new Set()).add(id);
for (const [relay, ids] of Object.entries(byRelay)) {
  const url = 'wss://' + relay; const r = await new Relay(url, () => {}).connect();
  const found = new Set(); const all = [...ids];
  for (let i = 0; i < all.length; i += 5) {   // nos.lol returned nothing for a single 20-id filter; chunks of 5 work
    const s = r.sub('ret' + i, [{ ids: all.slice(i, i + 5) }], (e) => found.add(e.id));
    await Promise.race([s.eosePromise, sleep(10000)]);
  }
  r.close();
  console.log(new Date().toISOString(), relay, `still served: ${found.size}/${ids.size}`);
}
process.exit(0);

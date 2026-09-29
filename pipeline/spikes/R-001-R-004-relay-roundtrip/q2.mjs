import { Relay, sleep } from './common.mjs';
import fs from 'fs';
const known = new Set(fs.readFileSync('published-events-full-list.tsv','utf8').split('\n').map(l=>l.split('\t')[4]));
const r = await new Relay('wss://nos.lol', () => {}).connect(); const evs = [];
const s = r.sub('q', [{ kinds: [9999], '#g': ['spike-945fc109de1b'] }], (e) => evs.push(e));
await Promise.race([s.eosePromise, sleep(10000)]);
for (const e of evs.sort((a,b)=>a.created_at-b.created_at)) console.log(e.id.slice(0,10), e.pubkey.slice(0,8), e.created_at, known.has(e.id)?'known':'UNKNOWN', JSON.stringify(e.tags).slice(0,120));
process.exit(0);

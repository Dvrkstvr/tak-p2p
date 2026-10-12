import { Relay, sleep, nip44, unhex } from './common.mjs';
import fs from 'fs';
const skB = unhex(fs.readFileSync('out/real-nos-lol/b.key','utf8').trim());
const r = await new Relay('wss://nos.lol', () => {}).connect();
const s = r.sub('q', [{ kinds:[9999], '#g':['spike-945fc109de1b'], authors:['2a891a46073422b0470f00c2aa53cd90b33ca824553876de4236e2cd2587673f'] }], (e) => { const p = JSON.parse(nip44.decrypt(e.content, nip44.getConversationKey(skB, e.tags[0][1]))); console.log(e.id.slice(0,10), 'turn', p.turn, 'sent_ms', p.sent_ms, 'created_at', e.created_at, 'prev', p.prev_state_hash.slice(0,8)); });
await Promise.race([s.eosePromise, sleep(8000)]); process.exit(0);

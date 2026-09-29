import { Relay, sleep } from './common.mjs';
const [url, filterJson] = process.argv.slice(2);
const r = await new Relay(url, () => {}).connect(); const ids = [];
const s = r.sub('q', [JSON.parse(filterJson)], (e) => ids.push(e.id.slice(0, 8) + ':k' + e.kind));
const res = await Promise.race([s.eosePromise, sleep(10000).then(() => 'timeout')]);
console.log(url, filterJson.slice(0, 90), '->', res, ids.length, 'events', ids.join(' ')); process.exit(0);

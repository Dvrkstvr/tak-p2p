// R-004: which event kinds can carry moves? For each kind: publish OK?, live fanout to #p subscriber?, history by #p / #g on a fresh connection?
// Content is NIP-44 ciphertext for every kind except 1059 (gift wrap built by nip59).
import fs from 'fs';
import { Relay, RELAYS, finalizeEvent, generateSecretKey, getPublicKey, nip44, nip59, hex, sleep, now } from './common.mjs';

const only = process.argv[2];
const KINDS = (process.argv[3] || '9999,30078').split(',').map(Number);
const out = {};
for (const url of RELAYS.filter((u) => !only || u.includes(only))) {
  const log = (s) => console.log(`[${url}] ${s}`);
  const skA = generateSecretKey(), pkA = getPublicKey(skA), skB = generateSecretKey(), pkB = getPublicKey(skB);
  const tag = 'tak-' + hex(skA).slice(0, 8);
  const rows = out[url] = {};
  let a, b, c;
  try { a = await new Relay(url, log).connect(); b = await new Relay(url, log).connect(); } catch (e) { log('CONNECT FAIL ' + e.message); out[url] = { error: e.message }; continue; }
  const got = new Map();
  for (const k of KINDS) {
    const t0 = now();
    const subId = 'k' + k;
    const s = b.sub(subId, [{ kinds: [k], '#p': [pkB], since: t0 - 5 }], (e, at) => got.set(e.id, at));
    const eb = await Promise.race([s.eosePromise, sleep(8000).then(() => 'EOSE-timeout')]);
    let evt;
    const convKey = nip44.getConversationKey(skA, pkB);
    if (k === 1059) evt = nip59.wrapEvent({ kind: 14, created_at: now(), tags: [['p', pkB], ['g', tag]], content: '{"turn":1}' }, skA, pkB);
    else evt = finalizeEvent({ kind: k, created_at: now(), tags: [['p', pkB], ['g', tag], ['d', tag]], content: nip44.encrypt('{"turn":1}', convKey) }, skA);
    const ts = Date.now();
    const ok = await a.publish(evt);
    await sleep(2500);
    const live = got.has(evt.id) ? got.get(evt.id) - ts : null;
    // fresh connection history
    c = await new Relay(url, log).connect();
    const q = async (f) => { const ids = []; const s2 = c.sub('h' + Math.random(), f, (e) => ids.push(e.id)); const r = await Promise.race([s2.eosePromise, sleep(8000).then(() => 'EOSE-timeout')]); return { r, has: ids.includes(evt.id) }; };
    const byP = await q({ kinds: [k], '#p': [pkB] });
    const byG = await q({ kinds: [k], '#g': [tag] });
    const byAuthor = await q({ kinds: [k], authors: [pkA] });
    c.close();
    rows[k] = { sub_eose: eb, publish_ok: ok.ok, publish_msg: ok.msg, publish_ms: ok.ms ?? null, live_ms: live, hist_by_p: byP.has, hist_by_p_status: byP.r, hist_by_g: byG.has, hist_by_g_status: byG.r, hist_by_author: byAuthor.has, hist_by_author_status: byAuthor.r };
    log(`kind ${k}: ` + JSON.stringify(rows[k]));
    b.unsub(subId);
  }
  a.close(); b.close();
}
fs.writeFileSync(`probe-kinds-${only || 'all'}.json`, JSON.stringify(out, null, 2));
process.exit(0);

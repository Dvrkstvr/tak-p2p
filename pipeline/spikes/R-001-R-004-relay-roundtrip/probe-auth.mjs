// R-004 follow-up: does NIP-42 AUTH as the p-tag recipient unlock reading kind 4 / 1059 on relays that require it (damus)?
import { Relay, nip11, finalizeEvent, generateSecretKey, getPublicKey, nip44, nip59, hex, sleep, now } from './common.mjs';

const url = process.argv[2] || 'wss://relay.damus.io';
const log = (s) => console.log(`[${url}] ${s}`);
const skA = generateSecretKey(), pkA = getPublicKey(skA), skB = generateSecretKey(), pkB = getPublicKey(skB);
const tag = 'tak-spike-' + hex(skA).slice(0, 8);
const t0 = now();

// subscribe, and if the relay answers CLOSED auth-required, AUTH with sk and retry once
async function subAuth(r, sk, id, filters, onEvent) {
  let s = r.sub(id, filters, onEvent);
  let res = await Promise.race([s.eosePromise, sleep(8000).then(() => 'TIMEOUT')]);
  if (typeof res === 'string' && res.startsWith('CLOSED:') && res.includes('auth-required')) {
    const a = await r.authenticate(sk);
    log(`  auth -> ${JSON.stringify({ ok: a.ok, msg: a.msg })}`);
    r.subs.delete(id);
    s = r.sub(id, filters, onEvent);
    res = await Promise.race([s.eosePromise, sleep(8000).then(() => 'TIMEOUT')]);
  }
  return res;
}

const a = await new Relay(url, log).connect(), b = await new Relay(url, log).connect();
const got = [];
const rB = await subAuth(b, skB, 'live', [{ kinds: [4, 1059], '#p': [pkB], since: t0 - 5 }], (e, at) => got.push({ id: e.id, kind: e.kind, at }));
log('B live sub (after auth if needed): ' + rB + ' authed=' + b.authed);
await sleep(300);
const convKey = nip44.getConversationKey(skA, pkB);
const e4 = finalizeEvent({ kind: 4, created_at: now(), tags: [['p', pkB], ['g', tag]], content: nip44.encrypt('{"turn":1}', convKey) }, skA);
const ts4 = Date.now(); const ok4 = await a.publish(e4); log('kind4 OK: ' + JSON.stringify(ok4));
const wrap = nip59.wrapEvent({ kind: 14, created_at: now(), tags: [['p', pkB], ['g', tag]], content: '{"turn":1}' }, skA, pkB);
const ts1059 = Date.now(); const ok1059 = await a.publish(wrap); log('kind1059 OK: ' + JSON.stringify(ok1059));
await sleep(3000);
const l = (id, ts) => { const x = got.find((y) => y.id === id); return x ? x.at - ts : null; };
log('live latency ms after auth: ' + JSON.stringify({ k4: l(e4.id, ts4), k1059: l(wrap.id, ts1059) }));

// Fresh connection, AUTH as recipient B: history
const c = await new Relay(url, log).connect();
const ids = [];
const rC = await subAuth(c, skB, 'h1', [{ kinds: [4, 1059], '#p': [pkB] }], (e) => ids.push(e.id));
log(`history as B: ${rC}; k4 present=${ids.includes(e4.id)} k1059 present=${ids.includes(wrap.id)}`);
// history as a DIFFERENT authed identity (should be denied/empty: proves relay auth-gates by p-tag)
const skX = generateSecretKey(); const d = await new Relay(url, log).connect(); const idsX = [];
const rD = await subAuth(d, skX, 'h2', [{ kinds: [4, 1059], '#p': [pkB] }], (e) => idsX.push(e.id));
log(`history as stranger X asking for B's p-tag: ${rD}; k4 present=${idsX.includes(e4.id)} (relay auth policy)`);
// history by #g tag only (non-p filter) as B
const e = await new Relay(url, log).connect(); const idsG = [];
const rE = await subAuth(e, skB, 'h3', [{ kinds: [4], '#g': [tag] }], (x) => idsG.push(x.id));
log(`history kind4 by #g as B: ${rE}; present=${idsG.includes(e4.id)}`);
[a, b, c, d, e].forEach((x) => x.close());
process.exit(0);

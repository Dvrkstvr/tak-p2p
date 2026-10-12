// R-004 probe: for each default relay, NIP-11, publish kind 4 (p+g tags), 1059 gift wrap, 20001 ephemeral, live fanout, history refetch.
import fs from 'fs';
import { Relay, RELAYS, nip11, finalizeEvent, generateSecretKey, getPublicKey, nip44, nip59, hex, sleep, now } from './common.mjs';

const out = {};
const only = process.argv[2];
for (const url of RELAYS.filter((u) => !only || u.includes(only))) {
  const R = { url }; out[url] = R; const log = (s) => console.log(`[${url}] ${s}`);
  R.nip11 = await nip11(url);
  const lim = R.nip11.limitation || {};
  log('NIP-11: ' + JSON.stringify({ name: R.nip11.name, software: R.nip11.software, version: R.nip11.version, supported_nips: R.nip11.supported_nips, limitation: lim, retention: R.nip11.retention, fees: R.nip11.fees }));
  const skA = generateSecretKey(), pkA = getPublicKey(skA);
  const skB = generateSecretKey(), pkB = getPublicKey(skB);
  const tag = 'tak-spike-' + hex(skA).slice(0, 8);      // unique game tag per run
  const t0 = now();
  let a, b;
  try { a = await new Relay(url, log).connect(); b = await new Relay(url, log).connect(); } catch (e) { R.error = e.message; log('CONNECT FAIL ' + e.message); continue; }
  R.connectMs = [a.connectMs, b.connectMs];

  // B subscribes live for all 3 kinds
  const got = { k4: [], k1059: [], k20001: [] };
  const sB = b.sub('live', [
    { kinds: [4], '#p': [pkB], since: t0 - 5 },
    { kinds: [1059], '#p': [pkB], since: t0 - 3 * 86400 },
    { kinds: [20001], '#t': ['tak_quickplay_' + tag], since: t0 - 5 }
  ], (e, at) => { (e.kind === 4 ? got.k4 : e.kind === 1059 ? got.k1059 : got.k20001).push({ id: e.id, at }); });
  const eose = await Promise.race([sB.eosePromise, sleep(6000).then(() => 'EOSE-timeout')]);
  R.liveSubEose = eose; log('live sub EOSE: ' + eose);
  await sleep(300);

  // kind 4, NIP-44 ciphertext, tags p + g
  const convKey = nip44.getConversationKey(skA, pkB);
  const e4 = finalizeEvent({ kind: 4, created_at: now(), tags: [['p', pkB], ['g', tag]], content: nip44.encrypt(JSON.stringify({ game_id: tag, turn: 1, ptn: 'a1' }), convKey) }, skA);
  const tSend4 = Date.now(); R.k4 = await a.publish(e4); log('kind4 -> ' + JSON.stringify(R.k4));

  // NIP-59 gift wrap (NIP-17 style: rumor kind 14 -> seal 13 -> wrap 1059)
  const rumor = { kind: 14, created_at: now(), tags: [['p', pkB], ['g', tag]], content: JSON.stringify({ game_id: tag, turn: 1, ptn: 'a1' }) };
  const wrap = nip59.wrapEvent(rumor, skA, pkB);
  const tSend1059 = Date.now(); R.k1059 = await a.publish(wrap); log('kind1059 -> ' + JSON.stringify(R.k1059));

  // ephemeral 20001
  const e20 = finalizeEvent({ kind: 20001, created_at: now(), tags: [['t', 'tak_quickplay_' + tag], ['board_size', '5'], ['client_version', '1.0']], content: 'ephemeral-key+relays' }, skA);
  const tSend20 = Date.now(); R.k20001 = await a.publish(e20); log('kind20001 -> ' + JSON.stringify(R.k20001));

  await sleep(3000);
  const lat = (arr, id, ts) => { const x = arr.find((y) => y.id === id); return x ? x.at - ts : null; };
  R.live = { k4: lat(got.k4, e4.id, tSend4), k1059: lat(got.k1059, wrap.id, tSend1059), k20001: lat(got.k20001, e20.id, tSend20) };
  log('live fanout latency ms (null = not delivered): ' + JSON.stringify(R.live));

  // History: fresh connection C, no live sub existed for it
  const c = await new Relay(url, log).connect();
  const q = async (name, f) => { const ev = []; const s = c.sub('h-' + name, f, (e) => ev.push(e.id)); const r = await Promise.race([s.eosePromise, sleep(6000).then(() => 'EOSE-timeout')]); c.unsub('h-' + name); return { eose: r, ids: ev }; };
  const hist = {};
  hist.k4_by_p = await q('k4p', { kinds: [4], '#p': [pkB] });
  hist.k4_by_g = await q('k4g', { kinds: [4], '#g': [tag] });
  hist.k4_by_author = await q('k4a', { kinds: [4], authors: [pkA] });
  hist.k1059_by_p = await q('wrap', { kinds: [1059], '#p': [pkB] });
  hist.k20001_by_t = await q('eph', { kinds: [20001], '#t': ['tak_quickplay_' + tag] });
  R.history = {
    k4_by_p: hist.k4_by_p.ids.includes(e4.id), k4_by_p_eose: hist.k4_by_p.eose,
    k4_by_g: hist.k4_by_g.ids.includes(e4.id), k4_by_g_eose: hist.k4_by_g.eose,
    k4_by_author: hist.k4_by_author.ids.includes(e4.id), k4_by_author_eose: hist.k4_by_author.eose,
    k1059_by_p: hist.k1059_by_p.ids.includes(wrap.id), k1059_by_p_eose: hist.k1059_by_p.eose,
    k20001_stored: hist.k20001_by_t.ids.includes(e20.id), k20001_eose: hist.k20001_by_t.eose
  };
  log('history refetch on fresh connection: ' + JSON.stringify(R.history));
  R.notices = [...a.notices, ...b.notices, ...c.notices]; R.closed = [...c.closedSubs.entries(), ...b.closedSubs.entries()]; R.authChallenge = a.authChallenge || b.authChallenge || c.authChallenge;
  a.close(); b.close(); c.close();
}
fs.writeFileSync(`probe-output-${only || 'all'}.json`, JSON.stringify(out, null, 2));
console.log('done');
process.exit(0);

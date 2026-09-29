// Minimal in-memory NIP-01 relay for offline validation of the spike harness (NOT the real platform).
// Verifies id + BIP-340 sig with nostr-tools (independent of the .NET signer). Regular events stored; ephemeral (20000-29999) fanned out only;
// addressable (30000-39999) latest per (kind,pubkey,d); filters: ids, authors, kinds, since, until, limit, #<letter>.
// Usage: node local-relay.mjs [port]
import { WebSocketServer } from 'ws';
import { verifyEvent } from 'nostr-tools/pure';

const port = Number(process.argv[2] || 7777);
const store = []; const conns = new Set(); let nextConn = 1;
const wss = new WebSocketServer({ port });
const ts = () => new Date().toISOString().slice(11, 23);
const matches = (f, e) => {
  if (f.ids && !f.ids.includes(e.id)) return false;
  if (f.authors && !f.authors.includes(e.pubkey)) return false;
  if (f.kinds && !f.kinds.includes(e.kind)) return false;
  if (f.since != null && e.created_at < f.since) return false;
  if (f.until != null && e.created_at > f.until) return false;
  for (const k of Object.keys(f)) if (k[0] === '#') { const vals = f[k]; if (!e.tags.some((t) => t[0] === k[1] && vals.includes(t[1]))) return false; }
  return true;
};
wss.on('connection', (ws) => {
  const c = { id: nextConn++, ws, subs: new Map() }; conns.add(c);
  const send = (o) => ws.send(JSON.stringify(o));
  ws.on('message', (raw) => {
    let m; try { m = JSON.parse(raw.toString()); } catch { return send(['NOTICE', 'invalid json']); }
    if (m[0] === 'EVENT') {
      const e = m[1];
      if (!verifyEvent(e)) { console.log(ts(), `c${c.id} EVENT k${e.kind} REJECT invalid sig/id`); return send(['OK', e.id, false, 'invalid: bad signature or id']); }
      const eph = e.kind >= 20000 && e.kind < 30000;
      if (!eph) {
        if (e.kind >= 30000 && e.kind < 40000) { const d = (e.tags.find((t) => t[0] === 'd') || [])[1] || ''; const i = store.findIndex((x) => x.kind === e.kind && x.pubkey === e.pubkey && ((x.tags.find((t) => t[0] === 'd') || [])[1] || '') === d); if (i >= 0) store.splice(i, 1); }
        if (store.some((x) => x.id === e.id)) return send(['OK', e.id, true, 'duplicate: already have this event']);
        store.push(e);
      }
      send(['OK', e.id, true, '']);
      for (const o of conns) for (const [sid, fs] of o.subs) if (fs.some((f) => matches(f, e))) o.ws.send(JSON.stringify(['EVENT', sid, e]));
      console.log(ts(), `c${c.id} EVENT k${e.kind} ${e.id.slice(0, 8)} stored=${!eph} total=${store.length}`);
    } else if (m[0] === 'REQ') {
      const [, sid, ...fs] = m; c.subs.set(sid, fs);
      let n = 0;
      for (const f of fs) { const hits = store.filter((e) => matches(f, e)).sort((a, b) => a.created_at - b.created_at); const lim = f.limit ? hits.slice(-f.limit) : hits; for (const e of lim) { send(['EVENT', sid, e]); n++; } }
      send(['EOSE', sid]); console.log(ts(), `c${c.id} REQ ${sid} ${JSON.stringify(fs)} -> ${n} stored events + EOSE`);
    } else if (m[0] === 'CLOSE') c.subs.delete(m[1]);
  });
  ws.on('close', () => { conns.delete(c); console.log(ts(), `c${c.id} closed`); });
});
console.log(ts(), `local relay listening ws://127.0.0.1:${port}`);

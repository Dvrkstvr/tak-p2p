// Shared helpers for the relay spike. Throwaway code. nostr-tools 2.x + ws.
import WebSocket from 'ws';
import fs from 'fs';
import { finalizeEvent, generateSecretKey, getPublicKey, verifyEvent } from 'nostr-tools/pure';
import * as nip44 from 'nostr-tools/nip44';
import * as nip59 from 'nostr-tools/nip59';
import { makeAuthEvent } from 'nostr-tools/nip42';
export { finalizeEvent, generateSecretKey, getPublicKey, verifyEvent, nip44, nip59 };

export const RELAYS = process.env.RELAYS ? process.env.RELAYS.split(',') : ['wss://relay.damus.io', 'wss://nos.lol', 'wss://relay.primal.net'];
export const hex = (u8) => Buffer.from(u8).toString('hex');
export const unhex = (h) => Uint8Array.from(Buffer.from(h, 'hex'));
export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
export const now = () => Math.floor(Date.now() / 1000);

// Minimal relay connection: tracks OK / NOTICE / CLOSED / AUTH / EOSE, event callbacks per subscription.
export class Relay {
  constructor(url, log = () => {}) {
    this.url = url; this.log = log; this.subs = new Map(); this.oks = new Map(); this.notices = []; this.authChallenge = null; this.closedSubs = new Map();
  }
  // connect with retries (public relays sometimes answer 503 / hang; retries counted in this.connectRetries)
  async connect(timeout = 15000, tries = 5) {
    this.connectRetries = 0;
    for (let i = 0; i < tries; i++) {
      try { return await this._connect1(timeout); } catch (e) {
        this.connectRetries++; this.log(`connect attempt ${i + 1} failed: ${e.message}`);
        if (i === tries - 1) throw e; await sleep(1500 * (i + 1));
      }
    }
  }
  _connect1(timeout) {
    return new Promise((resolve, reject) => {
      const t0 = Date.now();
      this.ws = new WebSocket(this.url);
      const to = setTimeout(() => reject(new Error('connect timeout ' + this.url)), timeout);
      this.ws.on('open', () => { this.opened = true; clearTimeout(to); this.connectMs = Date.now() - t0; resolve(this); });
      this.ws.on('error', (e) => { this.log('WS error ' + e.message); this.lastErr = e.message; if (!this.opened) { clearTimeout(to); reject(new Error(e.message)); } });
      this.ws.on('close', (c) => { this.log('WS close ' + c); this.closed = true; });
      this.ws.on('message', (d) => this._on(d.toString()));
    });
  }
  _on(raw) {
    let m; try { m = JSON.parse(raw); } catch { return; }
    const [t, a, b, c] = m;
    if (t === 'EVENT') { const s = this.subs.get(a); if (s) s.onEvent?.(b, Date.now()); }
    else if (t === 'EOSE') { const s = this.subs.get(a); if (s) { s.eose = true; s.onEose?.('EOSE'); } }
    else if (t === 'OK') { const w = this.oks.get(a); if (w) w({ ok: b, msg: c, at: Date.now() }); }
    else if (t === 'NOTICE') { this.notices.push(a); this.log('NOTICE ' + a); }
    else if (t === 'CLOSED') { this.closedSubs.set(a, b); const s = this.subs.get(a); if (s) s.onClosed?.(b); this.log(`CLOSED ${a} ${b}`); }
    else if (t === 'AUTH') { this.authChallenge = a; this.log('AUTH challenge ' + a); if (this.authSk) this._doAuth(); }
  }
  // NIP-42: once authSk is set, answer any AUTH challenge with a kind 22242 event; resolves with relay OK
  async authenticate(sk, waitChallengeMs = 1500) {
    this.authSk = sk;
    const t0 = Date.now();
    while (!this.authChallenge && Date.now() - t0 < waitChallengeMs) await sleep(50);
    if (!this.authChallenge) return { ok: null, msg: 'no AUTH challenge received' };
    return this._doAuth();
  }
  async _doAuth() {
    const evt = finalizeEvent(makeAuthEvent(this.url, this.authChallenge), this.authSk);
    const p = new Promise((res) => { this.oks.set(evt.id, res); setTimeout(() => res({ ok: null, msg: 'auth OK timeout' }), 5000); });
    this.send(['AUTH', evt]);
    const r = await p; this.log('AUTH result ' + JSON.stringify({ ok: r.ok, msg: r.msg })); this.authed = r.ok; return r;
  }
  send(o) { this.ws.send(JSON.stringify(o)); }
  publish(evt, timeout = 8000) {
    // audit trail of every event sent to any relay
    const rec = { at: new Date().toISOString(), relay: this.url, kind: evt.kind, id: evt.id, pubkey: evt.pubkey, bytes: JSON.stringify(evt).length, script: process.argv[1].replace(/^.*[\/]/, '') };
    const audit = () => { try { fs.appendFileSync('published-events.jsonl', JSON.stringify(rec) + String.fromCharCode(10)); } catch {} };
    return new Promise((resolve) => {
      const t0 = Date.now();
      const to = setTimeout(() => { this.oks.delete(evt.id); rec.ok = null; rec.msg = 'TIMEOUT'; audit(); resolve({ ok: null, msg: 'TIMEOUT no OK in ' + timeout + 'ms' }); }, timeout);
      this.oks.set(evt.id, (r) => { clearTimeout(to); this.oks.delete(evt.id); rec.ok = r.ok; rec.msg = r.msg; audit(); resolve({ ...r, ms: r.at - t0 }); });
      this.send(['EVENT', evt]);
    });
  }
  // subscribe; eosePromise resolves 'EOSE' or 'CLOSED:<msg>'
  sub(id, filters, onEvent) {
    const s = { onEvent, eose: false };
    s.eosePromise = new Promise((r) => { s.onEose = r; s.onClosed = (msg) => r('CLOSED:' + msg); });
    this.subs.set(id, s);
    this.send(['REQ', id, ...(Array.isArray(filters) ? filters : [filters])]);
    return s;
  }
  unsub(id) { try { this.send(['CLOSE', id]); } catch {} this.subs.delete(id); }
  close() { try { this.ws.close(); } catch {} }
}

export async function nip11(url) {
  const http = url.replace('wss://', 'https://');
  try {
    const r = await fetch(http, { headers: { Accept: 'application/nostr+json' } });
    return await r.json();
  } catch (e) { return { error: e.message }; }
}

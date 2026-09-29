// read-only reachability check for relay.primal.net (no events published)
import WebSocket from 'ws';
const t0 = Date.now(); const ws = new WebSocket('wss://relay.primal.net', { handshakeTimeout: 20000 });
ws.on('open', () => { console.log('open after', Date.now() - t0, 'ms'); ws.send(JSON.stringify(['REQ', 'x', { kinds: [9999], limit: 1 }])); });
ws.on('message', (d) => { console.log('msg after', Date.now() - t0, 'ms:', d.toString().slice(0, 200)); });
ws.on('error', (e) => console.log('error after', Date.now() - t0, 'ms:', e.message));
ws.on('close', (c) => console.log('close', c));
setTimeout(() => process.exit(0), 25000);

'use strict';
/*
 * Blockfall matchmaking + relay server.
 *
 * A tiny WebSocket service that pairs two waiting players and then RELAYS game
 * messages between them. Relaying (rather than P2P hole-punching) means it works
 * across any NAT/firewall with zero client configuration.
 *
 * Protocol:
 *   client -> server  (TEXT / JSON control):
 *     {"type":"queue","name":"Alice"}   enter matchmaking
 *     {"type":"cancel"}                  leave the queue
 *   server -> client  (TEXT / JSON):
 *     {"type":"queued"}                            waiting for an opponent
 *     {"type":"matched","isHost":true,             paired; host + shared seed
 *      "seed":"12345678901234567","rival":"Bob"}
 *     {"type":"opponent_left"}                     partner disconnected
 *   both directions (BINARY): opaque game bytes (Blockfall NetMessage) — the
 *     server forwards each binary frame verbatim to the current partner.
 *
 * The game simulation is deterministic from the shared seed; the server never
 * inspects gameplay, so it stays simple and cheap to host.
 */
const http = require('http');
const crypto = require('crypto');
const { WebSocketServer } = require('ws');

function randomSeed() {
  // 64-bit seed as a decimal string (JSON numbers can't hold it precisely).
  return BigInt('0x' + crypto.randomBytes(8).toString('hex')).toString();
}

function createMatchmaker(port = process.env.PORT || 8080) {
  const server = http.createServer((req, res) => {
    // Basic health check for load balancers / uptime pings.
    if (req.url === '/health') { res.writeHead(200); res.end('ok'); return; }
    res.writeHead(426); res.end('Upgrade Required');
  });

  const wss = new WebSocketServer({ server });
  let waiting = null; // a single socket waiting for an opponent (1v1)

  function send(ws, obj) {
    if (ws.readyState === ws.OPEN) ws.send(JSON.stringify(obj));
  }

  function pair(a, b) {
    a.partner = b; b.partner = a;
    const seed = randomSeed();
    send(a, { type: 'matched', isHost: true, seed, rival: b.playerName || 'Rival' });
    send(b, { type: 'matched', isHost: false, seed, rival: a.playerName || 'Rival' });
  }

  function onQueue(ws, name) {
    ws.playerName = (name || 'Player').toString().slice(0, 24);
    if (ws.partner) return; // already in a match
    if (waiting && waiting !== ws && waiting.readyState === ws.OPEN) {
      const opp = waiting; waiting = null;
      pair(opp, ws);
    } else {
      waiting = ws;
      send(ws, { type: 'queued' });
    }
  }

  function leaveQueue(ws) {
    if (waiting === ws) waiting = null;
  }

  function dropPartner(ws) {
    const p = ws.partner;
    if (p) {
      p.partner = null;
      ws.partner = null;
      send(p, { type: 'opponent_left' });
    }
  }

  wss.on('connection', (ws) => {
    ws.isAlive = true;
    ws.on('pong', () => { ws.isAlive = true; });

    ws.on('message', (data, isBinary) => {
      if (isBinary) {
        // Relay opaque game bytes straight to the partner.
        const p = ws.partner;
        if (p && p.readyState === ws.OPEN) p.send(data, { binary: true });
        return;
      }
      let msg;
      try { msg = JSON.parse(data.toString()); } catch { return; }
      if (msg.type === 'queue') onQueue(ws, msg.name);
      else if (msg.type === 'cancel') leaveQueue(ws);
    });

    ws.on('close', () => { leaveQueue(ws); dropPartner(ws); });
    ws.on('error', () => { leaveQueue(ws); dropPartner(ws); });
  });

  // Reap dead connections so a crashed client doesn't hold a queue slot forever.
  const heartbeat = setInterval(() => {
    wss.clients.forEach((ws) => {
      if (!ws.isAlive) { ws.terminate(); return; }
      ws.isAlive = false;
      ws.ping();
    });
  }, 30000);
  wss.on('close', () => clearInterval(heartbeat));

  server.listen(port);
  return {
    wss,
    server,
    get port() { return server.address() ? server.address().port : port; },
    close() { clearInterval(heartbeat); wss.close(); server.close(); },
  };
}

module.exports = { createMatchmaker, randomSeed };

if (require.main === module) {
  const mm = createMatchmaker();
  mm.server.on('listening', () =>
    console.log(`[blockfall] matchmaker listening on :${mm.port}`));
}

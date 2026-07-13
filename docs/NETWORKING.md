# Networking & Matchmaking

Blockfall's online versus has **two transports**, both driven by the same
`INetChannel` interface and the same deterministic game core:

| Mode | Transport | When to use |
| --- | --- | --- |
| **Direct connect** | ENet P2P (`NetPeer`), host/join by IP on port `7777` | Same Wi-Fi/LAN, or an internet host who can port-forward. No server needed. |
| **Quick Match** | WebSocket → matchmaking/relay server (`RelayChannel`) | Find any opponent online, across NAT, with zero configuration. Needs the server below running somewhere reachable. |

The match seed is shared so both clients simulate the same piece sequence; only
inputs/board-snapshots/attacks cross the wire. Swapping the transport never
touches gameplay code.

## Matchmaking / relay server

A tiny Node.js WebSocket service in [`server/`](../server) that **pairs two
waiting players and relays game frames** between them (relaying, not P2P
hole-punching, so it works behind any NAT).

### Run it

```bash
cd server
npm install          # one-time (ws)
npm start            # listens on :8080 (override with PORT=9000 npm start)
npm test             # integration test: pairing, shared seed, relay, disconnect
```

Point the game at it from the online lobby's **Quick Match** field, e.g.
`ws://192.168.0.10:8080` on a LAN, or `wss://matchmaker.yourdomain.com` in
production (put it behind TLS — Godot's `WebSocketPeer` speaks `wss://`).

### Protocol

- **Client → server** (JSON text): `{"type":"queue","name":"…"}`, `{"type":"cancel"}`
- **Server → client** (JSON text): `{"type":"queued"}`,
  `{"type":"matched","isHost":bool,"seed":"<u64 decimal>","rival":"…"}`,
  `{"type":"opponent_left"}`
- **Both directions** (binary): opaque `NetMessage` bytes, forwarded verbatim to
  the partner. The server never inspects gameplay.

### Deploying

The server is stateless and cheap. Any container/VM works:

```bash
PORT=8080 node matchmaker.js      # behind nginx/Caddy for TLS termination -> wss://
```

Scale horizontally by sharding on a room id later; for 1v1 quick match a single
process handles thousands of concurrent pairings. Health check: `GET /health`.

## Ranked anti-cheat: replay validation

Ranked score submissions can carry the run's `ReplayData`. The server (or a
validation worker) re-runs `ReplayValidator.Validate(replay)` — a deterministic
re-simulation from the seed + input stream — and only accepts the score if the
replay actually reproduces it. Because the core is deterministic and
cross-platform stable, a forged score has no legal input sequence that yields it.

`ReplayValidator` lives in `Blockfall.Core`, so the same check runs on the client
(pre-flight) and, ported or hosted as a .NET worker, on the server.

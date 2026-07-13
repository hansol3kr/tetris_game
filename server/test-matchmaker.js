'use strict';
/* Integration test for the matchmaker: pairing, shared seed, binary relay, and
 * disconnect notification. Run with `node test-matchmaker.js` (exit 0 = pass). */
const assert = require('assert');
const WebSocket = require('ws');
const { createMatchmaker } = require('./matchmaker');

const open = (ws) => new Promise((res, rej) => { ws.once('open', res); ws.once('error', rej); });

// Buffering reader: queues every message so none is lost between awaits.
function reader(ws) {
  const q = [], waiters = [];
  ws.on('message', (data, isBinary) => {
    const m = { data, isBinary };
    if (waiters.length) waiters.shift()(m); else q.push(m);
  });
  return () => q.length ? Promise.resolve(q.shift()) : new Promise((r) => waiters.push(r));
}
const json = (m) => JSON.parse(m.data.toString());

async function main() {
  const mm = createMatchmaker(0);
  await new Promise((r) => mm.server.on('listening', r));
  const url = `ws://127.0.0.1:${mm.port}`;
  let passed = 0;

  // --- Single client queues and waits -----------------------------------
  const solo = new WebSocket(url); await open(solo);
  const soloNext = reader(solo);
  solo.send(JSON.stringify({ type: 'queue', name: 'Solo' }));
  assert.strictEqual(json(await soloNext()).type, 'queued');
  passed++; console.log('✓ single client is queued');

  // --- Two clients get matched (one host, one not, same seed) -----------
  const a = new WebSocket(url); await open(a);
  const aNext = reader(a);
  a.send(JSON.stringify({ type: 'queue', name: 'Alice' }));
  const aMatched = json(await aNext());
  const soloMatched = json(await soloNext());
  assert.strictEqual(aMatched.type, 'matched');
  assert.strictEqual(soloMatched.type, 'matched');
  assert.notStrictEqual(aMatched.isHost, soloMatched.isHost, 'exactly one host');
  assert.strictEqual(aMatched.seed, soloMatched.seed, 'shared seed');
  assert.ok(/^\d+$/.test(aMatched.seed), 'seed is a decimal string');
  assert.strictEqual(aMatched.rival, 'Solo');
  assert.strictEqual(soloMatched.rival, 'Alice');
  passed++; console.log(`✓ paired with shared seed ${aMatched.seed}`);

  // --- Binary game frames relay to the partner --------------------------
  const payload = Buffer.from([1, 2, 3, 250, 0, 99]);
  a.send(payload, { binary: true });
  const relayed = await soloNext();
  assert.ok(relayed.isBinary, 'relayed as binary');
  assert.ok(Buffer.from(relayed.data).equals(payload), 'bytes forwarded verbatim');
  passed++; console.log('✓ binary frame relayed verbatim');

  // --- Disconnect notifies the partner ----------------------------------
  a.close();
  assert.strictEqual(json(await soloNext()).type, 'opponent_left');
  passed++; console.log('✓ partner notified on disconnect');

  solo.close();
  mm.close();
  console.log(`\nALL ${passed} MATCHMAKER TESTS PASSED`);
}

main().then(() => process.exit(0)).catch((e) => { console.error('FAIL:', e.message); process.exit(1); });

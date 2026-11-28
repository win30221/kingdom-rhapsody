import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { setImmediate } from 'node:timers/promises';
import test from 'node:test';
import { runInNewContext } from 'node:vm';

const unity = new URL('../../unity/', import.meta.url);
const binary = new TextEncoder();
const browserScript = readFileSync(new URL('Assets/StreamingAssets/KingdomRhapsody/transport.js', unity));
const transport = await import('data:text/javascript;base64,' + browserScript.toString('base64'));
const bridgeScript = readFileSync(new URL(
  'Packages/com.realtime.transport/Runtime/Plugins/WebGL/RealtimeTransport.jslib', unity), 'utf-8');
const page = new URL('https://game.example.test/release/');
const modulePath = 'StreamingAssets/KingdomRhapsody/transport.js';

async function open(t, coalesce, server = 'wss://game.example.test/ws') {
  let socket;
  t.mock.method(globalThis, 'WebSocket', function (url) {
    socket = { url, send() {}, close() { this.closed = true; } };
    return socket;
  });
  const bridge = {};
  const memory = new Uint8Array(65536);
  const values = {};
  const context = {
    LibraryManager: { library: bridge },
    mergeInto: Object.assign,
    document: { baseURI: page.href }, location: page, URL, TextDecoder, performance,
    HEAPU8: memory, UTF8ToString: value => value,
    setValue: (ptr, value) => { values[ptr] = value; },
  };
  runInNewContext(bridgeScript, context);
  context.RBLab = bridge.$RBLab;
  bridge.$RBLab.load = async runtime => {
    assert.equal(bridge.$RBLab.runtimeURL(runtime), new URL(modulePath, page).href);
    return transport;
  };

  const handle = bridge.RB_Create();
  const hello = binary.encode('{"type":"queue"}');
  memory.set(hello);
  bridge.RB_OpenBinary(handle, server, 0, hello.length, modulePath, coalesce ? 1 : 0);
  await setImmediate();
  assert.equal(socket.url, server);
  socket.onopen();
  t.after(() => bridge.RB_Destroy(handle));

  return {
    socket,
    runtimeURL: bridge.$RBLab.runtimeURL,
    queue: () => bridge.$RBLab.states[handle].packets,
    receive: message => socket.onmessage({ data: binary.encode(JSON.stringify(message)).buffer }),
    drain: () => {
      const messages = [];
      const decoder = new TextDecoder();
      for (;;) {
        const size = bridge.RB_PollPacket(handle, 0, memory.length, 1, 2);
        if (size < 0) return messages;
        if (values[2]) messages.push({ error: values[2] });
        else messages.push(JSON.parse(decoder.decode(memory.subarray(0, size))));
      }
    },
  };
}

const match = 'battle';
const state = (tick, processedSeq, winner = -2) => ({
  type: 'state', match, replaceable: winner === -2,
  state: { tick, winner, processedSeq, energy: 5 - 2 * processedSeq },
});

test('the WebGL module URL is independent of the selected WebSocket', async t => {
  const connection = await open(t, true, 'wss://match.example.test/ws');
  assert.equal(connection.runtimeURL(modulePath), new URL(modulePath, page).href);
  assert.throws(() => connection.runtimeURL('https://match.example.test/transport.js'), /game origin/);
});

test('battle snapshots coalesce across a pause and the final packets survive socket closure', async t => {
  const connection = await open(t, true);
  connection.receive({ type: 'waiting' });
  connection.receive({ type: 'start', match, state: { tick: 0 } });
  for (let tick = 2; tick <= 240; tick += 2) {
    connection.receive({ type: 'effects', match, effects: [{ begin: tick, end: tick + 12 }] });
    connection.receive(state(tick, Number(tick >= 12)));
    if (tick === 12) connection.receive({ type: 'ack', match, seq: 1, result: 0 });
  }
  assert.equal(connection.socket.closed, undefined);
  assert.equal(connection.queue().filter(packet => packet.snapshot).length, 1);
  const resumed = connection.drain();
  assert.deepEqual(resumed.slice(0, 3).map(packet => packet.type), ['waiting', 'start', 'ack']);
  assert.deepEqual(resumed.at(-1), state(240, 1));
  assert.ok(resumed.some(packet => packet.effects?.[0].end === 252));
  assert.ok(resumed.every(packet => !packet.effects || packet.effects[0].end >= 234));

  connection.receive(state(242, 1));
  const finalEffect = { type: 'effects', match, effects: [{ begin: 244, end: 256 }] };
  const final = state(244, 2, 0);
  const reply = { type: 'ack', match, seq: 2, result: 0 };
  connection.receive(finalEffect);
  connection.receive(final);
  connection.receive(reply);
  connection.socket.onclose();
  assert.deepEqual(connection.drain(), [finalEffect, final, reply, { error: 1 }]);
});

test('a full battle queue retains critical packets ahead of effects', async t => {
  const connection = await open(t, true);
  const reply = { type: 'ack', match, seq: 1, result: 0 };
  const final = state(244, 1, 0);
  connection.receive(reply);
  for (let i = 0; i < 80; i++)
    connection.receive({ type: 'effects', match, effects: [{ begin: 240, end: 252 }] });
  connection.receive(final);
  connection.socket.onclose();
  assert.equal(connection.queue().length, 64);
  const packets = connection.drain();
  assert.deepEqual(packets[0], reply);
  assert.deepEqual(packets.slice(-2), [final, { error: 1 }]);
});

test('ordinary binary clients retain all their packets in order', async t => {
  const connection = await open(t, false);
  connection.receive(state(2, 0));
  connection.receive(state(4, 0));
  assert.deepEqual(connection.drain(), [state(2, 0), state(4, 0)]);
});

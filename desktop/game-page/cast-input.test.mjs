import test from 'node:test';
import assert from 'node:assert/strict';
import { CastInput, FlightNotices } from './cast-input.mjs';

function fixture() {
  const context = { lease: 'first-grant', allowed: true }, messages = [];
  let transport = 0;
  const input = new CastInput({ readContext: () => context, post: m => messages.push(m), newTransportId: () => `transport-${++transport}` });
  return { input, context, messages, actions: () => messages.map(m => m.action) };
}

test('quick click pairs down/up without waiting for a host begin reply', () => {
  const f = fixture();
  assert.equal(f.input.pointer(3, 1, true), true);
  assert.equal(f.input.pointer(3, 0), true);
  assert.deepEqual(f.actions(), ['begin', 'release']);
  assert.equal(f.messages[0].inputSequence, f.messages[1].inputSequence);
  assert.equal(f.input.state().phase, 'prepared');
});
test('normal pointerup and automatic lost capture do not cancel release', () => {
  const f = fixture();
  f.input.pointer(3, 1, true); f.input.pointer(3, 0); f.input.pointerLost(3);
  assert.deepEqual(f.actions(), ['begin', 'release']);
});
test('right chord arriving as a move cancels and never fires on later up', () => {
  const f = fixture();
  f.input.pointer(3, 1, true); f.input.pointer(3, 3); f.input.pointer(3, 1); f.input.pointer(3, 0);
  assert.deepEqual(f.actions(), ['begin', 'cancel']);
});
test('other pointer and duplicated down/up cannot take over current gesture', () => {
  const f = fixture();
  f.input.pointer(3, 1, true); f.input.pointer(4, 1, true); f.input.pointer(3, 1, true);
  f.input.pointer(4, 0); f.input.pointer(3, 0); f.input.pointer(3, 0);
  assert.deepEqual(f.actions(), ['begin', 'release']);
});
test('lost capture while charging and explicit pointercancel cancel once', () => {
  for (const method of ['pointerLost', 'pointerCancelled']) {
    const f = fixture();
    f.input.pointer(3, 1, true); f.input[method](4); f.input[method](3); f.input[method](3); f.input.pointer(3, 0);
    assert.deepEqual(f.actions(), ['begin', 'cancel']);
  }
});
test('lease replacement discards held input and sequence never aliases old grant', () => {
  const f = fixture();
  f.input.pointer(3, 1, true); f.context.lease = 'second-grant'; f.input.reconcile();
  f.input.pointer(3, 1); f.input.pointer(3, 0);
  assert.deepEqual(f.actions(), ['begin']);
  f.input.pointer(3, 1, true); f.input.pointer(3, 0);
  assert.deepEqual(f.messages.map(m => [m.action, m.inputSequence, m.lease]), [
    ['begin', '1', 'first-grant'], ['begin', '2', 'second-grant'], ['release', '2', 'second-grant'],
  ]);
});
test('cancel keeps original lease even if called after a new grant', () => {
  const f = fixture();
  f.input.begin(); f.context.lease = 'second-grant'; f.input.cancel();
  assert.equal(f.messages[1].lease, 'first-grant');
});
test('pause, menu or portrait eligibility prevents down and late up', () => {
  const f = fixture();
  f.context.allowed = false; assert.equal(f.input.begin(), false);
  f.context.allowed = true; f.input.begin(); f.context.allowed = false;
  assert.equal(f.input.release(), false); f.context.allowed = true;
  assert.equal(f.input.release(), false); assert.deepEqual(f.actions(), ['begin']);
});
test('button and physical pointer releases stay paired with their own source', () => {
  const f = fixture();
  f.input.begin(); assert.equal(f.input.pointer(3, 0), false); f.input.release();
  assert.deepEqual(f.actions(), ['begin', 'release']);
});
test('failed begin reply releases pending input; old reply cannot cancel newer input', () => {
  const f = fixture();
  f.input.begin(); const old = f.messages[0].transportId;
  f.input.reply({ transportId: old, code: 'bait-empty' }); assert.equal(f.input.state(), null);
  f.input.begin(); f.input.reply({ transportId: old, code: 'control-required' });
  assert.equal(f.input.state().inputSequence, '2');
});
test('accepted delayed begin reply does not undo already sent release', () => {
  const f = fixture();
  f.input.begin(); f.input.release(); f.input.reply({ transportId: f.messages[0].transportId, code: 'charging' });
  assert.equal(f.input.state().phase, 'prepared');
  f.input.reply({ transportId: f.messages[1].transportId, code: 'prepared' });
  assert.equal(f.input.state().phase, 'prepared');
});
test('only matching terminal host state clears active input; no host event resends', () => {
  const f = fixture();
  f.input.begin(); f.input.release();
  f.input.hostState({ inputSequence: '0', phase: 'committed' }); assert.notEqual(f.input.state(), null);
  f.input.hostState({ inputSequence: '1', phase: 'committed' }); assert.equal(f.input.state(), null);
  f.input.hostState({ inputSequence: '1', phase: 'committed' }); assert.deepEqual(f.actions(), ['begin', 'release']);
});
test('cancel before begin reply stays cancelled; resuming needs a new press', () => {
  const f = fixture();
  f.input.begin(); const first = f.messages[0]; f.input.cancel();
  f.input.reply({ transportId: first.transportId, code: 'charging' });
  f.input.release(); assert.equal(f.input.state(), null); assert.deepEqual(f.actions(), ['begin', 'cancel']);
});
test('page input message contains intention and identity, never time/power/target/resources', () => {
  const f = fixture(); f.input.begin();
  assert.deepEqual(Object.keys(f.messages[0]).sort(), ['action', 'inputSequence', 'kind', 'lease', 'transportId']);
  for (const [pointer, buttons] of [[NaN, 1], [1, -1], [1, 32], [1, 1.5]]) assert.equal(f.input.pointer(pointer, buttons, true), false);
});
test('observer accepts new interaction document sequence reuse by owner emission revision', () => {
  const notices = new FlightNotices();
  const first = { revision: 1, inputSequence: '9007199254740993', sessionId: '10000000-0000-0000-0000-000000000001', powerPermille: 500 };
  const second = { revision: 2, inputSequence: '1', sessionId: '10000000-0000-0000-0000-000000000002', powerPermille: 150 };
  assert.equal(notices.apply(first), true); assert.equal(notices.apply(second), true);
  assert.equal(notices.apply(first), false); assert.equal(notices.apply(second), false);
  assert.equal(notices.count, 2); assert.equal(notices.last.sessionId, second.sessionId);
});
test('malformed flight notices do not advance presentation highwater', () => {
  const notices = new FlightNotices();
  const valid = { revision: 1, inputSequence: '1', sessionId: '10000000-0000-0000-0000-000000000001', powerPermille: 500 };
  for (const bad of [{ revision: 0 }, { revision: 1.5 }, { inputSequence: '01' }, { inputSequence: '9223372036854775808' }, { sessionId: 'wrong' }, { sessionId: '00000000-0000-0000-0000-000000000000' }, { powerPermille: 1001 }]) {
    assert.equal(notices.apply({ ...valid, ...bad }), false);
  }
  assert.equal(notices.revision, 0); assert.equal(notices.apply(valid), true);
});

test('aimed casts freeze only the selected three integers and keep release and cancel unchanged', () => {
  const f = fixture(); Object.assign(f.context, { requiresAim: true, geometryVersion: 2 });
  const aim = { u: 1234, v: 5678, geometryVersion: 2 };
  assert.equal(f.input.pointer(3, 1, true, aim), true);
  aim.u = 9; f.messages[0].aim.v = 3; const state = f.input.state(); state.aim.geometryVersion = 8;
  assert.deepEqual(f.input.state().aim, { u: 1234, v: 5678, geometryVersion: 2 });
  assert.equal(f.input.pointer(3, 0), true);
  assert.deepEqual(Object.keys(f.messages[1]).sort(), ['action', 'inputSequence', 'kind', 'lease', 'transportId']);
  assert.equal(f.messages[1].inputSequence, '1'); assert.equal(f.messages[1].action, 'release');
  f.input.hostState({ inputSequence: '1', phase: 'committed' });
  assert.equal(f.input.begin('button', { u: 5000, v: 5000, geometryVersion: 2 }), true); f.input.cancel();
  assert.equal('aim' in f.messages.at(-1), false); assert.equal(f.messages.at(-1).action, 'cancel');
});

test('aim-required mode rejects missing, partial, injected, out-of-range and stale geometry selections', () => {
  const f = fixture(); Object.assign(f.context, { requiresAim: true, geometryVersion: 1 });
  const valid = { u: 0, v: 10000, geometryVersion: 1 };
  for (const aim of [null, {}, { ...valid, u: -1 }, { ...valid, v: 10001 }, { ...valid, u: .5 }, { ...valid, v: NaN },
    { ...valid, geometryVersion: '1' }, { ...valid, geometryVersion: 0 }, { ...valid, geometryVersion: 2 },
    { ...valid, geometryVersion: Number.MAX_SAFE_INTEGER + 1 }, { ...valid, powerPermille: 1000 },
    Object.assign(Object.create({ geometryVersion: 1 }), { u: 0, v: 1, x: 1 })]) {
    assert.equal(f.input.begin('button', aim), false);
  }
  assert.deepEqual(f.messages, []); assert.equal(f.input.begin('button', valid), true);
  const legacy = fixture(); assert.equal(legacy.input.begin('button', valid), false); assert.equal(legacy.input.begin(), true);
  assert.equal('aim' in legacy.messages[0], false);
});

test('geometry or aim-mode changes invalidate old gestures without retargeting their release', () => {
  for (const change of [{ geometryVersion: 2 }, { requiresAim: false }, { lease: 'second-grant' }, { allowed: false }]) {
    const f = fixture(); Object.assign(f.context, { requiresAim: true, geometryVersion: 1 });
    f.input.pointer(3, 1, true, { u: 1, v: 2, geometryVersion: 1 }); Object.assign(f.context, change);
    f.input.reconcile(); assert.equal(f.input.state(), null); assert.equal(f.input.pointer(3, 0), false);
    assert.deepEqual(f.actions(), ['begin']);
  }
  const f = fixture(); f.input.begin(); Object.assign(f.context, { requiresAim: true, geometryVersion: 1 });
  assert.equal(f.input.release(), false); assert.deepEqual(f.actions(), ['begin']);
});

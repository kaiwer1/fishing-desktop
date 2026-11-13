import test from 'node:test';
import assert from 'node:assert/strict';
import { CampView, CampInput } from './camp-view.mjs';

const state = extra => ({ available: true, reason: null, lease: 'first', phase: 'idle', elapsedMilliseconds: 0,
  requiredMilliseconds: 60000, rewardMinor: '800', stateSequence: '12', ...extra });
const packet = (revision = 1, extra = {}, documentId = 'current') => ({ kind: 'game-camp-state', documentId, revision, state: state(extra) });
function fixture() {
  const context = { documentId: 'current', lease: 'first', allowed: true, menuOpen: true, sessionId: null, pendingResult: false, faulted: false };
  const messages = []; let revision = 1, next = 0;
  const input = new CampInput({ readContext: () => context, post: m => messages.push(m), newTransportId: () => `camp-${++next}` }); input.hostState(packet());
  return { input, context, messages,
    project(extra = {}) { return input.hostState(packet(++revision, { lease: context.lease, ...extra }, context.documentId)); },
    reply(code = 'camp-work-started') { return input.reply({ kind: 'game-reply', documentId: context.documentId, transportId: messages.at(-1).transportId, code }); }
  };
}

test('camp accepts current host progress only with fixed policy and exact sequence; copies the display whitelist', () => {
  const view = new CampView('current');
  for (const bad of [{ rewardMinor: '801' }, { requiredMilliseconds: 59999 }, { elapsedMilliseconds: -1 }, { elapsedMilliseconds: 60001 },
    { elapsedMilliseconds: 0.5 }, { phase: 'paid' }, { available: true, lease: null }, { stateSequence: '01' }, { stateSequence: '9223372036854775808' },
    { stateSequence: null }, { phase: 'unknown' }]) assert.equal(view.apply(packet(1, bad)), false);
  const p = packet(1, { phase: 'working', elapsedMilliseconds: 999, stateSequence: '9007199254740993', jobId: 'private' });
  assert.equal(view.apply(p), true); p.state.elapsedMilliseconds = 60000;
  assert.equal(view.state().elapsedMilliseconds, 999); assert.equal(view.state().stateSequence, '9007199254740993'); assert.equal('jobId' in view.state(), false);
  for (const p of [packet(), packet(2, {}, 'old'), { ...packet(2), kind: 'game-shop-state' }]) assert.equal(view.apply(p), false);
  assert.equal(view.apply(packet(2, { available: false, reason: 'camp-schema-required', lease: null, stateSequence: null })), true);
});

test('camp start and claim need fresh explicit gestures and reply then projection; page never sends rewards or duration', () => {
  const f = fixture(); assert.equal(f.input.claim(), false); assert.equal(f.input.start({ detail: 2 }), false); assert.equal(f.input.start({ repeat: true }), false);
  assert.equal(f.input.start(), true); assert.equal(f.input.start(), false);
  assert.deepEqual(f.messages[0], { kind: 'game-camp', documentId: 'current', lease: 'first', transportId: 'camp-1', action: 'start', inputSequence: '1' });
  f.project({ phase: 'ready', elapsedMilliseconds: 60000 }); assert.equal(f.input.claim(), false); f.reply(); assert.equal(f.input.claim(), false);
  f.project({ phase: 'ready', elapsedMilliseconds: 60000 }); assert.equal(f.messages.length, 1); assert.equal(f.input.claim(), true);
  assert.deepEqual(Object.keys(f.messages[1]).sort(), ['kind', 'documentId', 'lease', 'transportId', 'action', 'inputSequence'].sort());
  assert.equal(f.messages[1].inputSequence, '2');
});

test('camp known failure remains explicit retry, cancellation clears the round and no response auto-claims', () => {
  const f = fixture(); f.project({ phase: 'retry', elapsedMilliseconds: 60000 }); assert.equal(f.messages.length, 0);
  assert.equal(f.input.start(), false); assert.equal(f.input.claim(), true); f.reply('camp-work-not-written'); f.project({ phase: 'retry', elapsedMilliseconds: 60000 });
  assert.equal(f.messages.length, 1); assert.equal(f.input.cancel(), true); f.reply('camp-work-cancelled'); f.project({ phase: 'cancelled' });
  assert.equal(f.input.claim(), false); assert.equal(f.input.start(), true); assert.deepEqual(f.messages.map(m => m.inputSequence), ['1', '2', '3']);
});

test('camp authority changes discard work handles and unknown reply or projection freezes further actions', () => {
  for (const changes of [{ allowed: false }, { menuOpen: false }, { sessionId: 'active' }, { pendingResult: true }, { lease: 'second' }]) {
    const f = fixture(); f.project({ phase: 'ready', elapsedMilliseconds: 60000 }); Object.assign(f.context, changes); f.input.reconcile();
    assert.equal(f.input.claim(), false); assert.equal(f.input.state().phase, null);
  }
  for (const mode of ['reply', 'projection']) {
    const f = fixture(); f.project({ phase: 'ready', elapsedMilliseconds: 60000 }); f.input.claim();
    if (mode === 'reply') f.reply('camp-work-save-unknown');
    else f.project({ phase: 'unknown', available: false, reason: 'camp-work-save-unknown', lease: null, stateSequence: null });
    f.project(); assert.equal(f.input.state().faulted, true); assert.equal(f.input.start(), false); assert.equal(f.messages.length, 1);
  }
});

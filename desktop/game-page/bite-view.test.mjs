import test from 'node:test';
import assert from 'node:assert/strict';
import { BiteView, StrikeInput } from './bite-view.mjs';

const session = '10000000-0000-0000-0000-000000000001';
const encounter = '20000000-0000-0000-0000-000000000001';
const nextEncounter = '20000000-0000-0000-0000-000000000002';
const state = extra => ({ sessionId: session, encounterId: encounter, phase: 'nibbling', reason: null,
  stepIndex: '50', searchEpoch: '1', baitAttached: true, bobberOffsetMm: -20, bobberLateralMm: 30, ...extra });
const packet = (revision = 1, extra = {}) => ({ kind: 'game-bite-state', documentId: 'current', revision, state: state(extra) });
function fixture() {
  const context = { lease: 'first', allowed: true, sessionId: session, encounterId: encounter, phase: 'strikeable' }, messages = [];
  let transport = 0;
  const input = new StrikeInput({ readContext: () => context, post: m => messages.push(m), newTransportId: () => `strike-${++transport}` });
  return { input, context, messages };
}

test('bite accepts only its document and newer valid revision', () => {
  const view = new BiteView('current'); assert.equal(view.apply(packet()), true);
  for (const p of [packet(), packet(0), { ...packet(2), documentId: 'old' }, { ...packet(2), kind: 'game-flight-state' }]) assert.equal(view.apply(p), false);
  assert.equal(view.revision, 1); assert.equal(view.apply(packet(2)), true);
});
test('bite projection keeps large host counters as strings without extrapolation', () => {
  const view = new BiteView('current'); view.apply(packet(1, { stepIndex: '9223372036854775807', searchEpoch: '9007199254740993' }));
  const initial = view.diagram(); for (let n = 0; n < 1000; n++) assert.deepEqual(view.diagram(), initial);
  assert.equal(view.state().stepIndex, '9223372036854775807'); assert.equal(view.state().searchEpoch, '9007199254740993');
});
test('bite ignores malformed data without consuming revision', () => {
  const view = new BiteView('current');
  for (const bad of [{ phase: 'caught' }, { stepIndex: 50 }, { stepIndex: '-1' }, { stepIndex: '00' }, { stepIndex: '9223372036854775808' },
    { searchEpoch: '1e2' }, { bobberOffsetMm: NaN }, { bobberOffsetMm: 301 }, { bobberLateralMm: -301 }, { bobberLateralMm: 1.5 },
    { baitAttached: 1 }, { reason: {} }, { reason: 'x'.repeat(129) }, { sessionId: null }, { encounterId: null },
    { encounterId: '00000000-0000-0000-0000-000000000000' }, { phase: 'idle', sessionId: null }]) assert.equal(view.apply(packet(1, bad)), false);
  assert.equal(view.revision, 0); assert.equal(view.apply(packet()), true);
});
test('idle clears diagram, waiting permits absent encounter, active target requires one', () => {
  const view = new BiteView('current');
  assert.equal(view.apply(packet(1, { phase: 'searching', encounterId: null })), true);
  assert.equal(view.apply(packet(2, { phase: 'strikeable', encounterId: null })), false);
  assert.equal(view.apply(packet(2, { phase: 'idle', sessionId: null, encounterId: null })), true);
  assert.equal(view.diagram(), null);
});
test('only supplied display fields survive incoming and outgoing mutation', () => {
  const view = new BiteView('current'), message = packet(1, { speciesId: 'secret', massMg: '999', openStep: '60' }); view.apply(message);
  message.state.bobberOffsetMm = 200; const copy = view.state(); copy.phase = 'hooked';
  assert.equal(view.state().bobberOffsetMm, -20); assert.equal(view.state().phase, 'nibbling');
  assert.equal('speciesId' in view.state(), false); assert.equal('massMg' in view.state(), false); assert.equal('openStep' in view.state(), false);
});
test('new document reconstructs current drift and terminal display without replay', () => {
  const view = new BiteView('current'), reloaded = new BiteView('new');
  view.apply(packet(15)); reloaded.apply({ ...packet(1), documentId: 'new' }); assert.deepEqual(reloaded.diagram(), view.diagram());
  view.apply(packet(16, { phase: 'retrieve-required', baitAttached: false })); assert.equal(view.diagram().baitAttached, false);
  view.apply(packet(17, { phase: 'ended' })); assert.equal(view.diagram(), null);
});
test('float displacement stays bounded and positive height rises on screen', () => {
  const view = new BiteView('current'); view.apply(packet(1, { bobberOffsetMm: 300, bobberLateralMm: -300 }));
  assert.deepEqual(view.diagram(), { x: 60, y: 50, submerged: false, phase: 'nibbling', baitAttached: true });
  view.apply(packet(2, { bobberOffsetMm: -300, bobberLateralMm: 300 })); assert.equal(view.diagram().x, 300); assert.equal(view.diagram().y, 200); assert.equal(view.diagram().submerged, true);
});
test('fresh right down emits exact identity only, not timing fish or rewards', () => {
  const f = fixture(); assert.equal(f.input.mouseDown(2, 2), true);
  assert.deepEqual(f.messages, [{ kind: 'game-strike', lease: 'first', transportId: 'strike-1', sessionId: session, encounterId: encounter, inputSequence: '1' }]);
  assert.equal(f.input.mouseDown(2, 2), false); assert.equal(f.messages.length, 1);
});
test('holding through early reply or later strike window cannot auto-strike', () => {
  const f = fixture(); f.context.phase = 'nibbling'; f.input.mouseDown(2, 2);
  f.input.reply({ transportId: f.messages[0].transportId }); f.context.phase = 'strikeable';
  f.input.mouseButtons(2); assert.equal(f.input.mouseDown(2, 2), false); assert.equal(f.messages.length, 1);
  f.input.mouseButtons(0); assert.equal(f.input.mouseDown(2, 2), true); assert.equal(f.messages[1].inputSequence, '2');
});
test('held right during forbidden control remains suppressed after grant replacement', () => {
  const f = fixture(); f.context.allowed = false; assert.equal(f.input.mouseDown(2, 2), false);
  f.context.allowed = true; f.context.lease = 'second'; f.input.reconcile(); assert.equal(f.input.mouseDown(2, 2), false);
  f.input.mouseButtons(0); assert.equal(f.input.mouseDown(2, 2), true); assert.equal(f.messages[0].lease, 'second');
});
test('lease or encounter changes discard pending reply but never rewrite emitted identity', () => {
  const f = fixture(); f.input.strike(); const old = { ...f.messages[0] };
  f.context.encounterId = nextEncounter; f.context.lease = 'second'; f.input.reconcile(); f.input.strike();
  assert.deepEqual(f.messages[0], old); assert.equal(f.messages[1].encounterId, nextEncounter); assert.equal(f.messages[1].lease, 'second');
  assert.equal(f.input.reply({ transportId: old.transportId }), false); assert.equal(f.input.state().pending.inputSequence, '2');
});
test('duplicate fallback click waits for reply and old reply cannot clear newer input', () => {
  const f = fixture(); assert.equal(f.input.strike(), true); assert.equal(f.input.strike(), false);
  const old = f.messages[0].transportId; assert.equal(f.input.reply({ transportId: old }), true); f.input.strike();
  assert.equal(f.input.reply({ transportId: old }), false); assert.equal(f.input.state().pending.inputSequence, '2');
});
test('empty early strike captures null encounter; no new target is substituted', () => {
  const f = fixture(); f.context.phase = 'searching'; f.context.encounterId = null; assert.equal(f.input.strike(), true);
  f.context.phase = 'approaching'; f.context.encounterId = encounter; f.input.reconcile();
  assert.equal(f.messages[0].encounterId, null); assert.equal(f.messages.length, 1);
});
test('terminal phases, observer, malformed identities and non-right input refuse strike', () => {
  for (const phase of ['idle', 'hooked', 'retrieve-required', 'ended', 'unknown']) { const f = fixture(); f.context.phase = phase; assert.equal(f.input.strike(), false); }
  for (const field of [{ allowed: false }, { lease: null }, { sessionId: null }, { sessionId: 'bad' }, { encounterId: 'bad' }]) {
    const f = fixture(); Object.assign(f.context, field); assert.equal(f.input.strike(), false);
  }
  const f = fixture(); for (const [button, buttons] of [[0, 1], [2, 0], [2, 32], [2, 2.1]]) assert.equal(f.input.mouseDown(button, buttons), false);
  assert.equal(f.messages.length, 0);
});
test('moving in with right held is never treated as a new press', () => {
  const f = fixture(); f.input.mouseButtons(2); assert.equal(f.input.mouseDown(2, 2), false);
  f.input.mouseButtons(0); assert.equal(f.input.mouseDown(2, 2), true);
});

import test from 'node:test';
import assert from 'node:assert/strict';
import { FightView, FightInput } from './fight-view.mjs';

const session = '10000000-0000-0000-0000-000000000001';
const encounter = '20000000-0000-0000-0000-000000000001';
const nextEncounter = '20000000-0000-0000-0000-000000000002';
const state = extra => ({ sessionId: session, encounterId: encounter, phase: 'fighting', reason: 'fighting', landingReason: 'fish-too-far', stepIndex: '0',
  xMm: 0, yMm: -600, zMm: 15000, outLineMm: 15500, spoolRemainingMm: 34500, slackMm: 353,
  tensionMilliN: 0, dragMilliN: 6000, rodDeflectionMm: 0, energyPermille: 1000, reeling: false, dragLevel: 5, reelLevel: 5, canRetryLanding: false, ...extra });
const packet = (revision = 1, extra = {}) => ({ kind: 'game-fight-state', documentId: 'current', revision, state: state(extra) });
function fixture() {
  const context = { lease: 'first', allowed: true, sessionId: session, encounterId: encounter, phase: 'fighting', canRetryLanding: false }, messages = [];
  let transport = 0;
  const input = new FightInput({ readContext: () => context, post: m => messages.push(m), newTransportId: () => `fight-${++transport}` });
  return { input, context, messages, actions: () => messages.map(m => m.action) };
}
test('fight accepts only current document and strictly newer valid revision', () => {
  const view = new FightView('current'); assert.equal(view.apply(packet()), true);
  for (const p of [packet(), packet(0), { ...packet(2), documentId: 'old' }, { ...packet(2), kind: 'game-bite-state' }]) assert.equal(view.apply(p), false);
  assert.equal(view.revision, 1); assert.equal(view.apply(packet(2)), true);
});
test('projection never advances or auto-lands between supplied snapshots', () => {
  const view = new FightView('current'); view.apply(packet(1, { phase: 'landing-eligible', stepIndex: '9223372036854775807' }));
  const before = view.diagram(); for (let n = 0; n < 500; n++) assert.deepEqual(view.diagram(), before);
  assert.equal(view.state().phase, 'landing-eligible'); assert.equal(view.state().stepIndex, '9223372036854775807');
});
test('invalid fight numbers, identities and phases do not consume revision', () => {
  const view = new FightView('current');
  for (const bad of [{ phase: 'won' }, { sessionId: null }, { encounterId: null }, { encounterId: 'bad' }, { phase: 'idle' },
    { stepIndex: '00' }, { stepIndex: '9223372036854775808' }, { xMm: 10000001 }, { zMm: NaN }, { yMm: 1.5 },
    { outLineMm: -1 }, { spoolRemainingMm: 100000001 }, { slackMm: -1 }, { tensionMilliN: Infinity }, { dragMilliN: 10000001 },
    { rodDeflectionMm: 1000000001 }, { energyPermille: 1001 }, { dragLevel: -1 }, { reelLevel: 11 }, { reeling: 1 }, { canRetryLanding: null },
    { reason: 'x'.repeat(129) }, { landingReason: {} }]) assert.equal(view.apply(packet(1, bad)), false);
  assert.equal(view.revision, 0); assert.equal(view.apply(packet()), true);
});
test('idle clears and new document reconstructs without a new fight', () => {
  const view = new FightView('current'), second = new FightView('new'); view.apply(packet(9)); second.apply({ ...packet(), documentId: 'new' });
  assert.deepEqual(second.diagram(), view.diagram()); view.apply(packet(10, { phase: 'idle', sessionId: null, encounterId: null })); assert.equal(view.diagram(), null);
});
test('unknown private facts and mutations do not leak into retained projection', () => {
  const view = new FightView('current'), p = packet(1, { speciesId: 'secret', massMg: '500000' }); view.apply(p); p.state.zMm = 2;
  const s = view.state(); s.energyPermille = 0; assert.equal(view.state().energyPermille, 1000); assert.equal(view.state().zMm, 15000);
  assert.equal('speciesId' in view.state(), false); assert.equal('massMg' in view.state(), false);
});
test('diagram clips outside its range explicitly and uses real host depth', () => {
  const view = new FightView('current'); view.apply(packet(1, { xMm: 10000000, zMm: -10000000, yMm: -500 }));
  const d = view.diagram(); assert.equal(d.clipped, true); assert.equal(d.x, 440); assert.equal(d.y, 220); assert.equal(d.depthMm, 500);
});
test('terminal and pending phases remain visible and never imply successful catch', () => {
  const view = new FightView('current'); let revision = 0;
  for (const phase of ['landing-pending', 'capacity-blocked', 'faulted', 'landed']) { assert.equal(view.apply(packet(++revision, { phase })), true); assert.equal(view.diagram().phase, phase); }
  view.apply(packet(++revision, { phase: 'ended' })); assert.equal(view.diagram(), null);
});
test('hooked/current state does not automatically send reel or landing input', () => {
  const f = fixture(); for (let i = 0; i < 20; i++) f.input.reconcile(); f.context.phase = 'landing-eligible'; f.input.reconcile(); assert.deepEqual(f.messages, []);
});
test('quick press/release pairs immediately without waiting for acknowledgement', () => {
  const f = fixture(); assert.equal(f.input.begin('pointer:3'), true); assert.equal(f.input.end('pointer:3'), true);
  assert.deepEqual(f.actions(), ['reel-start', 'reel-stop']); assert.deepEqual(f.messages.map(m => m.inputSequence), ['1', '2']);
  assert.equal(f.input.state().held, null); assert.equal(f.input.end('pointer:3'), false);
});
test('different input source cannot release a held mouse gesture', () => {
  const f = fixture(); f.input.begin('pointer:3'); assert.equal(f.input.end('button'), false); assert.equal(f.input.begin('pointer:4'), false);
  f.input.end('pointer:3'); assert.deepEqual(f.actions(), ['reel-start', 'reel-stop']);
});
test('old release preserves captured authority when a replacement grant exists', () => {
  const f = fixture(); f.input.begin('pointer:3'); f.context.lease = 'second'; f.context.encounterId = nextEncounter; f.input.reconcile();
  assert.equal(f.messages[1].lease, 'first'); assert.equal(f.messages[1].encounterId, encounter);
  assert.equal(f.input.end('pointer:3'), false); f.input.begin('pointer:4');
  assert.equal(f.messages[2].lease, 'second'); assert.equal(f.messages[2].inputSequence, '3');
});
test('captured old release cannot end newer hold with the same physical source label', () => {
  const f = fixture(); f.input.begin('pointer:3'); const old = f.input.state().held.startTransportId;
  f.context.lease = 'second'; f.input.reconcile(); f.input.begin('pointer:3');
  const current = f.input.state().held.startTransportId;
  assert.equal(f.input.end('pointer:3', old), false); assert.equal(f.input.state().held.startTransportId, current);
  assert.equal(f.input.end('pointer:3', current), true); assert.equal(f.messages.at(-1).lease, 'second');
});
test('pause/menu/terminal state cancels once and restoring does not resume', () => {
  for (const change of [{ allowed: false }, { phase: 'landing-pending' }, { phase: 'ended' }]) {
    const f = fixture(); f.input.begin(); Object.assign(f.context, change); f.input.reconcile(); f.input.reconcile();
    f.context.allowed = true; f.context.phase = 'fighting'; f.input.reconcile(); assert.deepEqual(f.actions(), ['reel-start', 'reel-stop']);
  }
});
test('rejected start clears only matching held input and delayed old reply cannot clear new hold', () => {
  const f = fixture(); f.input.begin(); const old = f.messages[0].transportId; f.input.end(); f.input.begin();
  f.input.reply({ transportId: old, code: 'control-required' }); assert.notEqual(f.input.state().held, null);
  f.input.reply({ transportId: f.messages[2].transportId, code: 'control-required' }); assert.equal(f.input.state().held, null);
  assert.deepEqual(f.actions(), ['reel-start', 'reel-stop', 'reel-start', 'reel-stop']);
});
test('adjustments validate integer level and carry no physical state or reward facts', () => {
  const f = fixture(); assert.equal(f.input.adjust('drag', 10), true); assert.equal(f.input.adjust('speed', 0), true);
  for (const args of [['drag', -1], ['speed', 11], ['drag', 1.1], ['mass', 5]]) assert.equal(f.input.adjust(...args), false);
  assert.deepEqual(Object.keys(f.messages[0]).sort(), ['kind', 'lease', 'transportId', 'sessionId', 'encounterId', 'inputSequence', 'action', 'value'].sort());
  assert.deepEqual(f.messages.map(m => m.inputSequence), ['1', '2']);
});
test('landing requires current eligibility, stops hold first and never sends twice pending', () => {
  const f = fixture(); assert.equal(f.input.land(), false); f.context.phase = 'landing-eligible'; f.input.begin();
  assert.equal(f.input.land(), true); assert.equal(f.input.land(), false); assert.deepEqual(f.actions(), ['reel-start', 'reel-stop', 'land']);
  assert.equal(f.input.state().held, null);
});
test('retry only follows explicit known retry permission and preserves same fish identity', () => {
  const f = fixture(); assert.equal(f.input.retryLanding(), false); f.context.phase = 'landing-pending'; assert.equal(f.input.retryLanding(), false);
  f.context.canRetryLanding = true; assert.equal(f.input.retryLanding(), true); assert.equal(f.input.retryLanding(), false);
  assert.equal(f.messages[0].sessionId, session); assert.equal(f.messages[0].encounterId, encounter);
  f.input.reply({ transportId: f.messages[0].transportId, code: 'landing-not-written' }); assert.equal(f.input.retryLanding(), true);
});
test('read-only and malformed identity prevent every new control action', () => {
  for (const change of [{ allowed: false }, { lease: null }, { sessionId: null }, { encounterId: 'bad' }]) {
    const f = fixture(); Object.assign(f.context, change); assert.equal(f.input.begin(), false); assert.equal(f.input.adjust('drag', 5), false);
    f.context.phase = 'landing-eligible'; assert.equal(f.input.land(), false); assert.deepEqual(f.messages, []);
  }
});
test('reply pairing stays bounded without dropping the held stop', () => {
  const f = fixture(); f.input.begin(); for (let i = 0; i < 100; i++) f.input.adjust('drag', i % 11);
  assert.equal(f.input.state().pending.length, 64); f.input.end(); assert.equal(f.messages.at(-1).action, 'reel-stop');
});

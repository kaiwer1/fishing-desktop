import test from 'node:test';
import assert from 'node:assert/strict';
import { FlightView } from './flight-view.mjs';

const origin = { xMm: 0, yMm: 1500, zMm: 0 };
const session = '10000000-0000-0000-0000-000000000001';
const state = extra => ({ sessionId: session, phase: 'flying', reason: null,
  elapsedMilliseconds: 100, xMm: 0, yMm: 2000, zMm: 3000, lineUsedMm: 3100,
  origin: { ...origin }, waterYmm: 0, trail: [{ ...origin }], ...extra });
const packet = (revision = 1, extra = {}, documentId = 'current-document') => ({ kind: 'game-flight-state', documentId, revision, state: state(extra) });

test('only current document and strictly newer valid revision change projection', () => {
  const view = new FlightView('current-document');
  assert.equal(view.apply(packet(1)), true);
  for (const m of [packet(1), packet(0), packet(2, {}, 'old-document'), { ...packet(2), kind: 'game-flight' }]) assert.equal(view.apply(m), false);
  assert.equal(view.revision, 1); assert.equal(view.state().elapsedMilliseconds, 100);
  assert.equal(view.apply(packet(2, { elapsedMilliseconds: 200 })), true);
});
test('position and elapsed time remain exactly host supplied between snapshots', () => {
  const view = new FlightView('current-document'); view.apply(packet());
  const initial = view.diagram();
  for (let i = 0; i < 1000; i++) assert.deepEqual(view.diagram(), initial);
  assert.equal(view.state().elapsedMilliseconds, 100); assert.equal(view.state().zMm, 3000);
});
test('fresh page reconstructs current bobber and recent trail from one snapshot', () => {
  const existing = new FlightView('current-document'), reload = new FlightView('replacement-document');
  const update = packet(15, { trail: [{ ...origin }, { xMm: 0, yMm: 1800, zMm: 1500 }] });
  existing.apply(update); reload.apply({ ...update, documentId: 'replacement-document', revision: 1 });
  assert.deepEqual(reload.diagram(), existing.diagram());
});
test('new session replaces previous trail and position rather than concatenating', () => {
  const view = new FlightView('current-document'); view.apply(packet());
  view.apply(packet(2, { sessionId: '10000000-0000-0000-0000-000000000002', xMm: 0, yMm: 1500, zMm: 0, elapsedMilliseconds: 0, lineUsedMm: 0 }));
  assert.equal(view.diagram().recent.length, 1); assert.equal(view.diagram().distanceMm, 0);
});
test('waiting/invalid/ended are displayed without inventing a catch or a new cast', () => {
  const view = new FlightView('current-document');
  let revision = 0;
  for (const phase of ['waiting', 'invalid', 'ended']) {
    assert.equal(view.apply(packet(++revision, { phase, yMm: 0, reason: phase === 'invalid' ? 'line-limit' : null })), true);
    assert.equal(view.diagram().phase, phase); assert.equal(view.diagram().current.y, 190);
  }
});
test('idle requires no session and clears visible flight', () => {
  const view = new FlightView('current-document'); view.apply(packet());
  assert.equal(view.apply(packet(2, { phase: 'idle', sessionId: null, trail: [], xMm: 0, yMm: 0, zMm: 0, elapsedMilliseconds: 0, lineUsedMm: 0 })), true);
  assert.equal(view.diagram(), null);
});
test('malformed positions, counters, identities and oversized trails do not consume revision', () => {
  const view = new FlightView('current-document');
  for (const invalid of [
    { xMm: NaN }, { yMm: Infinity }, { zMm: 1.5 }, { xMm: 1000001 }, { yMm: -1000001 },
    { elapsedMilliseconds: -1 }, { elapsedMilliseconds: 86400001 }, { lineUsedMm: -1 }, { lineUsedMm: 1000001 },
    { origin: null }, { waterYmm: undefined }, { reason: 'x'.repeat(129) }, { reason: {} },
    { sessionId: null }, { sessionId: '00000000-0000-0000-0000-000000000000' }, { phase: 'caught' },
    { phase: 'idle' }, { trail: Array.from({ length: 33 }, () => ({ ...origin })) }, { trail: [null] },
  ]) assert.equal(view.apply(packet(1, invalid)), false);
  assert.equal(view.revision, 0); assert.equal(view.apply(packet()), true);
});
test('incoming and returned objects cannot mutate held snapshot', () => {
  const view = new FlightView('current-document'), message = packet(); view.apply(message);
  message.state.origin.yMm = 99; message.state.trail[0].xMm = 99;
  const read = view.state(); read.origin.yMm = 1; read.trail.length = 0;
  assert.deepEqual(view.state().origin, origin); assert.equal(view.state().trail.length, 1);
});
test('projection clips extreme valid coordinates visibly and never emits NaN or infinity', () => {
  const view = new FlightView('current-document');
  view.apply(packet(1, { xMm: 1000000, yMm: 1000000, zMm: -1000000 }));
  const graph = view.diagram(); assert.equal(graph.clipped, true);
  assert.deepEqual(graph.current, { x: 696, y: 20 });
  assert.ok(Number.isFinite(graph.distanceMm));
});
test('diagram uses host origin/water plane and retains reported required line span', () => {
  const view = new FlightView('current-document');
  view.apply(packet(1, { origin: { xMm: 10000, yMm: 2500, zMm: 10000 }, waterYmm: 1000, xMm: 13000, yMm: 1000, zMm: 14000, lineUsedMm: 6000 }));
  const graph = view.diagram(); assert.equal(graph.distanceMm, 5000); assert.equal(graph.lineUsedMm, 6000);
  assert.equal(graph.current.y, 190); assert.equal(graph.origin.x, 48);
});

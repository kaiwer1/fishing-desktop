import { test } from 'node:test';
import assert from 'node:assert/strict';
import { InputSession } from './input-session.mjs';

test('disabled input is ignored, with no text collection', () => {
  const s = new InputSession();
  assert.equal(s.down(1,0),false); s.key(); s.wheel();
  assert.equal(s.snapshot().presses,0); assert.equal(s.keys,0); assert.equal(s.wheels,0);
});
test('left/right/middle have separate held state and release only once', () => {
  const s = new InputSession(); s.setMode(true,1);
  for (const button of [0,1,2]) assert.equal(s.down(7,button),true);
  assert.equal(s.down(7,2),false); assert.equal(s.held.size,3);
  assert.equal(s.up(7,2),true); assert.equal(s.up(7,2),false);
  assert.equal(s.held.size,2); assert.equal(s.releases,1);
});
test('focus cancellation clears held state and requires explicit newer activation', () => {
  const s = new InputSession(); s.setMode(true,4); s.down(1,0);
  s.cancel('blur');
  assert.equal(s.held.size,0); assert.equal(s.up(1,0),false);
  assert.equal(s.setMode(true,4),false); assert.equal(s.down(1,0),false);
  assert.equal(s.setMode(true,5),true); assert.equal(s.held.size,0);
  assert.equal(s.up(1,0),false); assert.equal(s.down(1,0),true);
});
test('late enable cannot override a newer host cancellation', () => {
  const s = new InputSession(); s.setMode(true,1); s.setMode(false,3,'deactivate');
  assert.equal(s.setMode(true,2),false); assert.equal(s.enabled,false);
  assert.equal(s.setMode(true,NaN),false); assert.equal(s.setMode(true,3.5),false);
});
test('local cancellation invalidates an already queued newer enable', () => {
  const s = new InputSession(); s.setMode(true,1);
  const queuedEpoch = s.cancellationEpoch;
  s.pointerButtons(1,1,true); s.cancel('blur');
  assert.equal(s.setMode(true,2,'queued-enable',queuedEpoch),false);
  assert.equal(s.enabled,false); assert.equal(s.held.size,0);
  assert.equal(s.revision,2); assert.equal(s.cancellationEpoch,queuedEpoch+1);
  assert.equal(s.setMode(true,2,'replayed-enable',s.cancellationEpoch),false);
  assert.equal(s.setMode(true,3,'new-explicit-enable',s.cancellationEpoch),true);
  assert.deepEqual(s.pointerButtons(1,0),[]);
  assert.equal(s.releases,0);
});
test('disable always cancels even with an outdated epoch; enable does not advance epoch', () => {
  const s = new InputSession(); s.cancel('hidden');
  assert.equal(s.setMode(true,1,'explicit-enable',1),true);
  assert.equal(s.cancellationEpoch,1); s.pointerButtons(1,2,true);
  assert.equal(s.setMode(false,2,'host-deactivate',0),true);
  assert.equal(s.cancellationEpoch,2); assert.equal(s.enabled,false);
  assert.equal(s.held.size,0); assert.equal(s.trackedPointers.size,0);
  assert.equal(s.snapshot().cancellationEpoch,2);
});
test('a stale activation with a newer revision fails closed without inventing another epoch', () => {
  const s = new InputSession(); s.cancel('escape');
  s.setMode(true,3,'explicit-enable',1); s.pointerButtons(1,1,true);
  assert.equal(s.setMode(true,4,'stale-enable',0),false);
  assert.equal(s.enabled,false); assert.equal(s.held.size,0);
  assert.equal(s.cancellationEpoch,1); assert.equal(s.revision,4);
  assert.equal(s.setMode(true,4,'replay',1),false);
});

test('W3C chord sequence handles intermediate left/right transitions on pointermove', () => {
  const s = new InputSession(); s.setMode(true,1);
  assert.deepEqual(s.pointerButtons(1,1,true),[{kind:'down',button:0}]);
  assert.deepEqual(s.pointerButtons(1,3),[{kind:'down',button:2}]);
  assert.deepEqual(s.pointerButtons(1,2),[{kind:'up',button:0}]);
  assert.deepEqual(s.pointerButtons(1,0),[{kind:'up',button:2}]);
  assert.equal(s.held.size,0); assert.equal(s.presses,2); assert.equal(s.releases,2);
  assert.deepEqual(s.pointerButtons(1,0),[]);
});
test('canceled held button cannot restart through pointermove after reactivation', () => {
  const s = new InputSession(); s.setMode(true,1); s.pointerButtons(1,1,true);
  s.cancel('blur'); s.setMode(true,2);
  assert.deepEqual(s.pointerButtons(1,1),[]); assert.deepEqual(s.pointerButtons(1,0),[]);
  assert.equal(s.held.size,0); assert.equal(s.releases,0);
  assert.equal(s.pointerButtons(1,2,true)[0].button,2);
});
test('middle mask and independent pointers do not cancel each other on normal release', () => {
  const s = new InputSession(); s.setMode(true,1);
  assert.equal(s.pointerButtons(1,4,true)[0].button,1);
  s.pointerButtons(2,1,true); s.pointerButtons(1,0);
  assert.equal(s.hasPointer(1),false); assert.equal(s.hasPointer(2),true);
  assert.equal(s.enabled,true); s.pointerButtons(2,0);
  assert.deepEqual(s.snapshot().buttonTotals,[{down:1,up:1},{down:1,up:1},{down:0,up:0}]);
});

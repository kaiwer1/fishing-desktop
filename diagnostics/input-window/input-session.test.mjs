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

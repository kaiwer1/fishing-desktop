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

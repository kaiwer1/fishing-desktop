import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createVisualProbe } from './visual-probe.mjs';

function fixture() {
  const callbacks = new Map(), queued = new Set();let id = 0, allocations = 0;
  const document = { documentElement: { clientWidth: 1920, clientHeight: 1080 }, body: { append() {} },
    createElement() { allocations++;return { style: {}, getContext: () => ({ fillRect() {}, fillText() {} }) }; } };
  const probe = createVisualProbe(document, callback => { callbacks.set(++id, callback);queued.add(id);return id; }, handle => queued.delete(handle));
  return { probe, queued, allocations: () => allocations, run(handle) { queued.delete(handle);callbacks.get(handle)(); } };
}
test('idempotent start keeps one callback and stop cancels rendering', () => {
  const f = fixture();f.probe.start();f.probe.start();assert.equal(f.queued.size, 1);
  const pending = [...f.queued][0];f.probe.stop();f.run(pending);
  assert.deepEqual(f.probe.state(), { running: false, frames: 0 });assert.equal(f.queued.size, 0);
});
test('late callback from a stopped generation cannot consume or duplicate the resumed callback', () => {
  const f = fixture();f.probe.start();const old = [...f.queued][0];f.probe.stop();f.probe.start();
  const resumed = [...f.queued][0];f.run(old);
  assert.deepEqual([...f.queued], [resumed]);assert.equal(f.probe.state().frames, 0);
  f.run(resumed);assert.equal(f.probe.state().frames, 1);assert.equal(f.queued.size, 1);
});
test('repeated stop and restart reuse one bounded canvas and preserve the diagnostic frame count', () => {
  const f = fixture();f.probe.start();f.run([...f.queued][0]);f.probe.stop();f.probe.stop();f.probe.start();f.run([...f.queued][0]);
  assert.equal(f.allocations(), 1);assert.equal(f.probe.state().frames, 2);assert.equal(f.queued.size, 1);f.probe.stop();
});
test('host pause remembers running state while repeated pause does not discard it', () => {
  const f = fixture();f.probe.start();f.probe.pause();f.probe.pause();assert.equal(f.queued.size, 0);
  f.probe.resume();assert.equal(f.probe.state().running, true);assert.equal(f.queued.size, 1);
  f.probe.resume();assert.equal(f.queued.size, 1);f.probe.stop();
});
test('host resume does not start a stopped fixture or override explicit stop during pause', () => {
  const f = fixture();f.probe.pause();f.probe.resume();assert.equal(f.queued.size, 0);
  f.probe.start();f.probe.pause();f.probe.stop();f.probe.resume();assert.equal(f.queued.size, 0);
});

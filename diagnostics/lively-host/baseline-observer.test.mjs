import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('./baseline-observer.js', import.meta.url), 'utf8');
const drawMethods = ['drawArrays', 'drawElements', 'drawArraysInstanced', 'drawElementsInstanced'];

// These fakes test the observer's wrapping and accounting only. They do not create a
// browser/WebGL context, measure presented frames, or sample real CPU/GPU performance.
function createHarness({ playback = () => {} } = {}) {
  let now = 0, nextFrame = 1, nextTimer = 1;
  const frames = [], timers = new Map(), listeners = new Map();
  const reports = [], calls = [], rafReceivers = [];
  function makeContextClass(name) {
    function Context(width = 640, height = 360) {
      this.drawingBufferWidth = width;
      this.drawingBufferHeight = height;
    }
    for (const method of drawMethods) {
      const result = { name, method };
      Context.prototype[method] = function (...args) {
        calls.push({ name, method, receiver: this, args, result });
        return result;
      };
    }
    return Context;
  }
  const sandbox = {
    crypto: { randomUUID: () => 'observer-test-run' },
    performance: { now: () => now },
    innerWidth: 960,
    innerHeight: 540,
    document: { querySelector: () => null },
    WebGLRenderingContext: makeContextClass('WebGL1'),
    WebGL2RenderingContext: makeContextClass('WebGL2'),
    requestAnimationFrame(callback) {
      rafReceivers.push(this);
      const id = nextFrame++;
      frames.push({ id, callback });
      return id;
    },
    setInterval(callback, delay) {
      const id = nextTimer++;
      timers.set(id, { callback, delay });
      return id;
    },
    clearInterval(id) { timers.delete(id); },
    addEventListener(name, callback) { listeners.set(name, callback); },
    fetch(url, options) {
      assert.equal(url, 'http://127.0.0.1:43817/event');
      assert.equal(options.method, 'POST');
      reports.push(JSON.parse(options.body));
      return { catch() {} }; // No network or asynchronous work is started.
    },
  };
  sandbox.window = sandbox;
  if (playback !== null) sandbox.livelyWallpaperPlaybackChanged = playback;
  const context = vm.createContext(sandbox);
  vm.runInContext(source, context, { filename: 'baseline-observer.js' });
  // vm exposes its global through a proxy; compare against that actual window,
  // not the host-side sandbox object used to create the context.
  const window = vm.runInContext('window', context);
  function tick(delay) {
    const timer = [...timers.values()].find(value => value.delay === delay);
    assert.ok(timer, `Expected an active ${delay} ms diagnostic timer`);
    return timer.callback();
  }
  return {
    window, reports, calls, rafReceivers, timers, frames,
    advance(milliseconds) { now += milliseconds; },
    pollPlayback() { tick(25); },
    snapshot() { tick(1000); return reports.at(-1); },
    frame(time = 0) {
      assert.ok(frames.length, 'Expected a scheduled animation callback');
      return frames.shift().callback(time);
    },
  };
}

test('GL wrappers delegate every supported method with its receiver, arguments and return value', () => {
  const h = createHarness(); h.pollPlayback();
  for (const name of ['WebGLRenderingContext', 'WebGL2RenderingContext']) {
    const gl = new h.window[name]();
    for (const method of drawMethods) {
      const reference = { marker: method }, args = [4, 12, reference, 0];
      const result = gl[method](...args);
      const original = h.calls.at(-1);
      assert.equal(original.receiver, gl);
      assert.equal(original.method, method);
      assert.deepEqual(original.args, args);
      assert.equal(original.args[2], reference);
      assert.equal(result, original.result);
    }
  }
  const state = h.snapshot();
  assert.equal(h.calls.length, 8);
  assert.equal(state.drawCalls, 8);
  assert.equal(state.renderedCallbacks, 0, 'Draws outside RAF are not rendered RAF callbacks');
  assert.equal(state.callbackCpuMsP95, null);
});

test('only RAF callbacks that issue draws contribute to callback count and CPU samples', () => {
  const h = createHarness(); h.pollPlayback();
  const gl1 = new h.window.WebGLRenderingContext(), gl2 = new h.window.WebGL2RenderingContext();
  const token = { result: 'callback-result' };
  const frameId = h.window.requestAnimationFrame(time => {
    assert.equal(time, 123.5);
    gl1.drawArrays(4, 0, 3);
    gl1.drawElements(4, 3, 5123, 0);
    gl2.drawArraysInstanced(4, 0, 3, 2);
    h.advance(4);
    return token;
  });
  assert.equal(frameId, h.frames[0].id, 'RAF returns the native scheduler handle');
  assert.equal(h.rafReceivers[0], h.window, 'The native RAF scheduler retains its window receiver');
  assert.equal(h.frame(123.5), token);
  h.window.requestAnimationFrame(() => h.advance(500));
  h.frame(140);
  const state = h.snapshot();
  assert.equal(state.drawCalls, 3);
  assert.equal(state.renderedCallbacks, 1);
  assert.equal(state.callbackCpuMsP95, 4, 'An expensive no-draw callback is excluded');
});

test('a callback throwing after a draw is counted and preserves the original exception', () => {
  const h = createHarness(); h.pollPlayback();
  const gl = new h.window.WebGLRenderingContext(), failure = new Error('callback failed after draw');
  h.window.requestAnimationFrame(() => {
    gl.drawArrays(4, 0, 3);
    h.advance(9);
    throw failure;
  });
  assert.throws(() => h.frame(), error => error === failure);
  const state = h.snapshot();
  assert.equal(state.drawCalls, 1);
  assert.equal(state.renderedCallbacks, 1);
  assert.equal(state.callbackCpuMsP95, 9);
});

test('a throwing callback without draws propagates the error without adding a render sample', () => {
  const h = createHarness(); h.pollPlayback();
  const failure = new Error('callback failed before any draw');
  h.window.requestAnimationFrame(() => { h.advance(700); throw failure; });
  assert.throws(() => h.frame(), error => error === failure);
  const state = h.snapshot();
  assert.equal(state.renderedCallbacks, 0);
  assert.equal(state.callbackCpuMsP95, null);
});

test('CPU P95 uses the nearest rank of the most recent 120 drawing callbacks', () => {
  const h = createHarness(); h.pollPlayback();
  const gl = new h.window.WebGLRenderingContext();
  function renderFor(cost) {
    h.window.requestAnimationFrame(() => { gl.drawArrays(4, 0, 3); h.advance(cost); });
    h.frame();
  }
  renderFor(1000);
  for (let cost = 1; cost <= 119; cost++) renderFor(cost);
  assert.equal(h.snapshot().callbackCpuMsP95, 114);
  renderFor(120); // The oldest 1000 ms sample must now be evicted.
  const state = h.snapshot();
  assert.equal(state.renderedCallbacks, 121);
  assert.equal(state.callbackCpuMsP95, 114);
  // Without eviction this would be 115; a 119-sample window would also give 115.
});

test('a measured zero-cost drawing callback is a sample rather than missing data', () => {
  const h = createHarness(); h.pollPlayback();
  const gl = new h.window.WebGLRenderingContext();
  h.window.requestAnimationFrame(() => gl.drawArrays(4, 0, 3)); h.frame();
  assert.equal(h.snapshot().callbackCpuMsP95, 0);
  assert.equal(h.snapshot().renderedCallbacks, 1);
});

test('playback observation waits for the original callback and forwards pause/resume inputs unchanged', () => {
  const h = createHarness({ playback: null });
  h.pollPlayback();
  assert.equal(h.reports.length, 0, 'No boot report before the application callback exists');
  const received = [];
  h.window.livelyWallpaperPlaybackChanged = data => received.push(data);
  h.pollPlayback();
  assert.equal([...h.timers.values()].some(timer => timer.delay === 25), false);
  assert.equal(h.reports.at(-1).kind, 'boot');
  const pause = '{"IsPaused":true}', resume = { IsPaused: false, extra: 'untouched' };
  h.window.livelyWallpaperPlaybackChanged(pause);
  assert.equal(received[0], pause);
  assert.equal(h.reports.at(-1).kind, 'pause');
  assert.equal(h.reports.at(-1).paused, true);
  assert.deepEqual(h.reports.at(-1).callback, { IsPaused: true });
  h.window.livelyWallpaperPlaybackChanged(resume);
  assert.equal(received[1], resume, 'Object callback input keeps its identity');
  assert.equal(received.length, 2);
  assert.equal(h.reports.at(-1).paused, false);
  assert.deepEqual(h.reports.at(-1).callback, resume);
  assert.equal(h.snapshot().paused, false);
});

test('drawing buffer dimensions come from the last drawing context independently of viewport size', () => {
  const h = createHarness(); h.pollPlayback();
  assert.equal(h.snapshot().drawingBuffer, null);
  const gl1 = new h.window.WebGLRenderingContext(1920, 1080);
  gl1.drawArrays(4, 0, 3);
  let state = h.snapshot();
  assert.deepEqual(state.drawingBuffer, { width: 1920, height: 1080 });
  assert.equal(state.width, 960); assert.equal(state.height, 540);
  const gl2 = new h.window.WebGL2RenderingContext(1280, 720);
  gl2.drawElementsInstanced(4, 3, 5123, 0, 2);
  h.window.innerWidth = 800; h.window.innerHeight = 600;
  state = h.snapshot();
  assert.deepEqual(state.drawingBuffer, { width: 1280, height: 720 });
  assert.equal(state.width, 800); assert.equal(state.height, 600);
  gl2.drawingBufferWidth = 640; gl2.drawingBufferHeight = 360;
  gl2.drawArrays(4, 0, 3);
  assert.deepEqual(h.snapshot().drawingBuffer, { width: 640, height: 360 });
});

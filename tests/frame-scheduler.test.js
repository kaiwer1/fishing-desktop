import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createFrameScheduler } from '../src/frame-scheduler.js';

function driver({ refresh = 60, timestampAge = 0, renderCost = 0, jitter = () => 0, onRender, onError } = {}) {
  let time = 0;
  let nextHandle = 0;
  const jobs = new Map();
  const history = [];
  const renders = [];
  let scheduler;
  function enqueue(kind, callback, at, timestamp) {
    const handle = nextHandle++;
    const job = { handle, kind, callback, at, timestamp };
    jobs.set(handle, job);
    history.push(job);
    return handle;
  }
  scheduler = createFrameScheduler({
    render: (timestamp, dt) => {
      renders.push({ timestamp, dt });
      time += renderCost;
      onRender?.(scheduler, renders.length);
    },
    now: () => time,
    requestFrame: (callback) => {
      const period = 1000 / refresh;
      let index = Math.max(1, Math.floor((time - timestampAge) / period + 1e-9));
      while (index * period + timestampAge + jitter(index) <= time + 1e-9) index++;
      return enqueue('frame', callback, index * period + timestampAge + jitter(index), index * period);
    },
    cancelFrame: (handle) => jobs.delete(handle),
    setTimer: (callback, delay) => enqueue('timer', callback, time + delay),
    clearTimer: (handle) => jobs.delete(handle),
    onError,
  });
  function runUntil(end) {
    let steps = 0;
    while (true) {
      const next = [...jobs.values()].filter((job) => job.at <= end).sort((a, b) => a.at - b.at || a.handle - b.handle)[0];
      if (!next) break;
      assert.ok(++steps < 10000, 'scheduler must not create an unbounded callback loop');
      jobs.delete(next.handle);
      time = Math.max(time, next.at);
      next.callback(next.timestamp);
    }
    time = Math.max(time, end);
  }
  return { scheduler, jobs, history, renders, runUntil, setTime: (value) => { time = value; }, time: () => time };
}

function assertCadence(renders, fps) {
  for (const frame of renders) {
    const count = renders.filter((other) => other.timestamp >= frame.timestamp && other.timestamp < frame.timestamp + 1000 - 1e-7).length;
    assert.ok(count <= fps + 1, `${fps} fps cadence exceeded: ${count} submissions in one second`);
  }
}

test('30 fps ceiling ignores a 14.2 ms old RAF timestamp on a 60 Hz host', () => {
  const d = driver({ timestampAge: 14.2, renderCost: 0.5 });
  d.scheduler.wake(30);
  d.runUntil(5000);
  assert.equal(d.renders.length, 150);
  assertCadence(d.renders, 30);
  assert.ok(d.renders[0].timestamp > d.history[0].timestamp);
  assert.equal(d.jobs.size, 1);
});

test('18, 30 and 60 fps maintain requested cadence across common refresh rates', () => {
  for (const refresh of [60, 120, 144]) {
    for (const fps of [18, 30, 60]) {
      const d = driver({ refresh, timestampAge: 2.3 });
      d.scheduler.wake(fps);
      d.runUntil(5000);
      assertCadence(d.renders, fps);
      assert.ok(d.renders.length <= fps * 5);
      assert.ok(d.renders.length >= fps * 5 - 1, 'refresh quantization must not lower the long-window rate');
      assert.equal(d.jobs.size, 1);
    }
  }
});

test('30 fps tolerates refresh jitter and 10 ms rendering without drifting down to 20 fps', () => {
  for (const timestampAge of [0, 14.2]) {
    for (const renderCost of [0.5, 10]) {
      const d = driver({ timestampAge, renderCost, jitter: (index) => index % 3 === 0 ? 0.4 : -0.4 });
      d.scheduler.wake(30);
      d.runUntil(10000);
      assert.ok(Math.abs(d.renders.length - 300) <= 1, `${timestampAge} ms timestamp age / ${renderCost} ms draw: ${d.renders.length} frames`);
      assertCadence(d.renders, 30);
      assert.equal(d.jobs.size, 1);
    }
  }
});

test('18 fps alternates refresh counts to preserve its long-window cadence', () => {
  const d = driver();
  d.scheduler.wake(18);
  d.runUntil(10000);
  assert.ok(Math.abs(d.renders.length - 180) <= 1);
  const gaps = d.renders.slice(1).map((frame, i) => Math.round((frame.timestamp - d.renders[i].timestamp) / (1000 / 60)));
  assert.ok(gaps.includes(3));
  assert.ok(gaps.includes(4));
  assertCadence(d.renders, 18);
});

test('60 fps preserves throughput with sub-millisecond host jitter', () => {
  for (const timestampAge of [0, 14.2]) {
    const d = driver({ timestampAge, renderCost: 10, jitter: (index) => index % 3 === 0 ? 0.4 : -0.4 });
    d.scheduler.wake(60);
    d.runUntil(10000);
    assert.ok(Math.abs(d.renders.length - 600) <= 1, `${timestampAge} ms timestamp age: ${d.renders.length} frames`);
    assertCadence(d.renders, 60);
  }
});

test('a one-second blocked main thread drops missed frames and resumes without a burst', () => {
  const d = driver({ timestampAge: 14.2, renderCost: 10 });
  d.scheduler.wake(30);
  d.runUntil(200);
  const count = d.renders.length;
  d.setTime(1200);
  d.runUntil(1200);
  assert.ok(d.renders.length <= count + 1);
  const afterStall = d.renders.length;
  d.runUntil(1210);
  assert.equal(d.renders.length, afterStall);
  d.runUntil(2200);
  assert.ok(d.renders.length - afterStall <= 31);
  assert.ok(d.renders.every((frame) => frame.dt <= 0.1));
  assertCadence(d.renders, 30);
  assert.equal(d.jobs.size, 1);
});

test('frequent wake notifications keep one chain and cannot bypass the rate ceiling', () => {
  const d = driver();
  d.scheduler.wake(30);
  for (let t = 1; t <= 1000; t++) {
    d.runUntil(t);
    d.scheduler.wake(30);
    assert.equal(d.jobs.size, 1);
  }
  assert.ok(d.renders.length >= 29, `only ${d.renders.length} renders: ${d.renders.map((frame) => frame.timestamp).join(', ')}`);
  assertCadence(d.renders, 30);
});

test('stale frame callbacks cannot clear the current generation or restart after stop', () => {
  const d = driver();
  d.scheduler.wake(30);
  const staleFrame = d.history.at(-1);
  assert.equal(staleFrame.handle, 0);
  d.scheduler.wake(60);
  const current = d.scheduler.getState();
  staleFrame.callback(1e9);
  assert.deepEqual(d.scheduler.getState(), current);
  assert.equal(d.jobs.size, 1);
  d.scheduler.stop();
  for (const job of d.history) job.callback(1e9);
  assert.equal(d.renders.length, 0);
  assert.equal(d.jobs.size, 0);
  assert.equal(d.scheduler.getState().running, false);
});

test('stale timers cannot register a frame in a newer generation', () => {
  const d = driver();
  d.scheduler.wake(30);
  d.runUntil(17);
  const staleTimer = d.history.at(-1);
  assert.equal(staleTimer.kind, 'timer');
  d.scheduler.wake(18);
  const current = d.scheduler.getState();
  staleTimer.callback();
  assert.deepEqual(d.scheduler.getState(), current);
  assert.equal(d.jobs.size, 1);
});

test('render can change the rate without duplicating the scheduled chain', () => {
  const d = driver({ onRender: (scheduler, count) => { if (count === 1) scheduler.wake(30); } });
  d.scheduler.wake(60);
  d.runUntil(1000);
  assert.equal(d.renders.length, 30);
  assertCadence(d.renders, 30);
  assert.equal(d.jobs.size, 1);
});

test('render can stop itself with no pending work', () => {
  const d = driver({ onRender: (scheduler) => scheduler.stop() });
  d.scheduler.wake(30);
  d.runUntil(1000);
  assert.equal(d.renders.length, 1);
  assert.equal(d.jobs.size, 0);
  assert.equal(d.scheduler.getState().running, false);
});

test('zero fps pauses and a long resume does not replay background elapsed time', () => {
  const d = driver();
  d.scheduler.wake(30);
  d.runUntil(100);
  d.scheduler.wake(0);
  const count = d.renders.length;
  d.runUntil(60000);
  assert.equal(d.renders.length, count);
  assert.equal(d.jobs.size, 0);
  d.scheduler.wake(30);
  d.runUntil(60017);
  assert.equal(d.renders.length, count + 1);
  assert.ok(d.renders.at(-1).dt < 0.017);
  assert.equal(d.jobs.size, 1);
});

test('quick stop and resume preserve the last render deadline', () => {
  const d = driver();
  d.scheduler.wake(30);
  d.runUntil(17);
  d.scheduler.stop();
  d.scheduler.wake(30);
  d.runUntil(34);
  assert.equal(d.renders.length, 1);
  d.runUntil(51);
  assert.equal(d.renders.length, 2);
  assertCadence(d.renders, 30);
});

test('slow callbacks submit once each and clamp elapsed time without catch-up bursts', () => {
  const d = driver({ refresh: 2 });
  d.scheduler.wake(60);
  d.runUntil(5000);
  assert.equal(d.renders.length, 10);
  assert.ok(d.renders.every((frame) => frame.dt === 0.1));
  assert.equal(d.jobs.size, 1);
});

test('an early timer and early RAF callback are gated by the monotonic deadline', () => {
  const d = driver();
  d.scheduler.wake(30);
  d.runUntil(17);
  const timer = d.history.at(-1);
  d.jobs.delete(timer.handle);
  timer.callback();
  const earlyFrame = d.history.at(-1);
  assert.equal(earlyFrame.kind, 'frame');
  d.jobs.delete(earlyFrame.handle);
  earlyFrame.callback(1e9);
  assert.equal(d.renders.length, 1);
  assert.equal(d.jobs.size, 1);
  d.runUntil(51);
  assert.equal(d.renders.length, 2);
  assertCadence(d.renders, 30);
});

test('deadline jitter tolerance accepts half a millisecond but rejects two milliseconds early', () => {
  const d = driver();
  d.scheduler.wake(30);
  d.runUntil(17);
  const deadline = d.scheduler.getState().deadline;
  const timer = d.history.at(-1);
  d.jobs.delete(timer.handle);
  timer.callback();
  let frame = d.history.at(-1);
  d.jobs.delete(frame.handle);
  d.setTime(deadline - 2);
  frame.callback(1e9);
  assert.equal(d.renders.length, 1);
  frame = d.history.at(-1);
  d.jobs.delete(frame.handle);
  d.setTime(deadline - 0.5);
  frame.callback(-1e9);
  assert.equal(d.renders.length, 2);
  assert.ok(Math.abs(d.scheduler.getState().deadline - (deadline + 1000 / 30)) < 1e-7);
});

test('render exceptions stop even a newly awakened chain before reporting the error', () => {
  const error = new Error('render failed');
  const failures = [];
  const d = driver({
    onRender: (scheduler) => { scheduler.wake(60); throw error; },
    onError: (value) => failures.push(value),
  });
  d.scheduler.wake(30);
  d.runUntil(1000);
  assert.deepEqual(failures, [error]);
  assert.equal(d.jobs.size, 0);
  assert.equal(d.scheduler.getState().running, false);
  for (const job of d.history) job.callback();
  assert.equal(d.renders.length, 1);
});

test('render errors without a handler propagate after cleanup', () => {
  const d = driver({ onRender: () => { throw new Error('draw'); } });
  d.scheduler.wake(30);
  assert.throws(() => d.runUntil(1000), /draw/);
  assert.equal(d.jobs.size, 0);
  assert.equal(d.scheduler.getState().running, false);
});

test('an error handler that throws still leaves no pending chain', () => {
  const d = driver({
    onRender: () => { throw new Error('draw failed'); },
    onError: () => { throw new Error('report failed'); },
  });
  d.scheduler.wake(30);
  assert.throws(() => d.runUntil(1000), /report failed/);
  assert.equal(d.jobs.size, 0);
  assert.equal(d.scheduler.getState().running, false);
});

test('a non-monotonic clock fails closed', () => {
  const errors = [];
  const d = driver({ onError: (error) => errors.push(error) });
  d.scheduler.wake(30);
  d.runUntil(17);
  d.setTime(1);
  d.scheduler.wake(60);
  assert.equal(errors.length, 1);
  assert.match(errors[0].message, /monotonic/);
  assert.equal(d.jobs.size, 0);
  assert.equal(d.scheduler.getState().running, false);
});

test('invalid rates stop existing work instead of leaving it running', () => {
  for (const fps of [NaN, Infinity, -1, '30', Number.MIN_VALUE]) {
    const d = driver();
    d.scheduler.wake(30);
    assert.throws(() => d.scheduler.wake(fps), /fps/);
    assert.equal(d.jobs.size, 0);
    assert.equal(d.scheduler.getState().running, false);
  }
});

test('queueing failures stop the scheduler before onError', () => {
  for (const api of ['requestFrame', 'setTimer']) {
    const failures = [];
    let frame;
    let timestamp = 0;
    const scheduler = createFrameScheduler({
      render: () => {}, now: () => timestamp,
      requestFrame: (callback) => { frame = callback; return 0; }, cancelFrame: () => {},
      setTimer: () => 0, clearTimer: () => {},
      [api]: () => { throw new Error(api); },
      onError: (error) => failures.push(error.message),
    });
    scheduler.wake(30);
    if (api === 'setTimer') { timestamp = 17; frame(0); }
    assert.deepEqual(failures, [api]);
    assert.equal(scheduler.getState().running, false);
    assert.equal(scheduler.getState().pendingFrame, false);
    assert.equal(scheduler.getState().pendingTimer, false);
  }
});

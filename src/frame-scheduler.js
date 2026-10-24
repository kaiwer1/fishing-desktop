// The RAF timestamp can precede callback execution by most of a refresh period.
// All deadlines and elapsed times therefore use the same injected monotonic clock.
export function createFrameScheduler({
  render,
  now = () => performance.now(),
  requestFrame = (callback) => requestAnimationFrame(callback),
  cancelFrame = (handle) => cancelAnimationFrame(handle),
  setTimer = (callback, delay) => setTimeout(callback, delay),
  clearTimer = (handle) => clearTimeout(handle),
  onError,
  maxDeltaSeconds = 0.1,
  wakeAheadMs = 4,
  deadlineToleranceMs = 1,
} = {}) {
  for (const [name, value] of Object.entries({ render, now, requestFrame, cancelFrame, setTimer, clearTimer })) {
    if (typeof value !== 'function') throw new TypeError(`${name} must be a function`);
  }
  if (onError !== undefined && typeof onError !== 'function') throw new TypeError('onError must be a function');
  if (!Number.isFinite(maxDeltaSeconds) || maxDeltaSeconds < 0) throw new RangeError('maxDeltaSeconds must be nonnegative and finite');
  if (!Number.isFinite(wakeAheadMs) || wakeAheadMs < 0) throw new RangeError('wakeAheadMs must be nonnegative and finite');
  if (!Number.isFinite(deadlineToleranceMs) || deadlineToleranceMs < 0 || deadlineToleranceMs > 1) {
    throw new RangeError('deadlineToleranceMs must be between zero and one millisecond');
  }

  let generation = 0;
  let running = false;
  let fps = 0;
  let interval = 0;
  let deadline = null;
  let elapsedBase = null;
  let lastRenderedAt = null;
  let lastClockRead = null;
  let timerJob = null;
  let frameJob = null;
  // Only compensates floating point arithmetic, not early frame delivery.
  const epsilonMs = 1e-7;

  function clock() {
    const value = now();
    if (!Number.isFinite(value) || (lastClockRead !== null && value < lastClockRead)) {
      throw new RangeError('now() must return a finite, monotonic timestamp in milliseconds');
    }
    lastClockRead = value;
    return value;
  }

  function invalidate() {
    generation += 1;
    running = false;
    fps = 0;
    deadline = elapsedBase = null;
    const timer = timerJob;
    const frame = frameJob;
    timerJob = frameJob = null;
    // Invalidate before cancellation: even a callback already queued by a host
    // cannot affect the next generation, including handles whose value is zero.
    let failure;
    try { if (timer?.handle !== null && timer?.handle !== undefined) clearTimer(timer.handle); }
    catch (error) { failure = error; }
    try { if (frame?.handle !== null && frame?.handle !== undefined) cancelFrame(frame.handle); }
    catch (error) { failure ??= error; }
    return failure;
  }

  function fail(error) {
    invalidate();
    if (onError) onError(error);
    else throw error;
  }

  function guarded(action) {
    try { action(); }
    catch (error) { fail(error); }
  }

  function queueFrame(expectedGeneration) {
    if (!running || generation !== expectedGeneration || frameJob || timerJob) return;
    const job = { handle: null };
    frameJob = job;
    // RAF's timestamp is deliberately unused. Injected queue functions must be
    // asynchronous, just like the browser's RAF and setTimeout APIs.
    job.handle = requestFrame(() => {
      if (!running || generation !== expectedGeneration || frameJob !== job) return;
      frameJob = null;
      guarded(() => draw(expectedGeneration));
    });
  }

  function schedule(expectedGeneration) {
    if (!running || generation !== expectedGeneration || frameJob || timerJob) return;
    const remaining = deadline - clock();
    if (remaining <= wakeAheadMs) {
      queueFrame(expectedGeneration);
      return;
    }
    const job = { handle: null };
    timerJob = job;
    job.handle = setTimer(() => {
      if (!running || generation !== expectedGeneration || timerJob !== job) return;
      timerJob = null;
      guarded(() => queueFrame(expectedGeneration));
    }, remaining - wakeAheadMs);
  }

  function draw(expectedGeneration) {
    const timestamp = clock();
    // At most 1 ms of refresh/clock jitter may arrive early. Without this bound,
    // a 60 Hz callback arriving 0.4 ms early can incorrectly lose a whole frame.
    // It never advances the phase and cannot create a second chain or backlog.
    if (timestamp + deadlineToleranceMs + epsilonMs < deadline) {
      schedule(expectedGeneration);
      return;
    }
    const dtSeconds = Math.min(maxDeltaSeconds, Math.max(0, timestamp - elapsedBase) / 1000);
    elapsedBase = lastRenderedAt = timestamp;
    // Keep normal refresh jitter from shifting the cadence (18 fps can alternate
    // three/four refreshes on a 60 Hz screen). If an entire period was missed,
    // discard that debt; one callback must never produce catch-up submissions.
    const followingDeadline = deadline + interval;
    deadline = timestamp + epsilonMs >= followingDeadline ? timestamp + interval : followingDeadline;
    render(timestamp, dtSeconds);
    // render() may itself stop or wake the scheduler. Only that new generation
    // owns its pending work; this callback must not start another chain.
    if (running && generation === expectedGeneration) schedule(expectedGeneration);
  }

  function wake(nextFps) {
    // Resize/repeated host notifications need not cancel a frame that is already
    // waiting for the next refresh. Idempotence also preserves the phase.
    if (running && nextFps === fps) return;
    guarded(() => {
      const cancellationError = invalidate();
      if (cancellationError) throw cancellationError;
      if (!Number.isFinite(nextFps) || nextFps < 0 || (nextFps > 0 && !Number.isFinite(1000 / nextFps))) {
        throw new RangeError('fps must be zero or a finite positive rate');
      }
      if (nextFps === 0) return;
      const timestamp = clock();
      fps = nextFps;
      interval = 1000 / fps;
      running = true;
      elapsedBase = timestamp;
      // A genuine resume resets elapsed time and starts no earlier than one
      // period after the last draw. No elapsed background time is replayed.
      deadline = Math.max(timestamp, lastRenderedAt === null ? timestamp : lastRenderedAt + interval);
      schedule(generation);
    });
  }

  function stop() {
    const error = invalidate();
    if (error) fail(error);
  }

  return {
    wake,
    stop,
    getState: () => ({
      running,
      fps,
      generation,
      deadline,
      pendingFrame: frameJob !== null,
      pendingTimer: timerJob !== null,
    }),
  };
}

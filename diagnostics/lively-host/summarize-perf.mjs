// Offline, read-only correlation of private sampler output and observer telemetry.
import { readFileSync, readdirSync, realpathSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repository = realpathSync(fileURLToPath(new URL('../..', import.meta.url)));
const metrics = ['cpuMachineNormalizedPercent', 'privateBytes', 'maxSingleEngineReportedPercent',
  'sumAcrossEnginesReportedPercent', 'reportedDedicatedBytes', 'reportedSharedBytes'];
const countFields = ['processCount', 'cpuMatchedProcessCount', 'gpuStableProcessCount',
  'gpuEngineProcessCount', 'gpuMemoryProcessCount', 'gpuEngineNumericProcessCount',
  'gpuDedicatedNumericProcessCount', 'gpuSharedNumericProcessCount', 'gpuMemoryNumericProcessCount'];
const finite = value => typeof value === 'number' && Number.isFinite(value) && value >= 0;
const count = value => Number.isSafeInteger(value) && value >= 0;
class InputError extends Error {}
function requireInput(condition, code) { if (!condition) throw new InputError(code); }
function within(root, candidate) {
  const relative = path.relative(root, candidate);
  return relative === '' || (relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative));
}
function parseTime(value) {
  return typeof value === 'string' && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?Z$/.test(value)
    ? Date.parse(value) : NaN;
}
function range(values) {
  const valid = values.filter(finite);
  return { availableSamples: valid.length, min: valid.length ? Math.min(...valid) : null,
    max: valid.length ? Math.max(...valid) : null };
}
function flags(values) {
  return { true: values.filter(value => value === true).length,
    false: values.filter(value => value === false).length,
    null: values.filter(value => value !== true && value !== false).length };
}
function safeWeather(value) {
  return typeof value === 'string' && value.length <= 160 &&
    !/(?:[a-z]:[\\/]|\\\\|file:|https?:|(?:^|\s)\/(?:Users|home|tmp|var|mnt)\/)/i.test(value) ? value : null;
}
function sizes(rows, get) {
  const unique = new Map();
  for (const row of rows) {
    const size = get(row);
    if (size && count(size.width) && count(size.height)) unique.set(`${size.width}x${size.height}`, { width: size.width, height: size.height });
  }
  return [...unique.values()];
}
function summaryStats(summary, field) {
  const value = summary[field];
  if (value == null) return null;
  requireInput(typeof value === 'object' && count(value.availableSamples) &&
    (value.mean === null || finite(value.mean)) && (value.peak === null || finite(value.peak)), 'invalid-summary-metric');
  return { availableSamples: value.availableSamples, mean: value.mean, peak: value.peak };
}

function observerReport(events, start, end) {
  const inWindow = events.filter(event => event.time >= start && event.time <= end);
  // Other diagnostic pages also have heartbeat events; only this observer has
  // renderedCallbacks or an explicit observer marker.
  const observed = inWindow.filter(event => event.observer === true || Object.hasOwn(event, 'renderedCallbacks'));
  const runs = new Set(observed.map(event => event.run));
  const heartbeats = observed.filter(event => event.kind === 'heartbeat');
  const reasons = [];
  if (heartbeats.length < 2) reasons.push('insufficient-heartbeats');
  if (runs.size !== 1 || [...runs].some(run => typeof run !== 'string' || run.length === 0)) reasons.push('ambiguous-observer-run');
  if (observed.some(event => !count(event.drawCalls) || !count(event.renderedCallbacks))) reasons.push('invalid-counters');
  if (observed.some((event, index) => index > 0 && (event.time < observed[index - 1].time ||
    event.drawCalls < observed[index - 1].drawCalls || event.renderedCallbacks < observed[index - 1].renderedCallbacks))) reasons.push('non-monotonic-telemetry');
  const first = heartbeats[0], last = heartbeats.at(-1);
  const span = first && last ? (last.time - first.time) / 1000 : null;
  if (span !== null && span <= 0) reasons.push('no-positive-heartbeat-span');
  const gaps = heartbeats.slice(1).map((event, index) => (event.time - heartbeats[index].time) / 1000);
  // The observer emits once per second. Allow jitter, but do not describe a
  // mostly unobserved measurement window as verified from two distant events.
  if (first && last && (first.time - start > 2500 || end - last.time > 2500 || gaps.some(gap => gap > 2.5))) reasons.push('heartbeat-coverage-gap');
  const valid = reasons.length === 0;
  const paused = flags(heartbeats.map(event => event.paused));
  const pauseState = !heartbeats.length || paused.null === heartbeats.length ? 'unknown'
    : paused.true === heartbeats.length ? 'paused' : paused.false === heartbeats.length ? 'playing' : 'mixed';
  const profiles = heartbeats.map(event => event.requestedSettings).filter(profile => profile &&
    ['low','medium','high'].includes(profile.quality) && [0.65,0.85,1].includes(Number(profile.scale)) &&
    [30,60].includes(Number(profile.fps))).map(profile => ({quality:profile.quality,scale:Number(profile.scale),fps:Number(profile.fps)}));
  return {
    status: valid ? 'determinate' : 'indeterminate', reasons, observerRunCount: runs.size,
    heartbeatCount: heartbeats.length, heartbeatSpanSeconds: span,
    firstHeartbeatAtUtc: first ? new Date(first.time).toISOString() : null,
    lastHeartbeatAtUtc: last ? new Date(last.time).toISOString() : null,
    maximumHeartbeatGapSeconds: gaps.length ? Math.max(...gaps) : null,
    maximumAllowedHeartbeatGapSeconds: 2.5,
    drawCallsDelta: valid ? last.drawCalls - first.drawCalls : null,
    renderedCallbacksDelta: valid ? last.renderedCallbacks - first.renderedCallbacks : null,
    applicationSubmissionCallbacksPerSecond: valid ? (last.renderedCallbacks - first.renderedCallbacks) / span : null,
    rateMeaning: 'Drawing RAF callbacks per second between first and last heartbeat; not presented-frame FPS.',
    pausedHeartbeatCounts: paused, observedPauseState: valid ? pauseState : 'indeterminate',
    weather: { first: safeWeather(first?.weather), last: safeWeather(last?.weather) },
    callbackCpuMsP95: range(heartbeats.map(event => event.callbackCpuMsP95)),
    callbackCpuMeaning: 'Range of rolling 120 drawing-callback P95 values; samples may predate this window.',
    callbackTimestampAgeMsP95: range(heartbeats.map(event => event.callbackTimestampAgeMsP95)),
    requestedSettings: [...new Map(profiles.map(profile => [JSON.stringify(profile), profile])).values()],
    requestedSettingsSampleCount: profiles.length,
    viewportSizes: sizes(heartbeats, event => ({ width: event.width, height: event.height })),
    drawingBufferSizes: sizes(heartbeats, event => event.drawingBuffer),
    errorCount: inWindow.filter(event => event.kind === 'error').length,
    observerErrorCount: observed.filter(event => event.kind === 'error').length,
  };
}

export function summarizeSession(directory) {
  requireInput(typeof directory === 'string' && directory.length > 0, 'session-directory-required');
  const lexicalRoot = path.resolve(directory);
  requireInput(!within(repository, lexicalRoot), 'repository-input-rejected');
  const root = realpathSync(lexicalRoot);
  requireInput(statSync(root).isDirectory() && !within(repository, root), 'repository-input-rejected');
  function checkedFile(reference) {
    requireInput(typeof reference === 'string' && reference.length > 0 && !reference.includes('\0') &&
      !reference.split(/[\\/]/).includes('..'), 'unsafe-input-path');
    const candidate = path.resolve(root, reference);
    requireInput(within(root, candidate) && !within(repository, candidate), 'input-outside-session');
    const actual = realpathSync(candidate);
    requireInput(within(root, actual) && !within(repository, actual), 'input-outside-session');
    const info = statSync(actual);
    requireInput(info.isFile() && info.size <= 64 * 1024 * 1024, 'invalid-input-file');
    return actual;
  }
  function read(reference, lines = false) {
    const text = readFileSync(checkedFile(reference), 'utf8').replace(/^\uFEFF/, '');
    try { return lines ? text.split(/\r?\n/).filter(line => line.trim()).map(line => JSON.parse(line)) : JSON.parse(text); }
    catch { throw new InputError('invalid-json-input'); }
  }
  const events = read('events.jsonl', true).map(event => {
    requireInput(event && typeof event === 'object' && !Array.isArray(event) && Number.isFinite(parseTime(event.receivedAt)), 'invalid-event-time');
    return { ...event, time: parseTime(event.receivedAt) };
  });
  const names = readdirSync(root).filter(name => name.endsWith('-summary.json')).sort();
  requireInput(names.length > 0, 'no-measurement-summaries');
  const measurements = names.map(name => {
    const summary = read(name);
    requireInput(summary && typeof summary === 'object' && Number.isFinite(parseTime(summary.startedAtUtc)) &&
      finite(summary.elapsedSeconds) && summary.elapsedSeconds > 0 && count(summary.sampleCount), 'invalid-measurement-window');
    const start = parseTime(summary.startedAtUtc), end = start + summary.elapsedSeconds * 1000;
    requireInput(Number.isFinite(end) && end <= 8640000000000000, 'invalid-measurement-window');
    const samples = read(summary.rawSamplesFile, true);
    const sampleTimes = samples.map(sample => parseTime(sample?.atUtc));
    const withinWindow = samples.filter((sample, index) => sampleTimes[index] >= start && sampleTimes[index] <= end);
    const reasons = [];
    if (samples.length !== summary.sampleCount) reasons.push('sample-count-mismatch');
    if (sampleTimes.some(time => !Number.isFinite(time))) reasons.push('invalid-sample-time');
    if (withinWindow.length !== samples.length) reasons.push('samples-outside-window');
    if (sampleTimes.some((time, index) => index > 0 && time < sampleTimes[index - 1])) reasons.push('non-monotonic-sample-times');
    const observer = observerReport(events, start, end);
    return {
      startedAtUtc: new Date(start).toISOString(), endedAtUtc: new Date(end).toISOString(), elapsedSeconds: summary.elapsedSeconds,
      phaseLabel: ['active', 'paused', 'unlabelled'].includes(summary.phaseLabel) ? summary.phaseLabel : 'unknown',
      phaseLabelVerified: false,
      status: observer.status === 'determinate' && reasons.length === 0 ? 'determinate' : 'indeterminate',
      observer,
      samples: {
        status: reasons.length ? 'indeterminate' : 'consistent', reasons,
        reportedCount: summary.sampleCount, readCount: samples.length, withinWindowCount: withinWindow.length,
        gpuEngineProvider: flags(withinWindow.map(sample => sample.gpuEngineProviderAvailable)),
        gpuMemoryProvider: flags(withinWindow.map(sample => sample.gpuMemoryProviderAvailable)),
        processCoverage: Object.fromEntries(countFields.map(field => [field, range(withinWindow.map(sample => sample[field]))])),
        metricAvailableCounts: Object.fromEntries(metrics.map(field => [field, withinWindow.filter(sample => finite(sample[field])).length])),
      },
      originalSummaryMetrics: Object.fromEntries(metrics.map(field => [field, summaryStats(summary, field)])),
    };
  }).sort((a, b) => a.startedAtUtc.localeCompare(b.startedAtUtc));
  return {
    schema: 1, status: measurements.every(item => item.status === 'determinate') ? 'determinate' : 'indeterminate',
    measurements,
    interpretation: [
      'Phase labels are caller annotations; pause conclusions use observer heartbeats only.',
      'Summary metrics are original arithmetic means/peaks, not recomputed or time-weighted.',
      'Missing GPU counters remain unavailable; process coverage does not imply every process uses the GPU.',
      'Summed engine percentages are not overall GPU utilization; reported memory sums are not unique physical VRAM.',
      'Event times are collector receipt times; this report does not establish frame pacing, power or long-term performance.',
    ],
  };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    requireInput(process.argv.length === 3, 'usage: node summarize-perf.mjs <private-session-directory>');
    console.log(JSON.stringify(summarizeSession(process.argv[2]), null, 2));
  } catch (error) {
    // Never expose raw filesystem errors, private paths, PIDs or event messages.
    console.log(JSON.stringify({ error: error instanceof InputError ? error.message : 'input-unavailable' }));
    process.exitCode = 1;
  }
}

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, symlinkSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { summarizeSession } from './summarize-perf.mjs';

const start = Date.parse('2026-10-24T12:00:00.000Z');
const at = seconds => new Date(start + seconds * 1000).toISOString();
const metricNames = ['cpuMachineNormalizedPercent', 'privateBytes', 'maxSingleEngineReportedPercent',
  'sumAcrossEnginesReportedPercent', 'reportedDedicatedBytes', 'reportedSharedBytes'];
const cli = fileURLToPath(new URL('./summarize-perf.mjs', import.meta.url));
function fixture(t) {
  const root = mkdtempSync(path.join(os.tmpdir(), 'fishing-perf-summary-test-'));
  t.after(() => {
    assert.ok(path.basename(root).startsWith('fishing-perf-summary-test-'));
    assert.equal(path.dirname(root), path.resolve(os.tmpdir()));
    rmSync(root, { recursive: true, force: true });
  });
  mkdirSync(path.join(root, 'raw'));
  const events = Array.from({ length: 11 }, (_, second) => ({
    receivedAt: at(second), kind: 'heartbeat', observer: true, run: 'run-a',
    drawCalls: second * 20, renderedCallbacks: second * 10,
    paused: false, weather: `06:${String(second).padStart(2, '0')} · 晴朗 · 风 3.2 m/s`,
    callbackCpuMsP95: second / 10, width: 1920, height: 1080,
    drawingBuffer: { width: 1280, height: 720 },
  }));
  const samples = [1, 9].map((second, index) => ({
    atUtc: at(second), processCount: 4, cpuMatchedProcessCount: index * 4,
    gpuStableProcessCount: 4, gpuEngineProcessCount: index, gpuMemoryProcessCount: index,
    gpuEngineProviderAvailable: Boolean(index), gpuMemoryProviderAvailable: Boolean(index),
    ...Object.fromEntries(metricNames.map(name => [name, index ? 0 : null])),
    processes: [{ id: 987654, privatePath: 'C:\\Users\\private-person\\browser-profile' }],
  }));
  const summary = {
    startedAtUtc: at(0), elapsedSeconds: 10, sampleCount: 2, phaseLabel: 'paused',
    rawSamplesFile: path.join('raw', 'samples.jsonl'),
    rootProcessId: 987654, livelyExecutable: 'C:\\Users\\private-person\\Lively.exe',
    ...Object.fromEntries(metricNames.map((name, index) => [name, { availableSamples: 1, mean: index + 0.25, peak: index + 1 }])),
  };
  function save() {
    writeFileSync(path.join(root, 'events.jsonl'), events.map(event => JSON.stringify(event)).join('\n') + '\n');
    writeFileSync(path.join(root, 'raw', 'samples.jsonl'), samples.map(sample => JSON.stringify(sample)).join('\n') + '\n');
    writeFileSync(path.join(root, 'test-summary.json'), JSON.stringify(summary));
  }
  save();
  return { root, events, samples, summary, save };
}

test('correlates one run, preserves original metrics, and never treats a phase label as pause evidence', t => {
  const f = fixture(t);
  const originals = ['events.jsonl', 'test-summary.json', path.join('raw', 'samples.jsonl')]
    .map(name => [name, readFileSync(path.join(f.root, name))]);
  const report = summarizeSession(f.root), measurement = report.measurements[0], observed = measurement.observer;
  assert.equal(report.status, 'determinate');
  assert.equal(measurement.phaseLabel, 'paused'); assert.equal(measurement.phaseLabelVerified, false);
  assert.equal(observed.observedPauseState, 'playing');
  assert.deepEqual(observed.pausedHeartbeatCounts, { true: 0, false: 11, null: 0 });
  assert.equal(observed.drawCallsDelta, 200); assert.equal(observed.renderedCallbacksDelta, 100);
  assert.equal(observed.applicationSubmissionCallbacksPerSecond, 10);
  assert.match(observed.rateMeaning, /not presented-frame FPS/);
  assert.deepEqual(observed.callbackCpuMsP95, { availableSamples: 11, min: 0, max: 1 });
  assert.deepEqual(observed.viewportSizes, [{ width: 1920, height: 1080 }]);
  assert.deepEqual(observed.drawingBufferSizes, [{ width: 1280, height: 720 }]);
  assert.equal(observed.weather.first, f.events[0].weather);
  assert.equal(observed.weather.last, f.events.at(-1).weather);
  for (const name of metricNames) assert.deepEqual(measurement.originalSummaryMetrics[name], f.summary[name]);
  assert.deepEqual(measurement.samples.gpuEngineProvider, { true: 1, false: 1, null: 0 });
  assert.equal(measurement.samples.metricAvailableCounts.reportedDedicatedBytes, 1, 'Missing is not zero; actual zero is available');
  assert.equal(JSON.stringify(report).includes('987654'), false);
  assert.equal(JSON.stringify(report).includes('private-person'), false);
  for (const [name, bytes] of originals) assert.deepEqual(readFileSync(path.join(f.root, name)), bytes);
});

test('missing heartbeats, long gaps, multiple runs and counter regressions make rates indeterminate', t => {
  const cases = [
    { reason: 'insufficient-heartbeats', change: f => { f.events.length = 0; } },
    { reason: 'heartbeat-coverage-gap', change: f => { f.events.splice(2, 7); } },
    { reason: 'ambiguous-observer-run', change: f => { f.events[5].run = 'another-run'; } },
    { reason: 'non-monotonic-telemetry', change: f => { f.events[5].drawCalls = 1; } },
    { reason: 'invalid-counters', change: f => { delete f.events[5].renderedCallbacks; } },
  ];
  for (const entry of cases) {
    const f = fixture(t); entry.change(f); f.save();
    const report = summarizeSession(f.root), observed = report.measurements[0].observer;
    assert.equal(report.status, 'indeterminate'); assert.ok(observed.reasons.includes(entry.reason));
    assert.equal(observed.drawCallsDelta, null); assert.equal(observed.applicationSubmissionCallbacksPerSecond, null);
    assert.equal(observed.observedPauseState, 'indeterminate');
  }
});

test('filters measurement windows and counts observed true/false/null and errors without exporting messages', t => {
  const f = fixture(t);
  f.events[0].paused = null; f.events[1].paused = true;
  f.events.unshift({ ...f.events[0], receivedAt: at(-1), run: 'outside-run', drawCalls: 9999 });
  f.events.splice(7, 0, { ...f.events[6], kind: 'error', message: 'C:\\Users\\private-person\\secret' });
  f.events.push({ ...f.events.at(-1), receivedAt: at(11), kind: 'error', run: 'outside-run' });
  f.save();
  const observed = summarizeSession(f.root).measurements[0].observer;
  assert.equal(observed.status, 'determinate'); assert.equal(observed.observerRunCount, 1);
  assert.deepEqual(observed.pausedHeartbeatCounts, { true: 1, false: 9, null: 1 });
  assert.equal(observed.observedPauseState, 'mixed'); assert.equal(observed.errorCount, 1);
  assert.equal(observed.observerErrorCount, 1); assert.equal(JSON.stringify(observed).includes('private-person'), false);
});

test('null pause telemetry stays unknown even with paused label and flat counters', t => {
  const f = fixture(t);
  for (const event of f.events) { event.paused = null; event.drawCalls = 0; event.renderedCallbacks = 0; }
  f.save();
  const observed = summarizeSession(f.root).measurements[0].observer;
  assert.equal(observed.observedPauseState, 'unknown');
  assert.equal(observed.applicationSubmissionCallbacksPerSecond, 0);
});

test('raw sample count/time mismatch is explicit rather than silently trusting summary values', t => {
  const f = fixture(t); f.summary.sampleCount = 3; f.samples[1].atUtc = at(11); f.save();
  const result = summarizeSession(f.root), samples = result.measurements[0].samples;
  assert.equal(result.status, 'indeterminate');
  assert.deepEqual(samples.reasons, ['sample-count-mismatch', 'samples-outside-window']);
  assert.equal(samples.withinWindowCount, 1);
});

test('rejects traversal, outside absolute paths, repository inputs and symlink escapes', t => {
  const f = fixture(t), other = fixture(t);
  f.summary.rawSamplesFile = 'raw/../raw/samples.jsonl'; f.save();
  assert.throws(() => summarizeSession(f.root), /unsafe-input-path/);
  f.summary.rawSamplesFile = path.join(other.root, 'raw', 'samples.jsonl'); f.save();
  assert.throws(() => summarizeSession(f.root), /input-outside-session/);
  assert.throws(() => summarizeSession(fileURLToPath(new URL('../..', import.meta.url))), /repository-input-rejected/);
  const linked = path.join(f.root, 'linked');
  symlinkSync(path.join(other.root, 'raw'), linked, process.platform === 'win32' ? 'junction' : 'dir');
  f.summary.rawSamplesFile = path.join('linked', 'samples.jsonl'); f.save();
  assert.throws(() => summarizeSession(f.root), /input-outside-session/);
});

test('accepts contained absolute sample path and CLI prints only sanitized JSON', t => {
  const f = fixture(t); f.summary.rawSamplesFile = path.join(f.root, 'raw', 'samples.jsonl'); f.save();
  const child = spawnSync(process.execPath, [cli, f.root], { encoding: 'utf8', windowsHide: true });
  assert.equal(child.status, 0); assert.equal(child.stderr, '');
  assert.equal(JSON.parse(child.stdout).status, 'determinate');
  assert.equal(child.stdout.includes(f.root), false); assert.equal(child.stdout.includes('private-person'), false);
  const missing = spawnSync(process.execPath, [cli, path.join(f.root, 'missing-private-directory')], { encoding: 'utf8', windowsHide: true });
  assert.equal(missing.status, 1); assert.deepEqual(JSON.parse(missing.stdout), { error: 'input-unavailable' });
  assert.equal(missing.stderr, '');
});

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, symlinkSync, unlinkSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { nearestRankStats, parsePresentCsv, summarizePresents } from './summarize-presents.mjs';

const cli = fileURLToPath(new URL('./summarize-presents.mjs', import.meta.url));
const repository = fileURLToPath(new URL('../..', import.meta.url));
const targetId = 987654;
const fakeSession = `FishingDesktop-${'a'.repeat(32)}`;
const headers = ['Application', 'ProcessID', 'SwapChainAddress', 'PresentRuntime', 'VidPnSourceId', 'LayerIndex',
  'PresentMode', 'CPUStartTime', 'FrameTime', 'CPUBusy', 'DisplayLatency', 'DisplayedTime'];
const time = '2026-10-24T10:00:00.0000000Z';
function quote(value) {
  const text = String(value ?? '');
  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}
function closeTo(actual, expected) { assert.ok(Math.abs(actual - expected) < 1e-9, `${actual} != ${expected}`); }
function fixture(t) {
  const root = mkdtempSync(path.join(os.tmpdir(), 'fishing-presents-test-'));
  t.after(() => {
    assert.ok(path.basename(root).startsWith('fishing-presents-test-'));
    assert.equal(path.dirname(root), path.resolve(os.tmpdir()));
    rmSync(root, { recursive: true, force: true });
  });
  const rows = [0, 10, 20, 30].map((cpu, index) => ({
    Application: 'msedgewebview2.exe', ProcessID: targetId, SwapChainAddress: '0xABCDE987654', PresentRuntime: 'DXGI',
    VidPnSourceId: 0, LayerIndex: 0, PresentMode: 'Composed: Flip', CPUStartTime: cpu, FrameTime: 10,
    CPUBusy: [1, 3, 1, 2][index], DisplayLatency: index === 1 ? 'NA' : 5,
    DisplayedTime: [20, 'NA', 10, 10][index],
  }));
  const summary = { status: 'captured', reason: null, startedAtUtc: time, requestedSeconds: 30,
    elapsedSeconds: 30.25, toolExitCode: 0, timedOut: false, targetIdentityStillMatched: true,
    csvRowCount: 0, csvBytes: 0, rawCsvFile: path.join(root, 'presents.csv'), metadataFile: path.join(root, 'metadata.json'),
    cleanup: { ownedSessionName: fakeSession, queryReturnCode: 4201, sessionAbsent: true, helperExitCode: null, error: null } };
  const identity = (id, parentId, name, filename, ancestor, gpuFlag = false) => ({
    id, parentId, name, path: filename, startTimeUtc: time, playerAncestor: ancestor, gpuFlag,
  });
  const metadata = { createdAtUtc: time, requestedSeconds: 30, sessionName: fakeSession, sessionAbsentBeforeCapture: true,
    presentMonSha256: 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af', signatureStatus: 'Valid',
    rawCsvFile: path.join(root, 'presents.csv'), target: {
      root: identity(101001, 101002, 'Lively.exe', 'C:\\private-person\\Lively.exe', null),
      player: identity(101003, 101001, 'Lively.Player.WebView2.exe', 'C:\\private-person\\Plugins\\WebView2\\Lively.Player.WebView2.exe', 101003),
      gpu: identity(targetId, 101004, 'msedgewebview2.exe', 'C:\\private-person\\WebView\\msedgewebview2.exe', 101003, true),
    },
    arguments: ['--process_id', String(targetId), '--timed', '30', '--terminate_after_timed', '--terminate_on_proc_exit',
      '--session_name', fakeSession, '--no_console_stats', '--no_track_input', '--no_track_gpu', '--v2_metrics',
      '--write_display_metadata', '--output_file', `"${path.join(root, 'presents.csv')}"`],
  };
  const f = { root, rows, summary, metadata, headers: [...headers], bom: true };
  f.save = (overrides = {}) => {
    const csv = `${f.bom ? '\uFEFF' : ''}${f.headers.map(quote).join(',')}\r\n` +
      f.rows.map(row => f.headers.map(name => quote(row[name])).join(',')).join('\r\n') + (f.rows.length ? '\r\n' : '');
    writeFileSync(path.join(root, 'presents.csv'), csv);
    summary.csvRowCount = f.rows.length; summary.csvBytes = Buffer.byteLength(csv);
    Object.assign(summary, overrides);
    writeFileSync(path.join(root, 'summary.json'), `\uFEFF${JSON.stringify(summary)}`);
    writeFileSync(path.join(root, 'metadata.json'), JSON.stringify(metadata));
  };
  f.save(); return f;
}

function ownerFixture(t, explicitGpuFlag = false) {
  const f = fixture(t);
  const identity = (id, parentId, name, filename) => ({ id, parentId, name, path: filename, startTimeUtc: time });
  f.metadata.target = {
    root: identity(101001, 101002, 'DualWindowHost.exe', 'C:\\private-person\\DualWindowHost.exe'),
    player: null,
    browser: identity(101003, 101001, 'msedgewebview2.exe', 'C:\\private-person\\WebView\\msedgewebview2.exe'),
    gpu: identity(targetId, 101003, 'msedgewebview2.exe', 'C:\\private-person\\WebView\\msedgewebview2.exe'),
    scopeKind: 'Owner', ownerProfileVerified: true,
  };
  f.summary.hostKind = f.metadata.hostKind = 'Owner';
  f.summary.ownerProfileVerified = f.metadata.ownerProfileVerified = true;
  f.metadata.hostExecutable = f.metadata.target.root.path;
  if (explicitGpuFlag) {
    for (const part of ['root', 'browser', 'gpu']) {
      f.metadata.target[part].gpuFlag = part === 'gpu'; f.metadata.target[part].playerAncestor = null;
    }
  }
  f.save(); return f;
}

test('initial Owner selector records validate direct identity chain without rewriting absent derived flags', t => {
  const f = ownerFixture(t), names = ['summary.json', 'metadata.json', 'presents.csv'];
  const before = names.map(name => readFileSync(path.join(f.root, name)));
  const report = summarizePresents(f.root);
  assert.equal(report.status, 'summarized'); assert.equal(report.hostKind, 'Owner');
  assert.deepEqual(report.recordedScope, { ownerProfileVerified: true, gpuRoleEvidence: 'initial-owner-selector-role' });
  assert.equal(report.recordCount, 4); assert.equal(report.contentFramesPerSecond, null);
  for (const value of [String(targetId), 'ABCDE987654', fakeSession, 'private-person', f.root]) assert.equal(JSON.stringify(report).includes(value), false);
  names.forEach((name, index) => assert.deepEqual(readFileSync(path.join(f.root, name)), before[index]));
});

test('Owner records with newly preserved explicit GPU role fields retain the same scoped calculation', t => {
  const f = ownerFixture(t, true), report = summarizePresents(f.root);
  assert.equal(report.status, 'summarized'); assert.equal(report.recordedScope.gpuRoleEvidence, 'explicit-selector-flag');
  closeTo(report.groups[0].present.eventsPerSecond, 3000 / 31);
  delete f.metadata.target.gpu.gpuFlag; f.save();
  assert.throws(() => summarizePresents(f.root), /invalid-recorded-target/, 'mixed partial role metadata must not silently downgrade to the initial format');
});

test('new Lively kind markers preserve original selector ancestor and GPU assertions', t => {
  const f = fixture(t);
  f.summary.hostKind = f.metadata.hostKind = f.metadata.target.scopeKind = 'Lively';
  f.summary.ownerProfileVerified = f.metadata.ownerProfileVerified = f.metadata.target.ownerProfileVerified = false;
  f.metadata.hostExecutable = f.metadata.target.root.path; f.save();
  const report = summarizePresents(f.root);
  assert.equal(report.status, 'summarized'); assert.equal(report.hostKind, 'Lively');
  assert.deepEqual(report.recordedScope, { ownerProfileVerified: false, gpuRoleEvidence: 'explicit-selector-flag' });
});

test('Owner kind and profile assertions must agree in summary metadata and target rather than inferring from paths', t => {
  const cases = [
    f => { delete f.summary.hostKind; }, f => { f.metadata.hostKind = 'Lively'; }, f => { f.metadata.target.scopeKind = 'Unknown'; },
    f => { delete f.metadata.target.ownerProfileVerified; }, f => { f.summary.ownerProfileVerified = false; },
    f => { f.metadata.ownerProfileVerified = 'true'; },
    f => { delete f.summary.hostKind; delete f.metadata.hostKind; delete f.metadata.target.scopeKind; },
  ];
  for (const change of cases) { const f = ownerFixture(t); change(f); f.save(); assert.throws(() => summarizePresents(f.root), /invalid-recorded-scope/); }
});

test('Owner accepts only one direct host browser GPU chain with matching names paths and recorded host', t => {
  const cases = [
    f => { f.metadata.target.root.name = 'Lively.exe'; },
    f => { f.metadata.target.browser.parentId = 101004; },
    f => { f.metadata.target.gpu.parentId = f.metadata.target.root.id; },
    f => { f.metadata.target.browser.id = f.metadata.target.gpu.id; },
    f => { f.metadata.target.gpu.path = 'C:\\other\\msedgewebview2.exe'; },
    f => { f.metadata.hostExecutable = 'C:\\other\\DualWindowHost.exe'; },
    f => { f.metadata.target.player = { ...f.metadata.target.browser }; },
    f => { f.metadata.target.gpu.gpuFlag = false; },
    f => { f.metadata.target.browser.gpuFlag = true; },
    f => { f.metadata.target.gpu.playerAncestor = f.metadata.target.browser.id; },
    f => { f.metadata.target.browser.path = 'WebView\\msedgewebview2.exe'; },
    f => { f.metadata.target.gpu.name = 'unrelated.exe'; },
  ];
  for (const change of cases) { const f = ownerFixture(t); change(f); f.save(); assert.throws(() => summarizePresents(f.root), /invalid-recorded-target/); }
});

test('Owner chronology and CLI output preserve validation and identifier redaction', t => {
  const f = ownerFixture(t);
  f.metadata.target.browser.startTimeUtc = '2027-01-01T00:00:00.0000000Z'; f.save();
  assert.throws(() => summarizePresents(f.root), /invalid-recorded-target/);
  f.metadata.target.browser.startTimeUtc = time; f.save();
  const child = spawnSync(process.execPath, [cli, f.root], { encoding: 'utf8', windowsHide: true });
  assert.equal(child.status, 0); assert.equal(JSON.parse(child.stdout).hostKind, 'Owner'); assert.equal(child.stderr, '');
  for (const value of [String(targetId), 'ABCDE987654', fakeSession, 'private-person', f.root]) assert.equal(child.stdout.includes(value), false);
});

test('derives Present and display intervals separately, preserves NA and leaves private inputs unchanged', t => {
  const f = fixture(t);
  const before = ['summary.json', 'metadata.json', 'presents.csv'].map(name => [name, readFileSync(path.join(f.root, name))]);
  const report = summarizePresents(f.root), group = report.groups[0];
  assert.equal(report.status, 'summarized'); assert.equal(report.singleGroup, true); assert.equal(report.singleSwapchain, true);
  assert.equal(group.attributionComplete, true); assert.equal(group.recordCount, 4);
  assert.deepEqual(group.present.adjacentIntervalsMs, { availableSamples: 3, min: 8, max: 12, mean: 31 / 3, p50: 11, p95: 12, p99: 12 });
  closeTo(group.present.eventsPerSecond, 3000 / 31);
  assert.equal(group.cpuFrameTimeMs.mean, 10, 'CPU FrameTime must not be used as the Present interval');
  assert.equal(group.display.adjacentIntervalsMs.availableSamples, 2);
  closeTo(group.display.eventsPerSecond, 2000 / 30);
  assert.equal(group.reportedNotDisplayedCount, 1); assert.equal(group.reportedDisplayedCount, 3);
  assert.equal(group.reportedNotDisplayedFraction, 0.25); assert.equal(group.unknownDisplayStatusCount, 0);
  assert.equal(group.displayLatencyMs.availableSamples, 3); assert.equal(group.displayDurationMs.min, 10);
  assert.equal(group.boundary.finalKnownDisplayEndMs, 45);
  assert.equal(report.aggregateEventsPerSecond, null); assert.equal(report.contentFramesPerSecond, null);
  assert.match(report.limits.join(' '), /not identify wallpaper content frames/);
  for (const privateValue of [String(targetId), 'ABCDE987654', fakeSession, 'private-person', f.root]) {
    assert.equal(JSON.stringify(report).includes(privateValue), false);
  }
  for (const [name, bytes] of before) assert.deepEqual(readFileSync(path.join(f.root, name)), bytes);
});

test('nearest rank quantiles use ceil(p*N), including singleton and empty distributions', () => {
  const stats = nearestRankStats(Array.from({ length: 20 }, (_, index) => 20 - index));
  assert.equal(stats.p50, 10); assert.equal(stats.p95, 19); assert.equal(stats.p99, 20); assert.equal(stats.mean, 10.5);
  assert.equal(nearestRankStats([0]).p99, 0);
  assert.deepEqual(nearestRankStats([]), { availableSamples: 0, min: null, max: null, mean: null, p50: null, p95: null, p99: null });
});

test('CSV accepts reordered columns, quoted commas/newlines/quotes and BOM without exporting unknown fields', t => {
  const f = fixture(t); f.headers.reverse(); f.headers.push('PrivateNote');
  f.rows[0].PrivateNote = 'private-person, "quoted"\r\nC:\\secret\\file';
  f.save();
  const parsed = parsePresentCsv(readFileSync(path.join(f.root, 'presents.csv'), 'utf8'));
  assert.equal(parsed.rows[0].PrivateNote, f.rows[0].PrivateNote);
  const report = summarizePresents(f.root);
  assert.equal(report.recordCount, 4); assert.equal(JSON.stringify(report).includes('secret'), false);
  for (const value of ['a,a\n1,2', 'a,A\n1,2', 'a,b\n1', 'a,b\n"1,2', 'a,b\n"1"x,2', 'a,b\n1"x,2']) {
    assert.throws(() => parsePresentCsv(value), /csv/);
  }
});

test('separates swapchain, display source and layer and never combines their rates', t => {
  const f = fixture(t), original = [...f.rows];
  f.rows.push(...original.map(row => ({ ...row, VidPnSourceId: 1 })),
    ...original.map(row => ({ ...row, VidPnSourceId: 1, LayerIndex: 1 })),
    ...original.map(row => ({ ...row, SwapChainAddress: '0x12345678' })));
  f.save();
  const report = summarizePresents(f.root);
  assert.equal(report.groupCount, 4); assert.equal(report.swapchainCount, 2);
  assert.equal(report.singleGroup, false); assert.equal(report.singleSwapchain, false);
  assert.ok(report.reasons.includes('multiple-groups-not-combined'));
  assert.equal(report.aggregateEventsPerSecond, null);
  assert.deepEqual(report.groups.map(group => group.recordCount), [4, 4, 4, 4]);
  assert.equal(report.groups[0].swapchainAlias, report.groups[2].swapchainAlias);
  assert.notEqual(report.groups[0].displaySourceAlias, report.groups[1].displaySourceAlias);
  assert.notEqual(report.groups[1].layerAlias, report.groups[2].layerAlias);
  for (const group of report.groups) closeTo(group.present.eventsPerSecond, 3000 / 31);
});

test('missing timing breaks adjacency without creating zero or bridging unknown events', t => {
  const f = fixture(t);
  f.rows[1].CPUBusy = 'NA'; f.rows[1].DisplayedTime = ''; f.rows[0].CPUBusy = 0;
  f.save();
  const group = summarizePresents(f.root).groups[0];
  assert.equal(group.present.eventsPerSecond, null); assert.equal(group.present.missingTimestampCount, 1);
  assert.equal(group.present.firstTimestampMs, 0, 'Known zero remains a timestamp');
  assert.equal(group.present.adjacentIntervalsMs.availableSamples, 1);
  assert.equal(group.present.adjacentIntervalsMs.min, 11);
  assert.equal(group.display.eventsPerSecond, null); assert.equal(group.unknownDisplayStatusCount, 1);
  assert.equal(group.reportedNotDisplayedCount, 0); assert.equal(group.display.adjacentIntervalsMs.availableSamples, 1);
  assert.equal(group.display.adjacentIntervalsMs.mean, 10);
});

test('missing display columns stay unknown; known NA display records mean not-displayed without a 0 display rate', t => {
  const f = fixture(t);
  f.headers = f.headers.filter(name => !['DisplayedTime', 'DisplayLatency', 'LayerIndex'].includes(name)); f.save();
  let report = summarizePresents(f.root), group = report.groups[0];
  assert.equal(group.unknownDisplayStatusCount, 4); assert.equal(group.reportedNotDisplayedCount, 0);
  assert.equal(group.display.eventsPerSecond, null); assert.equal(group.displayDurationMs.mean, null);
  assert.equal(group.layerAlias, null); assert.ok(report.reasons.includes('missing-display-attribution'));
  f.headers = [...headers]; f.rows.forEach(row => { row.DisplayedTime = 'NA'; row.DisplayLatency = 'NA'; }); f.save();
  group = summarizePresents(f.root).groups[0];
  assert.equal(group.reportedNotDisplayedCount, 4); assert.equal(group.reportedDisplayedCount, 0);
  assert.equal(group.display.eventsPerSecond, null); assert.equal(group.display.status, 'unavailable');
});

test('single record and nonmonotonic timestamps cannot establish a frame rate', t => {
  const f = fixture(t);
  f.rows.length = 1; f.save();
  let group = summarizePresents(f.root).groups[0];
  assert.equal(group.present.eventsPerSecond, null); assert.equal(group.display.eventsPerSecond, null);
  f.rows.push({ ...f.rows[0], CPUStartTime: 20 }, { ...f.rows[0], CPUStartTime: 10 }); f.save();
  group = summarizePresents(f.root).groups[0];
  assert.ok(group.present.reasons.includes('non-monotonic-timestamps'));
  assert.equal(group.present.eventsPerSecond, null); assert.equal(group.present.adjacentIntervalsMs.availableSamples, 0);
  assert.ok(group.display.reasons.includes('non-monotonic-timestamps')); assert.equal(group.display.eventsPerSecond, null);
});

test('capture boundary metrics do not substitute wrapper duration or hide a final displayed tail', t => {
  const f = fixture(t);
  f.rows.splice(0, 4, { ...f.rows[0], CPUStartTime: 29900, CPUBusy: 5, DisplayLatency: 20, DisplayedTime: 100 },
    { ...f.rows[3], CPUStartTime: 30000, CPUBusy: 5, DisplayLatency: 20, DisplayedTime: 100 });
  f.save();
  const group = summarizePresents(f.root).groups[0];
  assert.equal(group.present.eventsPerSecond, 10); assert.equal(group.display.eventsPerSecond, 10);
  assert.equal(group.boundary.presentTimestampsOutsideRequestedWindow, 1);
  assert.equal(group.boundary.displayTimestampsOutsideRequestedWindow, 1);
  assert.equal(group.boundary.displayEndsBeyondRequestedWindow, 2);
  assert.equal(group.boundary.finalKnownDisplayEndMs, 30120);
});

test('source failure, process identity loss, timeout and unconfirmed cleanup suppress every metric', t => {
  for (const [change, reason] of [
    [f => { f.summary.status = 'unavailable'; f.summary.reason = 'C:\\private-person\\failure'; }, 'capture-not-successful'],
    [f => { f.summary.toolExitCode = 1; }, 'capture-exit-not-confirmed'],
    [f => { f.summary.timedOut = true; }, 'capture-exit-not-confirmed'],
    [f => { f.summary.targetIdentityStillMatched = false; }, 'target-identity-not-confirmed'],
    [f => { f.summary.cleanup.sessionAbsent = false; }, 'cleanup-not-confirmed'],
    [f => { f.summary.cleanup.queryReturnCode = 0; }, 'cleanup-not-confirmed'],
    [f => { f.summary.cleanup.error = 'C:\\private-person\\cleanup'; }, 'cleanup-not-confirmed'],
  ]) {
    const f = fixture(t); change(f); f.save(); unlinkSync(path.join(f.root, 'presents.csv'));
    const report = summarizePresents(f.root);
    assert.equal(report.status, 'unavailable'); assert.ok(report.reasons.includes(reason));
    assert.deepEqual(report.groups, []); assert.equal(report.recordCount, null);
    assert.equal(report.aggregateEventsPerSecond, null); assert.equal(JSON.stringify(report).includes('private-person'), false);
  }
});

test('empty successful-looking captures remain unavailable and count/byte mismatches are rejected', t => {
  const f = fixture(t); f.rows.length = 0; f.save();
  const report = summarizePresents(f.root);
  assert.equal(report.status, 'unavailable'); assert.deepEqual(report.reasons, ['no-present-records']);
  assert.equal(report.recordCount, 0); assert.equal(report.aggregateEventsPerSecond, null);
  f.save({ csvRowCount: 1 }); assert.throws(() => summarizePresents(f.root), /csv-row-count-mismatch/);
  f.save({ csvBytes: 1 }); assert.throws(() => summarizePresents(f.root), /csv-byte-count-mismatch/);
});

test('refuses wrong PID, mismatched recorded ownership/tool/session/arguments and unsupported fields', t => {
  const cases = [
    [f => { f.rows[2].ProcessID = 345678; }, /csv-target-scope-mismatch/],
    [f => { f.rows[2].Application = 'unrelated.exe'; }, /csv-target-scope-mismatch/],
    [f => { f.metadata.target.gpu.playerAncestor = 12; }, /invalid-recorded-target/],
    [f => { f.metadata.target.gpu.gpuFlag = false; }, /invalid-recorded-target/],
    [f => { f.metadata.signatureStatus = 'Unknown'; }, /unverified-capture-tool/],
    [f => { f.metadata.presentMonSha256 = '0'.repeat(64); }, /unverified-capture-tool/],
    [f => { f.metadata.sessionAbsentBeforeCapture = false; }, /invalid-owned-session/],
    [f => { f.summary.cleanup.ownedSessionName = 'unrelated'; }, /invalid-owned-session/],
    [f => { f.metadata.arguments[0] = '--process_name'; }, /unexpected-capture-arguments/],
    [f => { f.metadata.arguments.push('--captureall'); }, /unexpected-capture-arguments/],
    [f => { f.headers = f.headers.filter(name => name !== 'CPUBusy'); }, /unsupported-csv-schema/],
    [f => { f.rows[1].CPUBusy = 'Infinity'; }, /invalid-numeric-csv-field/],
    [f => { f.rows[1].CPUStartTime = '-1'; }, /invalid-numeric-csv-field/],
    [f => { f.rows[1].SwapChainAddress = 'NA'; }, /invalid-swapchain-identity/],
  ];
  for (const [change, expected] of cases) {
    const f = fixture(t); change(f); f.save(); assert.throws(() => summarizePresents(f.root), expected);
  }
});

test('target starts must precede metadata and metadata must precede capture at millisecond precision', t => {
  const futureGpu = fixture(t);
  futureGpu.metadata.target.gpu.startTimeUtc = '2027-01-01T00:00:00.0000000Z'; futureGpu.save();
  assert.throws(() => summarizePresents(futureGpu.root), /invalid-capture-chronology/);
  const futureTree = fixture(t);
  for (const target of Object.values(futureTree.metadata.target)) target.startTimeUtc = '2027-01-01T00:00:00.0000000Z';
  futureTree.save();
  assert.throws(() => summarizePresents(futureTree.root), /invalid-capture-chronology/);
  const reversedMetadata = fixture(t);
  reversedMetadata.metadata.createdAtUtc = '2026-10-24T10:00:00.0010000Z'; reversedMetadata.save();
  assert.throws(() => summarizePresents(reversedMetadata.root), /invalid-capture-chronology/);
  const lateTarget = fixture(t);
  lateTarget.metadata.target.gpu.startTimeUtc = '2026-10-24T10:00:00.0010000Z';
  lateTarget.summary.startedAtUtc = '2026-10-24T10:00:00.0020000Z'; lateTarget.save();
  assert.throws(() => summarizePresents(lateTarget.root), /invalid-capture-chronology/);
  const sameMillisecond = fixture(t);
  sameMillisecond.metadata.target.gpu.startTimeUtc = '2026-10-24T10:00:00.0000009Z';
  sameMillisecond.metadata.createdAtUtc = '2026-10-24T10:00:00.0000008Z';
  sameMillisecond.summary.startedAtUtc = '2026-10-24T10:00:00.0000007Z'; sameMillisecond.save();
  assert.equal(summarizePresents(sameMillisecond.root).status, 'summarized');
});

test('rejects repository input, traversal, foreign file references and junction escapes', t => {
  const f = fixture(t), other = fixture(t);
  assert.throws(() => summarizePresents(repository), /repository-input-rejected/);
  assert.throws(() => summarizePresents(path.join(path.parse(f.root).root, 'not-an-approved-private-directory')), /private-directory-required/);
  assert.throws(() => summarizePresents(`${f.root}${path.sep}..${path.sep}${path.basename(f.root)}`), /unsafe-input-path/);
  for (const reference of ['raw/../presents.csv', path.join(other.root, 'presents.csv'), 'another.csv']) {
    f.summary.rawCsvFile = reference; f.save();
    assert.throws(() => summarizePresents(f.root), /unsafe-input-path|input-outside-capture/);
  }
  f.summary.rawCsvFile = path.join(f.root, 'presents.csv'); f.metadata.rawCsvFile = path.join(other.root, 'presents.csv'); f.save();
  assert.throws(() => summarizePresents(f.root), /input-outside-capture/);
  f.metadata.rawCsvFile = f.summary.rawCsvFile;
  f.metadata.arguments[f.metadata.arguments.length - 1] = `"${path.join(other.root, 'presents.csv')}"`; f.save();
  assert.throws(() => summarizePresents(f.root), /input-outside-capture/);
  const linked = path.join(f.root, 'linked'); symlinkSync(other.root, linked, process.platform === 'win32' ? 'junction' : 'dir');
  assert.throws(() => summarizePresents(linked), /linked-input-rejected/);
});

test('unsafe mode/runtime strings never enter the aggregate and CLI errors contain no private path', t => {
  const f = fixture(t);
  f.rows[0].PresentMode = 'C:\\private-person\\secret'; f.rows[0].PresentRuntime = '0xABCDE987654'; f.save();
  const child = spawnSync(process.execPath, [cli, f.root], { encoding: 'utf8', windowsHide: true });
  assert.equal(child.status, 0); assert.equal(child.stderr, '');
  const report = JSON.parse(child.stdout);
  assert.ok(report.groups[0].presentModes.some(mode => mode.label === 'Unknown' && mode.records === 1));
  for (const value of [f.root, 'private-person', 'ABCDE987654', String(targetId)]) assert.equal(child.stdout.includes(value), false);
  const missing = path.join(f.root, 'missing-private-directory'); mkdirSync(missing);
  const failure = spawnSync(process.execPath, [cli, missing], { encoding: 'utf8', windowsHide: true });
  assert.equal(failure.status, 1); assert.equal(failure.stderr, '');
  assert.deepEqual(JSON.parse(failure.stdout), { status: 'unavailable', error: 'input-unavailable' });
  assert.equal(failure.stdout.includes(missing), false);
  f.summary.status = 'unavailable'; f.save();
  const unavailable = spawnSync(process.execPath, [cli, f.root], { encoding: 'utf8', windowsHide: true });
  assert.equal(unavailable.status, 2); assert.equal(JSON.parse(unavailable.stdout).status, 'unavailable');
});

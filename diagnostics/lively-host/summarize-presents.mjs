// Offline, read-only summarizer for measure-presents.ps1, PresentMon 2.6.0 --v2_metrics.
// Version/field compatibility: https://github.com/GameTechDev/PresentMon/blob/v2.6.0/README-ConsoleApplication.md
// The versioned documentation links --v2_metrics to its v2 column definitions.
import { lstatSync, readFileSync, realpathSync, statSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repository = realpathSync(fileURLToPath(new URL('../..', import.meta.url)));
const privateRoots = [path.resolve(repository, '../tools/temp'), path.resolve(os.tmpdir())];
const officialHash = 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af';
const maxCsvBytes = 128 * 1024 * 1024;
const knownModes = new Set(['Hardware: Legacy Flip', 'Hardware: Legacy Copy to front buffer',
  'Hardware: Independent Flip', 'Composed: Flip', 'Composed: Copy with GPU GDI',
  'Composed: Copy with CPU GDI', 'Hardware Composed: Independent Flip']);
const knownRuntimes = new Set(['DXGI', 'D3D9', 'Other']);
const limits = [
  'Null means unavailable (NA), never a substituted zero. Failed or empty captures do not establish 0 FPS.',
  'Groups separate the recorded process, swapchain, display source and layer; aliases contain no original identifiers. Group rates are never added or averaged.',
  'Present timestamps are inferred as CPUStartTime + CPUBusy. FrameTime is CPU frame-start spacing, not Present-to-Present spacing.',
  'Display starts are inferred as CPUStartTime + DisplayLatency. DisplayedTime=NA means PresentMon reports that record as not displayed; it does not prove a game simulation dropped a frame.',
  'Rates use N-1 adjacent intervals between first and last known timestamps, not wrapper elapsed time. Incomplete timestamps suppress the corresponding rate.',
  'First and last events and final display duration can straddle capture boundaries. This short window does not establish long-term pacing.',
  'A WebView GPU swapchain, display source, layer or Composed: Flip mode does not identify wallpaper content frames. Content FPS and DWM/content attribution remain unverified.',
  'Recorded target ownership, tool identity and cleanup assertions are checked offline, not independently authenticated or revalidated against live processes.',
  'Input/GPU-work tracking is disabled in this capture. No GPU busy time, power, temperature or VRAM conclusion follows from this report.',
];

class InputError extends Error {}
function requireInput(condition, code) { if (!condition) throw new InputError(code); }
const nonnegative = value => typeof value === 'number' && Number.isFinite(value) && value >= 0;
const count = value => Number.isSafeInteger(value) && value >= 0;
const pid = value => Number.isInteger(value) && value > 0 && value <= 0xffffffff;
function within(root, candidate) {
  const relative = path.relative(root, candidate);
  return relative === '' || (relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative));
}
function safePath(value) {
  return typeof value === 'string' && value.length > 0 && !value.includes('\0') &&
    !value.split(/[\\/]/).includes('..');
}
function noLinks(candidate) {
  for (let current = candidate; ; current = path.dirname(current)) {
    requireInput(!lstatSync(current).isSymbolicLink(), 'linked-input-rejected');
    if (current === path.dirname(current)) break;
  }
}
function parseTime(value) {
  return typeof value === 'string' && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?Z$/.test(value)
    ? Date.parse(value) : NaN;
}
function decode(bytes) {
  try { return new TextDecoder('utf-8', { fatal: true }).decode(bytes).replace(/^\uFEFF/, ''); }
  catch { throw new InputError('invalid-utf8-input'); }
}

// Strict CSV quoting, including escaped quotes, embedded commas/newlines and BOM.
// Unknown columns may be present, but duplicate headers and uneven rows are refused.
export function parsePresentCsv(text) {
  requireInput(typeof text === 'string' && Buffer.byteLength(text) <= maxCsvBytes, 'csv-size-limit');
  text = text.replace(/^\uFEFF/, '');
  const records = [];
  let record = [], field = '', quoted = false, closed = false;
  function endField() {
    requireInput(field.length <= 16384 && record.length < 256, 'csv-field-limit');
    record.push(field); field = ''; closed = false;
  }
  function endRecord() {
    endField();
    if (record.some(value => value !== '')) records.push(record);
    requireInput(records.length <= 1000001, 'csv-row-limit');
    record = [];
  }
  for (let index = 0; index < text.length; index++) {
    const char = text[index];
    if (quoted) {
      if (char === '"') {
        if (text[index + 1] === '"') { field += '"'; index++; }
        else { quoted = false; closed = true; }
      } else field += char;
    } else if (char === ',') endField();
    else if (char === '\r' || char === '\n') {
      if (char === '\r' && text[index + 1] === '\n') index++;
      endRecord();
    } else if (char === '"') {
      requireInput(field === '' && !closed, 'malformed-csv-quotes'); quoted = true;
    } else {
      requireInput(!closed, 'malformed-csv-quotes'); field += char;
    }
    requireInput(field.length <= 16384, 'csv-field-limit');
  }
  requireInput(!quoted, 'malformed-csv-quotes');
  if (record.length || field.length || closed) endRecord();
  if (!records.length) return { headers: [], rows: [] };
  const headers = records.shift().map(value => value.trim());
  requireInput(headers.every(Boolean) && new Set(headers.map(value => value.toLowerCase())).size === headers.length, 'invalid-csv-header');
  requireInput(records.every(row => row.length === headers.length), 'csv-column-count-mismatch');
  return { headers, rows: records.map(row => Object.fromEntries(headers.map((name, index) => [name, row[index]]))) };
}

export function nearestRankStats(values) {
  requireInput(Array.isArray(values) && values.every(nonnegative), 'invalid-statistic-input');
  const sorted = [...values].sort((left, right) => left - right);
  const quantile = probability => sorted.length ? sorted[Math.ceil(probability * sorted.length) - 1] : null;
  return { availableSamples: sorted.length, min: sorted[0] ?? null, max: sorted.at(-1) ?? null,
    mean: sorted.length ? sorted.reduce((sum, value) => sum + value, 0) / sorted.length : null,
    p50: quantile(0.50), p95: quantile(0.95), p99: quantile(0.99) };
}
function numberCell(row, name) {
  const value = row[name]?.trim();
  if (value === undefined || value === '' || value === 'NA') return null;
  requireInput(/^(?:\d+\.?\d*|\.\d+)$/.test(value), 'invalid-numeric-csv-field');
  const parsed = Number(value);
  requireInput(nonnegative(parsed) && parsed <= 86400000, 'numeric-csv-field-out-of-range');
  return parsed;
}
function dimension(row, name) {
  const value = row[name]?.trim();
  if (value === undefined || value === '' || value === 'NA') return null;
  requireInput(/^\d+$/.test(value) && Number.isSafeInteger(Number(value)), 'invalid-display-dimension');
  return Number(value);
}
function timeline(timestamps) {
  const known = timestamps.filter(nonnegative), intervals = [];
  let nonMonotonic = false, lastKnown = null;
  for (let index = 0; index < timestamps.length; index++) {
    const time = timestamps[index];
    if (time === null) continue;
    if (lastKnown !== null && time < lastKnown) nonMonotonic = true;
    lastKnown = time;
    if (index > 0 && timestamps[index - 1] !== null && time >= timestamps[index - 1]) intervals.push(time - timestamps[index - 1]);
  }
  const reasons = [];
  if (known.length < 2) reasons.push('insufficient-timestamps');
  if (known.length !== timestamps.length) reasons.push('missing-timestamps');
  if (nonMonotonic) reasons.push('non-monotonic-timestamps');
  const spanMs = known.length ? known.at(-1) - known[0] : null;
  if (known.length >= 2 && spanMs <= 0) reasons.push('no-positive-span');
  return { status: reasons.length ? 'unavailable' : 'available', reasons,
    timestampCount: known.length, missingTimestampCount: timestamps.length - known.length,
    firstTimestampMs: known[0] ?? null, lastTimestampMs: known.at(-1) ?? null,
    observedSpanMs: nonMonotonic ? null : spanMs,
    adjacentIntervalsMs: nearestRankStats(nonMonotonic ? [] : intervals),
    eventsPerSecond: reasons.length ? null : (known.length - 1) * 1000 / spanMs };
}
function groupedLabels(rows, column, allowed) {
  const counts = new Map();
  for (const row of rows) {
    const value = allowed.has(row[column]) ? row[column] : 'Unknown';
    counts.set(value, (counts.get(value) ?? 0) + 1);
  }
  return [...counts].map(([label, records]) => ({ label, records }));
}
function summarizeGroup(rows, aliases, requestedMs) {
  const presentTimes = [], displayTimes = [], cpuFrameTimes = [], displayDurations = [], displayLatencies = [], displayEnds = [];
  let reportedNotDisplayedCount = 0, unknownDisplayStatusCount = 0, reportedDisplayedCount = 0;
  for (const row of rows) {
    const cpu = numberCell(row, 'CPUStartTime'), busy = numberCell(row, 'CPUBusy');
    const frameTime = numberCell(row, 'FrameTime'), latency = numberCell(row, 'DisplayLatency');
    const duration = numberCell(row, 'DisplayedTime');
    presentTimes.push(cpu === null || busy === null ? null : cpu + busy);
    if (frameTime !== null) cpuFrameTimes.push(frameTime);
    if (latency !== null) displayLatencies.push(latency);
    if (row.DisplayedTime?.trim() === 'NA') {
      reportedNotDisplayedCount++;
      // A known non-displayed present does not interrupt adjacent DISPLAY events.
    } else if (duration === null) {
      unknownDisplayStatusCount++;
      displayTimes.push(null); // Do not bridge an unknown displayed event.
    } else {
      reportedDisplayedCount++; displayDurations.push(duration);
      const displayStart = cpu === null || latency === null ? null : cpu + latency;
      displayTimes.push(displayStart);
      if (displayStart !== null) displayEnds.push(displayStart + duration);
    }
  }
  const present = timeline(presentTimes), display = timeline(displayTimes);
  return { ...aliases, recordCount: rows.length,
    attributionComplete: aliases.displaySourceAlias !== null && aliases.layerAlias !== null,
    present, display, reportedDisplayedCount, reportedNotDisplayedCount, unknownDisplayStatusCount,
    reportedNotDisplayedFraction: rows.length ? reportedNotDisplayedCount / rows.length : null,
    cpuFrameTimeMs: nearestRankStats(cpuFrameTimes), displayDurationMs: nearestRankStats(displayDurations),
    displayLatencyMs: nearestRankStats(displayLatencies),
    presentModes: groupedLabels(rows, 'PresentMode', knownModes),
    presentRuntimes: groupedLabels(rows, 'PresentRuntime', knownRuntimes),
    boundary: { requestedWindowMs: requestedMs, finalKnownDisplayEndMs: displayEnds.at(-1) ?? null,
      presentTimestampsOutsideRequestedWindow: presentTimes.filter(value => value !== null && value >= requestedMs).length,
      displayTimestampsOutsideRequestedWindow: displayTimes.filter(value => value !== null && value >= requestedMs).length,
      displayEndsBeyondRequestedWindow: displayEnds.filter(value => value > requestedMs).length },
  };
}

function validateMetadata(summary, metadata, checkReference) {
  requireInput(summary && metadata && typeof summary === 'object' && typeof metadata === 'object', 'invalid-json-schema');
  requireInput(['captured', 'unavailable'].includes(summary.status) && count(summary.csvRowCount) && count(summary.csvBytes), 'invalid-capture-summary');
  requireInput(Number.isInteger(summary.requestedSeconds) && summary.requestedSeconds >= 5 && summary.requestedSeconds <= 60 &&
    summary.requestedSeconds === metadata.requestedSeconds && nonnegative(summary.elapsedSeconds) && summary.elapsedSeconds > 0 &&
    Number.isFinite(parseTime(summary.startedAtUtc)) && Number.isFinite(parseTime(metadata.createdAtUtc)), 'invalid-capture-window');
  checkReference(summary.rawCsvFile, 'presents.csv'); checkReference(metadata.rawCsvFile, 'presents.csv');
  checkReference(summary.metadataFile, 'metadata.json');
  requireInput(/^FishingDesktop-[a-f0-9]{32}$/.test(metadata.sessionName) &&
    metadata.sessionAbsentBeforeCapture === true && summary.cleanup?.ownedSessionName === metadata.sessionName, 'invalid-owned-session');
  requireInput(metadata.presentMonSha256 === officialHash && metadata.signatureStatus === 'Valid', 'unverified-capture-tool');
  const target = metadata.target;
  const kinds = [summary.hostKind, metadata.hostKind, target?.scopeKind];
  const legacyLively = kinds.every(value => value === undefined);
  const hostKind = legacyLively ? 'Lively' : kinds[0];
  const profileAssertions = [summary.ownerProfileVerified, metadata.ownerProfileVerified, target?.ownerProfileVerified];
  requireInput(legacyLively ? profileAssertions.every(value => value === undefined) :
    ['Lively', 'Owner'].includes(hostKind) && kinds.every(value => value === hostKind) &&
      profileAssertions.every(value => value === (hostKind === 'Owner')), 'invalid-recorded-scope');
  const parts = hostKind === 'Owner' ? ['root', 'browser', 'gpu'] : ['root', 'player', 'gpu'];
  requireInput(target && parts.every(part => target[part] && pid(target[part].id) &&
    pid(target[part].parentId) && Number.isFinite(parseTime(target[part].startTimeUtc)) &&
    typeof target[part].name === 'string' && safePath(target[part].path) && path.win32.isAbsolute(target[part].path)), 'invalid-recorded-target');
  requireInput(new Set(parts.map(part => target[part].id)).size === 3 && parts.every(part =>
    path.win32.basename(target[part].path).toLowerCase() === target[part].name.toLowerCase()), 'invalid-recorded-target');
  const samePath = (left, right) => typeof left === 'string' && typeof right === 'string' &&
    path.win32.normalize(left).toLowerCase() === path.win32.normalize(right).toLowerCase();
  let gpuRoleEvidence;
  if (hostKind === 'Owner') {
    // Initial Owner records predate the explicit gpuFlag. Their selector-role
    // assertion is accepted only with all three coherent scope/profile markers
    // and this exact direct parent chain. Never infer Owner from a filename.
    const initialOwnerRoles = parts.every(part => target[part].gpuFlag === undefined && target[part].playerAncestor === undefined);
    const explicitOwnerRoles = target.gpu.gpuFlag === true && ['root', 'browser'].every(part => target[part].gpuFlag === false) &&
      parts.every(part => target[part].playerAncestor === null);
    requireInput(target.player === null && target.root.name.toLowerCase() === 'dualwindowhost.exe' &&
      target.browser.name.toLowerCase() === 'msedgewebview2.exe' && target.gpu.name.toLowerCase() === 'msedgewebview2.exe' &&
      target.browser.parentId === target.root.id && target.gpu.parentId === target.browser.id &&
      samePath(target.browser.path, target.gpu.path) && safePath(metadata.hostExecutable) && samePath(metadata.hostExecutable, target.root.path) &&
      (initialOwnerRoles || explicitOwnerRoles) &&
      parseTime(target.root.startTimeUtc) <= parseTime(target.browser.startTimeUtc) &&
      parseTime(target.browser.startTimeUtc) <= parseTime(target.gpu.startTimeUtc), 'invalid-recorded-target');
    gpuRoleEvidence = initialOwnerRoles ? 'initial-owner-selector-role' : 'explicit-selector-flag';
  } else {
    requireInput(target.root.name.toLowerCase() === 'lively.exe' && target.player.name.toLowerCase() === 'lively.player.webview2.exe' &&
      target.gpu.name.toLowerCase() === 'msedgewebview2.exe' && target.gpu.gpuFlag === true &&
      target.player.playerAncestor === target.player.id && target.gpu.playerAncestor === target.player.id &&
      samePath(target.player.path, path.win32.join(path.win32.dirname(target.root.path), 'Plugins', 'WebView2', 'Lively.Player.WebView2.exe')) &&
      (legacyLively || (safePath(metadata.hostExecutable) && samePath(metadata.hostExecutable, target.root.path))) &&
      parseTime(target.root.startTimeUtc) <= parseTime(target.player.startTimeUtc) &&
      parseTime(target.player.startTimeUtc) <= parseTime(target.gpu.startTimeUtc), 'invalid-recorded-target');
    gpuRoleEvidence = 'explicit-selector-flag';
  }
  // Capture discovery precedes metadata creation, which precedes launch. Date.parse
  // deliberately compares milliseconds: sub-millisecond values in the same bucket
  // are equivalent at this precision, but a later millisecond cannot be accepted.
  const metadataTime = parseTime(metadata.createdAtUtc), captureTime = parseTime(summary.startedAtUtc);
  requireInput(parts.every(part => parseTime(target[part].startTimeUtc) <= metadataTime) &&
    metadataTime <= captureTime, 'invalid-capture-chronology');
  const args = metadata.arguments;
  requireInput(Array.isArray(args) && args.every(value => typeof value === 'string'), 'unexpected-capture-arguments');
  const expected = ['--process_id', String(target.gpu.id), '--timed', String(summary.requestedSeconds),
    '--terminate_after_timed', '--terminate_on_proc_exit', '--session_name', metadata.sessionName,
    '--no_console_stats', '--no_track_input', '--no_track_gpu', '--v2_metrics', '--write_display_metadata', '--output_file'];
  requireInput(args.length === expected.length + 1 && expected.every((value, index) => args[index] === value), 'unexpected-capture-arguments');
  const csvArgument = args.at(-1);
  checkReference(csvArgument.startsWith('"') && csvArgument.endsWith('"') ? csvArgument.slice(1, -1) : csvArgument, 'presents.csv');
  return { targetId: target.gpu.id, hostKind, ownerProfileVerified: hostKind === 'Owner', gpuRoleEvidence };
}

export function summarizePresents(directory) {
  requireInput(safePath(directory), 'unsafe-input-path');
  const lexical = path.resolve(directory);
  requireInput(!within(repository, lexical), 'repository-input-rejected');
  const privateRoot = privateRoots.find(root => within(root, lexical));
  requireInput(privateRoot, 'private-directory-required');
  noLinks(lexical);
  const root = realpathSync(lexical), actualPrivateRoot = realpathSync(privateRoot);
  requireInput(statSync(root).isDirectory() && within(actualPrivateRoot, root) && !within(repository, root), 'input-outside-private-directory');
  function checkReference(reference, expectedName) {
    requireInput(safePath(reference), 'unsafe-input-path');
    const candidate = path.resolve(root, reference), expected = path.join(root, expectedName);
    requireInput(path.relative(candidate, expected) === '' && within(root, candidate) && !within(repository, candidate), 'input-outside-capture');
  }
  function read(name, limit) {
    const candidate = path.join(root, name);
    noLinks(candidate);
    const actual = realpathSync(candidate), info = statSync(actual);
    requireInput(within(root, actual) && !within(repository, actual) && info.isFile() && info.size <= limit, 'invalid-input-file');
    const bytes = readFileSync(actual);
    requireInput(bytes.length === info.size && bytes.length <= limit, 'input-file-changed');
    return bytes;
  }
  function json(name) {
    try { return JSON.parse(decode(read(name, 1024 * 1024))); }
    catch (error) { if (error instanceof SyntaxError) throw new InputError('invalid-json-input'); throw error; }
  }
  const summary = json('summary.json'), metadata = json('metadata.json');
  const { targetId, hostKind, ownerProfileVerified, gpuRoleEvidence } = validateMetadata(summary, metadata, checkReference), reasons = [];
  if (summary.status !== 'captured' || summary.reason !== null) reasons.push('capture-not-successful');
  if (summary.toolExitCode !== 0 || summary.timedOut !== false) reasons.push('capture-exit-not-confirmed');
  if (summary.targetIdentityStillMatched !== true) reasons.push('target-identity-not-confirmed');
  if (summary.cleanup.sessionAbsent !== true || summary.cleanup.queryReturnCode !== 4201 || summary.cleanup.error !== null) reasons.push('cleanup-not-confirmed');
  const report = { schema: 1, status: 'unavailable', reasons, requestedSeconds: summary.requestedSeconds,
    hostKind, recordedScope: { ownerProfileVerified, gpuRoleEvidence },
    wrapperElapsedSeconds: summary.elapsedSeconds, quantileMethod: 'nearest-rank: sorted[ceil(p * N) - 1]',
    recordCount: null, groupCount: null, swapchainCount: null, singleSwapchain: null, singleGroup: null,
    aggregateEventsPerSecond: null, contentFramesPerSecond: null, groups: [], limits: [...limits] };
  if (reasons.length) return report;
  const bytes = read('presents.csv', maxCsvBytes);
  requireInput(bytes.length === summary.csvBytes, 'csv-byte-count-mismatch');
  const { headers, rows } = parsePresentCsv(decode(bytes));
  requireInput(rows.length === summary.csvRowCount, 'csv-row-count-mismatch');
  report.recordCount = rows.length;
  if (!rows.length) { reasons.push('no-present-records'); return report; }
  requireInput(['ProcessID', 'SwapChainAddress', 'CPUStartTime', 'CPUBusy'].every(name => headers.includes(name)), 'unsupported-csv-schema');
  const groups = new Map(), swapchains = new Map(), sources = new Map(), layers = new Map();
  function alias(map, key, prefix) {
    if (key === null) return null;
    if (!map.has(key)) map.set(key, `${prefix}-${map.size + 1}`);
    return map.get(key);
  }
  for (const row of rows) {
    requireInput(/^\d+$/.test(row.ProcessID) && Number(row.ProcessID) === targetId &&
      (row.Application === undefined || row.Application.toLowerCase() === 'msedgewebview2.exe'), 'csv-target-scope-mismatch');
    requireInput(/^0x[0-9a-f]{1,16}$/i.test(row.SwapChainAddress), 'invalid-swapchain-identity');
    const swapchain = BigInt(row.SwapChainAddress).toString(16);
    const source = dimension(row, 'VidPnSourceId'), layer = dimension(row, 'LayerIndex');
    const key = JSON.stringify([swapchain, source, layer]);
    if (!groups.has(key)) groups.set(key, { rows: [], aliases: { groupAlias: `group-${groups.size + 1}`,
      swapchainAlias: alias(swapchains, swapchain, 'swapchain'), displaySourceAlias: alias(sources, source, 'source'),
      layerAlias: layer === null ? null : alias(layers, JSON.stringify([source, layer]), 'layer') } });
    groups.get(key).rows.push(row);
  }
  report.groups = [...groups.values()].map(group => summarizeGroup(group.rows, group.aliases, summary.requestedSeconds * 1000));
  report.groupCount = groups.size; report.swapchainCount = swapchains.size;
  report.singleGroup = groups.size === 1; report.singleSwapchain = swapchains.size === 1;
  report.status = 'summarized';
  if (!report.singleGroup) reasons.push('multiple-groups-not-combined');
  if (report.groups.some(group => !group.attributionComplete)) reasons.push('missing-display-attribution');
  return report;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    requireInput(process.argv.length === 3, 'usage: node summarize-presents.mjs <private-capture-directory>');
    const report = summarizePresents(process.argv[2]);
    console.log(JSON.stringify(report, null, 2));
    if (report.status === 'unavailable') process.exitCode = 2;
  } catch (error) {
    // No raw filesystem errors, CSV strings, process IDs, addresses or paths.
    console.log(JSON.stringify({ status: 'unavailable', error: error instanceof InputError ? error.message : 'input-unavailable' }));
    process.exitCode = 1;
  }
}

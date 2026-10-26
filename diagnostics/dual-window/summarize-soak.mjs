import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const valid = value => typeof value === 'number' && Number.isFinite(value) && value >= 0;
const median = values => { const sorted = [...values].sort((a,b) => a-b), middle=Math.floor(sorted.length / 2);return sorted.length ? sorted.length%2 ? sorted[middle] : (sorted[middle-1]+sorted[middle])/2 : null; };
function stats(values, divisor = 1) {
  const present = values.filter(valid).map(value => value / divisor);
  return { samples: present.length, mean: present.length ? present.reduce((a,b) => a+b,0) / present.length : null,
    minimum: present.length ? Math.min(...present) : null, peak: present.length ? Math.max(...present) : null };
}
export function effectivePhase(row) {
  if (row.policyConsistent === false || row.rendererPending) return null;
  if (row.canRun === true && row.reasons === 'None' && row.isSuspended === false && row.rendererOutcome === 'active' && row.frozenShown === false) return 'active';
  if (row.manualPause === true && row.canRun === false && row.reasons === 'Manual' && row.isSuspended === true && row.rendererOutcome === 'suspended' && row.frozenShown === true) return 'paused';
  return null;
}
export function validateResourceRun(metadata, samples, expectedExecutable) {
  const normalize=value=>typeof value==='string'?path.win32.normalize(value).toLowerCase():null;
  if(normalize(metadata.ownerExecutable ?? metadata.livelyExecutable)!==normalize(expectedExecutable)) throw new Error('Resource run belongs to another executable.');
  const start=Date.parse(metadata.rootStartTimeUtc), pid=metadata.rootProcessId;
  if(!Number.isSafeInteger(pid) || pid<=0 || !Number.isFinite(start) || metadata.sampleCount!==samples.length || samples.length<2) throw new Error('Invalid or incomplete resource run identity/count.');
  let last=-Infinity;
  for(const sample of samples){
    const time=Date.parse(sample.atUtc), rows=sample.processes;
    if(!Number.isFinite(time) || time<=last || !Array.isArray(rows) || rows.length!==sample.processCount) throw new Error('Invalid resource process/time coverage.');
    const roots=rows.filter(row=>row.isRoot===true);
    if(roots.length!==1 || roots[0].id!==pid || Date.parse(roots[0].startTimeUtc)!==start || roots[0].processName?.toLowerCase()!=='dualwindowhost.exe') throw new Error('Resource root process identity changed.');
    last=time;
  }
  return {exactExecutableMatched:true,rootIdentityStable:true,sampleCount:samples.length,stopReason:metadata.stopReason,requestedSeconds:metadata.requestedSeconds,elapsedSeconds:metadata.elapsedSeconds};
}
function ownerTrend(rows, field, divisor = 1) {
  if (!rows.length) return { samples:0, firstWindowMedian:null, lastWindowMedian:null, medianChange:null };
  const first = rows[0].elapsedSeconds, last = rows.at(-1).elapsedSeconds;
  const a = median(rows.filter(row => row.elapsedSeconds <= first + 60).map(row => row[field]).filter(valid));
  const b = median(rows.filter(row => row.elapsedSeconds >= last - 60).map(row => row[field]).filter(valid));
  return { samples:rows.filter(row => valid(row[field])).length, firstWindowMedian:a === null ? null : a / divisor,
    lastWindowMedian:b === null ? null : b / divisor, medianChange:a === null || b === null ? null : (b-a) / divisor };
}
export function summarizeSoak(heartbeats, resourceSamples = []) {
  if (!Array.isArray(heartbeats) || heartbeats.length < 2) throw new Error('At least two complete soak heartbeats are required.');
  const owner = heartbeats[0].ownerId, sequence = heartbeats[0].sequence, value = heartbeats[0].value;
  if (typeof owner !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(owner)) throw new Error('Invalid owner identity.');
  if(!Number.isSafeInteger(sequence) || sequence<0 || !Number.isSafeInteger(value))throw new Error('Invalid committed sequence/value.');
  let lastElapsed = -1, lastUtc = -Infinity, pausedClockViolations = 0;
  for (let i=0;i<heartbeats.length;i++) {
    const row=heartbeats[i], utc=Date.parse(row.atUtc);
    if (row.kind !== 'soak-heartbeat' || row.ownerId !== owner || row.sequence !== sequence || row.value !== value) throw new Error('Mixed owner identities or committed state changes.');
    if (!valid(row.elapsedSeconds) || row.elapsedSeconds <= lastElapsed || !Number.isFinite(utc) || utc <= lastUtc) throw new Error('Non-monotonic or invalid heartbeat time.');
    if (!valid(row.activeSeconds)) throw new Error('Invalid activity clock.');
    if(!Number.isSafeInteger(row.policyRevision) || row.policyRevision<0 || !Number.isSafeInteger(row.rebuilds) || row.rebuilds<0)throw new Error('Invalid policy/view generation.');
    const previous=heartbeats[i-1];
    if(previous && (row.policyRevision<previous.policyRevision || row.rebuilds<previous.rebuilds || row.activeSeconds<previous.activeSeconds))throw new Error('Policy/view generation or activity clock went backwards.');
    if (previous && effectivePhase(previous)==='paused' && effectivePhase(row)==='paused' && row.policyRevision===previous.policyRevision && Math.abs(row.activeSeconds-previous.activeSeconds)>1e-6) pausedClockViolations++;
    lastElapsed=row.elapsedSeconds;lastUtc=utc;
  }
  const phases={active:heartbeats.filter(row=>effectivePhase(row)==='active'),paused:heartbeats.filter(row=>effectivePhase(row)==='paused')};
  const resourceGroups={active:[],paused:[]};let excludedResourceIntervals=0;
  const timed=heartbeats.map(row=>({row,time:Date.parse(row.atUtc)}));
  for(let i=1;i<resourceSamples.length;i++) {
    const sample=resourceSamples[i], previous=resourceSamples[i-1];
    const start=Date.parse(previous.atUtc), end=Date.parse(sample.atUtc);
    if(!Number.isFinite(start) || !Number.isFinite(end) || end<=start || end-start>10000){excludedResourceIntervals++;continue;}
    // Include one second for provider refresh uncertainty, then require nearby
    // heartbeats on BOTH sides and one unchanged phase/revision/view generation.
    const leftTarget=start-1000,rightTarget=end+1000;
    const left=timed.findLast(item=>item.time<=leftTarget),right=timed.find(item=>item.time>=rightTarget);
    if(!left || !right || leftTarget-left.time>2500 || right.time-rightTarget>2500){excludedResourceIntervals++;continue;}
    const interval=timed.filter(item=>item.time>=left.time && item.time<=right.time).map(item=>item.row);
    const phase=effectivePhase(left.row);
    if(!phase || interval.some(row=>effectivePhase(row)!==phase || row.policyRevision!==left.row.policyRevision || row.rebuilds!==left.row.rebuilds)){excludedResourceIntervals++;continue;}
    resourceGroups[phase].push(sample);
  }
  const drawRates=[];
  for(let i=1;i<heartbeats.length;i++) {
    const a=heartbeats[i-1],b=heartbeats[i];
    if(effectivePhase(a)==='active' && effectivePhase(b)==='active' && a.rebuilds===b.rebuilds && a.policyRevision===b.policyRevision && valid(a.frames) && valid(b.frames) && b.frames>=a.frames)
      drawRates.push((b.frames-a.frames)/(b.elapsedSeconds-a.elapsedSeconds));
  }
  const result={evidenceKind:'P0 2D diagnostic scene only',heartbeatCount:heartbeats.length,
    observedSeconds:heartbeats.at(-1).elapsedSeconds-heartbeats[0].elapsedSeconds,requestedSeconds:heartbeats[0].requestedSeconds,
    ownerIdentityStable:true,committedSequence:sequence,committedValue:value,pausedClockViolations,
    maximumViewRebuilds:Math.max(...heartbeats.map(row=>row.rebuilds)),runtimeVersions:[...new Set(heartbeats.map(row=>row.runtimeVersion).filter(item=>typeof item==='string'))],
    activeDrawCallbacksPerSecond:stats(drawRates),unclassifiedHeartbeats:heartbeats.length-phases.active.length-phases.paused.length,
    resourceIntervalsAccepted:resourceGroups.active.length+resourceGroups.paused.length,resourceIntervalsExcluded:excludedResourceIntervals,
    phases:{},limits:['2D diagnostic results do not predict 3D scene performance or prove a final quality budget.',
      'Only exact owner/proven descendant samples should be supplied. DWM, preparation Lively and collector overhead are outside this process scope.',
      'Phase correlation rejects transitions, missing nearby evidence, revision changes and view rebuild intervals; UTC clocks must remain monotonic.',
      'Means are arithmetic means of available samples. CPU means require complete current-process delta coverage; missing metrics stay null.',
      'GPU busiest-engine and available-instance memory sums are reported counters, not overall GPU utilization or unique physical VRAM.',
      'First/last window medians and handle changes are observations, not leak or long-term stability acceptance.']};
  for(const phase of ['active','paused']) {
    const rows=phases[phase], resources=resourceGroups[phase];
    result.phases[phase]={heartbeatCount:rows.length,resourceSamples:resources.length,
      ownerPrivateMiB:stats(rows.map(row=>row.ownerPrivateBytes),1048576),ownerGdiObjects:stats(rows.map(row=>row.ownerGdiObjects)),
      ownerUserObjects:stats(rows.map(row=>row.ownerUserObjects)),ownerHandles:stats(rows.map(row=>row.ownerHandleCount)),
      ownerPrivateTrendMiB:ownerTrend(rows,'ownerPrivateBytes',1048576),ownerGdiTrend:ownerTrend(rows,'ownerGdiObjects'),ownerHandleTrend:ownerTrend(rows,'ownerHandleCount'),
      cpuMachineNormalizedPercent:stats(resources.map(row=>row.cpuMatchedProcessCount===row.processCount?row.cpuMachineNormalizedPercent:null)),
      scopedPrivateMiB:stats(resources.map(row=>row.privateBytes),1048576),gpuBusiestEngineReportedPercent:stats(resources.map(row=>row.maxSingleEngineReportedPercent)),
      reportedDedicatedMiB:stats(resources.map(row=>row.reportedDedicatedBytes),1048576),reportedSharedMiB:stats(resources.map(row=>row.reportedSharedBytes),1048576),
      gpuNumericMemoryProcessCoverage:stats(resources.map(row=>row.gpuMemoryNumericProcessCount)),scopedProcessCount:stats(resources.map(row=>row.processCount))};
  }
  return result;
}
async function directDirectory(input) {
  const directory=await fs.realpath(input),repository=await fs.realpath(fileURLToPath(new URL('../..',import.meta.url)));
  const relative=path.relative(repository,directory);
  if(relative==='' || (!relative.startsWith('..'+path.sep) && relative!=='..' && !path.isAbsolute(relative))) throw new Error('Input records must stay outside the repository.');
  let current=path.resolve(input);
  for(;;){if((await fs.lstat(current)).isSymbolicLink())throw new Error('Use a direct directory without symbolic links.');const parent=path.dirname(current);if(parent===current)break;current=parent;}
  return directory;
}
async function jsonLines(file) {
  const text=await directText(file);const lines=text.split(/\r?\n/), rows=[];
  for(let i=0;i<lines.length;i++){if(!lines[i].trim())continue;try{rows.push(JSON.parse(lines[i]));}catch{if(i===lines.length-1 && !text.endsWith('\n'))break;throw new Error('Malformed complete JSONL record.');}}
  return rows;
}
async function directText(file) {
  const info=await fs.lstat(file);
  if(!info.isFile() || info.isSymbolicLink() || info.size>64*1024*1024)throw new Error('Expected a bounded direct record file.');
  return (await fs.readFile(file,'utf8')).replace(/^\uFEFF/,'');
}
async function main(args) {
  if(args.length!==1)throw new Error('Usage: node summarize-soak.mjs <private soak build directory>');
  const directory=await directDirectory(args[0]);
  const entries=(await fs.readdir(directory,{withFileTypes:true})).filter(item=>item.isDirectory() && /^gpu-run-[a-f0-9]+$/.test(item.name));
  if(entries.length>1)throw new Error('Multiple resource runs are ambiguous; summarize a single-session directory.');
  let resources=[], resourceRun=null;
  if(entries.length){
    const resource=await directDirectory(path.join(directory,entries[0].name));resources=await jsonLines(path.join(resource,'samples.jsonl'));
    const metadata=JSON.parse(await directText(path.join(resource,'summary.json')));
    resourceRun=validateResourceRun(metadata,resources,path.join(directory,'DualWindowHost.exe'));
  }
  const summary=summarizeSoak(await jsonLines(path.join(directory,'soak-heartbeats.jsonl')),resources);
  summary.resourceRun=resourceRun;
  const output=path.join(directory,`soak-aggregate-${Date.now()}.json`);await fs.writeFile(output,JSON.stringify(summary,null,2)+'\n',{flag:'wx'});
  console.log(JSON.stringify({output,summary},null,2));
}
if(process.argv[1] && import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href)main(process.argv.slice(2)).catch(error=>{console.error(error.message);process.exitCode=1;});

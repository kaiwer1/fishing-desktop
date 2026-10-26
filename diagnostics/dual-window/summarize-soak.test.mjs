import { test } from 'node:test';
import assert from 'node:assert/strict';
import { effectivePhase, summarizeSoak, validateResourceRun } from './summarize-soak.mjs';
const owner='11111111-1111-1111-1111-111111111111';
const at=seconds=>new Date(Date.UTC(2026,0,1)+seconds*1000).toISOString();
function beat(seconds,phase='active',extra={}) {
  const paused=phase==='paused';return {kind:'soak-heartbeat',ownerId:owner,sequence:2,value:10,atUtc:at(seconds),elapsedSeconds:seconds,
    requestedSeconds:120,manualPause:paused,canRun:!paused,reasons:paused?'Manual':'None',policyRevision:paused?2:1,
    rendererPending:false,isSuspended:paused,rendererOutcome:paused?'suspended':'active',frozenShown:paused,frames:paused?null:seconds*60,
    activeSeconds:paused?10:seconds,rebuilds:0,ownerPrivateBytes:1048576,ownerGdiObjects:30,ownerUserObjects:40,ownerHandleCount:100,
    runtimeVersion:'test-runtime',...extra};
}
function sample(seconds,extra={}) {return {atUtc:at(seconds),processCount:2,cpuMatchedProcessCount:2,cpuMachineNormalizedPercent:4,
  privateBytes:2097152,maxSingleEngineReportedPercent:0,reportedDedicatedBytes:null,reportedSharedBytes:null,gpuMemoryNumericProcessCount:0,...extra};}
test('phase uses effective host and renderer state instead of caller label',()=>{
  assert.equal(effectivePhase(beat(0)),'active');assert.equal(effectivePhase(beat(0,'paused')),'paused');
  assert.equal(effectivePhase(beat(0,'paused',{isSuspended:false})),null);assert.equal(effectivePhase(beat(0,'active',{rendererPending:true})),null);
  assert.equal(effectivePhase(beat(0,'active',{policyConsistent:false})),null);
});
test('stable intervals correlate both boundaries and keep real zero distinct from missing',()=>{
  const result=summarizeSoak([0,2,4,6,8,10].map(x=>beat(x)),[sample(2),sample(5),sample(8)]);
  assert.equal(result.resourceIntervalsAccepted,2);assert.equal(result.phases.active.cpuMachineNormalizedPercent.mean,4);
  assert.equal(result.phases.active.gpuBusiestEngineReportedPercent.mean,0);assert.equal(result.phases.active.reportedDedicatedMiB.mean,null);
});
test('intervals crossing pause boundaries or policy revisions are excluded',()=>{
  const rows=[beat(0),beat(2),beat(4,'paused'),beat(6,'paused'),beat(8,'paused')];
  const result=summarizeSoak(rows,[sample(2),sample(5)]);assert.equal(result.resourceIntervalsAccepted,0);
});
test('view rebuild boundaries are excluded from resource comparison',()=>{
  const rows=[0,2,4,6,8,10].map(x=>beat(x,'active',{rebuilds:x>=4?1:0}));
  assert.equal(summarizeSoak(rows,[sample(2),sample(5)]).resourceIntervalsAccepted,0);
});
test('partial CPU process coverage is not reported as a complete CPU average',()=>{
  const result=summarizeSoak([0,2,4,6,8].map(x=>beat(x)),[sample(2),sample(5,{cpuMatchedProcessCount:1})]);
  assert.equal(result.phases.active.cpuMachineNormalizedPercent.samples,0);assert.equal(result.phases.active.scopedPrivateMiB.mean,2);
});
test('mixed owners or committed state changes are rejected',()=>{
  assert.throws(()=>summarizeSoak([beat(0),beat(2,'active',{ownerId:'22222222-2222-2222-2222-222222222222'})]),/Mixed/);
  assert.throws(()=>summarizeSoak([beat(0),beat(2,'active',{sequence:3})]),/Mixed/);
});
test('paused clock changes are reported without hiding the failed invariant',()=>{
  const result=summarizeSoak([beat(0,'paused'),beat(2,'paused',{activeSeconds:11})]);assert.equal(result.pausedClockViolations,1);
});
test('drawing callback rate is separate from presented FPS and ignores rebuild resets',()=>{
  const result=summarizeSoak([beat(0),beat(2),beat(4,'active',{rebuilds:1,frames:1})]);
  assert.equal(result.activeDrawCallbacksPerSecond.mean,60);assert.equal(result.activeDrawCallbacksPerSecond.samples,1);
});
test('nonmonotonic UTC or monotonic sample time is rejected',()=>{
  assert.throws(()=>summarizeSoak([beat(2),beat(1)]),/time/);
  assert.throws(()=>summarizeSoak([beat(0),beat(2,'active',{atUtc:at(0)})]),/time/);
});
test('resource metadata must match the exact executable and every root identity',()=>{
  const executable='C:\\private\\build\\DualWindowHost.exe';
  const metadata={livelyExecutable:executable,rootProcessId:42,rootStartTimeUtc:at(0),sampleCount:2};
  const rows=[1,2].map(x=>({...sample(x),processCount:1,processes:[{isRoot:true,id:42,startTimeUtc:at(0),processName:'DualWindowHost.exe'}]}));
  assert.equal(validateResourceRun(metadata,rows,executable).rootIdentityStable,true);
  assert.throws(()=>validateResourceRun(metadata,rows,'C:\\other\\DualWindowHost.exe'),/another executable/);
  assert.throws(()=>validateResourceRun({...metadata,rootStartTimeUtc:at(1)},rows,executable),/identity changed/);
  assert.throws(()=>validateResourceRun({...metadata,sampleCount:3},rows,executable),/incomplete/);
});
test('owner trends use the mean of both central values for even windows',()=>{
  const result=summarizeSoak([beat(0),beat(2,'active',{ownerPrivateBytes:3*1048576})]);
  assert.equal(result.phases.active.ownerPrivateTrendMiB.firstWindowMedian,2);
});
test('malformed identity or absent committed state is rejected',()=>{
  assert.throws(()=>summarizeSoak([beat(0,'active',{ownerId:'-'.repeat(36)}),beat(2)]),/identity/);
  assert.throws(()=>summarizeSoak([beat(0,'active',{sequence:undefined}),beat(2,'active',{sequence:undefined})]),/committed/);
});
test('policy view generations and activity clocks cannot go backwards',()=>{
  assert.throws(()=>summarizeSoak([beat(0),beat(2,'active',{policyRevision:0})]),/backwards/);
  assert.throws(()=>summarizeSoak([beat(0,'active',{rebuilds:1}),beat(2)]),/backwards/);
  assert.throws(()=>summarizeSoak([beat(0,'active',{activeSeconds:4}),beat(2)]),/backwards/);
});

test('cross-navigation telemetry and stopped loops are not effective phases',()=>{
  assert.equal(effectivePhase(beat(0,'active',{viewConsistent:false})),null);
  assert.equal(effectivePhase(beat(0,'active',{drawLoopRunning:false})),null);
});

test('navigation boundaries exclude resource and draw comparisons even when rebuild count is unchanged',()=>{
  const rows=[0,2,4,6,8,10].map(x=>beat(x,'active',{viewNavigationGeneration:x>=4?2:1,viewConsistent:true,drawLoopRunning:true}));
  assert.equal(summarizeSoak(rows,[sample(2),sample(5)]).resourceIntervalsAccepted,0);
  assert.equal(summarizeSoak(rows).activeDrawCallbacksPerSecond.samples,4);
  assert.throws(()=>summarizeSoak([rows[0],beat(2)]),/navigation/);
});

test('stable active frame stalls are recorded without hiding the failed progress invariant',()=>{
  const result=summarizeSoak([beat(0,'active',{frames:4}),beat(2,'active',{frames:4})]);assert.equal(result.activeDrawStalls,1);
});

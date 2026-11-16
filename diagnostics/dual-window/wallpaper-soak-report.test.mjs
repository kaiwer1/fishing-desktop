import assert from 'node:assert/strict';
import test from 'node:test';
import {summarize} from './wallpaper-soak-report.mjs';

function fixture() {
  const stamp=i=>new Date(Date.UTC(2026,0,1)+5000*i).toISOString();
  const frames={water:'river-bend',quality:'medium',drawWidth:1920,drawHeight:1080,desktopGridPoints:35,
    frames:4500,elapsedMs:150000,meanFps:30,samples:Array.from({length:31},(_,i)=>({
      frames:i*150,elapsedMs:i*5000,fps:i?30:null,utc:stamp(i),desktopGridHits:0}))};
  const meter={ownerKind:'Owner',phaseLabel:'active',rootProcessId:7,stopReason:'duration-completed'};
  const processes=[{isRoot:true,id:7,processName:'DualWindowHost.exe'},{isRoot:false,id:8,processName:'msedgewebview2.exe'}];
  const resources=Array.from({length:29},(_,i)=>({atUtc:stamp(i+1),privateBytes:(200+i)*1048576,
    summedWorkingSetBytes:(300+i)*1048576,processCount:2,processes,
    maxSingleEngineReportedPercent:null,reportedDedicatedBytes:null,reportedSharedBytes:null,
    gpuEngineNumericProcessCount:0,gpuMemoryNumericProcessCount:0}));
  return {frames,meter,resources};
}

test('uses the game tree during the render window and preserves missing GPU data and covered desktop',()=>{
  const {frames,meter,resources}=fixture();
  const old={...resources[0],atUtc:'2025-12-31T00:00:00Z',privateBytes:1};
  const report=summarize(frames,meter,[old,...resources]);
  assert.equal(report.privateMiB.first,200);
  assert.equal(report.privateMiB.last,228);
  assert.equal(report.busiestGpuEnginePercent,null);
  assert.equal(report.dedicatedGpuMiB,null);
  assert.equal(report.desktopHits.max,0);
});

test('requires elapsed resource coverage and frame advances throughout the run',()=>{
  const {frames,meter,resources}=fixture();
  assert.throws(()=>summarize(frames,meter,resources.slice(0,23)));
  frames.samples[15].frames=frames.samples[14].frames;
  assert.throws(()=>summarize(frames,meter,resources));
});

test('does not include a third-party process in the reported curve',()=>{
  const {frames,meter,resources}=fixture();
  resources[0].processes=[...resources[0].processes,{isRoot:false,id:9,processName:'unrelated.exe'}];
  assert.throws(()=>summarize(frames,meter,resources));
});

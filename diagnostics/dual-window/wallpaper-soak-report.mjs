import assert from 'node:assert/strict';
import { readFile, readdir, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

function statistics(values) {
  const valid=values.filter(Number.isFinite);
  if (!valid.length) return null;
  return {count:valid.length, first:valid[0], last:valid.at(-1), min:Math.min(...valid), max:Math.max(...valid),
    mean:Math.round(valid.reduce((sum,n)=>sum+n,0)/valid.length*100)/100};
}

export function summarize(frame, meter, resources) {
  assert.equal(frame.water,'river-bend');
  assert.equal(frame.quality,'medium');
  assert.equal(frame.samples.length,31);
  assert.ok(frame.elapsedMs>=150000 && frame.drawWidth>0 && frame.drawHeight>0);
  const first=frame.samples[0], last=frame.samples.at(-1);
  assert.equal(first.frames,0);
  assert.equal(last.frames,frame.frames);
  for (let i=1;i<frame.samples.length;i++) {
    const a=frame.samples[i-1],b=frame.samples[i];
    assert.ok(b.frames>a.frames && b.elapsedMs>a.elapsedMs && Date.parse(b.utc)>Date.parse(a.utc));
    assert.ok(Math.abs(b.fps-(b.frames-a.frames)*1000/(b.elapsedMs-a.elapsedMs))<0.03);
    assert.ok(b.desktopGridHits>=0 && b.desktopGridHits<=frame.desktopGridPoints);
  }
  assert.ok(Math.abs(frame.meanFps-frame.frames*1000/frame.elapsedMs)<.01);
  assert.equal(meter.ownerKind,'Owner');
  assert.equal(meter.phaseLabel,'active');
  assert.equal(meter.stopReason,'duration-completed');
  const start=Date.parse(first.utc),end=Date.parse(last.utc);
  const inside=resources.filter(r=>Date.parse(r.atUtc)>=start && Date.parse(r.atUtc)<=end);
  assert.ok(inside.length>=20 && Date.parse(inside.at(-1).atUtc)-Date.parse(inside[0].atUtc)>=120000);
  const samples=inside.map(r=>{
    assert.ok(r.privateBytes>0 && r.summedWorkingSetBytes>0);
    assert.ok(r.processes.some(p=>p.isRoot && p.id===meter.rootProcessId));
    assert.ok(r.processes.every(p=>/^(DualWindowHost|msedgewebview2)\.exe$/i.test(p.processName)));
    return {seconds:Math.round((Date.parse(r.atUtc)-start)/10)/100,processes:r.processCount,
      privateMiB:r.privateBytes/1048576,summedWorkingSetMiB:r.summedWorkingSetBytes/1048576,
      busiestGpuEnginePercent:r.maxSingleEngineReportedPercent,
      dedicatedGpuMiB:Number.isFinite(r.reportedDedicatedBytes)?r.reportedDedicatedBytes/1048576:null,
      sharedGpuMiB:Number.isFinite(r.reportedSharedBytes)?r.reportedSharedBytes/1048576:null,
      gpuEnginePids:r.gpuEngineNumericProcessCount,gpuMemoryPids:r.gpuMemoryNumericProcessCount};
  });
  return {water:frame.water,quality:frame.quality,drawWidth:frame.drawWidth,drawHeight:frame.drawHeight,
    frames:frame.frames,seconds:frame.elapsedMs/1000,meanFps:frame.meanFps,intervalFps:statistics(frame.samples.slice(1).map(f=>f.fps)),
    desktopGridPoints:frame.desktopGridPoints,desktopHits:statistics(frame.samples.map(f=>f.desktopGridHits)),
    privateMiB:statistics(samples.map(s=>s.privateMiB)),summedWorkingSetMiB:statistics(samples.map(s=>s.summedWorkingSetMiB)),
    busiestGpuEnginePercent:statistics(samples.map(s=>s.busiestGpuEnginePercent)),dedicatedGpuMiB:statistics(samples.map(s=>s.dedicatedGpuMiB)),
    sharedGpuMiB:statistics(samples.map(s=>s.sharedGpuMiB)),resources:samples,
    frameIntervals:frame.samples.map(({elapsedMs,frames,fps,desktopGridHits})=>({elapsedMs,frames,fps,desktopGridHits}))};
}

if (process.argv[1] && import.meta.url===pathToFileURL(resolve(process.argv[1])).href) {
  const folder=resolve(process.argv[2]);
  const subdirs=(await readdir(folder,{withFileTypes:true})).filter(d=>d.isDirectory() && d.name.startsWith('gpu-run-'));
  assert.equal(subdirs.length,1);
  const raw=join(folder,subdirs[0].name);
  const read=async path=>(await readFile(path,'utf8')).replace(/^\uFEFF/,'');
  const [frames,meter,rows]=await Promise.all([read(join(folder,'wallpaper-soak-frames.json')),read(join(raw,'summary.json')),read(join(raw,'samples.jsonl'))]);
  const result=summarize(JSON.parse(frames),JSON.parse(meter),rows.trim().split(/\r?\n/).map(line=>JSON.parse(line)));
  await writeFile(join(folder,'wallpaper-soak-report.json'),JSON.stringify(result,null,2)+'\n');
}

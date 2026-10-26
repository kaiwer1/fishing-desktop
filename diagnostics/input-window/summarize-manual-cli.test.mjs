import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {fileURLToPath} from 'node:url';
const run=promisify(execFile),cli=fileURLToPath(new URL('./summarize-manual.mjs',import.meta.url));
async function bundle(t,{mode='bounded-desktop-input',desktopPassed=true,unattended=true}={}) {
  const directory=await fs.mkdtemp(path.join(os.tmpdir(),'fishing-evidence-fixture-'));
  t.after(()=>fs.rm(directory,{recursive:true,force:true}));
  const session={mode,requestedSeconds:60,unattended,exitCode:0,presentationShown:true,inputPanelReady:true};
  const runtime={mode,unattended,normalExit:true,exitCode:0,forcedCleanup:false,remainingDedicatedProcesses:0,outputDirectory:directory};
  const desktop={passed:desktopPassed,remainingLeases:0,attachments:1,detachments:1};
  const data={kind:'ready',documentId:'11111111-1111-1111-1111-111111111111',enabled:false,lastReason:'startup',revision:0,cancellationEpoch:0,held:0,presses:0,releases:0,keys:0,wheels:0,cancellations:0,buttonTotals:[0,1,2].map(()=>({down:0,up:0}))};
  for(const [name,value] of Object.entries({'manual-session.json':session,'session-result.json':runtime,'desktop-summary.json':desktop}))await fs.writeFile(path.join(directory,name),JSON.stringify(value));
  await fs.writeFile(path.join(directory,'input-events.jsonl'),'2026-01-01T00:00:00.0000000Z '+JSON.stringify(data)+'\n');
  return directory;
}
test('desktop CLI requires clean attachment but never accepts unattended input',async t=>{
  const directory=await bundle(t),{stdout}=await run(process.execPath,[cli,directory]);const {summary}=JSON.parse(stdout);
  assert.equal(summary.hostMode,'bounded-desktop-input');assert.equal(summary.normalExit,true);assert.equal(summary.ordinaryWindowInputAccepted,false);
});
test('desktop cleanup failure prevents a normal evidence result',async t=>{
  const directory=await bundle(t,{desktopPassed:false}),{stdout}=await run(process.execPath,[cli,directory]);assert.equal(JSON.parse(stdout).summary.normalExit,false);
});
test('desktop CLI refuses missing desktop report',async t=>{
  const directory=await bundle(t);await fs.unlink(path.join(directory,'desktop-summary.json'));
  await assert.rejects(run(process.execPath,[cli,directory]));
});
test('unattended CLI cannot acquire operator confirmation',async t=>{
  const directory=await bundle(t);await assert.rejects(run(process.execPath,[cli,directory,'--operator-confirmed']),/Unattended/);
});
test('ordinary window bundles remain supported without a desktop report',async t=>{
  const directory=await bundle(t,{mode:'bounded-manual-input'});await fs.unlink(path.join(directory,'desktop-summary.json'));
  const {stdout}=await run(process.execPath,[cli,directory]);assert.equal(JSON.parse(stdout).summary.normalExit,true);
});
test('one runtime bundle cannot claim another directory',async t=>{
  const directory=await bundle(t),file=path.join(directory,'session-result.json'),runtime=JSON.parse(await fs.readFile(file));runtime.outputDirectory=path.dirname(directory);
  await fs.writeFile(file,JSON.stringify(runtime));await assert.rejects(run(process.execPath,[cli,directory]),/another private directory/);
});

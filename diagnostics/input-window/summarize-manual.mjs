import { summarizeManual } from './manual-evidence.mjs';
export { summarizeManual } from './manual-evidence.mjs';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const count=value=>Number.isSafeInteger(value) && value>=0;
export function parseManualLog(text) {
  const rows=[];let previous=-Infinity;
  for(const line of text.replace(/^\uFEFF/,'').split(/\r?\n/)){
    if(!line.trim())continue;
    const split=line.indexOf(' '),atUtc=line.slice(0,split),time=Date.parse(atUtc);
    if(split<0 || !Number.isFinite(time) || time<previous)throw new Error('Invalid or backwards input event time.');
    rows.push({atUtc,data:JSON.parse(line.slice(split+1))});previous=time;
  }
  return rows;
}
async function directDirectory(input) {
  let cursor=path.resolve(input);
  for(;;){if((await fs.lstat(cursor)).isSymbolicLink())throw new Error('Use a direct private path.');const parent=path.dirname(cursor);if(parent===cursor)break;cursor=parent;}
  const directory=await fs.realpath(input),repository=await fs.realpath(fileURLToPath(new URL('../..',import.meta.url))),relative=path.relative(repository,directory);
  if(relative==='' || (!relative.startsWith('..'+path.sep) && relative!=='..' && !path.isAbsolute(relative)))throw new Error('Input evidence must stay outside the repository.');
  return directory;
}
async function readDirect(file) {
  const info=await fs.lstat(file);if(!info.isFile() || info.isSymbolicLink() || info.size>16*1024*1024)throw new Error('Expected a bounded direct evidence file.');
  return (await fs.readFile(file,'utf8')).replace(/^\uFEFF/,'');
}
async function main(args) {
  if(args.length<1 || args.length>2 || (args[1] && args[1]!=='--operator-confirmed'))throw new Error('Usage: node summarize-manual.mjs <private run> [--operator-confirmed]');
  const directory=await directDirectory(args[0]),session=JSON.parse(await readDirect(path.join(directory,'manual-session.json'))),runtime=JSON.parse(await readDirect(path.join(directory,'session-result.json')));
  if(!['bounded-manual-input','bounded-desktop-input'].includes(session.mode) || runtime.mode!==session.mode || !count(session.requestedSeconds) || session.requestedSeconds<60 || session.requestedSeconds>600)throw new Error('Unexpected manual session metadata.');
  if((session.unattended===true || runtime.unattended===true) && args[1])throw new Error('Unattended evidence cannot carry manual operator confirmation.');
  const normalize=value=>typeof value==='string'?path.win32.normalize(value).replace(/[\\/]+$/,'').toLowerCase():null;
  if(typeof runtime.outputDirectory!=='string' || normalize(await directDirectory(runtime.outputDirectory))!==normalize(directory))throw new Error('Runtime evidence belongs to another private directory.');
  let normalExit=runtime.normalExit===true && runtime.exitCode===0 && runtime.forcedCleanup===false && runtime.remainingDedicatedProcesses===0 && session.exitCode===0;
  if(session.mode==='bounded-desktop-input'){
    const desktop=JSON.parse(await readDirect(path.join(directory,'desktop-summary.json')));
    normalExit &&=desktop.passed===true && desktop.remainingLeases===0 && count(desktop.attachments) && desktop.attachments>0 && desktop.attachments===desktop.detachments &&
      session.presentationShown===true && session.inputPanelReady===true;
  }
  const summary=summarizeManual(parseManualLog(await readDirect(path.join(directory,'input-events.jsonl'))),{operatorConfirmed:args[1]==='--operator-confirmed',normalExit});
  summary.hostMode=session.mode;
  if(session.mode==='bounded-desktop-input'){
    summary.evidenceKind='ordinary interaction window within a desktop diagnostic only';
    summary.limits.push('Host-ineligible down/up/wheel/key records are downgraded before aggregation; live page hints do not replace this host check. Desktop icons, system menus and composition require separate acceptance.');
  }
  const output=path.join(directory,`manual-aggregate-${Date.now()}.json`);await fs.writeFile(output,JSON.stringify(summary,null,2)+'\n',{flag:'wx'});
  console.log(JSON.stringify({output,summary},null,2));
}
if(process.argv[1] && import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href)main(process.argv.slice(2)).catch(error=>{console.error(error.message);process.exitCode=1;});

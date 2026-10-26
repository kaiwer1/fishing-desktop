import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const count=value=>Number.isSafeInteger(value) && value>=0;
const uuid=value=>typeof value==='string' && /^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i.test(value);
const counters=['revision','cancellationEpoch','held','presses','releases','keys','wheels','cancellations'];
const focusReasons=new Set(['blur','hidden','host-deactivate','pointercancel','lostcapture']);
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
export function summarizeManual(rows,{operatorConfirmed=false,normalExit=false}={}) {
  if(!Array.isArray(rows) || !rows.length)throw new Error('No input event records.');
  const documents=new Map(),pairs=[0,0,0],downs=[0,0,0],ups=[0,0,0];
  let wheel=0,key=0,untrusted=0,heldCancel=false,returnedDisabled=false,staleEnable=0;
  for(const {data} of rows){
    if(!data || !uuid(data.documentId) || typeof data.kind!=='string' || typeof data.enabled!=='boolean' || typeof data.lastReason!=='string' ||
      !counters.every(field=>count(data[field])) || !Array.isArray(data.buttonTotals) || data.buttonTotals.length!==3 ||
      !data.buttonTotals.every(value=>value && count(value.down) && count(value.up) && value.up<=value.down))throw new Error('Invalid input snapshot schema.');
    if(data.buttonTotals.reduce((sum,value)=>sum+value.down,0)!==data.presses || data.buttonTotals.reduce((sum,value)=>sum+value.up,0)!==data.releases)throw new Error('Input counters disagree.');
    let document=documents.get(data.documentId);
    if(!document){document={last:null,pending:new Set(),awaitingReturn:false};documents.set(data.documentId,document);}
    if(document.last && counters.some(field=>field!=='held' && data[field]<document.last[field]))throw new Error('Input counters went backwards within a document.');
    if(document.last && data.buttonTotals.some((value,index)=>value.down<document.last.buttonTotals[index].down || value.up<document.last.buttonTotals[index].up))throw new Error('Button counters went backwards.');
    const input=['down','up','wheel','key'].includes(data.kind);
    const trusted=input && data.trusted===true && data.enabled===true;
    if(input && !trusted){untrusted++;document.pending.clear();document.awaitingReturn=false;}
    if(data.kind==='mode-rejected' && data.lastReason==='stale-enable')staleEnable++;
    if(data.kind==='mode' && data.enabled)document.awaitingReturn=false;
    if(trusted && (data.kind==='down' || data.kind==='up')){
      if(![0,1,2].includes(data.button) || !['pointerdown','pointerup','pointermove'].includes(data.sourceEvent))throw new Error('Invalid trusted pointer event.');
      if(data.kind==='down'){downs[data.button]++;document.pending.add(data.button);}
      else {ups[data.button]++;if(document.pending.delete(data.button))pairs[data.button]++;}
    }
    if(trusted && data.kind==='wheel')wheel++;
    if(trusted && data.kind==='key')key++;
    if(!data.enabled && data.held===0){
      if(['cancel','mode'].includes(data.kind) && document.pending.has(0) && focusReasons.has(data.lastReason)){heldCancel=true;document.awaitingReturn=true;}
      document.pending.clear();
    }else if(data.held===0 && data.kind!=='up')document.pending.clear();
    if(data.kind==='focus' && data.trusted===true && document.awaitingReturn && !data.enabled && data.held===0){returnedDisabled=true;document.awaitingReturn=false;}
    document.last=data;
  }
  const evidenceComplete=pairs.every(value=>value>0) && wheel>0 && key>0 && heldCancel && returnedDisabled;
  const pointer={};for(const [name,index] of [['left',0],['right',2],['middle',1]])pointer[name]={trustedDownEvents:downs[index],trustedUpEvents:ups[index],matchedPairs:pairs[index]};
  return {evidenceKind:'ordinary input diagnostic only',eventCount:rows.length,documentCount:documents.size,pointer,
    trustedWheelEvents:wheel,trustedKeyEvents:key,untrustedOrDisabledInputEventsIgnored:untrusted,staleEnableRejections:staleEnable,heldLeftCancelObserved:heldCancel,
    returnedDisabledObserved:returnedDisabled,evidenceComplete,operatorConfirmed:operatorConfirmed===true,normalExit:normalExit===true,
    ordinaryWindowInputAccepted:evidenceComplete && operatorConfirmed===true && normalExit===true,
    limits:['DOM trusted events and operator confirmation support this ordinary diagnostic window only; they do not prove a physical hardware source.',
      'Cancelled holds and cumulative up totals are not counted as trusted pointer-up events. Matches and return evidence stay within one document.',
      'Missing actions remain incomplete. No game, desktop icon, desktop context menu, wallpaper click-through or multi-monitor acceptance is inferred.',
      'Output contains counts and fixed labels, not document identities, key contents, coordinates or raw events.']};
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
  if(session.mode!=='bounded-manual-input' || runtime.mode!==session.mode || !count(session.requestedSeconds) || session.requestedSeconds<60 || session.requestedSeconds>600)throw new Error('Unexpected manual session metadata.');
  if((session.unattended===true || runtime.unattended===true) && args[1])throw new Error('Unattended evidence cannot carry manual operator confirmation.');
  const normalize=value=>typeof value==='string'?path.win32.normalize(value).replace(/[\\/]+$/,'').toLowerCase():null;
  if(normalize(runtime.outputDirectory)!==normalize(directory))throw new Error('Runtime evidence belongs to another private directory.');
  const normalExit=runtime.normalExit===true && runtime.exitCode===0 && runtime.forcedCleanup===false && runtime.remainingDedicatedProcesses===0 && session.exitCode===0;
  const summary=summarizeManual(parseManualLog(await readDirect(path.join(directory,'input-events.jsonl'))),{operatorConfirmed:args[1]==='--operator-confirmed',normalExit});
  const output=path.join(directory,`manual-aggregate-${Date.now()}.json`);await fs.writeFile(output,JSON.stringify(summary,null,2)+'\n',{flag:'wx'});
  console.log(JSON.stringify({output,summary},null,2));
}
if(process.argv[1] && import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href)main(process.argv.slice(2)).catch(error=>{console.error(error.message);process.exitCode=1;});

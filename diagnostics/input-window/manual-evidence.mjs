const count=value=>Number.isSafeInteger(value) && value>=0;
const uuid=value=>typeof value==='string' && /^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i.test(value);
const counters=['revision','cancellationEpoch','held','presses','releases','keys','wheels','cancellations'];
const focusReasons=new Set(['blur','hidden','host-deactivate','pointercancel','lostcapture']);
export class ManualEvidence {
  constructor() {
    this.documents=new Map();this.pairs=[0,0,0];this.downs=[0,0,0];this.ups=[0,0,0];
    this.wheel=0;this.key=0;this.untrusted=0;this.heldCancel=false;this.returnedDisabled=false;this.staleEnable=0;this.eventCount=0;
  }
  add(data) {
    const {documents,pairs,downs,ups}=this;
    let {wheel,key,untrusted,heldCancel,returnedDisabled,staleEnable}=this;
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
    // Retain our own immutable counter copy; callers may reuse their snapshot object.
    document.last={...data,buttonTotals:data.buttonTotals.map(value=>({...value}))};
    Object.assign(this,{wheel,key,untrusted,heldCancel,returnedDisabled,staleEnable});this.eventCount++;
  }
  snapshot({operatorConfirmed=false,normalExit=false}={}) {
  const {documents,pairs,downs,ups,wheel,key,untrusted,heldCancel,returnedDisabled,staleEnable}=this;
  const evidenceComplete=pairs.every(value=>value>0) && wheel>0 && key>0 && heldCancel && returnedDisabled;
  const pointer={};for(const [name,index] of [['left',0],['right',2],['middle',1]])pointer[name]={trustedDownEvents:downs[index],trustedUpEvents:ups[index],matchedPairs:pairs[index]};
  return {evidenceKind:'ordinary input diagnostic only',eventCount:this.eventCount,documentCount:documents.size,pointer,
    trustedWheelEvents:wheel,trustedKeyEvents:key,untrustedOrDisabledInputEventsIgnored:untrusted,staleEnableRejections:staleEnable,heldLeftCancelObserved:heldCancel,
    returnedDisabledObserved:returnedDisabled,evidenceComplete,operatorConfirmed:operatorConfirmed===true,normalExit:normalExit===true,
    ordinaryWindowInputAccepted:evidenceComplete && operatorConfirmed===true && normalExit===true,
    limits:['DOM trusted events and operator confirmation support this ordinary diagnostic window only; they do not prove a physical hardware source.',
      'Cancelled holds and cumulative up totals are not counted as trusted pointer-up events. Matches and return evidence stay within one document.',
      'Missing actions remain incomplete. No game, desktop icon, desktop context menu, wallpaper click-through or multi-monitor acceptance is inferred.',
      'Output contains counts and fixed labels, not document identities, key contents, coordinates or raw events.']};
  }
}
export function summarizeManual(rows,options={}) {
  if(!Array.isArray(rows) || !rows.length)throw new Error('No input event records.');
  const evidence=new ManualEvidence();for(const {data} of rows)evidence.add(data);return evidence.snapshot(options);
}

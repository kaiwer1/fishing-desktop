import {test} from 'node:test';
import assert from 'node:assert/strict';
import {parseManualLog,summarizeManual} from './summarize-manual.mjs';
import {ManualEvidence} from './manual-evidence.mjs';

const id='11111111-1111-1111-1111-111111111111';
function fixture(trusted=true) {
  const rows=[],state={documentId:id,enabled:true,revision:1,cancellationEpoch:0,held:0,presses:0,releases:0,keys:0,wheels:0,cancellations:0,lastReason:'explicit-enable',buttonTotals:[0,1,2].map(()=>({down:0,up:0}))};
  const emit=(kind,extra={})=>rows.push({atUtc:new Date(Date.UTC(2026,0,1)+rows.length*1000).toISOString(),data:structuredClone({...state,kind,...extra})});
  emit('mode');
  for(const button of [0,2,1]){state.held++;state.presses++;state.buttonTotals[button].down++;emit('down',{button,trusted,sourceEvent:'pointerdown'});state.held--;state.releases++;state.buttonTotals[button].up++;emit('up',{button,trusted,sourceEvent:'pointerup'});}
  state.wheels++;emit('wheel',{trusted});state.keys++;emit('key',{trusted});
  state.held++;state.presses++;state.buttonTotals[0].down++;emit('down',{button:0,trusted,sourceEvent:'pointerdown'});
  state.enabled=false;state.held=0;state.lastReason='blur';state.cancellations++;state.cancellationEpoch++;emit('cancel');emit('focus',{trusted});return rows;
}
test('complete browser event evidence still requires operator confirmation and normal cleanup',()=>{
  const rows=fixture();assert.equal(summarizeManual(rows).evidenceComplete,true);assert.equal(summarizeManual(rows).ordinaryWindowInputAccepted,false);
  assert.equal(summarizeManual(rows,{operatorConfirmed:true,normalExit:true}).ordinaryWindowInputAccepted,true);
  assert.equal(summarizeManual(rows,{operatorConfirmed:true,normalExit:false}).ordinaryWindowInputAccepted,false);
});
test('left right and middle use DOM button index order',()=>{
  const result=summarizeManual(fixture());assert.equal(result.pointer.right.matchedPairs,1);assert.equal(result.pointer.middle.matchedPairs,1);assert.equal(result.pointer.left.trustedDownEvents,2);assert.equal(result.pointer.left.matchedPairs,1);
});
test('synthetic input and cumulative totals cannot satisfy trusted actions',()=>{
  const result=summarizeManual(fixture(false));assert.equal(result.evidenceComplete,false);assert.equal(result.pointer.left.matchedPairs,0);assert.equal(result.trustedKeyEvents,0);
});
test('missing return evidence is incomplete without inventing an automatic-enable failure',()=>{
  const rows=fixture();rows.at(-1).data.enabled=true;
  const result=summarizeManual(rows);assert.equal(result.heldLeftCancelObserved,true);assert.equal(result.returnedDisabledObserved,false);assert.equal(result.evidenceComplete,false);
});
test('an explicit enable between cancellation and focus cannot prove return stayed disabled',()=>{
  const rows=fixture(),extra=structuredClone(rows.at(-2));extra.data.kind='mode';extra.data.enabled=true;rows.splice(-1,0,extra);
  assert.equal(summarizeManual(rows).returnedDisabledObserved,false);
});
test('cancel without a pending trusted left hold does not satisfy held-switch evidence',()=>{
  const rows=fixture().filter(row=>!(row.data.kind==='down' && row.data.presses===4));assert.equal(summarizeManual(rows).heldLeftCancelObserved,false);
});
test('pointer-up in a different document cannot pair with an earlier press',()=>{
  const rows=fixture().slice(0,3);rows[2].data.documentId='22222222-2222-2222-2222-222222222222';assert.equal(summarizeManual(rows).pointer.left.matchedPairs,0);
});
test('counter rollback or malformed snapshot is rejected',()=>{
  const rows=fixture();rows.at(-1).data.revision=0;assert.throws(()=>summarizeManual(rows),/backwards/);
  const malformed=fixture();malformed[0].data.held=-1;assert.throws(()=>summarizeManual(malformed),/schema/);
});
test('raw prefix parser supports UTF8 BOM and repeated millisecond timestamps',()=>{
  const row=fixture()[0].data,line='2026-01-01T00:00:00.0000000Z '+JSON.stringify(row);
  assert.equal(parseManualLog('\uFEFF'+line+'\r\n'+line+'\r\n').length,2);
  assert.throws(()=>parseManualLog('not-a-time '+JSON.stringify(row)),/time/);
});
test('public summary omits document identity and arbitrary key contents',()=>{
  const rows=fixture();rows[0].data.keyContents='should-never-be-forwarded';const text=JSON.stringify(summarizeManual(rows));assert.ok(!text.includes(id));assert.ok(!text.includes('should-never-be-forwarded'));
});
test('return snapshot alone cannot invent the missing held-cancel record',()=>{
  const rows=fixture().filter(row=>row.data.kind!=='cancel');assert.equal(summarizeManual(rows).heldLeftCancelObserved,false);assert.equal(summarizeManual(rows).returnedDisabledObserved,false);
});
test('an untrusted release breaks a trusted hold chain',()=>{
  const rows=fixture(),release=structuredClone(rows.at(-2));release.data.kind='up';release.data.enabled=true;release.data.button=0;release.data.trusted=false;release.data.sourceEvent='pointerup';rows.splice(-2,0,release);
  assert.equal(summarizeManual(rows).heldLeftCancelObserved,false);
});

test('live incremental evidence matches offline summary after every event',()=>{
  const rows=fixture(),evidence=new ManualEvidence();
  assert.equal(evidence.snapshot().evidenceComplete,false);
  for(let index=0;index<rows.length;index++){
    evidence.add(rows[index].data);
    assert.deepEqual(evidence.snapshot(),summarizeManual(rows.slice(0,index+1)));
    assert.equal(evidence.snapshot().ordinaryWindowInputAccepted,false);
  }
});

test('incremental evidence owns counter snapshots when a caller reuses objects',()=>{
  const evidence=new ManualEvidence(),data=fixture()[0].data;evidence.add(data);
  data.buttonTotals[0].down=9;data.presses=9;
  const lower=fixture()[0].data;lower.presses=1;lower.buttonTotals[0].down=1;
  assert.doesNotThrow(()=>evidence.add(lower));
  lower.presses=0;lower.buttonTotals[0].down=0;
  assert.throws(()=>evidence.add(lower),/backwards/);
});

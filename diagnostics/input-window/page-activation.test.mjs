import {test} from 'node:test';
import assert from 'node:assert/strict';
import {InputSession} from './input-session.mjs';
import {PageBridge} from './page-bridge.mjs';
import {PageActivation} from './page-activation.mjs';
const first='11111111-1111-1111-1111-111111111111',second='22222222-2222-2222-2222-222222222222';
const prepare=(requestId=first)=>({kind:'prepare-input',documentId:'doc',requestId});
const grant=(epoch=0,request=first)=>({kind:'mode',documentId:'doc',activationRequestId:request,enabled:true,revision:1,cancellationEpoch:epoch,reason:'explicit-enable'});
test('fresh prepare reads cancellation after focus transfer without weakening epoch checks',()=>{
  const s=new InputSession(),a=new PageActivation('doc',s),b=new PageBridge('doc',s);s.cancel('blur');
  const ack=a.prepare(prepare(),{focused:true});assert.equal(ack.cancellationEpoch,1);
  assert.equal(a.matches(grant(0)),false);const command=grant(ack.cancellationEpoch);assert.equal(a.matches(command),true);assert.equal(b.apply(command,{focused:true}),true);a.applied(command);assert.equal(s.enabled,true);
});
test('cancellation after preparation rejects late grant even after refocus',()=>{
  const s=new InputSession(),a=new PageActivation('doc',s);a.prepare(prepare(),{focused:true});s.cancel('blur');assert.equal(a.cancel(),first);assert.equal(a.matches(grant(s.cancellationEpoch)),false);
});
test('new request replaces pending identity without accepting old grant',()=>{
  const s=new InputSession(),a=new PageActivation('doc',s);a.prepare(prepare(),{focused:true});a.prepare(prepare(second),{focused:true});assert.equal(a.matches(grant()),false);assert.equal(a.matches(grant(0,second)),true);
});
test('wrong document and malformed prepare cannot replace current request',()=>{
  const s=new InputSession(),a=new PageActivation('doc',s);a.prepare(prepare(),{focused:true});assert.equal(a.prepare({...prepare(second),documentId:'old'},{focused:true}),null);assert.equal(a.prepare(prepare('bad'),{focused:true}),null);assert.equal(a.matches(grant()),true);
});
test('unfocused prepare reports rejection and cannot later grant on focus alone',()=>{
  const s=new InputSession(),a=new PageActivation('doc',s);assert.equal(a.prepare(prepare(),{focused:false}).focused,false);assert.equal(a.matches(grant()),false);
});
test('applied grant is single-use and cancel identifies its issued request',()=>{
  const s=new InputSession(),a=new PageActivation('doc',s);a.prepare(prepare(),{focused:true});a.applied(grant());assert.equal(a.matches(grant()),false);assert.equal(a.cancel(),first);assert.equal(a.cancel(),'');
});
test('host observation cancellation clears pending preparation',()=>{
  const s=new InputSession(),a=new PageActivation('doc',s);a.prepare(prepare(),{focused:true});a.applied({enabled:false});assert.equal(a.matches(grant()),false);
});

import test from 'node:test';
import assert from 'node:assert/strict';
import { BankWalk } from './bank-walk.mjs';

const base={enabled:true, waterId:'river-bend', geometryVersion:2, minXmm:-8000,maxXmm:8000,xMm:0,direction:0,fast:false,poseRevision:0};
function fixture() {
  const context={allowed:true,lease:'first',waterId:'river-bend',geometryVersion:2}, messages=[];
  let number=0;
  const w=new BankWalk({documentId:'page',readContext:()=>context,post:m=>messages.push(m),newTransportId:()=>String(++number)});
  const state=(rev,s)=>w.apply({kind:'game-bank-state',documentId:'page',revision:rev,state:s});
  const reply=(message,code)=>w.reply({kind:'game-reply',documentId:'page',transportId:message.transportId,code});
  state(1,base);
  return {w,context,messages,state,reply,key:(code,down,extra={})=>w.key({code,...extra},down)};
}
test('movement projection accepts only whole, bounded frames for this document and does not extrapolate position', () => {
  const {w,state,key}=fixture();
  for(const bad of [{poseRevision:-1},{poseRevision:'1'},{maxXmm:-8000},{xMm:9000},{fast:true},{direction:1.5},{waterId:' bad'}])
    assert.equal(state(2,{...base,...bad}),false);
  assert.equal(w.apply({kind:'game-bank-state',documentId:'old',revision:2,state:base}),false);
  key('KeyD',true); assert.equal(w.state().xMm,0);
  state(2,{...base,xMm:750,poseRevision:8,direction:1}); w.state().xMm=0; assert.equal(w.state().xMm,750);
  assert.equal(w.forWater({waterId:'coastal-bay',geometryVersion:3}),null);
  state(3,{enabled:false}); key('KeyD',true); assert.deepEqual(w.state(),{enabled:false});
});
test('both aliases, simultaneous opposite directions, Shift and repeats issue only changed direction or speed', () => {
  const {key,messages}=fixture(); key('ShiftLeft',true); key('KeyA',true); key('KeyA',true,{repeat:true});
  key('ArrowLeft',true);key('KeyA',false);key('ShiftLeft',false);key('KeyD',true);key('ArrowLeft',false);
  key('ShiftRight',true);key('ShiftRight',false);key('KeyD',false);
  assert.deepEqual(messages.map(m=>[m.direction,m.fast]),[[-1,true],[-1,false],[0,false],[1,false],[1,true],[1,false],[0,false]]);
  assert.deepEqual(messages.map(m=>m.inputSequence),['1','2','3','4','5','6','7']);
  assert.equal(key('KeyW',true),false);assert.equal(key('KeyS',true),false);
});
test('stop barrier ignores delayed starts and releases only after the latest stop and matching authoritative anchor', () => {
  const {w,key,messages,state,reply}=fixture();
  const water={waterId:'river-bend',geometryVersion:2,anchor:{xMm:0}};
  assert.deepEqual(w.aim(water,{u:10,v:20,geometryVersion:2}),{u:10,v:20,geometryVersion:2,poseRevision:0});
  key('KeyD',true);w.stop();assert.equal(w.canCast(water),false);
  assert.equal(reply(messages[0],'walk-updated'),false);
  state(2,{...base,xMm:1000,poseRevision:4,direction:1});reply(messages[1],'walk-stopped');assert.equal(w.pending,true);
  state(3,{...base,xMm:1100,poseRevision:5});assert.equal(w.pending,false);assert.equal(w.canCast(water),false);
  water.anchor.xMm=1100;assert.equal(w.aim(water,{u:11,v:21,geometryVersion:2}).poseRevision,5);
  key('ArrowRight',true,{repeat:true});assert.equal(messages.length,2);
});
test('lease change cancels using its own original lease and geometry; delayed keyup cannot restart', () => {
  const {w,key,messages,context}=fixture();key('KeyA',true);context.lease='next';context.geometryVersion=3;
  w.reconcile();key('KeyA',false);key('KeyA',true,{repeat:true});assert.equal(messages.length,2);
  assert.deepEqual([messages[1].lease,messages[1].direction,messages[1].geometryVersion],['first',0,2]);
});

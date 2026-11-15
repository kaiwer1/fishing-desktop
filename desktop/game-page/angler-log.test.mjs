import test from 'node:test';
import assert from 'node:assert/strict';
import { AnglerLog } from './angler-log.mjs';

const packet = (revision=1) => ({kind:'game-angler-log', documentId:'page-a', revision, state:{
  rulesVersion:1, sequence:8, totalCasts:3, totalCaught:1, totalSold:0, saleMinor:'0', totalXp:'80',
  methods:[{id:'float',casts:1,caught:0,xp:'0'},{id:'bottom',casts:2,caught:1,xp:'80'},{id:'lure',casts:0,caught:0,xp:'0'}],
  species:[{id:'carp',caught:1,maxMassMg:'9007199254740993'}] }});

test('an accepted sale keeps the caught species, method and exact record, independent of incoming and outgoing copies',()=>{
  const view = new AnglerLog('page-a'), m = packet();
  assert.equal(view.apply(m),true);
  m.state.methods[1].caught=44; view.state().species[0].maxMassMg='1';
  const sale = packet(2); Object.assign(sale.state,{sequence:9,totalSold:1,saleMinor:'9007199254740993'});
  assert.equal(view.apply(sale),true);
  assert.equal(view.sequence,9); assert.equal(view.state().methods[1].caught,1);
  assert.equal(view.state().species[0].maxMassMg,'9007199254740993');
  assert.equal(view.state().saleMinor,'9007199254740993');
  assert.equal(view.apply(packet(3)),false); assert.equal(view.apply({...sale,revision:3,documentId:'page-b'}),false);
  assert.equal(view.revision,2);
});

test('corrupt totals, duplicate methods or species and malformed decimal strings cannot replace a confirmed log',()=>{
  const view = new AnglerLog('page-a'); view.apply(packet());
  for (const change of [s=>s.methods[1].id='float',s=>s.methods[0].id=['float'],s=>s.methods[0].xp='01',
      s=>s.species.push({...s.species[0]}),s=>s.species[0].caught=0,s=>s.species[0].maxMassMg='0',
      s=>s.species[0].id='unsafe<key>',s=>s.totalCasts++,s=>s.totalXp='81',s=>s.saleMinor='9223372036854775808',
      s=>s.totalCaught=10001,s=>s.sequence='8']) {
    const m=packet(2); change(m.state); assert.equal(view.apply(m),false);
  }
  const legacy=packet(2); legacy.state.species[0].id='legacy_bream';
  assert.equal(view.apply(legacy),true); assert.equal(view.state().species[0].id,'legacy_bream');
});

test('skill projection discards surplus data and rejects invalid durations or a learned rank without float XP',()=>{
  const view=new AnglerLog('page-a'), p=packet(), base={learned:false,requiredXp:10,baseMs:4000,bonusMs:750};
  p.state.steadyStrike={...base,extra:1};assert(view.apply(p));assert.deepEqual(view.state().steadyStrike,base);
  for(const changes of [{bonusMs:-750},{baseMs:0},{learned:1},{requiredXp:'10'},{learned:true}]){
    const next=packet(2);next.state.steadyStrike={...base,...changes};assert.equal(view.apply(next),false);
  }
});

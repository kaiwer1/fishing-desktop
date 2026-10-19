import { test } from 'node:test';
import assert from 'node:assert/strict';
import { freshWorld, restoreWorld, schoolsAt, fishingConditions, advanceWorld, disturb, pressureAt, isWater, environmentAt } from '../src/ecosystem.js';
import { castLanding, castPower } from '../src/casting.js';

test('cast location changes the local fish mix and compatible bait filters species', () => {
  const world=freshWorld(), school=schoolsAt(world,'pine').find(item=>item.species==='crucian');
  const near=fishingConditions(world,'pine',school,'worm');
  const far=fishingConditions(world,'pine',{x:33,z:-46},'worm');
  assert.ok(near.weights.find(item=>item.id==='crucian').weight > far.weights.find(item=>item.id==='crucian').weight*5);
  assert.ok(near.rate>far.rate);
  assert.equal(fishingConditions(world,'pine',school,'minnow').weights.length,0);
  assert.ok(fishingConditions(world,'river',{x:11,z:-18},'minnow').weights.every(item=>['perch','pike'].includes(item.id)));
});

test('schools migrate continuously with simulation time and conditions vary over the day', () => {
  const world=freshWorld(), first=schoolsAt(world,'pine');
  assert.deepEqual(first,schoolsAt(freshWorld(),'pine'));
  const before=environmentAt(world,'pine');advanceWorld(world,150);
  const after=schoolsAt(world,'pine');assert.notDeepEqual(first,after);
  assert.notEqual(before.hour,environmentAt(world,'pine').hour);
  for (const school of after) assert.ok(isWater('pine',school));
});

test('fishing disturbs the local area, resting recovers it, and unrelated locations stay usable', () => {
  const world=freshWorld(), point={x:-21,z:-12}, other={x:30,z:-43};
  const before=fishingConditions(world,'pine',point,'worm').rate;
  for (let i=0;i<15;i++) disturb(world,'pine',point,.12);
  const high=pressureAt(world,'pine',point);
  assert.ok(high>.6);assert.ok(pressureAt(world,'pine',other)<.05);
  assert.ok(fishingConditions(world,'pine',point,'worm').rate<before*.65);
  advanceWorld(world,240);assert.ok(pressureAt(world,'pine',point)<high*.38);
});

test('equal simulated time preserves migration and pressure independently of frame partitioning', () => {
  const a=freshWorld(), b=freshWorld(), point={x:0,z:-25};disturb(a,'pine',point);disturb(b,'pine',point);
  for(let i=0;i<400;i++)advanceWorld(a,.05);
  for(let i=0;i<200;i++)advanceWorld(b,.1);
  assert.ok(Math.abs(a.elapsed-b.elapsed)<1e-9);
  assert.ok(Math.abs(pressureAt(a,'pine',point)-pressureAt(b,'pine',point))<1e-12);
  const sa=schoolsAt(a,'pine'),sb=schoolsAt(b,'pine');
  for(let i=0;i<sa.length;i++)assert.ok(Math.hypot(sa[i].x-sb[i].x,sa[i].z-sb[i].z)<1e-9);
});

test('restored world data is bounded and invalid water is excluded', () => {
  const world=restoreWorld({version:1,seed:Infinity,elapsed:NaN,pressure:{pine:[-9,Infinity,1e9,'bad']}});
  assert.equal(world.elapsed,0);assert.deepEqual(world.pressure.pine.slice(0,4),[0,0,1,0]);
  assert.equal(world.pressure.pine.length,30);
  assert.equal(isWater('river',{x:22,z:-10}),false);assert.equal(isWater('pine',{x:22,z:-10}),true);
  assert.equal(isWater('__proto__',{x:0,z:-10}),false);
});

test('mouse charge changes cast reach; full charge is capped and invalid release never casts', () => {
  const origin={x:1.2,z:1},aim={x:5,z:-30};
  const short=castLanding('pine',origin,aim,.1),long=castLanding('pine',origin,aim,1.4);
  assert.ok(Math.hypot(long.x-origin.x,long.z-origin.z)>Math.hypot(short.x-origin.x,short.z-origin.z)*2);
  assert.equal(castPower(20),1);assert.equal(castPower(-1),0);assert.equal(castPower(NaN),0);
  assert.deepEqual(castLanding('pine',origin,aim,20),long);
  assert.equal(castLanding('pine',origin,{x:0,z:5},1),null);
});

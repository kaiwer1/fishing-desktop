import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import {createRodStand,standAtWater} from './rod-stand.mjs';
import {toWorld} from './water-space.mjs';

const id=n=>'10000000-0000-0000-0000-'+String(n).padStart(12,'0');
const water={enabled:true,waterId:'lake',spotId:'reed-shore',geometryVersion:2,surfaceYmm:100,
  waterBounds:{minXmm:-40000,maxXmm:40000,minZmm:3000,maxZmm:80000},anchor:{xMm:2000,yMm:1600,zMm:0}};
const end=(x=2800,z=12200)=>({xMm:x,yMm:-950,zMm:z});
const rig=(n,endpoint)=>({rigId:id(n),sessionId:id(30+n),phase:'waiting',label:'底竿 '+n,lineEnd:endpoint});
const row=(slot,rod=null,alarm='none')=>({slot,rig:rod,alarm,reachable:true});
const sample=()=>({waterId:'lake',spotId:'reed-shore',geometryVersion:2,standId:id(1),heldRig:null,
  origin:{xMm:-700,yMm:210,zMm:800},slots:[row(3,rig(3,end(-5000))),row(1,rig(4,end())),row(2)]});

test('stand needs its own matching surveyed world origin; unsupported or foreign stands stay hidden',()=>{
  const s=sample(),render=createRodStand();
  assert.equal(render.group.visible,false);
  for(const input of [null,{...s,origin:null},{...s,origin:{...s.origin,zMm:3100}},{...s,geometryVersion:3},
    {...s,waterId:'river'},{...s,spotId:'old-shore'},{...s,slots:[s.slots[0],s.slots[0],s.slots[2]]}]){
    assert.equal(standAtWater(input,water),false);
    assert.equal(render.update(input,water),false);
    assert.equal(render.state().visible,false);
  }
  assert.equal(render.update(s,water),true);assert.deepEqual(render.group.position.toArray(),[-.7,.21,-.8]);
  assert.equal(render.update(s,{...water,enabled:false}),false);
  assert.ok(render.state().slots.every(s=>s.rigId===null&&!s.lineVisible));
  render.dispose();
});

test('a purchased empty stand shows just its three-post frame and drawing a rod removes both slot meshes',()=>{
  const render=createRodStand(),s=sample();
  s.slots=[row(1),row(2),row(3)];
  render.update(s,water);
  assert.ok(render.group.getObjectByName('three-post-frame').visible);
  assert.ok(render.state().slots.every(r=>!r.rigId));
  const r=rig(7,end());s.slots[1]=row(2,r);render.update(s,water);
  assert.ok(render.group.getObjectByName('bank-rod-2').visible);
  assert.ok(render.group.getObjectByName('bank-line-2').visible);
  s.heldRig=r;s.slots[1]=row(2);render.update(s,water);
  assert.ok(!render.group.getObjectByName('bank-rod-2').visible&&!render.group.getObjectByName('bank-line-2').visible);
  s.slots[1]=row(2,r);render.update(s,water);
  assert.ok(!render.group.getObjectByName('bank-rod-2').visible);
  s.heldRig=null;s.slots[0]=row(1,r);render.update(s,water);
  assert.ok(render.state().slots.every(r=>!r.rigId));
  render.dispose();
});

test('numbered slot offsets, aim yaw and the mono endpoint stay anchored to world millimetres as the camera walks',()=>{
  const render=createRodStand(),s=sample();
  render.update(s,water);
  const snapshots=[];
  for(const n of [1,3]){
    const mesh=render.group.getObjectByName('bank-rod-'+n),line=render.group.getObjectByName('bank-line-'+n);
    const rod=s.slots.find(x=>x.slot===n).rig;
    const p=mesh.getWorldPosition(new THREE.Vector3()),last=toWorld(rod.lineEnd);
    assert.ok(Math.abs(p.x-(s.origin.xMm/1000+(n-2)*.75))<1e-10);
    const dir=new THREE.Vector3(0,0,-1).applyQuaternion(mesh.quaternion);
    assert.ok(Math.abs(dir.x/dir.z-(last.x-p.x)/(last.z-p.z))<1e-10);
    const attr=line.geometry.attributes.position;
    const observed=line.localToWorld(new THREE.Vector3().fromBufferAttribute(attr,1));
    assert.ok(observed.distanceTo(last)<1e-6);
    snapshots.push([...mesh.position.toArray(),mesh.rotation.y,...attr.array]);
  }
  render.update(s,{...water,anchor:{...water.anchor,xMm:8000}});
  [1,3].forEach((n,i)=>{
    const mesh=render.group.getObjectByName('bank-rod-'+n),line=render.group.getObjectByName('bank-line-'+n);
    assert.deepEqual([...mesh.position.toArray(),mesh.rotation.y,...line.geometry.attributes.position.array],snapshots[i]);
  });
  render.dispose();
});

test('only active surveyed line ends can draw filament, and an idle rod has no fish signal',()=>{
  const render=createRodStand(),s=sample();const target=s.slots[1].rig;
  for(const point of [null,{...end(),zMm:2999},{...end(),yMm:101},{...end(),xMm:-40001},{...end(),yMm:1.3},{...end(),xMm:NaN}]){
    target.lineEnd=point;render.update(s,water);
    assert.equal(render.state().slots[0].rigId,target.rigId);
    assert.equal(render.state().slots[0].lineVisible,false);
  }
  Object.assign(target,{phase:'idle',sessionId:null,lineEnd:end(),tipPermille:1000});s.slots[1].alarm='bite';
  render.update(s,water);
  const mesh=render.group.getObjectByName('bank-rod-1');
  assert.ok(mesh.visible);assert.equal(mesh.morphTargetInfluences[0],0);assert.equal(render.state().slots[0].lineVisible,false);
  render.dispose();
});

test('carbon blank and painted quiver share geometry; confirmed touches and pulls affect the matching tips only',()=>{
  const s=sample(),render=createRodStand();
  const l=s.slots.find(s=>s.slot===1),r=s.slots.find(s=>s.slot===3);l.alarm='touch';r.alarm='bite';
  render.update(s,water);
  const a=render.group.getObjectByName('bank-rod-1'),b=render.group.getObjectByName('bank-rod-3');
  assert.equal(a.geometry,b.geometry);assert.equal(a.material,b.material);
  assert.ok(a.morphTargetInfluences[0]>0&&b.morphTargetInfluences[0]>a.morphTargetInfluences[0]);
  const original=a.geometry.attributes.position,delta=a.geometry.morphAttributes.position[0];
  let largest=0;
  for(let i=0;i<delta.count;i++){
    if(original.getZ(i)>=-1.38)assert.ok(delta.getY(i)===0);
    largest=Math.max(largest,-delta.getY(i));
  }
  assert.ok(Math.abs(largest-.046)<1e-6);
  r.rig.tipPermille=413;l.alarm='none';render.update(s,water);
  assert.equal(b.morphTargetInfluences[0],.413);assert.equal(a.morphTargetInfluences[0],0);
  const tipLine=render.group.getObjectByName('bank-line-3').geometry.attributes.position;
  const expected=new THREE.Vector3(0,-.046*.413,-2.13).applyMatrix4(b.matrix);
  assert.ok(new THREE.Vector3().fromBufferAttribute(tipLine,0).distanceTo(expected)<1e-6);
  render.dispose();
});

test('three occupied rods fit seven draw objects and all owned GPU resources release exactly once',()=>{
  const parent=new THREE.Group(),render=createRodStand(),s=sample();parent.add(render.group);
  s.slots[2]=row(2,rig(11,end()));render.update(s,water);
  const geometry=new Set(),materials=new Set();let draws=0;
  render.group.traverseVisible(o=>{if(o.geometry){draws++;geometry.add(o.geometry);materials.add(o.material);}});
  assert.equal(draws,7);assert.equal(geometry.size,5);assert.equal(materials.size,2);
  let disposed=0;for(const res of [...geometry,...materials])res.addEventListener('dispose',()=>disposed++);
  for(let i=0;i<50;i++)render.update(s,water);
  render.dispose();render.dispose();
  assert.equal(disposed,7);assert.equal(parent.children.length,0);assert.equal(render.group.children.length,0);
  assert.equal(render.update(s,water),false);assert.equal(render.state().visible,false);
});

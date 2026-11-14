import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { createGameScene } from './game-scene.mjs';
import { pickAim, projectAim, setBankCamera, validWater } from './water-space.mjs';
import { createTackle, rodPose } from './tackle.mjs';
import { createLake } from './lake.mjs';
import { sampleFightFish } from './fight-fish.mjs';

const water={enabled:true,geometryVersion:1,waterId:'lake',spotId:'bank',
  waterBounds:{minXmm:-40000,maxXmm:40000,minZmm:3000,maxZmm:80000},
  bounds:{minXmm:-12000,maxXmm:12000,minZmm:12000,maxZmm:40000},
  anchor:{xMm:0,yMm:1500,zMm:0},surfaceYmm:0,
  bed:[{zMm:3000,depthMm:600},{zMm:12000,depthMm:1200},{zMm:80000,depthMm:4000}],target:null};
const f={sessionId:'this-cast',phase:'waiting',xMm:550,yMm:0,zMm:13000};
const fight={sessionId:f.sessionId,phase:'fighting',xMm:1250,yMm:-270,zMm:13050,
  tensionMilliN:3000,rodDeflectionMm:280,slackMm:190,reeling:true,reelLevel:6};

test('scene has an offline entry and requires the surveyed surface, anchor and bed',()=>{
  assert.equal(typeof createGameScene,'function');assert.ok(validWater(water));
  for(const broken of [{enabled:false},{waterBounds:null},{bounds:{...water.bounds,maxZmm:80001}},
    {bed:[{zMm:3000,depthMm:600},{zMm:3000,depthMm:300},{zMm:80000,depthMm:4000}]},{anchor:{...water.anchor,yMm:-1}}]){
    assert.equal(validWater({...water,...broken}),false);
  }
});

test('actual camera picks projected edges and interior for wide and narrow landscape',()=>{
  for(const [width,height] of [[820,600],[1600,900],[2560,1080]]){
    const camera=new THREE.PerspectiveCamera(47,1,.022,700),rect={left:137,top:22,width,height};
    setBankCamera(camera,water,width/height);
    for(const u of [0,1640,5000,9020,10000])for(const v of [0,4600,10000]){
      const p=projectAim(camera,water,rect,u,v);
      assert.ok(p,`visible ${u},${v} at ${width}x${height}`);
      assert.deepEqual(pickAim(camera,water,rect,p.clientX,p.clientY),{u,v,geometryVersion:1});
    }
    assert.equal(pickAim(camera,water,rect,rect.left+width/2,rect.top),null);
    assert.equal(pickAim(camera,water,rect,rect.left+width/2,rect.top+height),null);
    assert.equal(pickAim(camera,water,rect,rect.left-1,rect.top),null);
  }
});

test('charge uses host power and flying uses host elapsed milliseconds',()=>{
  const low=rodPose({cast:{phase:'charging',powerPermille:5}});
  const high=rodPose({cast:{phase:'charging',powerPermille:990}});
  assert.ok(high.pitch-low.pitch>.9);
  const launch=t=>rodPose({flight:{...f,phase:'flying',yMm:700,elapsedMilliseconds:t}});
  assert.ok(launch(0).pitch-launch(190).pitch>1);
  assert.deepEqual(launch(130).end.toArray(),[.55,.7,-13]);
});

test('waiting float uses observed bite increments only for the live session',()=>{
  const bite={sessionId:f.sessionId,phase:'strikeable',bobberOffsetMm:-90,bobberLateralMm:14};
  const p=rodPose({flight:f,bite});assert.equal(p.phase,'waiting');assert.ok(p.float);
  assert.ok(p.end.distanceTo(new THREE.Vector3(.564,-.09,-13))<1e-10);
  assert.deepEqual(rodPose({flight:f,bite:{...bite,sessionId:'previous'}}).end.toArray(),[.55,0,-13]);
});

test('fight load bends the articulated rod, actual line endpoint follows underwater fish',()=>{
  const rig=createTackle();
  const unloaded={water,flight:f,fight:{...fight,rodDeflectionMm:0}};
  const loaded={water,flight:f,fight};
  rig.animate(unloaded,0,.1);
  const start=rig.tip.getWorldPosition(new THREE.Vector3());
  for(let i=0;i<6;i++)rig.animate(loaded,i*.1,.1);
  const bent=rig.tip.getWorldPosition(new THREE.Vector3());assert.ok(bent.y<start.y-.13);
  assert.equal(rig.float.visible,false);assert.equal(rig.line.visible,true);
  const last=new THREE.Vector3().fromBufferAttribute(rig.line.geometry.attributes.position,22);
  assert.ok(last.distanceTo(new THREE.Vector3(1.25,-.27,-13.05))<1e-6);
  let disposed=0;rig.line.geometry.addEventListener('dispose',()=>disposed++);rig.dispose();assert.equal(disposed,1);
});

test('a new cast overrides the prior terminal snapshot and stale fight never animates',()=>{
  assert.equal(rodPose({flight:f,fight:{...fight,sessionId:'previous'}}).phase,'waiting');
  assert.equal(rodPose({flight:f,fight:{...fight,phase:'escaped'}}).end,null);
  assert.equal(rodPose({cast:{phase:'charging',powerPermille:45},flight:f,fight:{...fight,phase:'landed'}}).phase,'charging');
});

test('the coastal bay opens to a scenic horizon beyond the surveyed bed',()=>{
  const coast=createLake({...water,waterId:'coastal-bay'});
  try {
    assert.equal(coast.kind,'sea');
    const surface=coast.group.getObjectByName('surveyed-surface');
    assert.equal(surface.geometry.parameters.width,80);
    assert.equal(surface.geometry.parameters.height,77);
    const horizon=coast.group.children.filter(o=>o.name==='open-sea-horizon');
    assert.equal(horizon.length,3);
    assert.ok(horizon.every(o=>o.material===surface.material&&!o.layers.isEnabled(0)));
    assert.ok(coast.group.getObjectByName('tidal-rocks'));
    const points=coast.group.getObjectByName('surveyed-bed').geometry.attributes.position;
    assert.ok(Math.abs(points.getY(points.count-1)+4)<1e-6);
    coast.animate(4);assert.equal(surface.material.uniforms.time.value,4);
    coast.impact(new THREE.Vector3(3,0,-14),4);assert.deepEqual(surface.material.uniforms.impact.value.toArray(),[3,-14,4]);
  }finally{coast.dispose();}
});

test('confirmed clothing changes the sleeve cloth only and can return to the default',()=>{
  const rig=createTackle();
  try{
    const cloth=rig.root.getObjectByProperty('type','Mesh');
    const shaftColor=cloth.material.color.getHex();
    rig.animate({water,sleeveColor:'#375a90'},0,.1);
    assert.equal(rig.sleeveColor(),'#375a90');assert.equal(cloth.material.color.getHex(),shaftColor);
    let confirmed=0;
    rig.root.traverse(o=>{if(o.material?.color?.getHex()===0x375a90)confirmed++;});
    assert.equal(confirmed,1);
    rig.animate({water,sleeveColor:null},1,.1);assert.equal(rig.sleeveColor(),null);
  }finally{rig.dispose();}
});

test('the single shallow fish image follows confirmed encounter measurements and clears on end',()=>{
  const active={...fight,encounterId:'hooked-encounter'};
  const views={water,flight:f,fight:active};
  const initial=sampleFightFish(views,null);
  assert.ok(initial.opacity>0);assert.equal(initial.zMm,active.zMm);assert.equal(initial.length,.4);
  const moved=sampleFightFish({...views,fight:{...active,xMm:active.xMm+600}},initial);
  assert.equal(moved.xMm,active.xMm+600);assert.ok(Math.abs(moved.yaw)<1e-12);
  assert.ok(sampleFightFish({...views,fight:{...active,speciesKey:'grass-carp'}},initial).length>initial.length);
  for(const patch of [{phase:'landed'},{phase:'failure-pending'},{phase:'escaped'},{yMm:-950},{sessionId:'old'},{encounterId:null}]){
    assert.equal(sampleFightFish({...views,fight:{...active,...patch}},moved),null);
  }
  assert.equal(sampleFightFish({...views,bite:{sessionId:f.sessionId,encounterId:'other'}},moved),null);
  assert.equal(sampleFightFish({water,bite:{phase:'strikeable'},flight:f},null),null);
});

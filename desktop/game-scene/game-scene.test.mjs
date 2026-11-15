import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import * as THREE from 'three';
import { createGameScene } from './game-scene.mjs';
import { pickAim, projectAim, setBankCamera, validWater, waterGeometryKey } from './water-space.mjs';
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

test('spinning tackle shows the actual airborne or submerged spoon, never a guessed float',()=>{
  const rig=createTackle(),lake=createLake({...water,waterId:'coastal-bay'});
  const views={water,flight:f,lureSelected:true};
  const lure={sessionId:f.sessionId,phase:'in-water',position:{xMm:475,yMm:-420,zMm:11950},reeling:true,speedLevel:3};
  try{
    rig.animate(views,0,.05);assert.ok(!rig.float.visible&&!rig.spoon.visible&&!rig.line.visible);
    rig.animate({...views,flight:{...f,phase:'flying',elapsedMilliseconds:250,yMm:490}},.25,.05);
    assert.ok(rig.spoon.visible&&!rig.float.visible);assert.deepEqual(rig.spoon.position.toArray(),[.55,.49,-13]);
    rig.animate({...views,lure:{...lure,sessionId:'earlier'}},.5,.05);assert.ok(!rig.spoon.visible&&!rig.line.visible);
    rig.animate({...views,lure},1,.05);assert.deepEqual(rig.spoon.position.toArray(),[.475,-.42,-11.95]);
    assert.ok(rig.spoon.visible&&!rig.float.visible&&rig.line.visible);lake.lure(rig.spoon);
    const uniform=lake.group.getObjectByName('surveyed-surface').material.uniforms.spoon.value;
    assert.deepEqual(uniform.toArray().slice(0,3),[.475,-.42,-11.95]);assert.ok(uniform.w>0);
    assert.deepEqual(rodPose({...views,lure:{...lure,phase:'following'}}),rodPose({...views,lure}));
    assert.equal(rodPose({...views,lure:{...lure,position:{...lure.position,yMm:700}}}).end,null);
    rig.animate({...views,lure:{...lure,phase:'hooked'},fight},1.05,.05);
    assert.ok(!rig.spoon.visible&&!rig.float.visible&&rig.line.visible);
    assert.deepEqual(rodPose({...views,lure:{...lure,phase:'hooked'},fight}).end.toArray(),[1.25,-.27,-13.05]);
    lake.lure(null);assert.equal(uniform.w,0);
  }finally{lake.dispose();rig.dispose();}
});

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
  for(const powerPermille of [25,450,980]){
    const load=rodPose({cast:{phase:'prepared',powerPermille}});
    const release=rodPose({flight:{...f,phase:'flying',elapsedMilliseconds:0,powerPermille}});
    for(const axis of ['pitch','yaw','roll','flex','shiftX','shiftY'])assert.equal(release[axis],load[axis]);
  }
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

const bottomSignal=(phase,tipPermille=0,lineEnd=null)=>({sessionId:f.sessionId,phase,tipPermille,lineEnd});

test('bottom waiting hides the bobber and never invents a terminal position from the water or float',()=>{
  for(const phase of ['sinking','settling','settled','touch','bite','missed','retrieve-required']){
    const bottom=bottomSignal(phase,130);
    const pose=rodPose({water,flight:f,bottom,bite:{sessionId:f.sessionId,phase:'strikeable',bobberOffsetMm:-90}});
    assert.equal(pose.phase,'bottom-'+phase);
    assert.equal(pose.float,false);assert.equal(pose.end,null);assert.equal(pose.bottomTipPermille,130);
  }
  const observed={xMm:430,yMm:-510,zMm:12940};
  const view={water,flight:f,bottom:bottomSignal('sinking',44,observed)};
  assert.deepEqual(rodPose(view).end.toArray(),[.43,-.51,-12.94]);
  for(const bad of [{...observed,yMm:NaN},{...observed,zMm:null},{...observed,xMm:1.5},
    {...observed,zMm:90000},{...observed,yMm:10}]){
    const pose=rodPose({...view,bottom:{...view.bottom,lineEnd:bad}});
    assert.equal(pose.float,false);assert.equal(pose.end,null);
  }
});

test('bottom gear and the wait signal belong to this one cast, ending or changing casts clears the line',()=>{
  const bottom=bottomSignal('bite',755,{xMm:450,yMm:-1150,zMm:13150});
  assert.deepEqual(rodPose({water,flight:f,bottom:null}),rodPose({water,flight:f}));
  assert.deepEqual(rodPose({water,flight:f,bottom:{...bottom,sessionId:'previous'}}),rodPose({water,flight:f}));
  const terminal=rodPose({water,flight:f,bottom:{...bottom,phase:'retrieved'},fight});
  assert.equal(terminal.float,false);assert.equal(terminal.end,null);
  const waiting=rodPose({water,flight:f,bottom,fight});
  assert.equal(waiting.phase,'bottom-bite');assert.equal(waiting.end.x,.45);
  const hooked=rodPose({water,flight:f,bottom:{...bottom,phase:'hooked'},fight});
  assert.equal(hooked.phase,'fighting');assert.equal(hooked.float,false);
  assert.deepEqual(hooked.end.toArray(),[1.25,-.27,-13.05]);
  assert.equal(rodPose({water,flight:{...f,phase:'ended'},bottom}).end,null);
});

test('observed bottom signal bends only the bright quiver tip by up to 46 mm and restores float gear',()=>{
  const rig=createTackle();
  const rodTipLocal=()=>rig.root.worldToLocal(rig.tip.getWorldPosition(new THREE.Vector3()));
  const painted=()=>{const found=[];rig.root.traverse(o=>{if(o.material?.color?.getHex()===0xe6e59a)found.push(o);});return found;};
  try{
    rig.animate({water,flight:f},0,.1);assert.equal(painted().length,0);
    const lineEnd={xMm:470,yMm:-1020,zMm:12710};
    const render=(value,time)=>rig.animate({water,flight:f,bottom:bottomSignal('settled',value,lineEnd)},time,1/30);
    render(0,0);assert.equal(painted().length,3);
    const start=rodTipLocal();
    for(const value of [1000,500,0,173]){
      render(value,20);const tip=rodTipLocal();
      assert.ok(Math.abs(start.y-tip.y-.046*value/1000)<.00005);
      assert.equal(rig.float.visible,false);assert.equal(rig.bottomTipPermille(),value);
      const line=rig.line.geometry.attributes.position;
      assert.ok(new THREE.Vector3().fromBufferAttribute(line,line.count-1).distanceTo(new THREE.Vector3(.47,-1.02,-12.71))<1e-6);
      render(value,200);assert.ok(rodTipLocal().distanceTo(tip)<1e-9);
    }
    rig.animate({water,flight:f,bottom:bottomSignal('sinking')},201,.1);
    assert.equal(rig.line.visible,false);
    rig.animate({water,flight:f,bottom:null},202,.1);
    assert.equal(rig.float.visible,true);assert.equal(rig.bottomTipPermille(),null);assert.equal(painted().length,0);
  }finally{rig.dispose();}
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
    const wash=coast.group.getObjectByName('tidal-wash');
    assert.ok(wash.material===surface.material&&!wash.layers.isEnabled(0));
    wash.geometry.computeBoundingBox();surface.geometry.computeBoundingBox();
    assert.equal(wash.geometry.boundingBox.max.y,surface.geometry.boundingBox.min.y);
    const cape=coast.group.getObjectByName('distant-headland');
    cape.geometry.computeBoundingBox();
    assert.ok(cape.geometry.boundingBox.max.z < -water.waterBounds.maxZmm/1000);
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

const bankView=x=>({...water,anchor:{...water.anchor,xMm:x}});
test('walking preserves the survey, ground origin and scenery identity; a changed bed or station rebuilds',()=>{
  const left=bankView(-8000),right=bankView(8000);
  assert.equal(waterGeometryKey(left),waterGeometryKey(right));
  assert.notEqual(waterGeometryKey(left),waterGeometryKey({...left,spotId:'other'}));
  assert.notEqual(waterGeometryKey(left),waterGeometryKey({...left,bed:left.bed.map(k=>({...k,depthMm:k.depthMm+5}))}));
  const coast=w=>({...w,waterId:'coastal-bay'});
  const before=createLake(coast(left)),after=createLake(coast(right));
  try{
    const rocks=group=>group.getObjectByName('tidal-rocks').instanceMatrix.array;
    assert.deepEqual(rocks(before.group),rocks(after.group));
    assert.deepEqual(before.group.getObjectByName('surveyed-surface').position.toArray(),
      after.group.getObjectByName('surveyed-surface').position.toArray());
  }finally{before.dispose();after.dispose();}
});

test('both bank limits fit the same lens and round-trip fixed world targets on three landscape sizes',()=>{
  for(const [width,height] of [[820,600],[1600,900],[2560,1080]]){
    const camera=new THREE.PerspectiveCamera(47,1,.022,700),rect={left:48,top:21,width,height};
    let initialFov,firstPixel;
    for(const x of [-8000,0,8000]){
      const station=bankView(x);setBankCamera(camera,station,width/height);
      assert.equal(camera.position.x,x/1000);
      if(initialFov===undefined)initialFov=camera.fov;
      assert.equal(camera.fov,initialFov);
      for(const u of [0,4500,10000])for(const v of [0,6600,10000]){
        const pixel=projectAim(camera,station,rect,u,v);
        assert.ok(pixel,`target ${u},${v} from ${x} at ${width}x${height}`);
        assert.deepEqual(pickAim(camera,station,rect,pixel.clientX,pixel.clientY),{u,v,geometryVersion:1});
      }
      const middle=projectAim(camera,station,rect,5000,5000);
      if(firstPixel)assert.ok(Math.abs(firstPixel.clientX-middle.clientX)>5);
      firstPixel=middle;
    }
  }
});

test('grip follows the confirmed moving anchor, the charge pose and fish line stay in world space',()=>{
  const rig=createTackle(),camera=new THREE.PerspectiveCamera(47,1,.022,700);
  try{
    for(const x of [-8000,0,8000]){
      const station=bankView(x),yaw=setBankCamera(camera,station,1.6);
      rig.animate({water:station,cast:{phase:'charging',powerPermille:800}},0,.1,yaw);
      assert.equal(rig.anchorXmm(),x);
      const hand=rig.root.getWorldPosition(new THREE.Vector3()).project(camera);
      assert.ok(hand.x>0&&hand.x<.95&&hand.y>-.98&&hand.y<0,`hand at ${x}`);
      rig.animate({water:station,flight:f},1,.1,yaw);
      assert.deepEqual(rig.float.position.toArray(),[.55,0,-13]);
      const tip=new THREE.Vector3().fromBufferAttribute(rig.line.geometry.attributes.position,22);
      assert.ok(tip.distanceTo(rig.float.position)<1e-5);
    }
  }finally{rig.dispose();}
});

test('offline shoreline scans are the three credited CC0 originals and fit the texture budget',async()=>{
  const assets=new URL('./assets/',import.meta.url);
  const credits=await readFile(new URL('SCENE-ASSET-CREDITS.txt',assets),'utf8');
  let bytes=0;
  for(const name of ['forest-ground.jpg','river-pebbles.jpg','coastal-rock.jpg']){
    const file=await readFile(new URL(name,assets));
    assert.deepEqual([...file.subarray(0,2)],[255,216]);
    assert.deepEqual([...file.subarray(-2)],[255,217]);
    assert.ok(credits.includes(name));assert.ok(credits.includes(createHash('sha256').update(file).digest('hex')));
    bytes+=file.byteLength;
  }
  assert.ok(credits.includes('https://polyhaven.com/license'));
  assert.equal(bytes,2451752);assert.ok(bytes<4*1024*1024);
});

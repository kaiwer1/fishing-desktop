import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { createTackle } from './tackle.mjs';
import { setBankCamera } from './water-space.mjs';

const water={anchor:{xMm:0,yMm:1500,zMm:0},surfaceYmm:0,
  bounds:{minXmm:-12000,maxXmm:12000,minZmm:12000,maxZmm:40000}};
const flight={sessionId:'a',phase:'waiting',xMm:0,yMm:0,zMm:14000};
const lure={sessionId:'a',phase:'in-water',reeling:true,speedLevel:3,position:{xMm:0,yMm:-300,zMm:14000}};
const lureWater={...water,waterBounds:{minXmm:-20000,maxXmm:20000,minZmm:1000,maxZmm:60000}};
const retrieve={water:lureWater,lureSelected:true,lure,flight};

function position(node){return node.getWorldPosition(new THREE.Vector3());}

test('left wrist stays in its cuff and the left palm stays on the orbiting crank, even across a pause',()=>{
  const rig=createTackle();
  try{
    const find=name=>rig.root.getObjectByName(name);
    const hand=find('winding-hand'),knob=find('crank-grip'),sleeve=find('winding-sleeve'),wrist=find('winding-wrist'),spool=find('line-spool');
    const samples=[];
    for(let frame=0;frame<100;frame++){
      rig.animate(retrieve,frame/60,1/60);
      assert.ok(position(hand).distanceTo(position(knob))<1e-9);
      assert.ok(position(wrist).distanceTo(position(sleeve))<1e-9);
      samples.push(spool.position.z);
    }
    assert.ok(Math.max(...samples)-Math.min(...samples)>.01);
    const before=position(hand),rotation=find('reel-crank').rotation.x;
    rig.animate(retrieve,13,0);
    assert.equal(find('reel-crank').rotation.x,rotation);
    assert.ok(before.distanceTo(position(hand))<1e-9);
    rig.animate({...retrieve,lure:{...lure,reeling:false}},14,.08);
    assert.equal(find('reel-crank').rotation.x,rotation);
  }finally{rig.dispose();}
});

test('the supporting hand braces the butt while charging and returns to the knob after launch',()=>{
  const rig=createTackle();
  try{
    const hand=rig.root.getObjectByName('winding-hand'),knob=rig.root.getObjectByName('crank-grip');
    rig.animate({water},0,1/60);
    const butt=new THREE.Vector3(-.015,-.008,.183);
    const start=hand.position.distanceTo(butt);let previous=start;
    for(let frame=0;frame<24;frame++){
      rig.animate({water,cast:{phase:'charging',powerPermille:800}},frame/60,1/60);
      const d=hand.position.distanceTo(butt);assert.ok(d<=previous+1e-6);previous=d;
    }
    assert.ok(start>.20&&previous<1e-9);
    for(let frame=0;frame<42;frame++)rig.animate({water,flight:{...flight,phase:'flying',elapsedMilliseconds:frame*1000/60}},frame/60,1/60);
    assert.ok(position(knob).distanceTo(position(hand))<1e-9);
    for(const aspect of [4/3,16/9,21/9,32/9]){
      const camera=new THREE.PerspectiveCamera();const yaw=setBankCamera(camera,water,aspect);
      for(let frame=0;frame<120;frame++){
        rig.animate(retrieve,frame/60,1/60,yaw,camera.fov);
        const screen=position(hand).project(camera);
        assert.ok(screen.x>-.8&&screen.x<.8&&screen.y<-.12&&screen.y>-.9,
          'crank grip should stay in frame below the aim throughout the orbit: '+screen.toArray());
        if(frame%4!==0)continue;
        let visible=0,total=0;
        hand.updateWorldMatrix(true,true);
        hand.traverse(mesh=>{
          if(!mesh.isMesh)return;
          const vertices=mesh.geometry.attributes.position,p=new THREE.Vector3();
          for(let i=0;i<vertices.count;i+=5){
            p.fromBufferAttribute(vertices,i).applyMatrix4(mesh.matrixWorld).project(camera);
            if(Math.abs(p.x)<1&&Math.abs(p.y)<1)visible++;
            total++;
          }
        });
        assert.ok(visible/total>.94,'the palm and bent fingers should remain on screen at '+aspect);
      }
    }
  }finally{rig.dispose();}
});

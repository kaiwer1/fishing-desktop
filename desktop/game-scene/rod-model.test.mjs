import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { createTackle } from './tackle.mjs';

const water={anchor:{xMm:2500,yMm:1500,zMm:1000},surfaceYmm:0,
  waterBounds:{minXmm:-30000,maxXmm:30000,minZmm:3000,maxZmm:80000}};
const flight={sessionId:'cast',phase:'waiting',xMm:500,yMm:0,zMm:13000};
const inAir={...flight,phase:'flying',elapsedMilliseconds:120,yMm:4500};
const lineEnd={xMm:450,yMm:-1200,zMm:13000};
const bottom={sessionId:flight.sessionId,phase:'bite',tipPermille:850,lineEnd};
const lure={sessionId:flight.sessionId,phase:'in-water',position:lineEnd,reeling:true,speedLevel:4};
const world=node=>node.getWorldPosition(new THREE.Vector3());
const attributePoint=(attribute,index)=>new THREE.Vector3().fromBufferAttribute(attribute,index);
const close=(actual,expected)=>assert.ok(actual.distanceTo(expected)<2e-6,
  'expected '+actual.toArray()+' to reach '+expected.toArray());

test('selected kits change the blank, grip and tip at the bank before the first cast',()=>{
  const rig=createTackle();
  try{
    const fullGrip=rig.root.getObjectByName('rear-grip'),splitGrip=rig.root.getObjectByName('spinning-split-grip');
    const distance=()=>world(rig.tip).distanceTo(world(rig.root));
    const quiverSections=()=>{
      let count=0;rig.root.traverse(node=>{if(node.material?.color.getHex()===0xe6e59a)count++;});
      return count;
    };
    rig.animate({water},0,1);
    const floatLength=distance(),cork=fullGrip.material;
    assert.equal(rig.rodType(),'float');assert.ok(fullGrip.visible&&!splitGrip.visible);
    assert.ok(cork.map?.isDataTexture);assert.equal(quiverSections(),0);
    rig.animate({water,lureSelected:true},0,1);
    assert.equal(rig.rodType(),'spinning');assert.ok(!fullGrip.visible&&splitGrip.visible);
    assert.ok(floatLength-distance()>.28);assert.equal(quiverSections(),0);
    assert.equal(splitGrip.children[0].material,cork);assert.notEqual(splitGrip.children[1].material,cork);
    rig.animate({water,bottomSelected:true},0,1);
    assert.equal(rig.rodType(),'bottom');assert.ok(fullGrip.visible&&!splitGrip.visible);
    assert.notEqual(fullGrip.material,cork);assert.equal(quiverSections(),3);
    assert.ok(Math.abs(distance()-floatLength)<1e-8);assert.equal(rig.bottomTipPermille(),null);
    rig.animate({water},0,1);
    assert.equal(rig.rodType(),'float');assert.equal(fullGrip.material,cork);assert.equal(quiverSections(),0);
  }finally{rig.dispose();}
});

test('line stays on its spool, bail roller and each moving guide through casting, reeling and quiver bites',()=>{
  const rig=createTackle();
  try{
    const thread=rig.group.getObjectByName('threaded-line').geometry.attributes.position;
    const spool=rig.root.getObjectByName('line-spool'),rotor=rig.root.getObjectByName('bail-rotor');
    const guides=[];rig.root.traverse(o=>{if(o.geometry?.type==='TorusGeometry')guides.push(o);});
    assert.equal(guides.length,9);assert.equal(thread.count,guides.length+2);
    const views=[
      {water,cast:{phase:'charging',powerPermille:820}},
      {water,flight:inAir},
      ...Array.from({length:45},()=>({water,flight,lureSelected:true,lure})),
      {water,flight,bottomSelected:true,bottom}
    ];
    for(const [frame,view] of views.entries()){
      rig.animate(view,frame/30,1/30,.2);
      const first=spool.worldToLocal(attributePoint(thread,0));
      close(first,new THREE.Vector3(.036*Math.cos(rotor.rotation.z),.036*Math.sin(rotor.rotation.z),-.048));
      close(attributePoint(thread,1),rotor.localToWorld(new THREE.Vector3(.049,.004,-.05)));
      guides.forEach((guide,i)=>close(attributePoint(thread,i+2),world(guide)));
      close(attributePoint(thread,thread.count-1),world(rig.tip));
      if(rig.line.visible)close(attributePoint(rig.line.geometry.attributes.position,0),world(rig.tip));
    }
    assert.ok(rotor.rotation.z>2*Math.PI);
  }finally{rig.dispose();}
});

test('a bottom cast carries the sinker through the air and to the reported bed, including the packet gap',()=>{
  const rig=createTackle();
  try{
    rig.animate({water,bottomSelected:true,flight:inAir},0,.05);
    assert.ok(rig.line.visible&&rig.sinker.visible&&!rig.float.visible&&!rig.spoon.visible);
    close(rig.sinker.position,new THREE.Vector3(.5,4.5,-13));
    rig.animate({water,bottomSelected:true,flight},.1,.05);
    assert.ok(!rig.line.visible&&!rig.sinker.visible&&!rig.float.visible);
    rig.animate({water,bottomSelected:true,flight,bottom},.2,.05);
    assert.ok(rig.sinker.visible&&!rig.float.visible);
    close(rig.sinker.position,new THREE.Vector3(.45,-1.2,-13));
    rig.animate({water,flight:inAir},.3,.05);
    assert.ok(!rig.sinker.visible&&rig.float.visible);
  }finally{rig.dispose();}
});

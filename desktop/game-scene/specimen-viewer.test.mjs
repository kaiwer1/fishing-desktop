import test from 'node:test';
import assert from 'node:assert/strict';
import { createSpecimenViewer } from './specimen-viewer.mjs';

function fixture(){
  const frames=new Map(),captures=new Set();let serial=0;
  const win=Object.assign(new EventTarget(),{requestAnimationFrame:cb=>{frames.set(++serial,cb);return serial;},cancelAnimationFrame:n=>frames.delete(n),devicePixelRatio:2});
  const canvas=Object.assign(new EventTarget(),{clientWidth:560,clientHeight:260,ownerDocument:{defaultView:win},focus(){},
    setPointerCapture:id=>captures.add(id),hasPointerCapture:id=>captures.has(id),releasePointerCapture:id=>captures.delete(id)});
  const renderer={info:{render:{triangles:0}},renders:0,disposed:0,lost:0,
    setSize(){},setPixelRatio(r){this.dpr=r;},render(scene){this.renders++;let count=0;scene.traverse(o=>{
      if(o.isMesh)count+=(o.geometry.index?.count??o.geometry.attributes.position.count)/3;
    });this.info.render.triangles=count;},dispose(){this.disposed++;},forceContextLoss(){this.lost++;}};
  return {canvas,renderer,captures,frames,makeRenderer:()=>renderer,
    step(){const queue=[...frames.values()];frames.clear();queue.forEach(cb=>cb(100));},
    event(type,fields={}){const e=Object.assign(new Event(type,{cancelable:true}),fields);canvas.dispatchEvent(e);return e;}};
}
test('one real specimen renders on demand, coalesces drag/zoom, pauses and releases the GPU context',()=>{
  const f=fixture(),v=createSpecimenViewer(f.canvas,'spotted-seabass',{makeRenderer:f.makeRenderer});
  assert.equal(f.frames.size,0);v.setActive(true);v.setActive(true);assert.equal(f.frames.size,1);f.step();
  assert.equal(v.state().frames,1);assert.ok(v.state().triangles>1000);assert.equal(f.renderer.dpr,1.5);
  v.setActive(true);assert.equal(f.frames.size,0);
  const before=v.state();f.event('pointerdown',{button:0,pointerId:1,clientX:100,clientY:92});
  f.event('pointermove',{buttons:1,pointerId:1,clientX:200,clientY:110});f.event('wheel',{deltaY:-100,deltaMode:0});
  f.event('keydown',{key:'ArrowLeft'});assert.equal(f.frames.size,1);f.step();
  assert.notEqual(v.state().yaw,before.yaw);assert.notEqual(v.state().pitch,before.pitch);assert.ok(v.state().zoom>before.zoom);
  for(let i=0;i<15;i++)f.event('wheel',{deltaY:-500,deltaMode:0});
  assert.equal(v.state().zoom,2.6);v.setActive(false);assert.equal(f.frames.size,0);assert.equal(f.captures.size,0);
  const stopped=v.state();f.event('wheel',{deltaY:150});f.event('keydown',{key:'ArrowLeft'});assert.deepEqual(v.state(),stopped);
  v.setActive(true);f.step();assert.equal(v.state().frames,3);v.dispose();v.dispose();
  assert.equal(f.renderer.disposed,1);assert.equal(f.renderer.lost,1);assert.equal(f.frames.size,0);
  f.event('wheel',{deltaY:-160});v.setActive(true);assert.equal(f.frames.size,0);
});
test('context loss cancels pending rendering and reports once; an unknown catch does not construct a renderer',()=>{
  const f=fixture(),faults=[],v=createSpecimenViewer(f.canvas,'carp',{makeRenderer:f.makeRenderer,onFault:s=>faults.push(s)});
  v.setActive(true);f.event('webglcontextlost');f.event('webglcontextlost');assert.deepEqual(faults,['specimen-context-lost']);
  assert.equal(f.frames.size,0);assert.equal(v.state().active,false);v.dispose();
  let built=false;assert.throws(()=>createSpecimenViewer(f.canvas,'unknown',{makeRenderer:()=>{built=true;}}),/unknown-specimen/);assert.equal(built,false);
});

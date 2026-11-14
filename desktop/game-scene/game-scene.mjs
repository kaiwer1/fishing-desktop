import * as THREE from 'three';
import { createFrameScheduler } from '../../src/frame-scheduler.js';
import { renderSize } from '../../src/display.js';
import { createLake } from './lake.mjs';
import { createTackle } from './tackle.mjs';
import { sampleFightFish } from './fight-fish.mjs';
import { aimPoint, pickAim, projectAim, setBankCamera, validWater } from './water-space.mjs';

const viewNames=['water','cast','flight','bite','fight','sleeveColor'];

// All inputs are read-only page projections. The renderer neither advances a
// fishing clock nor posts input. Projection helpers use live CSS client pixels.
export function createGameScene(canvas,{onFault}={}) {
  const renderer=new THREE.WebGLRenderer({canvas,antialias:true,alpha:false,powerPreference:'low-power'});
  renderer.outputColorSpace=THREE.SRGBColorSpace;
  renderer.toneMapping=THREE.ACESFilmicToneMapping;renderer.toneMappingExposure=1.08;
  renderer.setPixelRatio(1);
  const scene=new THREE.Scene();scene.background=new THREE.Color(0xc7c5ad);scene.fog=new THREE.FogExp2(0xc4c7b9,.0068);
  const camera=new THREE.PerspectiveCamera(47,1,.022,700);camera.layers.enable(1);
  scene.add(new THREE.HemisphereLight(0xfff1d6,0x79795c,1.77));
  const sun=new THREE.DirectionalLight(0xffdfae,2.15);sun.position.set(-47,23,-100);scene.add(sun);
  const tackle=createTackle();scene.add(tackle.group);
  const aimMarker=new THREE.Mesh(new THREE.RingGeometry(.42,.467,56),
    new THREE.MeshBasicMaterial({color:0xeceec7,transparent:true,opacity:.86,depthWrite:false,side:THREE.DoubleSide}));
  aimMarker.name='legal-water-hover';aimMarker.layers.set(1);aimMarker.visible=false;aimMarker.rotation.x=-Math.PI/2;scene.add(aimMarker);
  let canAim=false;
  function hideAim(){aimMarker.visible=false;}
  function hover(event){
    if(!canAim||!active||disposed||error||!water||event.buttons!==0){hideAim();return;}
    const picked=pickAim(camera,water,canvas.getBoundingClientRect(),event.clientX,event.clientY);
    if(!picked){hideAim();return;}
    aimPoint(water,picked.u,picked.v,aimMarker.position);aimMarker.position.y+=.012;
    // Increase only the far marker's diameter, so it stays legible at low
    // grazing angles while remaining centred on the actual camera ray.
    aimMarker.scale.setScalar(Math.min(2.2,Math.max(1,camera.position.distanceTo(aimMarker.position)/19)));
    aimMarker.visible=true;
  }
  let lake=null,key=null,water=null,disposed=false,error=null,active=false;
  let width=0,height=0,aspect=0,frames=0,elapsed=0,lastFlight=null,pendingImpact=null,rodPhase='idle',fish=null;
  const views={water:null,cast:null,flight:null,bite:null,fight:null};
  const scheduler=createFrameScheduler({render(_now,dt){
    elapsed+=dt;
    if(pendingImpact){lake.impact(pendingImpact,elapsed);pendingImpact=null;}
    lake.animate(elapsed);
    rodPhase=tackle.animate(views,elapsed,dt);
    renderer.render(scene,camera);frames++;
  },onError(){fault('webgl-render-failed');}});
  function fault(code){
    if(disposed||error)return;
    error=code;active=false;hideAim();scheduler.stop();onFault?.(code);
  }
  function lost(event){event.preventDefault();fault('webgl-context-lost');}
  function sync(){
    if(!disposed&&!error&&active&&lake&&width&&height&&canvas.ownerDocument?.visibilityState!=='hidden')scheduler.wake(30);
    else {hideAim();scheduler.stop();}
  }
  function resize(){
    if(disposed||error)return;
    const r=canvas.getBoundingClientRect();
    const size=renderSize(r.width,r.height,globalThis.devicePixelRatio||1);
    const w=r.width>0&&r.height>0?size.width:0,h=w?size.height:0;
    if(w!==width||h!==height){width=w;height=h;if(w&&h)renderer.setSize(w,h,false);}
    if(lake&&r.width>0&&r.height>0&&aspect!==r.width/r.height){
      aspect=r.width/r.height;setBankCamera(camera,water,aspect);lake.refreshMirror();
    }
    sync();
  }
  function resetLake(next){
    const nextKey=validWater(next)?JSON.stringify([next.waterId,next.spotId,next.geometryVersion,next.waterBounds,next.bounds,next.anchor,next.surfaceYmm,next.bed]):null;
    if(nextKey===key){water=nextKey?next:null;return;}
    if(lake)lake.dispose();
    lake=null;water=null;key=null;aspect=0;pendingImpact=null;lastFlight=null;fish=null;hideAim();
    if(nextKey){
      lake=createLake(next);water=next;key=nextKey;scene.add(lake.group);resize();
    }else sync();
  }
  function update(data={}){
    if(disposed||error)return;
    try {
      for(const name of viewNames)if(Object.hasOwn(data,name))views[name]=data[name];
      if(Object.hasOwn(data,'canAim')){canAim=data.canAim===true;if(!canAim)hideAim();}
      if(Object.hasOwn(data,'water'))resetLake(data.water);
      fish=lake?sampleFightFish(views,fish):null;
      lake?.fish(fish);
      const f=views.flight;
      if(lake&&f?.phase==='waiting'&&lastFlight?.phase==='flying'&&f.sessionId===lastFlight.sessionId){
        pendingImpact=new THREE.Vector3(f.xMm/1000,f.yMm/1000,-f.zMm/1000);
      }
      lastFlight=f?{phase:f.phase,sessionId:f.sessionId}:null;
    }catch{fault('webgl-render-failed');}
  }
  function state(){
    return {ready:Boolean(lake&&!disposed&&!error),active,running:scheduler.getState().running,frames,width,height,
      triangles:renderer.info.render.triangles,calls:renderer.info.render.calls,geometryVersion:water?.geometryVersion??null,rodPhase,
      shoreKind:lake?.kind??null,aimVisible:aimMarker.visible,sleeveColor:tackle.sleeveColor(),fightFishVisible:Boolean(fish),
      bobberVisible:frames>0&&Boolean(lake)&&tackle.float.visible,lineVisible:frames>0&&Boolean(lake)&&tackle.line.visible,error,disposed};
  }
  canvas.addEventListener('webglcontextlost',lost);
  canvas.addEventListener('pointermove',hover);
  for(const name of ['pointerleave','pointerdown','pointercancel'])canvas.addEventListener(name,hideAim);
  renderer.debug.onShaderError=()=>fault('webgl-render-failed');
  const observer=typeof ResizeObserver==='function'?new ResizeObserver(resize):null;
  observer?.observe(canvas);
  const doc=canvas.ownerDocument;
  doc?.addEventListener('visibilitychange',sync);
  doc?.defaultView?.addEventListener('blur',hideAim);
  return {update,resize,state,
    setActive(value){if(disposed||error)return;active=value===true;if(!active)hideAim();resize();},
    pickAim(x,y){if(!water||disposed||error||!width||!height)return null;return pickAim(camera,water,canvas.getBoundingClientRect(),x,y);},
    projectAim(u,v){if(!water||disposed||error||!width||!height)return null;return projectAim(camera,water,canvas.getBoundingClientRect(),u,v);},
    dispose(){
      if(disposed)return;
      disposed=true;active=false;scheduler.stop();observer?.disconnect();canvas.removeEventListener('webglcontextlost',lost);
      canvas.removeEventListener('pointermove',hover);
      for(const name of ['pointerleave','pointerdown','pointercancel'])canvas.removeEventListener(name,hideAim);
      aimMarker.geometry.dispose();aimMarker.material.dispose();
      doc?.removeEventListener('visibilitychange',sync);doc?.defaultView?.removeEventListener('blur',hideAim);
      lake?.dispose();lake=null;tackle.dispose();renderer.dispose();scene.clear();
    }
  };
}

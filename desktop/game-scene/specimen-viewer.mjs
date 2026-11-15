import * as THREE from 'three';
import { createSpecimen, disposeSpecimen } from './specimen-model.mjs';

const clamp=(n,min,max)=>Math.min(max,Math.max(min,n));

// A low oval plinth and two specimen pins keep the fish supported in profile.
// All surfaces share the studio lighting and stay behind the fish details.
function displayBench(size) {
  const group=new THREE.Group(),top=-size.y*.60;
  const steel=new THREE.MeshStandardMaterial({color:0x53565a,roughness:.53,metalness:.40});
  const base=new THREE.MeshStandardMaterial({color:0x343a39,roughness:.72,metalness:.12});
  const bench=new THREE.MeshStandardMaterial({color:0x424644,roughness:.94});
  const plinth=new THREE.Mesh(new THREE.CylinderGeometry(1,1.04,.020,48),base);
  plinth.scale.set(size.x*.38,1,Math.max(size.z*.68,.07));
  plinth.position.y=top-.010;
  group.add(plinth);
  const pinGeometry=new THREE.CylinderGeometry(.003,.005,-top,10);
  for(const x of [-.12,.19]){
    const pin=new THREE.Mesh(pinGeometry,steel);
    pin.position.set(x*size.x,top/2,0);group.add(pin);
  }
  const plane=new THREE.Mesh(new THREE.PlaneGeometry(32,32),bench);
  plane.rotation.x=-Math.PI/2;plane.position.y=top-.021;group.add(plane);
  return {group,dispose(){
    plinth.geometry.dispose();pinGeometry.dispose();plane.geometry.dispose();
    steel.dispose();base.dispose();bench.dispose();
  }};
}

// This preview has no simulation. Resize, input and opening the window
// request at most one frame; a stationary specimen has no animation loop.
export function createSpecimenViewer(canvas,speciesId,{onFault,makeRenderer=options=>new THREE.WebGLRenderer(options)}={}){
  const win=canvas.ownerDocument.defaultView;
  const model=createSpecimen(THREE,speciesId);
  if(!model)throw new Error('unknown-specimen');
  let renderer;
  try{renderer=makeRenderer({canvas,antialias:true,alpha:false,powerPreference:'low-power'});}
  catch(error){disposeSpecimen(model);throw error;}
  const scene=new THREE.Scene();
  const camera=new THREE.PerspectiveCamera(34,1,.002,25);
  scene.background=new THREE.Color(0x414543);
  scene.fog=new THREE.FogExp2(0x414543,.38);
  renderer.outputColorSpace=THREE.SRGBColorSpace;
  renderer.toneMapping=THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure=1.16;
  const fill=new THREE.HemisphereLight(0xe2efe8,0x889287,1.85);
  const key=new THREE.DirectionalLight(0xffead0,2.35);key.position.set(2,3,3);
  const rim=new THREE.DirectionalLight(0xc5e1ff,1.45);rim.position.set(-1.5,1.5,-2);
  scene.add(fill,key,rim,model);
  const box=new THREE.Box3().setFromObject(model),center=box.getCenter(new THREE.Vector3()),size=box.getSize(new THREE.Vector3());
  model.position.sub(center);
  const bench=displayBench(size);scene.add(bench.group);
  let frameId=null, frames=0, active=false, disposed=false, fault=null, observer=null, width=0,height=0;
  let yaw=.13,pitch=.17,zoom=1,drag=null,distance=2;

  function cancelFrame(){if(frameId!==null){win.cancelAnimationFrame(frameId);frameId=null;}}
  function fail(code){
    if(disposed||fault)return;fault=code;active=false;cancelFrame();releaseDrag();onFault?.(code);
  }
  function schedule(){
    if(!active||disposed||fault||frameId!==null||!width||!height)return;
    frameId=win.requestAnimationFrame(()=>{
      frameId=null;if(!active||disposed||fault)return;
      const r=distance/zoom;
      camera.position.set(r*Math.sin(yaw)*Math.cos(pitch),r*Math.sin(pitch),r*Math.cos(yaw)*Math.cos(pitch));
      camera.lookAt(0,0,0);
      try{renderer.render(scene,camera);frames++;}catch{fail('specimen-render-failed');}
    });
  }
  function resize(){
    if(disposed||fault)return;
    const w=Math.round(canvas.clientWidth),h=Math.round(canvas.clientHeight);
    if(w===width&&h===height)return;
    width=w;height=h;if(!w||!h)return;
    camera.aspect=w/h;
    const reach=Math.max(size.x/camera.aspect,size.y,size.z)*.59/Math.tan(THREE.MathUtils.degToRad(camera.fov*.5));
    distance=Math.max(reach+size.z*.65,size.length()*.75);
    camera.far=Math.max(10,distance*6);camera.updateProjectionMatrix();
    renderer.setPixelRatio(Math.min(1.5,win.devicePixelRatio||1));renderer.setSize(w,h,false);schedule();
  }
  function rotate(dx,dy){
    if(!active||disposed||fault||!Number.isFinite(dx)||!Number.isFinite(dy))return;
    yaw=(yaw-dx)%(Math.PI*2);pitch=clamp(pitch+dy,.015,1.38);schedule();
  }
  function magnify(amount){
    if(!active||disposed||fault||!Number.isFinite(amount))return;
    zoom=clamp(zoom*Math.exp(clamp(amount,-1,1)),.6,2.6);schedule();
  }
  function releaseDrag(){
    const held=drag;drag=null;
    if(held && canvas.hasPointerCapture(held.id))canvas.releasePointerCapture(held.id);
  }
  function down(e){
    if(!active||disposed||fault||drag||e.button!==0)return;
    try{canvas.setPointerCapture(e.pointerId);}catch{return;}
    drag={id:e.pointerId,x:e.clientX,y:e.clientY};e.preventDefault();canvas.focus({preventScroll:true});
  }
  function move(e){
    if(!drag||drag.id!==e.pointerId)return;
    if(!(e.buttons&1)){releaseDrag();return;}
    const dx=e.clientX-drag.x,dy=e.clientY-drag.y;drag.x=e.clientX;drag.y=e.clientY;
    rotate(dx*Math.PI*2/Math.max(width,1),dy*Math.PI/Math.max(height,1));
  }
  function end(e){if(drag?.id===e.pointerId)releaseDrag();}
  function wheel(e){
    if(!active||disposed||fault||!Number.isFinite(e.deltaY)||!e.deltaY)return;
    e.preventDefault();const scale=e.deltaMode===1?16:e.deltaMode===2?height:1;
    magnify(-e.deltaY*scale*.0016);
  }
  function keys(e){
    if(!active||e.altKey||e.ctrlKey||e.metaKey)return;
    switch(e.key){
      case 'ArrowLeft':rotate(-.12,0);break;case 'ArrowRight':rotate(.12,0);break;
      case 'ArrowUp':rotate(0,.10);break;case 'ArrowDown':rotate(0,-.10);break;
      case '+':case '=':magnify(.12);break;case '-':magnify(-.12);break;default:return;
    }
    e.preventDefault();
  }
  function contextLost(e){e.preventDefault();fail('specimen-context-lost');}
  const handlers={pointerdown:down,pointermove:move,pointerup:end,pointercancel:end,lostpointercapture:end,keydown:keys,
    webglcontextlost:contextLost,wheel};
  for(const [type,handler] of Object.entries(handlers))canvas.addEventListener(type,handler,type==='wheel'?{passive:false}:undefined);
  if(win.ResizeObserver){observer=new win.ResizeObserver(resize);observer.observe(canvas);}
  win.addEventListener('resize',resize);
  return {
    setActive(value){
      if(disposed||fault)return;
      if(active===Boolean(value)){if(active)resize();return;}
      active=Boolean(value);
      if(active){resize();schedule();}else{releaseDrag();cancelFrame();}
    },
    state(){return {speciesId,active,frames,yaw,pitch,zoom,width,height,triangles:renderer.info.render.triangles,error:fault,disposed};},
    dispose(){
      if(disposed)return;disposed=true;active=false;cancelFrame();releaseDrag();observer?.disconnect();
      for(const [type,handler] of Object.entries(handlers))canvas.removeEventListener(type,handler);
      win.removeEventListener('resize',resize);scene.remove(model);disposeSpecimen(model);bench.dispose();scene.clear();
      renderer.dispose();renderer.forceContextLoss();
    }
  };
}

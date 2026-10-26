import * as THREE from 'three';
import {createLakeEnvironment} from './lake-environment.mjs';
import {createFrameScheduler} from '../../src/frame-scheduler.js';
import {renderSize} from '../../src/display.js';

export function createLakeVisualProbe(document) {
  let canvas,overlay,renderer,environment,scheduler,running=false,resumeWanted=false,frames=0,time=0,disposed=false;
  let proof=null,error=null;
  const state=()=>({kind:'webgl3d',running,frames,targetDrawCallbacksPerSecond:30,sampledAt:performance.now(),width:canvas?.width??0,height:canvas?.height??0,
    proof,error,sceneOnly:true,gameLoaded:false});
  function resize(){
    if(!renderer)return;
    const width=document.documentElement.clientWidth,height=document.documentElement.clientHeight;
    const size=renderSize(width,height,devicePixelRatio,{quality:'medium',scale:1,fps:30});
    renderer.setSize(size.width,size.height,false);environment.camera.aspect=size.aspect;environment.camera.updateProjectionMatrix();
    overlay.width=width;overlay.height=height;const context=overlay.getContext('2d');
    context.fillStyle='#71d9a5';context.fillRect(32,32,120,80);context.fillStyle='#477db7';context.fillRect(200,32,120,80);
    context.fillStyle='#eff5e7';context.font='24px sans-serif';context.fillText('P0 · original placeholder lake · 30 draw callbacks/s target',32,170);
  }
  function inspectFrame(){
    const gl=renderer.getContext(),pixel=new Uint8Array(4),colors=new Set();
    for(const x of [.2,.4,.6,.8])for(const y of [.15,.35,.65,.85]){
      gl.readPixels(Math.floor(canvas.width*x),Math.floor(canvas.height*y),1,1,gl.RGBA,gl.UNSIGNED_BYTE,pixel);colors.add([...pixel].join(','));
    }
    proof={triangles:renderer.info.render.triangles,calls:renderer.info.render.calls,distinctSampleColors:colors.size,glError:gl.getError(),contextLost:gl.isContextLost()};
  }
  function initialize(){
    if(renderer)return;
    canvas=document.createElement('canvas');overlay=document.createElement('canvas');
    for(const element of [canvas,overlay]){element.style.cssText='position:fixed;inset:0;width:100%;height:100%;pointer-events:none';document.body.append(element);}
    renderer=new THREE.WebGLRenderer({canvas,antialias:true,powerPreference:'default'});renderer.setPixelRatio(1);
    canvas.addEventListener('webglcontextlost',event=>{event.preventDefault();error='webgl-context-lost';stop();});
    renderer.outputColorSpace=THREE.SRGBColorSpace;renderer.toneMapping=THREE.ACESFilmicToneMapping;renderer.toneMappingExposure=1.05;
    environment=createLakeEnvironment();resize();
    scheduler=createFrameScheduler({render:(_now,dt)=>{
      if(disposed)return;time+=dt;environment.waterMaterial.uniforms.time.value=time;renderer.render(environment.scene,environment.camera);frames++;
      // One warm-up readback only. It is not part of steady-state performance sampling.
      if(frames===3)inspectFrame();
    },onError:failure=>{error=failure.message;running=false;}});
  }
  function stop(){running=false;scheduler?.stop();return state();}
  const api={state,
    start(){if(disposed)throw Error('Disposed lake probe');if(running)return state();try{initialize();running=true;scheduler.wake(30);}catch(failure){error=failure.message;api.dispose();}return state();},
    stop(){resumeWanted=false;return stop();},
    pause(){resumeWanted ||= running;return stop();},
    resume(){const wanted=resumeWanted;resumeWanted=false;return wanted?api.start():state();},
    dispose(){if(disposed)return;stop();disposed=true;environment?.dispose();renderer?.dispose();canvas?.remove();overlay?.remove();window.removeEventListener('resize',resize);}
  };
  window.addEventListener('resize',resize);window.addEventListener('pagehide',()=>api.dispose(),{once:true});return api;
}

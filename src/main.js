import * as THREE from 'three';
import { QUALITY, normalizeSettings, renderSize, frameRate } from './display.js';
import { mountGameUI } from './game-ui.js';
import { SPECIES } from './content.js';
import { isWater } from './ecosystem.js';
import './style.css';

const canvas = document.querySelector('#lake');
const settingsDialog = document.querySelector('#settings');
const qualityInput = document.querySelector('#quality');
const scaleInput = document.querySelector('#resolution');
const fpsInput = document.querySelector('#frame-limit');
let settings;
try { settings = normalizeSettings(JSON.parse(localStorage.getItem('fishing-desktop.settings.v1') ?? '{}')); }
catch { settings = normalizeSettings(); }
qualityInput.value = settings.quality;
scaleInput.value = String(settings.scale);
fpsInput.value = String(settings.fps);

let renderer;
try { renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'default' }); }
catch { document.querySelector('#fallback').hidden = false; }

if (renderer) initialize();

function initialize() {
  renderer.setPixelRatio(1);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.05;
  const scene = new THREE.Scene();
  scene.fog = new THREE.FogExp2(0xbacbc7, 0.0036);
  const camera = new THREE.PerspectiveCamera(49, 1, 0.1, 550);
  camera.position.set(0, 3.35, 12);
  camera.lookAt(0, 1.2, -42);
  const ambient = new THREE.HemisphereLight(0xd3e4e7, 0x3b4839, 2.15);
  scene.add(ambient);
  const sun = new THREE.DirectionalLight(0xffe2ae, 2.5);
  sun.position.set(-70, 50, -100);
  scene.add(sun);
  const sunDirection = sun.position.clone().normalize();

  const sky = new THREE.Mesh(new THREE.SphereGeometry(450, 32, 20), new THREE.ShaderMaterial({
    side: THREE.BackSide, depthWrite: false,
    uniforms: { sunDirection: { value: sunDirection }, daylight: { value: 1 } },
    vertexShader: 'varying vec3 vDirection; void main(){ vDirection=position; gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0); }',
    fragmentShader: `varying vec3 vDirection; uniform vec3 sunDirection; uniform float daylight;
      void main(){ vec3 d=normalize(vDirection); float h=max(d.y,0.0);
        vec3 color=mix(vec3(.75,.81,.79),vec3(.29,.48,.61),pow(h,.38));
        float glow=pow(max(dot(d,sunDirection),0.0),9.0);
        color+=vec3(.3,.22,.12)*glow;
        color+=vec3(1.5,1.2,.7)*smoothstep(.9994,.99975,dot(d,sunDirection));
        gl_FragColor=vec4(color*(.2+.8*daylight),1.0);
        #include <tonemapping_fragment>
        #include <colorspace_fragment>
      }`,
  }));
  scene.add(sky);

  const waterMaterial = new THREE.ShaderMaterial({
    uniforms: { time: { value: 0 }, detail: { value: 0.75 }, daylight: { value: 1 }, waterTint: { value: new THREE.Color(0x244f43) }, sunDirection: { value: sunDirection }, ripple: { value: new THREE.Vector3(1.4, 0, -6) }, rippleStart: { value: -100 } },
    vertexShader: `uniform float time; varying vec3 vWorld;
      void main(){ vec3 p=position; p.z+=sin(p.x*.42+time*.6)*.025+sin(p.y*.33+time*.43)*.022;
        vWorld=(modelMatrix*vec4(p,1.0)).xyz;
        gl_Position=projectionMatrix*viewMatrix*vec4(vWorld,1.0); }`,
    fragmentShader: `uniform float time; uniform float detail; uniform float daylight; uniform vec3 sunDirection; uniform vec3 waterTint;
      uniform vec3 ripple; uniform float rippleStart; varying vec3 vWorld;
      float waves(vec2 p){ return sin(p.x*1.3+sin(p.y*.7)+time*.9)*.22
        +sin(p.y*2.7-p.x*.8+time*1.1)*.12+sin(p.x*6.2+p.y*3.7-time*1.4)*.04*detail; }
      void main(){ vec2 p=vWorld.xz; float e=.035;
        vec3 N=normalize(vec3((waves(p-vec2(e,0.))-waves(p+vec2(e,0.)))*.9,1.,(waves(p-vec2(0.,e))-waves(p+vec2(0.,e)))*.9));
        float age=time-rippleStart; float r=length(p-ripple.xz);
        float ring=sin((r-age*1.4)*20.)*exp(-pow(r-age*1.4,2.)*5.)*exp(-max(age,0.)*.55);
        if(age>0. && age<12.) N=normalize(N+vec3(ring*.03,0.,ring*.02));
        vec3 V=normalize(cameraPosition-vWorld); vec3 R=reflect(-V,N);
        float fresnel=.04+.96*pow(1.-max(dot(V,N),0.),4.);
        vec3 reflected=mix(vec3(.73,.79,.74),vec3(.3,.49,.59),pow(max(R.y,0.),.45));
        vec3 body=mix(waterTint*.32,waterTint,clamp(V.y,0.,1.));
        vec3 color=mix(body,reflected,fresnel*.85+.16);
        float sparkle=pow(max(dot(reflect(-sunDirection,N),V),0.),180.);
        color+=vec3(1.,.77,.4)*sparkle*.8;
        float mist=1.-exp(-length(cameraPosition-vWorld)*.004);
        color=mix(color,vec3(.67,.76,.71),mist*.63);
        gl_FragColor=vec4(color*(.22+.78*daylight),1.);
        #include <tonemapping_fragment>
        #include <colorspace_fragment>
      }`,
  });
  const water = new THREE.Mesh(new THREE.PlaneGeometry(450, 400, 100, 80), waterMaterial);
  water.rotation.x = -Math.PI / 2;
  water.position.z = -100;
  scene.add(water);

  const terrain = new THREE.PlaneGeometry(340, 120, 110, 48);
  terrain.rotateX(-Math.PI / 2);
  const points = terrain.attributes.position;
  for (let i=0; i<points.count; i++) {
    const x=points.getX(i), z=points.getZ(i);
    const inland=Math.max(0,(-z+50)/100);
    const hills=7+9*Math.sin(x*.028+1.7)+5*Math.cos(x*.059);
    points.setY(i,Math.max(-1, hills*inland+Math.sin(x*.14+z*.07)*1.5));
  }
  terrain.computeVertexNormals();
  const bank = new THREE.Mesh(terrain,new THREE.MeshStandardMaterial({color:0x536854,roughness:1}));
  bank.position.z=-125;
  scene.add(bank);
  const treeMaterial=new THREE.MeshStandardMaterial({color:0x2c4b3d,roughness:1});
  const trees=new THREE.InstancedMesh(new THREE.ConeGeometry(2.4,8,10),treeMaterial,300);
  const treeTransform=new THREE.Object3D();
  for(let i=0;i<150;i++) {
    const r=random(i+12), x=(random(i+2)-.5)*310, z=-93-random(i+180)*65;
    const scale=.65+random(i+60)*1.25;
    const ground=2+Math.max(0,(-z-70)/100)*(7+9*Math.sin(x*.028+1.7)+5*Math.cos(x*.059));
    for(let layer=0;layer<2;layer++) {
      treeTransform.position.set(x,ground+scale*(3+layer*2),z);
      treeTransform.scale.setScalar(scale*(layer? .73:1));
      treeTransform.rotation.y=r*Math.PI;
      treeTransform.updateMatrix(); trees.setMatrixAt(i*2+layer,treeTransform.matrix);
    }
  }
  trees.instanceMatrix.needsUpdate=true;
  scene.add(trees);

  const riverBanks = new THREE.Group();
  for (const x of [-48, 48]) {
    const shore = new THREE.Mesh(new THREE.SphereGeometry(1, 28, 16), new THREE.MeshStandardMaterial({ color: 0x64755b, roughness: 1 }));
    shore.position.set(x, -1.2, -48); shore.scale.set(24, 4.3, 75); riverBanks.add(shore);
  }
  scene.add(riverBanks);
  const reeds = new THREE.InstancedMesh(new THREE.ConeGeometry(.065, 2.7, 5), new THREE.MeshStandardMaterial({ color: 0x7f8450, roughness: 1 }), 220);
  for(let i=0;i<220;i++) {
    treeTransform.position.set((i%2?1:-1)*(10+random(i+11)*13),1,-5-random(i+31)*34);
    treeTransform.scale.set(1,.6+random(i+92)*.7,1); treeTransform.rotation.set(0,0,(random(i+34)-.5)*.2);
    treeTransform.updateMatrix(); reeds.setMatrixAt(i,treeTransform.matrix);
  }
  reeds.instanceMatrix.needsUpdate=true; scene.add(reeds);

  const wood=woodTexture();
  const playerRig = new THREE.Group(); scene.add(playerRig);
  const plankMaterial=new THREE.MeshStandardMaterial({map:wood,color:0x9b8d72,roughness:.94});
  for(let i=0;i<13;i++) {
    const plank=new THREE.Mesh(new THREE.BoxGeometry(5.6,.16,.48),plankMaterial);
    plank.position.set(0,.28,7+i*.5); playerRig.add(plank);
  }
  for(const x of [-2.55,2.55]) {
    const post=new THREE.Mesh(new THREE.CylinderGeometry(.13,.18,2.5,10),plankMaterial);
    post.position.set(x,-.15,7.1); playerRig.add(post);
  }

  const rodPoints=[new THREE.Vector3(2.2,1,11),new THREE.Vector3(2,2.1,7.5),new THREE.Vector3(1.65,3.3,4),new THREE.Vector3(1.2,3.9,1)];
  const rodMaterial=new THREE.MeshStandardMaterial({color:0x25312d,metalness:.35,roughness:.5});
  const rod=new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(rodPoints),28,.025,8,false),rodMaterial);
  playerRig.add(rod);
  const sleeveMaterial=new THREE.MeshStandardMaterial({color:0xb9aa8b,roughness:.9});
  const sleeve=new THREE.Mesh(new THREE.CylinderGeometry(.16,.22,.8,12),sleeveMaterial);
  sleeve.rotation.x=.7;sleeve.position.set(2,1.5,8.5);playerRig.add(sleeve);
  const float=new THREE.Group();
  const floatBody=new THREE.Mesh(new THREE.SphereGeometry(.065,12,10),new THREE.MeshStandardMaterial({color:0xd8c7a1,roughness:.4}));
  floatBody.scale.y=2.8;floatBody.position.y=.06;float.add(floatBody);
  const tip=new THREE.Mesh(new THREE.CylinderGeometry(.018,.025,.28,8),new THREE.MeshStandardMaterial({color:0xd35237}));
  tip.position.y=.32;float.add(tip);float.position.set(1.4,.03,-6);scene.add(float);
  const lineGeometry=new THREE.BufferGeometry();
  lineGeometry.setAttribute('position',new THREE.BufferAttribute(new Float32Array(31*3),3));
  const line=new THREE.Line(lineGeometry,new THREE.LineBasicMaterial({color:0xb3c5b7,transparent:true,opacity:.5}));
  scene.add(line);
  const lineCurve=new THREE.QuadraticBezierCurve3(rodPoints[3].clone(),new THREE.Vector3(),new THREE.Vector3());
  const linePoint=new THREE.Vector3();
  function updateLine() {
    lineCurve.v0.copy(rodPoints[3]); lineCurve.v0.x += playerRig.position.x;
    lineCurve.v1.set((float.position.x+1.2)/2,1.3,(float.position.z+1)/2);lineCurve.v2.copy(float.position);
    for(let i=0;i<=30;i++){lineCurve.getPoint(i/30,linePoint);lineGeometry.attributes.position.setXYZ(i,linePoint.x,linePoint.y,linePoint.z);}
    lineGeometry.attributes.position.needsUpdate=true;lineGeometry.computeBoundingSphere();
  }
  updateLine();

  const fishBodyMaterial=new THREE.MeshStandardMaterial({color:0xb9ac83,roughness:.6,metalness:.15});
  const hookedFish=new THREE.Group();
  const fishBody=new THREE.Mesh(new THREE.SphereGeometry(1,18,12),fishBodyMaterial);fishBody.scale.set(.38,.15,.1);hookedFish.add(fishBody);
  const fishTail=new THREE.Mesh(new THREE.ConeGeometry(.16,.3,3),fishBodyMaterial);fishTail.rotation.z=Math.PI/2;fishTail.position.x=.4;hookedFish.add(fishTail);
  scene.add(hookedFish);
  const waterClues = Array.from({length:4}, () => {
    const clue = new THREE.Mesh(new THREE.RingGeometry(.8,.84,36), new THREE.MeshBasicMaterial({color:0xd2e0cf,transparent:true,opacity:.16,depthWrite:false,side:THREE.DoubleSide}));
    clue.rotation.x = -Math.PI / 2; clue.position.y = .08; scene.add(clue); return clue;
  });
  const landingMarker = new THREE.Mesh(new THREE.RingGeometry(.18,.23,32), new THREE.MeshBasicMaterial({color:0xf1d599,transparent:true,opacity:.85,depthWrite:false,side:THREE.DoubleSide}));
  landingMarker.rotation.x=-Math.PI/2;landingMarker.visible=false;scene.add(landingMarker);
  const castTarget=new THREE.Vector3(1.4,0,-9);
  const gameUI=mountGameUI({
    onCast(point){castTarget.set(point.x,0,point.z);waterMaterial.uniforms.ripple.value.copy(castTarget);waterMaterial.uniforms.rippleStart.value=time+1;},
    onMapChange(place,id){
      waterMaterial.uniforms.waterTint.value.setHex(place.water);bank.material.color.setHex(place.ground);treeMaterial.color.setHex(place.forest);
      bank.position.z=id==='pine'?-125:id==='reed'?-108:-135;
      bank.scale.set(id==='river'?1.2:1,1,id==='reed'?.75:1);
      trees.position.z=id==='reed'?12:0;trees.count=id==='reed'?160:300;
      riverBanks.visible=id==='river';reeds.visible=id==='reed';
      camera.position.y=id==='reed'?2.9:3.35;camera.lookAt(0,1.2,-42);
    },
    onEquipment(equipment,clothes){rodMaterial.color.set(equipment.color);sleeveMaterial.color.set(clothes.color);},
    onAim(point){landingMarker.visible=Boolean(point);if(point)landingMarker.position.set(point.x,.12,point.z);},
    onSpotChange(place){
      playerRig.position.x=place.x; camera.position.x=place.x; camera.lookAt(place.target.x,1.2,-42);
    },
    onEnvironment(environment, schools){
      const angle=(environment.hour-6)/12*Math.PI;
      sun.position.set(Math.cos(angle)*80,Math.max(7,Math.sin(angle)*65),-100);
      sunDirection.copy(sun.position).normalize();sun.intensity=.2+environment.daylight*2.3*(1-environment.cloud*.3);
      ambient.intensity=.45+environment.daylight*1.7;sky.material.uniforms.daylight.value=environment.daylight;
      waterMaterial.uniforms.daylight.value=environment.daylight;
      scene.fog.color.setHex(0xbacbc7).multiplyScalar(.3+.7*environment.daylight);
      for(let i=0;i<waterClues.length;i++){
        waterClues[i].visible=Boolean(schools[i]);
        if(schools[i])waterClues[i].position.set(schools[i].x,.08,schools[i].z);
      }
    },
  });

  const state={observe:false,paused:false,hidden:document.hidden,fps:settings.fps};
  const reducedMotion=matchMedia('(prefers-reduced-motion: reduce)');
  let time=0,last=performance.now(),timer=0,animation=0;
  let lastGameTime=performance.now();
  // Fishing timing must continue at its own rate when an occluded browser delays GPU frames.
  setInterval(()=>{
    const now=performance.now(),elapsed=Math.min(1,Math.max(0,(now-lastGameTime)/1000));lastGameTime=now;
    if(!state.hidden&&!state.paused&&!state.observe)gameUI.tick(elapsed);
  },50);
  function resize() {
    const size=renderSize(innerWidth,innerHeight,devicePixelRatio,settings);
    renderer.setSize(size.width,size.height,false);
    camera.aspect=size.aspect;camera.fov=size.aspect<1?65:49;camera.updateProjectionMatrix();
    waterMaterial.uniforms.detail.value=QUALITY[settings.quality].waveDetail;
    document.querySelector('#render-info').textContent=`画面 ${size.width} × ${size.height} · 界面 ${innerWidth} × ${innerHeight}`;
  }
  function draw(now) {
    const fps=frameRate(state);if(!fps)return;
    const dt=Math.min(.1,Math.max(0,(now-last)/1000));time+=dt;last=now;
    const environmentTime=reducedMotion.matches?4:time;
    waterMaterial.uniforms.time.value=environmentTime;
    const phase=gameUI.game.phase,session=gameUI.game.session;
    float.visible=line.visible=gameUI.game.busy;
    rod.rotation.x=0;
    if(session&&gameUI.game.busy){
      if(phase==='casting'){
        const flight=Math.min(1,session.elapsed);float.position.lerpVectors(lineCurve.v0,castTarget,flight);float.position.y+=Math.sin(flight*Math.PI)*2;
        rod.rotation.x=-.08*Math.sin(flight*Math.PI);
      }else{
        float.position.copy(castTarget);float.position.y=.03+Math.sin(environmentTime*1.9)*.018;
        if(phase==='bite')float.position.y=-.2+Math.sin(environmentTime*12)*.08;
        if(phase==='fighting'){
          float.position.x+=Math.sin(session.elapsed*2)*(1-session.progress)*2;
          float.position.z=THREE.MathUtils.lerp(castTarget.z,2,session.progress);
          rod.rotation.x=-session.tension*.0006;
        }
      }
      updateLine();
    }
    hookedFish.visible=phase==='fighting'&&session.progress>.7;
    if(hookedFish.visible){hookedFish.position.copy(float.position);hookedFish.position.y=.08;hookedFish.rotation.y=Math.sin(session.elapsed*4)*.5;fishBodyMaterial.color.set(SPECIES[session.species].color);}
    for(let i=0;i<waterClues.length;i++){
      const age=(environmentTime*.28+i*.23)%1;waterClues[i].scale.setScalar(1+age*1.8);waterClues[i].material.opacity=(1-age)*.17;
    }
    renderer.render(scene,camera);
    timer=setTimeout(()=>{animation=requestAnimationFrame(draw);},Math.max(0,1000/fps-(performance.now()-now)-4));
  }
  function wake() { clearTimeout(timer);cancelAnimationFrame(animation);last=performance.now();lastGameTime=last;if(frameRate(state))animation=requestAnimationFrame(draw); }
  function changeSettings() {
    settings=normalizeSettings({quality:qualityInput.value,scale:scaleInput.value,fps:fpsInput.value});state.fps=settings.fps;
    try { localStorage.setItem('fishing-desktop.settings.v1',JSON.stringify(settings)); } catch { /* Private hosts may disable storage; keep settings in memory. */ }
    resize();wake();
  }
  for(const input of [qualityInput,scaleInput,fpsInput]) input.addEventListener('change',changeSettings);
  document.querySelector('#settings-open').addEventListener('click',()=>settingsDialog.showModal());
  document.querySelector('#observe').addEventListener('click',()=>{
    if(gameUI.game.busy)return;
    state.observe=true;document.querySelector('#interface').hidden=true;document.querySelector('#restore').hidden=false;wake();
  });
  function restore(){state.observe=false;document.querySelector('#interface').hidden=false;document.querySelector('#restore').hidden=true;wake();}
  document.querySelector('#restore').addEventListener('click',restore);
  document.addEventListener('keydown',event=>{if(event.key==='Escape'&&state.observe)restore();});
  document.addEventListener('visibilitychange',()=>{state.hidden=document.hidden;wake();});
  window.addEventListener('resize',()=>{resize();wake();});
  reducedMotion.addEventListener('change',wake);

  const raycaster=new THREE.Raycaster(),pointer=new THREE.Vector2();
  const lakePlane=new THREE.Plane(new THREE.Vector3(0,1,0),0),hit=new THREE.Vector3();
  let activePointer=null;
  function waterPoint(event){
    const rect=canvas.getBoundingClientRect();
    if(event.clientX<rect.left||event.clientX>rect.right||event.clientY<rect.top||event.clientY>rect.bottom)return null;
    pointer.set((event.clientX-rect.left)/rect.width*2-1,-(event.clientY-rect.top)/rect.height*2+1);
    raycaster.setFromCamera(pointer,camera);
    return raycaster.ray.intersectPlane(lakePlane,hit)&&isWater(gameUI.game.progress.map,hit)?{x:hit.x,z:hit.z}:null;
  }
  function cancelMouse(){
    gameUI.cancelMouse();const pointerId=activePointer;activePointer=null;
    if(pointerId!==null&&canvas.hasPointerCapture(pointerId))canvas.releasePointerCapture(pointerId);
  }
  canvas.addEventListener('contextmenu',event=>{if(!state.observe&&!state.paused)event.preventDefault();});
  canvas.addEventListener('pointerdown',event=>{
    if(state.observe||state.paused||settingsDialog.open||!event.isPrimary)return;
    if(event.button===2){event.preventDefault();gameUI.alternateMouse();return;}
    if(event.button!==0)return;
    if(gameUI.beginMouse(waterPoint(event))){event.preventDefault();activePointer=event.pointerId;canvas.setPointerCapture(event.pointerId);}
  });
  canvas.addEventListener('pointermove',event=>{
    if(event.pointerId===activePointer)gameUI.aimMouse(waterPoint(event));
  });
  canvas.addEventListener('pointerup',event=>{
    if(event.pointerId!==activePointer||event.button!==0)return;
    activePointer=null;gameUI.releaseMouse(waterPoint(event));
    if(canvas.hasPointerCapture(event.pointerId))canvas.releasePointerCapture(event.pointerId);
  });
  canvas.addEventListener('pointercancel',cancelMouse);
  canvas.addEventListener('lostpointercapture',()=>{if(activePointer!==null)cancelMouse();});
  window.addEventListener('blur',cancelMouse);
  document.addEventListener('visibilitychange',()=>{if(document.hidden)cancelMouse();});
  // Official Lively property callbacks; embedding and occlusion behavior still need host testing.
  window.livelyPropertyListener=(name,value)=>{
    if(name==='quality'){qualityInput.value=['low','medium','high'][Number(value)]??'medium';changeSettings();}
    if(name==='frameLimit'){fpsInput.value=Number(value)===60?'60':'30';changeSettings();}
  };
  window.livelyWallpaperPlaybackChanged=data=>{
    try {
      const status=JSON.parse(data);
      if(typeof status?.IsPaused==='boolean'){state.paused=status.IsPaused;if(state.paused)cancelMouse();wake();}
    } catch { /* Ignore malformed host events without changing the current pause state. */ }
  };
  resize();wake();
}

function random(seed){ const v=Math.sin(seed*127.1+311.7)*43758.5453123;return v-Math.floor(v); }
function woodTexture(){
  const canvas=document.createElement('canvas');canvas.width=256;canvas.height=256;
  const context=canvas.getContext('2d');context.fillStyle='#a89c80';context.fillRect(0,0,256,256);
  for(let i=0;i<500;i++) { const y=random(i+1)*256;context.strokeStyle=`rgba(44,38,28,${.05+random(i+40)*.15})`;context.lineWidth=.3+random(i+50);context.beginPath();context.moveTo(0,y);context.bezierCurveTo(80,y+random(i+70)*8,180,y-random(i+80)*5,256,y);context.stroke(); }
  const texture=new THREE.CanvasTexture(canvas);texture.colorSpace=THREE.SRGBColorSpace;texture.wrapS=texture.wrapT=THREE.RepeatWrapping;return texture;
}

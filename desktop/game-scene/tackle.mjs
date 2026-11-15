import * as THREE from 'three';
import { createAnglerHands } from './angler-hands.mjs';
import { disposeGroup, toWorld } from './water-space.mjs';

const fightPhases = new Set(['fighting','landing-eligible','landing-pending','capacity-blocked','failure-pending']);
const endedFights = new Set(['landed','escaped','faulted','ended']);
const bottomPhases = new Set(['sinking','settling','settled','touch','bite','hooked','missed','retrieve-required','retrieved']);
const clamp = THREE.MathUtils.clamp;
function smooth(t) { t = clamp(t,0,1); return t * t * (3 - 2 * t); }

function loadedGrip(powerPermille) {
  const load=smooth(powerPermille/1000);
  return {pitch:.31+load*1.02,yaw:.12-load*.48,roll:-.12-load*.25,
    flex:-.08-load*.29,shiftX:.06*load,shiftY:.11*load};
}

function bottomLineEnd(point, water) {
  const bounds=water?.waterBounds;
  if (!point || !bounds || ![point.xMm,point.yMm,point.zMm].every(n=>Number.isInteger(n)&&Math.abs(n)<=1000000) ||
      point.xMm<bounds.minXmm || point.xMm>bounds.maxXmm || point.zMm<bounds.minZmm || point.zMm>bounds.maxZmm ||
      point.yMm>water.surfaceYmm) return null;
  return toWorld(point);
}

export function rodPose({water,cast,flight,bite,fight,bottom,bottomSelected,lureSelected,lure}, time=0, viewYaw=0) {
  const f = flight?.sessionId ? flight : null;
  const hooked = f && fight?.sessionId === f.sessionId ? fight : null;
  const b = f && bite?.sessionId === f.sessionId ? bite : null;
  const pose = {phase:'idle', pitch:.115, yaw:.16, roll:-.1, flex:.04,
    shiftX:0, shiftY:0, reeling:false, reelLevel:0, end:null, float:false, spoon:false, sinker:false, bottomTipPermille:null};
  if (cast?.phase === 'charging' || cast?.phase === 'prepared') {
    return Object.assign(pose,{phase:'charging'},loadedGrip(cast.powerPermille||0));
  }
  if (!f || (hooked && endedFights.has(hooked.phase))) return pose;
  const bottomHere=bottom?.sessionId===f.sessionId && bottomPhases.has(bottom.phase) &&
    Number.isInteger(bottom.tipPermille) && bottom.tipPermille>=0 && bottom.tipPermille<=1000 &&
    f.phase==='waiting';
  if (bottomHere) {
    pose.bottomTipPermille=bottom.tipPermille;
    if (bottom.phase==='retrieved') return pose;
  }
  const isLure=lureSelected===true, currentLure=isLure&&lure?.sessionId===f.sessionId?lure:null;
  if (hooked && fightPhases.has(hooked.phase) && (!bottomHere || bottom.phase==='hooked') &&
      (!isLure || currentLure?.phase==='hooked')) {
    pose.phase='fighting'; pose.pitch=.42+Math.min(.32,hooked.tensionMilliN/43000);
    const a=water?.anchor;
    const bearing=-Math.atan2(hooked.xMm-(a?.xMm??0),hooked.zMm-(a?.zMm??0));
    pose.yaw=.14+clamp((bearing-viewYaw)*.43,-.33,.33);
    pose.roll=-.18;pose.flex=.1+clamp(hooked.rodDeflectionMm/730,0,1.18);
    pose.shiftY=.055;pose.reeling=hooked.reeling;pose.reelLevel=hooked.reelLevel;
    pose.end = toWorld(hooked);return pose;
  }
  if (bottomHere) {
    pose.phase='bottom-'+bottom.phase;pose.pitch=.105;pose.roll=-.03;pose.flex=0;
    pose.end=bottomLineEnd(bottom.lineEnd,water);
    pose.sinker=Boolean(pose.end);
    return pose;
  }
  if (f.phase==='flying') {
    const t=f.elapsedMilliseconds/1000;
    const power=f.powerPermille??1000, start=loadedGrip(power), effort=clamp(power/1000,0,1);
    const stroke=.13+.085*effort, sweep=smooth(t/stroke), settle=Math.max(0,t-stroke);
    const recoil=Math.exp(-settle*8)*Math.sin(settle*20);
    pose.phase='casting';
    pose.pitch=THREE.MathUtils.lerp(start.pitch,.035,sweep)+recoil*(.055+.11*effort);
    pose.yaw=THREE.MathUtils.lerp(start.yaw,.17,sweep);
    pose.roll=THREE.MathUtils.lerp(start.roll,-.05,sweep);
    pose.flex=THREE.MathUtils.lerp(start.flex,.04,sweep)+Math.sin(sweep*Math.PI)*(.17+.48*effort)+recoil*(.07+.12*effort);
    pose.shiftX=start.shiftX*(1-sweep);
    pose.shiftY=THREE.MathUtils.lerp(start.shiftY,-.02,sweep);
    if([f.xMm,f.yMm,f.zMm].every(Number.isFinite)){
      pose.end=toWorld(f);pose.spoon=isLure;pose.sinker=bottomSelected===true;pose.float=!isLure&&!pose.sinker;
    }
    return pose;
  }
  if(isLure){
    if(f.phase!=='waiting'||!currentLure||!['in-water','following','strikeable','bottom-recovery','retrieve-pending'].includes(currentLure.phase))return pose;
    pose.end=bottomLineEnd(currentLure.position,water);
    if(!pose.end)return pose;
    pose.spoon=true;pose.reeling=currentLure.reeling===true;
    pose.phase=pose.reeling?'lure-retrieving':'lure-sinking';
    pose.pitch=pose.reeling?.19:.11;pose.flex=pose.reeling?.09:.025;
    pose.reelLevel=Number.isInteger(currentLure.speedLevel)?clamp(currentLure.speedLevel,0,4)*2:0;
    return pose;
  }
  if (f.phase==='waiting' && !bottomSelected && b?.phase!=='ended') {
    pose.phase='waiting';pose.pitch=.079;pose.end=toWorld(f);
    if (b) {pose.end.x+=(b.bobberLateralMm||0)/1000;pose.end.y+=(b.bobberOffsetMm||0)/1000;}
    pose.float=true;
  } else {
    pose.pitch+=Math.sin(time*.85)*.004;
  }
  return pose;
}

function finish(color, metalness=0, roughness=.78) {
  return new THREE.MeshStandardMaterial({color,metalness,roughness});
}
function part(parent, geometry, material, x, y, z) {
  const mesh = new THREE.Mesh(geometry,material); mesh.position.set(x,y,z); parent.add(mesh);return mesh;
}
function axial(parent, material, z, length, near, far=near, x=0, y=0) {
  const p=part(parent,new THREE.CylinderGeometry(far,near,length,16),material,x,y,z);
  p.rotation.x=-Math.PI/2;return p;
}
function wire(parent, material, coords, radius, segments=20) {
  const curve=new THREE.CatmullRomCurve3(coords.map(a=>new THREE.Vector3(...a)));
  return part(parent,new THREE.TubeGeometry(curve,segments,radius,5,false),material,0,0,0);
}
function rounded(parent, material, x,y,z,sx,sy,sz) {
  const shape=part(parent,new THREE.SphereGeometry(1,16,10),material,x,y,z);shape.scale.set(sx,sy,sz);return shape;
}

function corkFinish() {
  const width=128,height=256,data=new Uint8Array(width*height*4);
  let seed=19427;
  const random=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed/4294967296;};
  for(let y=0;y<height;y++){
    const grain=3*Math.sin(y*.16)+2*Math.sin(y*.61);
    for(let x=0;x<width;x++){
      const k=(y*width+x)*4,tone=grain+(random()-.5)*16;
      data[k]=181+tone;data[k+1]=153+tone;data[k+2]=113+tone;data[k+3]=255;
    }
  }
  // Longitudinal pits and fine joints follow a turned stack of cork rings.
  for(let i=0;i<460;i++){
    const x=Math.floor(random()*width),y=Math.floor(random()*height);
    const span=1+Math.floor(random()**2*9),shade=.53+random()*.30;
    for(let j=0;j<span&&y+j<height;j++){
      const k=((y+j)*width+x)*4;
      for(let channel=0;channel<3;channel++)data[k+channel]*=shade;
    }
  }
  for(let y=15;y<height;y+=26)for(let x=0;x<width;x++){
    const k=(y*width+x)*4;
    for(let channel=0;channel<3;channel++)data[k+channel]*=.83;
  }
  const map=new THREE.DataTexture(data,width,height,THREE.RGBAFormat);
  map.colorSpace=THREE.SRGBColorSpace;map.magFilter=THREE.LinearFilter;
  map.minFilter=THREE.LinearMipmapLinearFilter;map.generateMipmaps=true;map.needsUpdate=true;
  const material=finish(0xffffff,.01,.9);material.map=map;return material;
}

function createReel(grip, black, steel, gunmetal, amber, thread) {
  // The rod runs along local -Z. The spindle, rotor and bail share that axis.
  wire(grip,gunmetal,[[0,-.014,.006],[0,-.048,.006],[0,-.085,-.02],[0,-.098,-.055]],.009);
  rounded(grip,gunmetal,0,-.103,-.05,.038,.04,.068);
  const rotor=new THREE.Group();rotor.name='bail-rotor';rotor.position.set(0,-.105,-.094);grip.add(rotor);
  const spool=new THREE.Group();spool.position.copy(rotor.position);grip.add(spool);
  spool.name='line-spool';
  axial(rotor,gunmetal,0,.033,.044,.039);
  axial(spool,amber,-.034,.013,.042,.04);
  axial(spool,thread,-.048,.019,.036);
  axial(spool,steel,-.062,.005,.042);
  axial(spool,black,-.068,.008,.024,.022);
  for(let i=0;i<5;i++)axial(spool,steel,-.037-i*.004,.001,.0366);
  for(const side of [-1,1])wire(rotor,gunmetal,[[side*.03,0,.012],[side*.048,0,-.006],[side*.049,.004,-.05]],.0045,8);
  wire(rotor,steel,[[-.049,.004,-.05],[-.043,.031,-.068],[0,.054,-.082],[.042,.035,-.069],[.049,.004,-.05]],.002,24);
  const crank=new THREE.Group();crank.name='reel-crank';crank.position.set(-.031,-.108,-.047);grip.add(crank);
  wire(crank,steel,[[0,0,0],[-.03,0,0],[-.048,-.02,.02],[-.054,-.04,.046]],.0046,10);
  const knob=rounded(crank,black,-.066,-.04,.046,.019,.012,.012);
  knob.name='crank-grip';
  return {rotor,crank,spool,knob};
}

export function createTackle() {
  const group=new THREE.Group(), root=new THREE.Group();group.add(root);
  const carbon=finish(0x303b3b,.42,.32), black=finish(0x302e28,.03,.91), steel=finish(0xb9c1ba,.7,.25);
  const gunmetal=finish(0x727976,.49,.30), amber=finish(0xa1844e,.42,.4), cork=corkFinish();
  const thread=finish(0x9caa9a,.11,.6);
  const rearGrip=axial(root,cork,.115,.28,.027,.025);rearGrip.name='rear-grip';
  const splitGrip=new THREE.Group();splitGrip.name='spinning-split-grip';root.add(splitGrip);splitGrip.visible=false;
  axial(splitGrip,cork,.013,.076,.026,.024);
  axial(splitGrip,carbon,.123,.146,.014,.016);
  axial(splitGrip,cork,.219,.072,.025,.021);
  axial(root,black,.257,.019,.027);
  axial(root,gunmetal,-.05,.105,.022);
  axial(root,cork,-.139,.081,.022,.018);
  axial(root,steel,-.1,.008,.024);axial(root,amber,-.182,.007,.019);
  const reel=createReel(root,black,steel,gunmetal,amber,thread);
  const hands=createAnglerHands(root,reel.crank,reel.knob);
  const sections=[],guides=[],l=.252,segmentCount=9;
  const quiverStart=segmentCount-3;
  const quiverPaint=carbon.clone(),quiverWrap=amber.clone();
  let parent=root;
  for(let i=0;i<segmentCount;i++){
    const section=new THREE.Group();section.position.z=i?-l:-.18;parent.add(section);
    const r=.015*(1-i/segmentCount)+.0016;
    const painted=i>=quiverStart;
    axial(section,painted?quiverPaint:carbon,-l*.5,l,r,r-.0016);
    const guide=Math.max(.004,.022*(1-i/segmentCount));
    if(i>0){
      axial(section,painted?quiverWrap:amber,-.207,.01,r*.96);
      wire(section,gunmetal,[[0,-r,-.18],[0,-r-guide,-.2],[0,-r-guide,-.217]],.0018,7);
      const ring=part(section,new THREE.TorusGeometry(guide,.0017,5,14),steel,0,-r-guide,-.218);
      ring.rotation.x=.09;
      guides.push(ring);
    }
    sections.push(section);parent=section;
  }
  const tip=new THREE.Object3D();tip.position.set(0,-.004,-l);parent.add(tip);
  part(tip,new THREE.TorusGeometry(.003,.0008,5,12),steel,0,0,0);
  const tipWorld=new THREE.Vector3();
  const guidePoints=new THREE.BufferAttribute(new Float32Array((guides.length+3)*3),3).setUsage(THREE.DynamicDrawUsage);
  const guideGeometry=new THREE.BufferGeometry();guideGeometry.setAttribute('position',guidePoints);
  const fittedLine=new THREE.Line(guideGeometry,new THREE.LineBasicMaterial({color:0xc2cbbf,transparent:true,opacity:.48,depthWrite:false}));
  fittedLine.name='threaded-line';fittedLine.frustumCulled=false;group.add(fittedLine);
  const threadPoint=new THREE.Vector3();
  const float=new THREE.Group();group.add(float);
  rounded(float,finish(0x925d24),0,-.022,0,.02,.059,.02);
  part(float,new THREE.CylinderGeometry(.005,.008,.118,10),finish(0xff5521,0,.65),0,.07,0);
  part(float,new THREE.CylinderGeometry(.0055,.0055,.022,10),finish(0xffe6a2),0,.095,0);
  const sinker=new THREE.Group();sinker.name='bottom-sinker';sinker.visible=false;group.add(sinker);
  rounded(sinker,gunmetal,0,0,0,.009,.017,.009);
  const spoon=new THREE.Group();spoon.name='coast-spoon-12g';spoon.visible=false;group.add(spoon);
  const nickel=finish(0xc9d0ca,.58,.26);
  rounded(spoon,nickel,0,0,0,.011,.0035,.032);
  rounded(spoon,finish(0xaa6c3e,.44,.38),.004,.002,.007,.003,.0015,.017);
  wire(spoon,steel,[[0,0,-.034],[0,-.006,-.043],[.006,-.008,-.047],[.009,-.003,-.043]],.0014,7);
  const positions=new THREE.BufferAttribute(new Float32Array(23*3),3).setUsage(THREE.DynamicDrawUsage);
  const geo=new THREE.BufferGeometry();geo.setAttribute('position',positions);
  const line=new THREE.Line(geo,new THREE.LineBasicMaterial({color:0xc4d0be,transparent:true,opacity:.66,depthWrite:false}));
  line.frustumCulled=false;group.add(line);
  group.traverse(o=>o.layers.set(1));
  // Keep both the rod butt and the reel in the lower right of the view.
  let previousPhase=null,shownColor=null,lastYaw=0,anchorXmm=null,bottomTipPermille=null,shownRod=null;
  // Each flexible tip section contributes a share of the observed 0–46 mm
  // displacement, increasing towards the tip. No wave or local bite clock.
  const tipShares=[.12,.33,.55];
  return {group, root, float, spoon, sinker, line, tip,
    animate(views, time, dt, viewYaw=0, verticalFov=60) {
      const pose=rodPose(views,time,viewYaw);
      const isBottom=pose.bottomTipPermille!==null;
      const selectedRod=views.bottomSelected || isBottom ? 'bottom' : views.lureSelected ? 'spinning' : 'float';
      if (selectedRod!==shownRod) {
        const isFeeder=selectedRod==='bottom',isSpinning=selectedRod==='spinning';
        rearGrip.material=isFeeder?black:cork;
        rearGrip.visible=!isSpinning;splitGrip.visible=isSpinning;
        sections[0].scale.z=isSpinning ? .86 : 1;
        quiverPaint.color.setHex(isFeeder?0xe6e59a:carbon.color.getHex());
        quiverWrap.color.setHex(isFeeder?0xe46429:amber.color.getHex());
        shownRod=selectedRod;
      }
      bottomTipPermille=pose.bottomTipPermille;
      const color=typeof views.sleeveColor==='string'&&/^#[0-9a-f]{6}$/i.test(views.sleeveColor)?views.sleeveColor:null;
      if(color!==shownColor){hands.dress(color);shownColor=color;}
      const approach=previousPhase===null?1:1-Math.exp(-dt*(pose.phase==='casting'?31:17));
      const a=views.water.anchor;
      const gripX=.215+pose.shiftX,gripZ=-.74,c=Math.cos(viewYaw),s=Math.sin(viewYaw);
      // The wider shoreline view has a narrower vertical field. Raise the
      // near-field grip enough to keep the palm in frame at the crank's low point.
      const gripLift=.075*smooth((60-verticalFov)/13);
      root.position.set(a.xMm/1000+gripX*c+gripZ*s,a.yMm/1000-.105+gripLift+pose.shiftY,-a.zMm/1000+.5-gripX*s+gripZ*c);
      anchorXmm=a.xMm;
      root.rotation.order='YXZ';
      root.rotation.x=THREE.MathUtils.lerp(root.rotation.x,pose.pitch,approach);
      const target=pose.phase==='fighting'?null:views.water.target;
      const aimYaw=target?(-Math.atan2(target.xMm-a.xMm,target.zMm-a.zMm)-viewYaw)*.56:0;
      root.rotation.y=THREE.MathUtils.lerp(root.rotation.y+viewYaw-lastYaw,viewYaw+pose.yaw+aimYaw,approach);
      lastYaw=viewYaw;
      root.rotation.z=THREE.MathUtils.lerp(root.rotation.z,pose.roll,approach);
      let previousTipAngle=0;
      for(let i=0;i<sections.length;i++){
        const ratio=(i+1)/sections.length;
        const base=-pose.flex*(.025+ratio*ratio*.26);
        if (isBottom && pose.phase!=='fighting') {
          const share=tipShares[i-quiverStart]??0;
          const angle=-Math.asin(share*.046*bottomTipPermille/1000/l);
          sections[i].rotation.x=base+angle-previousTipAngle;
          previousTipAngle=angle;
        } else {
          sections[i].rotation.x=THREE.MathUtils.lerp(sections[i].rotation.x,base,approach);
        }
      }
      if(pose.reeling){
        const spin=dt*(2.5+.88*pose.reelLevel);
        reel.crank.rotation.x+=spin;
        reel.rotor.rotation.z+=spin*3.8;
        reel.spool.position.z=reel.rotor.position.z-.006*Math.sin(reel.crank.rotation.x*.7);
      }
      hands.update(pose,views.flight,dt);
      // Read one up-to-date set of transforms for the moving roller and every guide.
      // The line leaves the wound pack on the same side as the rotating pickup.
      tip.updateWorldMatrix(true,false);
      reel.spool.updateWorldMatrix(false,false);
      reel.rotor.updateWorldMatrix(false,false);
      const rotorAngle=reel.rotor.rotation.z;
      threadPoint.set(.036*Math.cos(rotorAngle),.036*Math.sin(rotorAngle),-.048).applyMatrix4(reel.spool.matrixWorld);
      guidePoints.setXYZ(0,threadPoint.x,threadPoint.y,threadPoint.z);
      threadPoint.set(.049,.004,-.05).applyMatrix4(reel.rotor.matrixWorld);
      guidePoints.setXYZ(1,threadPoint.x,threadPoint.y,threadPoint.z);
      for(let i=0;i<guides.length;i++){
        guides[i].updateWorldMatrix(false,false);
        threadPoint.setFromMatrixPosition(guides[i].matrixWorld);
        guidePoints.setXYZ(i+2,threadPoint.x,threadPoint.y,threadPoint.z);
      }
      tipWorld.setFromMatrixPosition(tip.matrixWorld);
      guidePoints.setXYZ(guides.length+2,tipWorld.x,tipWorld.y,tipWorld.z);
      guidePoints.needsUpdate=true;
      previousPhase=pose.phase;
      line.visible=Boolean(pose.end);float.visible=Boolean(pose.float);spoon.visible=pose.spoon;sinker.visible=pose.sinker;
      if(pose.end){
        if(pose.float)float.position.copy(pose.end);
        if(pose.sinker)sinker.position.copy(pose.end);
        if(pose.spoon){
          spoon.position.copy(pose.end);
          spoon.rotation.y=Math.atan2(a.xMm/1000-pose.end.x,-a.zMm/1000-pose.end.z);
          spoon.rotation.z=pose.reeling?.6*Math.sin(time*(8+pose.reelLevel)):.16*Math.sin(time*3);
        }
        const slack=pose.phase==='fighting'?Math.min(.45,(views.fight?.slackMm||0)/1000*.15):pose.phase==='casting'?.09:.2;
        for(let i=0;i<positions.count;i++){
          const t=i/(positions.count-1);
          positions.setXYZ(i,THREE.MathUtils.lerp(tipWorld.x,pose.end.x,t),THREE.MathUtils.lerp(tipWorld.y,pose.end.y,t)-slack*4*t*(1-t),
            THREE.MathUtils.lerp(tipWorld.z,pose.end.z,t));
        }
        positions.needsUpdate=true;
      }
      return pose.phase;
    },
    sleeveColor(){return shownColor;},
    rodType(){return shownRod;},
    bottomTipPermille(){return bottomTipPermille;},
    anchorXmm(){return anchorXmm;},
    dispose(){disposeGroup(group);}
  };
}

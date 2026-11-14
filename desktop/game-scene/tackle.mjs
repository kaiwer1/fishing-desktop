import * as THREE from 'three';
import { disposeGroup, toWorld } from './water-space.mjs';

const fightPhases = new Set(['fighting','landing-eligible','landing-pending','capacity-blocked','failure-pending']);
const endedFights = new Set(['landed','escaped','faulted','ended']);
const clamp = THREE.MathUtils.clamp;
function smooth(t) { t = clamp(t,0,1); return t * t * (3 - 2 * t); }

export function rodPose({cast,flight,bite,fight}, time=0) {
  const f = flight?.sessionId ? flight : null;
  const hooked = f && fight?.sessionId === f.sessionId ? fight : null;
  const b = f && bite?.sessionId === f.sessionId ? bite : null;
  const pose = {phase:'idle', pitch:.115, yaw:.16, roll:-.1, flex:.04,
    shiftX:0, shiftY:0, reeling:false, reelLevel:0, end:null, float:false};
  if (cast?.phase === 'charging' || cast?.phase === 'prepared') {
    const load = smooth((cast.powerPermille || 0) / 1000);
    pose.phase='charging'; pose.pitch=.31+load*1.02; pose.yaw=.12-load*.48;
    pose.roll=-.12-load*.25;pose.flex=-.08-load*.29;pose.shiftX=.06*load;
    pose.shiftY=.11*load;return pose;
  }
  if (!f || (hooked && endedFights.has(hooked.phase))) return pose;
  if (hooked && fightPhases.has(hooked.phase)) {
    pose.phase='fighting'; pose.pitch=.42+Math.min(.32,hooked.tensionMilliN/43000);
    pose.yaw=.14+clamp(-Math.atan2(hooked.xMm,Math.max(1,hooked.zMm))*.43,-.33,.33);
    pose.roll=-.18;pose.flex=.1+clamp(hooked.rodDeflectionMm/730,0,1.18);
    pose.shiftY=.055;pose.reeling=hooked.reeling;pose.reelLevel=hooked.reelLevel;
    pose.end = toWorld(hooked);return pose;
  }
  if (f.phase==='flying') {
    const t=f.elapsedMilliseconds/1000;
    const sweep=smooth(t/.19), recoil=Math.exp(-Math.max(0,t-.19)*6)*Math.sin(Math.max(0,t-.19)*17);
    pose.phase='casting';pose.pitch=1.28-(1.26*sweep)+recoil*.17;
    pose.yaw=-.32+sweep*.49;pose.roll=-.37+.32*sweep;
    pose.flex=-.28+Math.sin(sweep*Math.PI)*.87+recoil*.22;
    pose.shiftY=.09*(1-sweep)-.03*sweep;pose.end=toWorld(f);pose.float=true;return pose;
  }
  if (f.phase==='waiting' && b?.phase!=='ended') {
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

function createReel(grip, black, steel, gunmetal, amber, thread) {
  // The rod runs along local -Z. The spindle, rotor and bail share that axis.
  wire(grip,gunmetal,[[0,-.014,.006],[0,-.048,.006],[0,-.085,-.02],[0,-.098,-.055]],.009);
  rounded(grip,gunmetal,0,-.103,-.05,.038,.04,.068);
  const rotor=new THREE.Group();rotor.position.set(0,-.105,-.094);grip.add(rotor);
  axial(rotor,gunmetal,0,.033,.044,.039);
  axial(rotor,amber,-.034,.013,.042,.04);
  axial(rotor,thread,-.048,.019,.036);
  axial(rotor,steel,-.062,.005,.042);
  for(let i=0;i<5;i++)axial(rotor,steel,-.037-i*.004,.001,.0366);
  for(const side of [-1,1])wire(rotor,gunmetal,[[side*.03,0,.012],[side*.048,0,-.006],[side*.049,.004,-.05]],.0045,8);
  wire(rotor,steel,[[-.049,.004,-.05],[-.043,.031,-.068],[0,.054,-.082],[.042,.035,-.069],[.049,.004,-.05]],.002,24);
  const crank=new THREE.Group();crank.position.set(-.031,-.108,-.047);grip.add(crank);
  wire(crank,steel,[[0,0,0],[-.03,0,0],[-.048,-.02,.02],[-.054,-.04,.046]],.0046,10);
  rounded(crank,black,-.066,-.04,.046,.019,.012,.012);
  return {rotor,crank};
}

function anglerGrip(root) {
  const fabric=finish(0x526451,0,.94), glove=finish(0x514a3c,0,.86), cuff=finish(0x42433c,0,.94);
  const wrist=new THREE.Vector3(.057,-.03,.104);
  const elbow=new THREE.Vector3(.237,-.163,.281);
  const axis=elbow.clone().sub(wrist), length=axis.length();axis.normalize();
  const sleeve=new THREE.Group();
  sleeve.position.copy(wrist);sleeve.quaternion.setFromUnitVectors(new THREE.Vector3(0,1,0),axis);root.add(sleeve);
  const folds=[[.001,.035],[.012,.036],[.018,.039],[.026,.04],[.029,.047],[.045,.041],
    [.056,.049],[.067,.045],[.102,.053],[.118,.055],[.127,.051],[.155,.056],[length,.069]];
  const cloth=part(sleeve,new THREE.LatheGeometry(folds.map(([y,r])=>new THREE.Vector2(r,y)),18),fabric,0,0,0);
  cloth.scale.z=.86;
  part(sleeve,new THREE.CylinderGeometry(.038,.034,.019,18),cuff,0,.008,0);
  for(const y of [.005,.01,.015])part(sleeve,new THREE.TorusGeometry(.0358,.0013,5,18),cuff,0,y,0).rotation.x=Math.PI/2;
  // A single gloved hand circles the cork handle. The separate thumb crosses
  // the four fingers and remains attached through the wrist's casting arc.
  const palm=rounded(root,glove,.034,-.013,.066,.032,.039,.057);palm.rotation.x=-.26;
  for(let i=0;i<4;i++){
    const z=.025+i*.019, reach=.036-i*.001;
    wire(root,glove,[[.043,-.022,z],[.014,-.037,z-.002],[-.018,-.029,z-.005],[-reach,-.002,z-.006]],.0098-i*.00055,12);
  }
  wire(root,glove,[[.047,.006,.086],[.037,.025,.064],[.018,.033,.045],[-.008,.028,.034]],.012,15);
  return fabric;
}

export function createTackle() {
  const group=new THREE.Group(), root=new THREE.Group();group.add(root);
  const carbon=finish(0x303b3b,.42,.32), black=finish(0x302e28,.03,.91), steel=finish(0xb9c1ba,.7,.25);
  const gunmetal=finish(0x575d5c,.53,.32), amber=finish(0xa1844e,.42,.4), cork=finish(0xb09469,.01,.94);
  const thread=finish(0x9caa9a,.11,.6);
  axial(root,cork,.115,.28,.027,.025);
  axial(root,black,.257,.019,.027);
  for(const z of [.219,.157,.097,.05])axial(root,black,z,.002,.0265);
  axial(root,gunmetal,-.05,.105,.022);
  axial(root,cork,-.139,.081,.022,.018);
  axial(root,steel,-.1,.008,.024);axial(root,amber,-.182,.007,.019);
  const reel=createReel(root,black,steel,gunmetal,amber,thread);
  const sleeve=anglerGrip(root);
  const sections=[], l=.252, segmentCount=9;
  let parent=root;
  for(let i=0;i<segmentCount;i++){
    const section=new THREE.Group();section.position.z=i?-l:-.18;parent.add(section);
    const r=.015*(1-i/segmentCount)+.0016;
    axial(section,carbon,-l*.5,l,r,r-.0016);
    const guide=Math.max(.004,.022*(1-i/segmentCount));
    if(i>0){
      axial(section,amber,-.207,.01,r*.96);
      wire(section,gunmetal,[[0,-r,-.18],[0,-r-guide,-.2],[0,-r-guide,-.217]],.0018,7);
      const ring=part(section,new THREE.TorusGeometry(guide,.0017,5,14),steel,0,-r-guide,-.218);
      ring.rotation.x=.09;
    }
    sections.push(section);parent=section;
  }
  const tip=new THREE.Object3D();tip.position.set(0,-.004,-l);parent.add(tip);
  const tipWorld=new THREE.Vector3();
  const float=new THREE.Group();group.add(float);
  rounded(float,finish(0x925d24),0,-.022,0,.02,.059,.02);
  part(float,new THREE.CylinderGeometry(.005,.008,.118,10),finish(0xff5521,0,.65),0,.07,0);
  part(float,new THREE.CylinderGeometry(.0055,.0055,.022,10),finish(0xffe6a2),0,.095,0);
  const positions=new THREE.BufferAttribute(new Float32Array(23*3),3).setUsage(THREE.DynamicDrawUsage);
  const geo=new THREE.BufferGeometry();geo.setAttribute('position',positions);
  const line=new THREE.Line(geo,new THREE.LineBasicMaterial({color:0xc4d0be,transparent:true,opacity:.66,depthWrite:false}));
  line.frustumCulled=false;group.add(line);
  group.traverse(o=>o.layers.set(1));
  // Keep both the rod butt and the reel in the lower right of the view.
  let previousPhase=null,shownColor=null;
  return {group, root, float, line, tip,
    animate(views, time, dt) {
      const pose=rodPose(views,time);
      const color=typeof views.sleeveColor==='string'&&/^#[0-9a-f]{6}$/i.test(views.sleeveColor)?views.sleeveColor:null;
      if(color!==shownColor){sleeve.color.set(color??0x526451);shownColor=color;}
      const approach=previousPhase===null?1:1-Math.exp(-dt*(pose.phase==='casting'?31:17));
      const a=views.water.anchor;
      root.position.set(a.xMm/1000+.215+pose.shiftX,a.yMm/1000-.105+pose.shiftY,-a.zMm/1000-.24);
      root.rotation.order='YXZ';
      root.rotation.x=THREE.MathUtils.lerp(root.rotation.x,pose.pitch,approach);
      const target=views.water.target;
      const aimYaw=target?-Math.atan2(target.xMm-a.xMm,target.zMm-a.zMm)*.56:0;
      root.rotation.y=THREE.MathUtils.lerp(root.rotation.y,pose.yaw+aimYaw,approach);
      root.rotation.z=THREE.MathUtils.lerp(root.rotation.z,pose.roll,approach);
      for(let i=0;i<sections.length;i++){
        const ratio=(i+1)/sections.length;
        sections[i].rotation.x=THREE.MathUtils.lerp(sections[i].rotation.x,-pose.flex*(.025+ratio*ratio*.26),approach);
      }
      if(pose.reeling){const spin=dt*(2.5+.88*pose.reelLevel);reel.crank.rotation.x+=spin;reel.rotor.rotation.z+=spin*3.8;}
      previousPhase=pose.phase;
      line.visible=Boolean(pose.end);float.visible=Boolean(pose.float);
      if(pose.end){
        if(pose.float)float.position.copy(pose.end);
        tip.getWorldPosition(tipWorld);
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
    dispose(){disposeGroup(group);}
  };
}

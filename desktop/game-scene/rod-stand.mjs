import * as THREE from 'three';
import { disposeGroup, toWorld } from './water-space.mjs';

const slots = [1, 2, 3];
const spacing = .75, restHeight = .57, restZ = -.35, tipLength = 2.13;
const pitch = .15;
const uuid = value => typeof value === 'string' && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(value)
  && value !== '00000000-0000-0000-0000-000000000000';
const point = p => p && [p.xMm,p.yMm,p.zMm].every(v => Number.isInteger(v) && Math.abs(v) <= 1000000);
const v = (x,y,z) => new THREE.Vector3(x,y,z);

export function standAtWater(stand, water) {
  const b=water?.waterBounds;
  return Boolean(water?.enabled && b && uuid(stand?.standId) && stand.waterId===water.waterId && stand.spotId===water.spotId &&
    stand.geometryVersion===water.geometryVersion && point(stand.origin) && stand.origin.zMm<b.minZmm &&
    stand.origin.xMm>=b.minXmm && stand.origin.xMm<=b.maxXmm && Array.isArray(stand.slots) &&
    stand.slots.length===3 && slots.every(n=>stand.slots.filter(s=>s?.slot===n).length===1) &&
    (stand.heldRig===null || uuid(stand.heldRig?.rigId)));
}

// Assemble a rigid mesh once with vertex colours. All three rods share one
// blank and one quiver-tip morph, but each mesh owns its own morph weight.
function parts() {
  const position=[],normal=[],color=[];
  const up=v(0,1,0), shade=new THREE.Color();
  function add(g,m,tint) {
    g.applyMatrix4(m);
    const p=g.attributes.position,n=g.attributes.normal,ix=g.index;
    shade.setHex(tint);
    for(let j=0;j<(ix?.count??p.count);j++){
      const i=ix?ix.getX(j):j;
      position.push(p.getX(i),p.getY(i),p.getZ(i));normal.push(n.getX(i),n.getY(i),n.getZ(i));
      color.push(shade.r,shade.g,shade.b);
    }
    g.dispose();
  }
  function tube(a,b,r1,r2,tint,segments=8) {
    const axis=b.clone().sub(a);
    const g=new THREE.CylinderGeometry(r2,r1,axis.length(),segments);
    const rotation=new THREE.Quaternion().setFromUnitVectors(up,axis.normalize());
    add(g,new THREE.Matrix4().compose(a.clone().lerp(b,.5),rotation,v(1,1,1)),tint);
  }
  function ring(x,y,z,r,tint) {
    add(new THREE.TorusGeometry(r,Math.max(.0012,r*.13),5,12),new THREE.Matrix4().makeTranslation(x,y,z),tint);
  }
  function finish(withTip) {
    const g=new THREE.BufferGeometry();
    g.setAttribute('position',new THREE.Float32BufferAttribute(position,3));
    g.setAttribute('normal',new THREE.Float32BufferAttribute(normal,3));
    g.setAttribute('color',new THREE.Float32BufferAttribute(color,3));
    if(withTip){
      const bend=new Float32Array(position.length);
      for(let i=0;i<bend.length;i+=3){
        const u=THREE.MathUtils.clamp((-position[i+2]-1.38)/(tipLength-1.38),0,1);
        bend[i+1]=-.046*u*u;
      }
      g.morphTargetsRelative=true;
      g.morphAttributes.position=[new THREE.BufferAttribute(bend,3)];
    }
    g.computeBoundingSphere();return g;
  }
  return {tube,ring,finish};
}

function rackGeometry() {
  const p=parts(), grey=0x55615b, rubber=0x323b34;
  for (const [z,h] of [[.41,.42],[restZ,restHeight]]) {
    p.tube(v(-.89,h-.05,z),v(.89,h-.05,z),.02,.02,grey);
    for (const x of [-.72,.72]){
      p.tube(v(x,h-.05,z),v(x*1.12,.018,z+Math.sign(z)*.19),.018,.013,grey);
      p.tube(v(x*1.12,.018,z+Math.sign(z)*.19),v(x*1.12,.006,z+Math.sign(z)*.19),.023,.027,rubber);
    }
    for(const n of slots){
      const x=(n-2)*spacing;
      p.tube(v(x,h-.055,z),v(x,h-.01,z),.012,.012,grey);
      for(const sign of [-1,1])p.tube(v(x,h-.015,z),v(x+sign*.036,h+.032,z),.010,.008,rubber);
    }
  }
  for(const x of [-.45,.45])p.tube(v(x,.37,.41),v(x,.52,restZ),.015,.015,grey);
  return p.finish(false);
}

function rodGeometry() {
  const p=parts(), carbon=0x394341,cork=0xaa895b, rubber=0x33362f,metal=0x8b9c94;
  const axial=(start,end,r1,r2,tint)=>p.tube(v(0,0,-start),v(0,0,-end),r1,r2,tint,10);
  axial(-.83,-.80,.021,.023,rubber);axial(-.80,-.54,.023,.021,cork);
  axial(-.54,-.37,.020,.018,0x595d53);axial(-.37,-.27,.020,.018,cork);
  axial(-.27,1.38,.016,.005,carbon);
  for(const z of [-.54,-.38,-.27,.38,.98])axial(z,z+.012,z<0?.022:.009,z<0?.022:.009,0x9f895f);
  p.tube(v(0,-.013,.48),v(0,-.094,.50),.009,.013,metal);
  p.tube(v(0,-.092,.54),v(0,-.092,.45),.038,.030,carbon,12);
  p.tube(v(0,-.092,.45),v(0,-.092,.395),.030,.032,metal,12);
  p.tube(v(-.029,-.092,.50),v(-.058,-.105,.53),.005,.005,metal);
  p.tube(v(-.058,-.105,.53),v(-.066,-.122,.545),.006,.009,rubber);
  for(let i=0;i<10;i++){
    const start=1.38+(tipLength-1.38)*i/10,end=1.38+(tipLength-1.38)*(i+1)/10;
    axial(start,end,.005-i*.0003,.005-(i+1)*.0003,i%3===1?0xdf692c:0xe3e394);
  }
  for(const [d,r] of [[.16,.018],[.78,.012],[1.32,.009],[1.69,.006],[2.01,.0045]]){
    p.tube(v(0,-.005,-d+.018),v(0,-r*1.7,-d),.002,.0017,metal);
    p.ring(0,-r*1.85,-d,r,metal);
  }
  return p.finish(true);
}

function observedEnd(rig,water) {
  const p=rig?.lineEnd,b=water.waterBounds;
  if(!uuid(rig.sessionId)||!point(p)||p.xMm<b.minXmm||p.xMm>b.maxXmm||p.zMm<b.minZmm||
    p.zMm>b.maxZmm||p.yMm>water.surfaceYmm)return null;
  return p;
}

export function createRodStand() {
  const group=new THREE.Group();group.name='bank-rod-stand';group.visible=false;
  const solid=new THREE.MeshStandardMaterial({vertexColors:true,roughness:.68,metalness:.23});
  const filament=new THREE.LineBasicMaterial({color:0xb4c4b3,transparent:true,opacity:.63,depthWrite:false});
  const rack=new THREE.Mesh(rackGeometry(),solid);rack.name='three-post-frame';group.add(rack);
  const blank=rodGeometry();
  const rods=slots.map(n=>{
    const mesh=new THREE.Mesh(blank,solid);
    mesh.name='bank-rod-'+n;mesh.position.set((n-2)*spacing,restHeight,restZ);mesh.rotation.order='YXZ';
    mesh.visible=false;group.add(mesh);
    const geometry=new THREE.BufferGeometry();
    geometry.setAttribute('position',new THREE.BufferAttribute(new Float32Array(6),3).setUsage(THREE.DynamicDrawUsage));
    const line=new THREE.Line(geometry,filament);line.name='bank-line-'+n;line.frustumCulled=false;line.visible=false;group.add(line);
    return {mesh,line,rigId:null};
  });
  const tip=new THREE.Vector3(),end=new THREE.Vector3();
  let disposed=false;
  function clear(){
    group.visible=false;
    for(const rod of rods){rod.mesh.visible=false;rod.line.visible=false;rod.rigId=null;}
  }
  function update(stand,water){
    if(disposed)return false;
    if(!standAtWater(stand,water)){clear();return false;}
    toWorld(stand.origin,group.position);group.visible=true;
    const seen=new Map();
    for(const row of stand.slots){
      const id=row.rig?.rigId;
      if(uuid(id))seen.set(id,(seen.get(id)??0)+1);
    }
    for(const row of stand.slots){
      const rod=rods[row.slot-1],rig=row.rig,id=rig?.rigId;
      const valid=uuid(id)&&id!==stand.heldRig?.rigId&&seen.get(id)===1&&
        (rig.phase==='idle'?rig.sessionId===null:['waiting','biting'].includes(rig.phase)&&uuid(rig.sessionId));
      rod.mesh.visible=Boolean(valid);rod.rigId=valid?id:null;rod.line.visible=false;
      if(!valid)continue;
      const actual=observedEnd(rig,water);
      const confirmed=Number.isInteger(rig.tipPermille)&&rig.tipPermille>=0&&rig.tipPermille<=1000;
      const bend=rig.sessionId?(confirmed?rig.tipPermille/1000:row.alarm==='bite'?.78:row.alarm==='touch'?.23:0):0;
      rod.mesh.morphTargetInfluences[0]=bend;
      rod.mesh.rotation.x=pitch;rod.mesh.rotation.y=0;
      if(actual){
        toWorld(actual,end).sub(group.position);
        rod.mesh.rotation.y=-Math.atan2(end.x-rod.mesh.position.x,rod.mesh.position.z-end.z);
        rod.mesh.updateMatrix();
        tip.set(0,-.046*bend,-tipLength).applyMatrix4(rod.mesh.matrix);
        const positions=rod.line.geometry.attributes.position;
        positions.setXYZ(0,tip.x,tip.y,tip.z);positions.setXYZ(1,end.x,end.y,end.z);
        positions.needsUpdate=true;rod.line.visible=true;
      }
    }
    return true;
  }
  function state(){
    return {visible:group.visible,slots:rods.map((r,i)=>({slot:i+1,rigId:r.rigId,lineVisible:r.line.visible}))};
  }
  return {group,update,state,dispose(){
    if(disposed)return;
    clear();disposed=true;disposeGroup(group);
  }};
}

import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

// Right hand wraps the cork. Left hand alternates between the crank and the
// butt. Geometry uses metres in the rod frame, whose forward axis is -Z.
const vector=(x,y,z)=>new THREE.Vector3(x,y,z);
const material=(color,roughness)=>new THREE.MeshStandardMaterial({color,roughness});
const up=vector(0,1,0);

function ellipsoid(x,y,z,rx,ry,rz,tilt=0){
  return new THREE.SphereGeometry(1,16,10).scale(rx,ry,rz).rotateX(tilt).translate(x,y,z);
}

function tube(points,radius){
  return new THREE.TubeGeometry(new THREE.CatmullRomCurve3(points.map(p=>vector(...p))),10,radius,8,false);
}

function addPieces(parent,geometries,surface){
  const geometry=mergeGeometries(geometries);
  for(const part of geometries)part.dispose();
  const mesh=new THREE.Mesh(geometry,surface);
  parent.add(mesh);
  return mesh;
}

function mark(parent,name,point){
  const node=new THREE.Object3D();
  node.name=name;
  node.position.copy(point);
  parent.add(node);
  return node;
}

function finger(points,radius,leather,skin){
  // Closed tips and a distinct cuff at the first bent joint.
  const split=points[2], tip=points[4];
  leather.push(tube(points.slice(0,3),radius*1.09));
  skin.push(tube(points.slice(2),radius),ellipsoid(...tip,radius,radius,radius));
  leather.push(ellipsoid(...split,radius*1.1,radius*1.1,radius*.97));
}

function makeGrip(parent,palette){
  const leather=[ellipsoid(.034,-.008,.066,.033,.035,.058,-.28)];
  const skin=[];
  for(let index=0;index<4;index++){
    const z=.021+index*.019, tipX=-.035+index*.0018;
    finger([[.041,-.018,z],[.029,-.031,z-.001],[.007,-.036,z-.002],
      [-.021,-.027,z-.004],[tipX,-.004,z-.006]],.0093-index*.00065,leather,skin);
  }
  finger([[.048,.002,.087],[.042,.019,.071],[.029,.026,.051],
    [.01,.031,.043],[-.008,.028,.032]],.011,leather,skin);
  addPieces(parent,leather,palette.leather);addPieces(parent,skin,palette.skin);
  addPieces(parent,[ellipsoid(.05,.003,.077,.023,.026,.04,-.43)],palette.pad);
  return mark(parent,'gripping-wrist',vector(.058,-.025,.103));
}

function makeCrankGrip(parent,palette){
  const leather=[ellipsoid(-.034,-.01,.026,.033,.024,.044,-.65)];
  const skin=[];
  for(let index=0;index<4;index++){
    const x=-.006-index*.0125;
    const back=.03+index*.003;
    finger([[x,-.006,back],[x,-.017,back-.009],[x,-.019,.005],
      [x,-.013,-.009],[x,-.003,-.011]],.0086-index*.00065,leather,skin);
  }
  finger([[-.042,.001,.037],[-.031,.013,.021],[-.021,.017,.005],
    [-.009,.014,-.002],[.002,.008,-.006]],.0097,leather,skin);
  addPieces(parent,leather,palette.leather);addPieces(parent,skin,palette.skin);
  addPieces(parent,[ellipsoid(-.045,-.012,.032,.023,.017,.032,-.58)],palette.pad);
  return mark(parent,'winding-wrist',vector(-.057,-.022,.061));
}

function sleeve(root,name,fabric,cuff){
  const group=new THREE.Group();group.name=name;root.add(group);
  // Use stitched folds with a gathered opening; the fixed cuff covers the
  // wrist end of the palm in every pose.
  const rings=[[0,.029],[.012,.032],[.022,.031],[.026,.037],[.038,.039],
    [.042,.038],[.048,.044],[.066,.04],[.082,.049],[.099,.045],
    [.13,.049],[.156,.055],[.165,.052],[.19,.054],[.24,.06],[.31,.064]];
  const cloth=new THREE.Mesh(new THREE.LatheGeometry(rings.map(([y,r])=>new THREE.Vector2(r,y)),16).scale(1,1,.81),fabric);
  group.add(cloth);
  const cuffGeometry=new THREE.CylinderGeometry(.033,.029,.024,18).scale(1,1,.84).translate(0,.006,0);
  const trim=[cuffGeometry];
  for(const y of [-.003,.002,.008,.013])trim.push(new THREE.TorusGeometry(.0308,.0011,4,18).rotateX(Math.PI/2).scale(1,1,.85).translate(0,y,0));
  addPieces(group,trim,cuff);
  return group;
}

export function createAnglerHands(root,crank,knob){
  const palette={leather:material(0x333a36,.85),pad:material(0x48504a,.89),skin:material(0xb17e61,.72),cuff:material(0x344139,.93)};
  const fabric=material(0x526451,.91),leftFabric=fabric.clone();
  leftFabric.color.multiplyScalar(.93);
  const gripWrist=makeGrip(root,palette);
  const rightSleeve=sleeve(root,'grip-sleeve',fabric,palette.cuff);
  rightSleeve.position.copy(gripWrist.position);
  const hand=new THREE.Group();hand.name='winding-hand';root.add(hand);
  const wrist=makeCrankGrip(hand,palette);
  const leftSleeve=sleeve(root,'winding-sleeve',leftFabric,palette.cuff);
  const butt=vector(-.015,-.008,.183);
  const crankPoint=new THREE.Vector3(), wristAt=new THREE.Vector3(), axis=new THREE.Vector3();
  function aimSleeve(sleeve,shoulder){
    axis.copy(shoulder);
    root.worldToLocal(axis);
    axis.sub(sleeve.position);
    sleeve.scale.y=axis.length()/.31;
    sleeve.quaternion.setFromUnitVectors(up,axis.normalize());
  }
  let transfer=0;
  return {
    dress(color){
      fabric.color.set(color??0x526451);
      leftFabric.color.copy(fabric.color).multiplyScalar(.93);
    },
    update(pose,flight,dt,netGrip,leftShoulder,rightShoulder){
      // The knob orbits around X. Its freely rotating grip does not spin the
      // wrist upside down. Follow it exactly without a separate elapsed clock.
      crankPoint.copy(knob.position).applyQuaternion(crank.quaternion).add(crank.position);
      const bracing=pose.phase==='charging'||(pose.phase==='casting'&&flight?.elapsedMilliseconds<270);
      const goal=bracing?1:0;
      const delta=Math.min(Math.abs(goal-transfer),Math.max(0,dt)*5);
      transfer+=Math.sign(goal-transfer)*delta;
      const eased=transfer*transfer*(3-2*transfer);
      hand.position.copy(crankPoint).lerp(butt,eased);
      hand.position.y-=.032*Math.sin(Math.PI*eased);
      hand.rotation.y=-1.1*eased;
      if(netGrip){
        hand.position.copy(netGrip);
        root.worldToLocal(hand.position);
        hand.rotation.y=-.55;
      }
      wristAt.copy(wrist.position).applyQuaternion(hand.quaternion).add(hand.position);
      leftSleeve.position.copy(wristAt);
      aimSleeve(leftSleeve,leftShoulder);
      aimSleeve(rightSleeve,rightShoulder);
    }
  };
}

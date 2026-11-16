import * as THREE from 'three';
import { createSpecimen, disposeSpecimen } from './specimen-model.mjs';
import { disposeGroup, toWorld } from './water-space.mjs';

const segments = 28, rows = 6;
function meshPoint(row, spoke) {
  const depth = row / rows;
  const angle = (spoke + (row % 2) * .5) * Math.PI * 2 / segments;
  const radius = .98 - .72 * depth * depth;
  return new THREE.Vector3(.36 * radius * Math.cos(angle), -.23 * depth, .29 * radius * Math.sin(angle));
}

export function createLandingNet() {
  const group = new THREE.Group(), basket = new THREE.Group();
  group.name = 'landing-net';
  group.visible = false;
  const rim = new THREE.Mesh(new THREE.TorusGeometry(.36,.009,8,56),
    new THREE.MeshStandardMaterial({color:0x83968b,metalness:.5,roughness:.47}));
  rim.rotation.x = Math.PI/2;
  rim.scale.y = .29/.36;
  basket.add(rim);
  const threads = [];
  for (let row=0;row<rows;row++) {
    for (let i=0;i<segments;i++) {
      const here=meshPoint(row,i);
      threads.push(here,meshPoint(row+1,i),here,meshPoint(row+1,i+(row%2?1:-1)));
    }
  }
  for(let i=0;i<segments;i++) threads.push(meshPoint(rows,i),new THREE.Vector3(0,-.24,0));
  const bag=new THREE.LineSegments(new THREE.BufferGeometry().setFromPoints(threads),
    new THREE.LineBasicMaterial({color:0x56675f,transparent:true,opacity:.85}));
  basket.add(bag);group.add(basket);
  const shaft=new THREE.Mesh(new THREE.CylinderGeometry(.012,.019,1,10),
    new THREE.MeshStandardMaterial({color:0x4f5954,metalness:.46,roughness:.51}));
  group.add(shaft);
  group.traverse(part=>part.layers.set(1));
  const hand=new THREE.Vector3(),front=new THREE.Vector3(),start=new THREE.Vector3(),end=new THREE.Vector3();
  const socket=new THREE.Vector3(),along=new THREE.Vector3(),up=new THREE.Vector3(0,1,0);
  let session=null,fish=null,lastReceipt=null,elapsed=0,receiptWait=0,lifting=false;

  function clear() {
    group.visible=false;lifting=false;session=null;receiptWait=0;
    if(fish){basket.remove(fish);disposeSpecimen(fish);fish=null;}
  }
  function place(handle) {
    // The basket stays level with the water. The shaft always meets its near rim.
    socket.copy(basket.position);socket.z+=.29;
    shaft.position.addVectors(socket,handle).multiplyScalar(.5);
    along.subVectors(socket,handle);
    shaft.scale.y=along.length();
    shaft.quaternion.setFromUnitVectors(up,along.normalize());
  }
  return {
    group, clear,
    update(views,dt,viewYaw) {
      const water=views.water, fight=views.fight, receipt=views.netCatch;
      if(!water?.anchor || !views.netAllowed){clear();return;}
      toWorld(water.anchor,hand);
      hand.x-=.31*Math.cos(viewYaw);hand.z+=.09+.31*Math.sin(viewYaw);hand.y-=.39;
      const live=fight?.sessionId && fight.sessionId===views.flight?.sessionId &&
        ['landing-eligible','landing-pending'].includes(fight.phase) &&
        [fight.xMm,fight.yMm,fight.zMm].every(Number.isFinite);
      if(session && views.flight?.sessionId && views.flight.sessionId!==session)clear();
      if(live && !lifting && receipt?.session!==session){
        if(session!==fight.sessionId)clear();
        session=fight.sessionId;receiptWait=0;
        toWorld(fight,front);
        basket.position.set(front.x-.18,water.surfaceYmm/1000+.065,front.z+.22);
        group.visible=true;place(hand);return;
      }
      if(!lifting && session && receipt?.id && receipt.id!==lastReceipt && receipt.session===session && receipt.waterId===water.waterId){
        lastReceipt=receipt.id;
        fish=createSpecimen(THREE,receipt.speciesKey);
        if(!fish){clear();return;}
        fish.scale.setScalar(THREE.MathUtils.clamp(.42*Math.cbrt(Number(receipt.massMg)/1e6),.21,.63));
        fish.rotation.set(.33,-.13,.035);fish.position.set(0,-.045,0);
        fish.traverse(part=>part.layers.set(1));basket.add(fish);
        start.copy(basket.position);elapsed=0;lifting=true;
      }
      if(!lifting){
        group.visible=false;
        // A cleared fight and the new creel row can arrive in separate host messages.
        if(session && (!fight || fight.phase==='landed') && (receiptWait+=dt)<1.25)return;
        clear();return;
      }
      elapsed+=dt;
      if(elapsed>1.65){clear();return;}
      const t=THREE.MathUtils.smoothstep(elapsed,0,.82);
      end.copy(hand);end.x-=.05+1.12*Math.sin(viewYaw);end.y-=.23;end.z-=1.12*Math.cos(viewYaw);
      basket.position.lerpVectors(start,end,t);
      basket.position.y+=.10*Math.sin(t*Math.PI);
      place(hand);group.visible=true;
    },
    lifting:()=>lifting,
    dispose(){clear();disposeGroup(group);}
  };
}

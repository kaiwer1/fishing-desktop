import {test} from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import {createSpecimen,disposeSpecimen} from './specimen-model.mjs';

const ids=['carp','crucian','grass-carp','blunt-snout-bream','black-seabream','spotted-seabass','yellowfin-seabream','yellow-catfish','black-rockfish','topmouth-culter','large-yellow-croaker'];
const finite=a=>a.every(Number.isFinite);
for(const id of ids)test(id+' has drawable two-sided anatomy, a detailed skin and a centred metre-scale silhouette',()=>{
  const g=createSpecimen(THREE,id),box=new THREE.Box3().setFromObject(g),size=box.getSize(new THREE.Vector3());
  assert.equal(g.userData.speciesId,id);
  assert.ok(size.x>.95&&size.x<1.05&&size.y>.30&&size.y<.63);
  const maxWidth=id==='yellow-catfish' ? .34 : .30; // Includes the spread of the barbels.
  assert.ok(size.z>.17&&size.z<maxWidth);
  assert.ok(box.getCenter(new THREE.Vector3()).length()<.06);
  let draws=0,triangles=0;
  g.traverse(o=>{
    if(!o.isMesh)return;draws++;const geom=o.geometry;
    assert.ok(geom.isBufferGeometry&&geom.index.count%3===0&&finite(geom.attributes.position.array)&&finite(geom.attributes.normal.array));
    triangles+=geom.index.count/3;
  });
  assert.ok(draws<=(id==='yellow-catfish'?26:22)&&triangles<19000);
  const eyes=g.children.filter(o=>o.name.startsWith('iris-'));
  assert.equal(eyes.length,2);assert.ok(eyes.every(e=>e.position.x>.28)&&eyes[0].position.z*eyes[1].position.z<0);
  const m=g.getObjectByName('skin').material;
  assert.ok(m.isMeshStandardMaterial&&m.map.image.width===512&&m.bumpMap.image.height===256&&m.map.colorSpace===THREE.SRGBColorSpace);
  const fins=g.getObjectByName('fins');assert.ok(fins.material.vertexColors&&fins.geometry.attributes.color.count>1000);
  assert.equal(g.children.filter(o=>o.name==='barbel').length,id==='yellow-catfish'?8:id==='carp'?4:0);
  disposeSpecimen(g);
});
test('unavailable species are rejected and separate catches have independent GPU resources',()=>{
  assert.equal(createSpecimen(THREE,'pike'),null);assert.equal(createSpecimen(THREE,'constructor'),null);
  const a=createSpecimen(THREE,'crucian'),b=createSpecimen(THREE,'crucian');
  const shared=[],disposals=new Map();
  a.traverse(o=>{if(o.isMesh){shared.push(o.geometry,o.material);if(o.material.map)shared.push(o.material.map,o.material.bumpMap);}});
  for(const res of new Set(shared)){disposals.set(res,0);res.addEventListener('dispose',()=>disposals.set(res,disposals.get(res)+1));}
  assert.notEqual(a.getObjectByName('skin').material.map,b.getObjectByName('skin').material.map);
  disposeSpecimen(a);disposeSpecimen(a);assert.ok([...disposals.values()].every(n=>n===1));
  assert.ok(!b.userData.specimenDisposed);disposeSpecimen(b);
});

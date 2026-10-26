import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createLakeEnvironment} from './lake-environment.mjs';
test('placeholder scene has finite geometry and transforms before GPU upload',()=>{
  const environment=createLakeEnvironment();let vertices=0;
  try{environment.scene.traverse(item=>{
    for(const value of [...item.position.toArray(),...item.scale.toArray(),...item.quaternion.toArray()])assert.ok(Number.isFinite(value));
    if(item.geometry)for(const attribute of Object.values(item.geometry.attributes)){
      vertices+=attribute.count;for(const value of attribute.array)assert.ok(Number.isFinite(value));
    }
    if(item.isInstancedMesh)for(const value of item.instanceMatrix.array)assert.ok(Number.isFinite(value));
  });assert.ok(vertices>0);assert.ok(environment.camera.near>0 && environment.camera.far>environment.camera.near);
  }finally{environment.dispose();}
});
test('disposing the scene releases every unique geometry and material once',()=>{
  const environment=createLakeEnvironment(),geometries=new Set(),materials=new Set(),events=new Map();
  environment.scene.traverse(item=>{if(item.geometry)geometries.add(item.geometry);if(item.material)for(const value of [item.material].flat())materials.add(value);});
  for(const resource of [...geometries,...materials]){events.set(resource,0);resource.addEventListener('dispose',()=>events.set(resource,events.get(resource)+1));}
  environment.dispose();for(const count of events.values())assert.equal(count,1);assert.equal(environment.scene.children.length,0);
});

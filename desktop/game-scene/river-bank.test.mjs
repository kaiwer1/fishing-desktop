import assert from 'node:assert/strict';
import test from 'node:test';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import * as THREE from 'three';
import { createLake } from './lake.mjs';
import { riverRidge } from './river-bank.mjs';

test('river water beyond the wavy banks continues the actual bordering soundings',()=>{
  const water={waterId:'river-bend',surfaceYmm:0,anchor:{xMm:0,yMm:1500,zMm:0},
    waterBounds:{minXmm:-46000,maxXmm:46000,minZmm:3000,maxZmm:80000},
    crossBed:[{xMm:-46000,extraDepthMm:-300},{xMm:0,extraDepthMm:0},{xMm:46000,extraDepthMm:300}],
    bed:[{zMm:3000,depthMm:520},{zMm:28000,depthMm:2750},{zMm:80000,depthMm:4650}]};
  const lake=createLake(water);
  try{
    const geometry=lake.group.getObjectByName('surveyed-surface').geometry;
    const {position,sounding}=geometry.attributes,cols=water.crossBed.length;
    assert.equal(position.count,(water.bed.length+2)*cols);
    for(const [outer,inner] of [[0,cols],[position.count-cols,position.count-cols*2]]){
      for(let j=0;j<cols;j++){
        assert.equal(position.getX(outer+j),position.getX(inner+j));
        assert.equal(Math.abs(position.getY(outer+j)-position.getY(inner+j)),4);
        assert.equal(sounding.getX(outer+j),sounding.getX(inner+j));
      }
    }
    assert.equal(lake.group.getObjectByName('surveyed-bed').geometry.attributes.position.count,water.bed.length*cols);
    assert.ok(lake.group.getObjectByName('bank-vegetation').geometry.index);
  }finally{lake.dispose();}
});

test('local skyline is reflected on the world layer and darkens with the host light',async()=>{
  const asset=await readFile(new URL('./assets/shudu-ridge.webp',import.meta.url));
  assert.equal(createHash('sha256').update(asset).digest('hex'),'92ac5c3e2a9de87ff5114d8e7ba77e33b02cddddd1e938a285c45fa5aec34cbd');
  const ridge=riverRidge(0,-3,0),texture=new THREE.Texture();
  try{
    assert.equal(ridge.mesh.visible,false);ridge.setTexture(texture);
    assert.ok(ridge.mesh.visible&&ridge.mesh.layers.isEnabled(0));
    ridge.setConditions({waterLight:1,fogColor:[.72,.77,.82],fogDensity:.003});
    const day=ridge.mesh.material.color.r;
    ridge.setConditions({waterLight:.22,fogColor:[.07,.08,.10],fogDensity:.009});
    assert.ok(ridge.mesh.material.color.r<day*.2);
  }finally{texture.dispose();ridge.mesh.material.dispose();ridge.mesh.geometry.dispose();}
});

import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createLandingNet} from './landing-net.mjs';

const session='cast-7';
const ready={netAllowed:true,water:{waterId:'river',surfaceYmm:0,anchor:{xMm:200,yMm:1300,zMm:0}},
  flight:{sessionId:session},fight:{sessionId:session,phase:'landing-eligible',xMm:-100,yMm:-130,zMm:2200}};
const receipt={id:'owned-7',session,waterId:'river',speciesKey:'carp',massMg:'1680000'};

test('an empty basket meets the live fish, then lifts the newly owned specimen through reordered reports',()=>{
  const net=createLandingNet(), landed={...ready,flight:null,fight:null};
  net.update(ready,.05,0);
  assert.equal(net.group.visible,true);
  assert.equal(net.group.getObjectByName('specimen-carp'),undefined);
  net.update(landed,.05,0);
  assert.equal(net.group.visible,false);
  const confirmation={...landed,netCatch:receipt};
  net.update(confirmation,.06,0);
  const fish=net.group.getObjectByName('specimen-carp'),firstHeight=fish.parent.position.y;
  assert.equal(net.lifting(),true);
  for(let i=0;i<12;i++)net.update(confirmation,.05,0);
  assert.ok(fish.parent.position.y>firstHeight+.25);
  assert.equal(net.group.getObjectByName('specimen-carp'),fish);
  for(let i=0;i<28;i++)net.update(confirmation,.05,0);
  net.update(ready,.05,0);
  net.update(confirmation,.05,0);
  assert.equal(net.lifting(),false);
  assert.equal(fish.userData.specimenDisposed,true);
  net.dispose();
});

test('a full creel, stale catch, new cast or revoked controls cannot display a catch',()=>{
  for(const event of [
    {...ready,fight:{...ready.fight,phase:'capacity-blocked'}},
    {...ready,flight:{sessionId:'new-cast'},fight:null},
    {...ready,netAllowed:false}
  ]){
    const net=createLandingNet();
    net.update(ready,.05,0);net.update(event,.05,0);
    net.update({...ready,fight:null,flight:null,netCatch:receipt},.05,0);
    assert.equal(net.group.visible,false);net.dispose();
  }
  const net=createLandingNet();
  net.update(ready,.05,0);
  net.update({...ready,fight:null,flight:null,netCatch:{...receipt,waterId:'sea'}},.1,0);
  for(let i=0;i<14;i++)net.update({...ready,fight:null,flight:null},.1,0);
  net.update({...ready,fight:null,flight:null,netCatch:receipt},.05,0);
  assert.equal(net.group.visible,false);net.dispose();
});

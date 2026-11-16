import test from 'node:test';
import assert from 'node:assert/strict';
import { confirmedSpecimen,createSpecimenView,specimenMass } from './specimen-view.mjs';

const id='10000000-0000-4000-8000-000000000001',bass='spotted-seabass';
const snap=()=>({HasConfirmedState:true,MenuOpen:true,Creel:[{Id:id,SpeciesKey:bass,MassMg:'1759020'}]});
const wait=()=>new Promise(resolve=>setImmediate(resolve));
class Node extends EventTarget {
  children=[];dataset={};open=false;isConnected=true;
  setAttribute(){} append(...items){this.children.push(...items);}replaceChildren(...items){this.children=items;}
  showModal(){this.open=true;}close(){this.open=false;}focus(){this.focused=true;}remove(){this.isConnected=false;}
}
function fixture(load){
  const win=Object.assign(new Node(),{innerWidth:1000,innerHeight:600});
  const nodes=[],doc=Object.assign(new Node(),{body:new Node(),defaultView:win,hidden:false,hasFocus:()=>true,
    createElement(){const item=new Node();nodes.push(item);return item;}});
  const snapshot=snap(),view=createSpecimenView(doc,{readSnapshot:()=>snapshot,load});
  return {view,win,doc,snapshot,nodes,byId:key=>nodes.find(n=>n.id===key)};
}
test('only the confirmed current creel entry can supply species and exact positive mass',()=>{
  const s=snap();
  assert.deepEqual(confirmedSpecimen(s,id),{id,speciesId:bass,massMg:'1759020'});
  for(const invalid of [{MenuOpen:false},{HasConfirmedState:false},{Faulted:true},{Creel:[]},
    {Creel:[{...s.Creel[0],SpeciesKey:'unknown'}]},{Creel:[{...s.Creel[0],MassMg:'012'}]}])
    assert.equal(confirmedSpecimen({...s,...invalid},id),null);
  assert.deepEqual(['1','10500','720000','1000000','1759020'].map(specimenMass),['0.001 克','10.5 克','720 克','1 千克','1.75902 千克']);
  assert.equal(specimenMass('9223372036854775808'),null);
});
test('real catch opens read-only, suspends on blur and closes with Escape, removal or portrait',async()=>{
  let starts=0,disposals=0,active=false;
  const f=fixture(async()=>({createSpecimenViewer:(canvas,species)=>{
    assert.equal(canvas.id,'specimen-canvas');assert.equal(species,bass);starts++;
    return {setActive:on=>{active=on;},state:()=>({active}),dispose:()=>{disposals++;}};
  }}));
  const b=f.view.buttonFor(id);b.dispatchEvent(new Event('click'));await wait();
  assert.equal(f.byId('specimen-species').textContent,'花鲈');assert.equal(f.byId('specimen-mass').textContent,'1.75902 千克');
  assert.equal(starts,1);assert.equal(active,true);f.win.dispatchEvent(new Event('blur'));assert.equal(active,false);f.win.dispatchEvent(new Event('focus'));assert.equal(active,true);
  f.byId('specimen-dialog').dispatchEvent(Object.assign(new Event('keydown',{cancelable:true}),{key:'Escape'}));
  assert.equal(f.view.state().open,false);assert.equal(disposals,1);assert.equal(b.focused,true);
  f.view.open(id);await wait();f.snapshot.Creel=[];f.view.reconcile();
  assert.equal(f.view.state().open,false);assert.equal(disposals,2);
  f.snapshot.Creel=snap().Creel;f.view.open(id);await wait();f.win.innerWidth=400;f.win.dispatchEvent(new Event('resize'));
  assert.equal(disposals,3);assert.equal(f.view.open(id),false);f.view.dispose();f.view.dispose();
});
test('confirmed river culter and marine croaker creel rows open their own named specimens',async()=>{
  const seen=[];
  const f=fixture(async()=>({createSpecimenViewer:(canvas,species)=>{
    seen.push(species);return {setActive(){},state(){return {};},dispose(){}};
  }}));
  for(const [species,name] of [['topmouth-culter','翘嘴鲌'],['large-yellow-croaker','大黄鱼']]){
    f.snapshot.Creel[0].SpeciesKey=species;
    const button=f.view.buttonFor(id);button.dispatchEvent(new Event('click'));await wait();
    assert.equal(f.byId('specimen-species').textContent,name);
    assert.equal(f.view.state().fish.speciesId,species);
    f.view.close();
  }
  assert.deepEqual(seen,['topmouth-culter','large-yellow-croaker']);f.view.dispose();
});
test('a late viewer import cannot replace a newer modal and WebGL failure leaves the fish identity and return action usable',async()=>{
  let resolve,created=0;
  const f=fixture(()=>new Promise(r=>{resolve=r;}));
  f.view.open(id);await wait();f.view.close();resolve({createSpecimenViewer(){created++;}});await wait();
  assert.equal(created,0);assert.equal(f.view.state().open,false);
  f.view.open(id);await wait();resolve({createSpecimenViewer(){throw new Error('no graphics');}});await wait();
  assert.equal(f.view.state().fish.speciesId,bass);assert.match(f.byId('specimen-help').textContent,/无法显示三维/);
  f.byId('specimen-close').dispatchEvent(new Event('click'));assert.equal(f.view.state().open,false);
});

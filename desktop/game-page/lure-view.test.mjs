import test from 'node:test';
import assert from 'node:assert/strict';
import { LureView, createLurePanel, lurePrompt } from './lure-view.mjs';

const session='10000000-0000-4000-8000-000000000010', encounter='10000000-0000-4000-8000-000000000020';
const state=(phase='in-water')=>({phase,sessionId:session,encounterId:['strikeable','following','hooked'].includes(phase)?encounter:null,
  position:{xMm:400,yMm:-1100,zMm:6500},depthMm:1100,bedClearanceMm:1800,outLineMm:7720,remainingLineMm:42280,
  speedLevel:2,reeling:false,bailOpen:false,layer:'middle'});
const packet=(revision,s)=>({kind:'game-lure-state',documentId:'coast',revision,state:s});
class Node {
  children=[];
  append(...items){this.children.push(...items);}
  setAttribute(){}
}
test('independent lure view publishes only the current session and never renders underwater followers or fish details',()=>{
  const v=new LureView('coast'),panel=createLurePanel({createElement:()=>new Node()});
  const s=state();assert.equal(v.apply(packet(1,s)),true);panel.render(v,session);
  const [title,hint,metrics]=panel.element.children, original=[title.textContent,hint.textContent];
  assert.match(metrics.textContent,/水下 1.10 米.*速度 3\/5 档.*出线 7.72 米.*余线 42.28 米/);
  s.position.yMm=-1;const copy=v.state();copy.position.zMm=0;
  assert.deepEqual(v.state().position,{xMm:400,yMm:-1100,zMm:6500});
  let rev=1;
  for(const phase of ['following','strikeable','hooked','bottom-recovery','retrieve-pending','ended']){
    assert.equal(v.apply(packet(++rev,state(phase))),true);panel.render(v,session);
    assert.deepEqual([title.textContent,hint.textContent],lurePrompt(phase));
    if(phase==='following')assert.deepEqual([title.textContent,hint.textContent],original);
    if(phase==='strikeable')assert.match(hint.textContent,/右键提竿/);
  }
  panel.render(v,encounter);assert.equal(panel.element.hidden,true);
  assert.equal(v.apply(packet(++rev,{phase:'idle',sessionId:null})),true);assert.equal(v.forSession(session),null);
});
test('invalid geometry, fish identity, documents and conflicting signals do not advance the projection',()=>{
  const v=new LureView('coast');v.apply(packet(2,state()));
  for(const bad of [{speedLevel:5},{layer:'sky'},{outLineMm:80000},{remainingLineMm:-1},{depthMm:1.5},{position:{xMm:NaN,yMm:0,zMm:0}},
    {encounterId:encounter},{phase:'strikeable'},{phase:'hooked',encounterId:session},{reeling:true,bailOpen:true},{species:'bass'},{mass:'1200'}]){
    assert.equal(v.apply(packet(3,{...state(),...bad})),false);
  }
  assert.equal(v.apply({...packet(3,state()),documentId:'old'}),false);
  assert.equal(v.apply(packet(2,state('strikeable'))),false);
  assert.equal(v.apply(packet(3,{phase:'idle',sessionId:null,position:{}})),false);
  assert.equal(v.revision,2);assert.equal(v.state().phase,'in-water');
  assert.equal(v.apply({...packet(3,state('in-water')),canRetryRetrieve:true}),false);
  assert.equal(v.apply({...packet(3,state('retrieve-pending')),canRetryRetrieve:true}),true);
  assert.equal(v.canRetryRetrieve,true);
  assert.equal(v.apply(packet(4,{phase:'idle',sessionId:null})),true);
  assert.equal(v.canRetryRetrieve,false);
});

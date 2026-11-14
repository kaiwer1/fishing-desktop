import test from 'node:test';
import assert from 'node:assert/strict';
import { StandView, createStandPanel, bindStandKeys } from './stand-view.mjs';

const uuid = n => '10000000-0000-0000-0000-'+String(n).padStart(12,'0');
const rod = n => ({ rigId:uuid(n), sessionId:uuid(n+30), phase:'waiting', label:'江钓 '+n,tipPermille:0,lineEnd:null });
const place = (slot,rig=null,alarm='none') => ({slot,rig,alarm,reachable:true});
const initial = () => ({ waterId:'lake',spotId:'bank',standId:uuid(1),standRevision:'1',controlEpoch:'3',
  geometryVersion:1,origin:{xMm:0,yMm:0,zMm:1200},handGeneration:'2',
  pending:false, heldRig:null, aimedSlot:1, slots:[place(3,rod(6),'touch'),place(1,rod(4),'bite'),place(2)] });
const packet = (revision,state) => ({kind:'game-stand-state',documentId:'bank-page',revision,state});
function fixture() {
  const c={waterId:'lake',spotId:'bank',geometryVersion:1,lease:'first',allowed:true,menuOpen:false,focused:true}, sent=[];
  const view=new StandView({documentId:'bank-page',readContext:()=>c,post:m=>sent.push(m),newTransportId:()=>uuid(100+sent.length)});
  const state=initial(); view.apply(packet(1,state));
  return {view,c,state,sent,up:code=>view.key({code},false),down:(code,more={})=>view.key({code,...more},true)};
}
class Node extends EventTarget {
  children=[];dataset={};
  append(...children){this.children.push(...children);}
}

test('three numbered physical positions render current independent signals, including an actual empty place',()=>{
  const f=fixture(),panel=createStandPanel({createElement:()=>new Node()});
  f.c.allowed=false;f.c.lease=null;
  panel.render(f.view);const [,hand,list]=panel.element.children;
  assert.equal(hand.textContent,'手中无竿');
  assert.deepEqual(list.children.map(row=>row.children[0].textContent),['1 号竿位 · 江钓 4','2 号竿位 · 空位','3 号竿位 · 江钓 6']);
  assert.deepEqual(list.children.map(row=>row.dataset.alarm),['bite','none','touch']);
  assert.match(list.children[0].children[1].textContent,/竿尖下压/);
  assert.match(list.children[2].children[1].textContent,/竿尖轻点/);
  f.state.slots[0].alarm='none';f.view.state().slots[0].rig.label='old';
  assert.equal(f.view.state().slots[0].rig.label,'江钓 4');assert.equal(f.view.state().slots[2].alarm,'touch');
  f.view.apply(packet(2,f.state));panel.render(f.view);
  assert.deepEqual(list.children.map(row=>row.dataset.alarm),['bite','none','none']);
  assert.equal(f.sent.length,0);
});

test('stand rejects stale documents, water, ownership generations, duplicate rig/session/slot and contradictory alarms',()=>{
  const {view,state}=fixture();
  const invalid=[
    {...state,standRevision:'01'}, {...state,controlEpoch:'2'}, {...state,slots:state.slots.slice(1)},
    {...state,heldRig:rod(4)}, {...state,heldRig:{...rod(9),sessionId:rod(6).sessionId}},
    {...state,slots:[place(1,rod(4)),place(1),place(3,rod(6))]}, {...state,slots:[place(1),place(2,null,'bite'),place(3)]},
    {...state,slots:[place(1,rod(4)),place(2),place(3,{...rod(6),phase:'idle'})]},
    {...state,slots:[place(1,rod(4)),place(2),place(3,{...rod(6),phase:'fighting'})]},
    {...state,slots:[place(1,rod(4)),place(2),place(3,{...rod(6),rigId:rod(4).sessionId})]},
    {...state,slots:[place(1),place(2,rod(4)),place(3,rod(6))]},
  ];
  for(const s of invalid)assert.equal(view.apply(packet(2,s)),false);
  for(const bad of [packet(1,state),{...packet(2,state),documentId:'elsewhere'},packet(2,{...state,spotId:'another-bank'}),
    packet(2,{...state,waterId:'coast'}),{...packet(2,state),kind:'game-bottom-state'}])assert.equal(view.apply(bad),false);
  assert.equal(view.revision,1);
  assert.equal(view.apply(packet(2,{waterId:'lake',spotId:'bank',standId:null})),true);assert.equal(view.state(),null);
});

test('X places on the aimed empty slot; only owner confirmation changes held and frees the next physical key',()=>{
  const {view,state,sent,down,up}=fixture();const held=rod(9);
  Object.assign(state,{standRevision:'2',heldRig:held,aimedSlot:2});view.apply(packet(2,state));
  assert.equal(down('KeyX'),true);assert.equal(down('KeyX'),false);assert.equal(down('KeyX',{repeat:true}),false);
  assert.deepEqual(sent[0],{kind:'game-stand',documentId:'bank-page',lease:'first',transportId:uuid(100),key:'place',slot:2,
    standId:uuid(1),expectedRevision:'2',controlEpoch:'3',expectedHeld:held.rigId,expectedOccupant:null,inputSequence:'1'});
  assert.equal(view.state().heldRig.rigId,held.rigId);assert.equal(view.state().slots[1].rig,null);
  const reply={kind:'game-reply',documentId:'bank-page',transportId:uuid(100),code:'stand-updated'};
  assert.equal(view.reply({...reply,transportId:uuid(200)}),false);assert.equal(view.reply(reply),true);assert.equal(view.pending,true);
  state.slots[2]=place(2,held);state.heldRig=null;state.standRevision='3';view.apply(packet(3,state));assert.equal(view.pending,false);
  assert.equal(down('KeyE'),true);assert.equal(sent[1].expectedOccupant,held.rigId);assert.equal(sent[1].inputSequence,'2');
  up('KeyE');assert.equal(down('KeyE'),false);assert.equal(sent.length,2);
});

test('digits resolve stable slot numbers; handoffs do not replay a partial switch or select based on an alarm',()=>{
  for(const number of [1,2,3]){
    const f=fixture(),s=f.state; s.standRevision='9007199254740993';s.heldRig=rod(7);
    s.slots=[3,1,2].map(i=>place(i,i===number?rod(8):null));f.view.apply(packet(2,s));
    assert.equal(f.down('Digit'+number),true);const msg=f.sent[0];
    assert.equal(msg.slot,number);assert.equal(msg.key,['one','two','three'][number-1]);
    assert.equal(msg.expectedRevision,'9007199254740993');assert.equal(msg.expectedHeld,uuid(7));assert.equal(msg.expectedOccupant,uuid(8));
    const free=s.slots.find(row=>!row.rig);free.rig=s.heldRig;s.heldRig=null;s.pending=true;s.standRevision='9007199254740994';
    f.view.apply(packet(3,s));assert.equal(f.view.state().heldRig,null);
    f.view.reply({kind:'game-reply',documentId:'bank-page',transportId:msg.transportId,code:'handoff-cancelled'});
    assert.equal(f.sent.length,1);f.up('Digit'+number);assert.equal(f.down('Digit'+number),false);
  }
});

test('ineligible hands, out-of-reach slots, editable controls, menu, lost focus and changed waters cannot post or replay keys',()=>{
  for(const change of [{menuOpen:true},{focused:false},{lease:null},{allowed:false},{waterId:'coast'},{spotId:'north'}]){
    const f=fixture();Object.assign(f.c,change);assert.equal(f.down('Digit1'),false);assert.equal(f.sent.length,0);
  }
  const f=fixture();f.state.slots[1].reachable=false;f.view.apply(packet(2,f.state));
  assert.equal(f.down('Digit1'),false);f.up('Digit1');
  assert.equal(f.down('KeyX'),false);f.up('KeyX');
  assert.equal(f.down('Digit3',{target:{closest:()=>({})}}),false);
  assert.equal(f.down('Digit3',{ctrlKey:true}),false);
  f.state.heldRig={...rod(7),phase:'fighting'};f.state.standRevision='2';f.view.apply(packet(3,f.state));
  assert.equal(f.down('Digit3'),false);
  assert.equal(f.sent.length,0);
});

test('bound inputs clear on blur and hidden, ignore held key repeats after focus, and detach cleanly',()=>{
  const f=fixture(),doc=new Node(),win=new Node();
  const release=bindStandKeys(f.view,doc,win);
  const key=(type,repeat=false)=>doc.dispatchEvent(Object.assign(new Event(type,{cancelable:true}),{code:'Digit1',repeat}));
  key('keydown');assert.equal(f.sent.length,1);
  f.c.focused=false;win.dispatchEvent(new Event('blur'));assert.equal(f.view.pending,false);
  f.c.focused=true;key('keydown',true);assert.equal(f.sent.length,1);
  key('keyup');key('keydown');assert.equal(f.sent.length,2);
  doc.hidden=true;doc.dispatchEvent(new Event('visibilitychange'));assert.equal(f.view.pending,false);
  key('keyup');key('keydown');assert.equal(f.sent.length,2);
  release();key('keyup');key('keydown');assert.equal(f.sent.length,2);
});

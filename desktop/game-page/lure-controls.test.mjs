import test from 'node:test';
import assert from 'node:assert/strict';
import { LureInput, LureShop } from './lure-controls.mjs';

const session='10000000-0000-4000-8000-000000000010',encounter='10000000-0000-4000-8000-000000000020';
function rig(){
  const messages=[],c={lease:'first',allowed:true,sessionId:session,lure:{sessionId:session,phase:'in-water',speedLevel:2,encounterId:null},
    sequence:'6',walletMinor:'5600'};
  const props={documentId:'doc',readContext:()=>c,post:m=>messages.push(m),newTransportId:()=>String(messages.length+1)};
  const input=new LureInput(props),shop=new LureShop(props);
  const reply=(subject,code,id=messages.at(-1)?.transportId)=>subject.reply({kind:'game-reply',documentId:'doc',transportId:id,code});
  const offered=(r,changes={})=>shop.apply({kind:'game-lure-shop-state',documentId:'doc',revision:r,
    state:{owned:false,available:true,selected:false,allowed:true,priceMinor:5600,...changes}});
  return {input,shop,c,messages,reply,offered};
}
test('a physical left release pairs with its actual retrieve and stops on loss of authority or fight handoff',()=>{
  const {input,c,messages}=rig();
  assert.equal(input.begin('pointer:3'),true);assert.equal(input.begin('pointer:4'),false);
  assert.equal(input.stop('pointer:3','old-press'),false);assert.equal(input.stop('pointer:3','1'),true);
  assert.deepEqual(messages.map(m=>m.action),['reel-start','reel-stop']);
  assert.deepEqual(Object.keys(messages[0]).sort(),['action','documentId','inputSequence','kind','lease','sessionId','transportId']);
  input.begin();c.lease='next';input.reconcile();
  assert.deepEqual([messages.at(-1).lease,messages.at(-1).sessionId,messages.at(-1).inputSequence],['first',session,'4']);
  c.lure.phase='hooked';assert.equal(input.begin(),false);assert.equal(input.stop(),false);
  c.lure.phase='strikeable';c.sessionId=encounter;assert.equal(input.begin(),false);
  c.sessionId=session;input.begin();c.lure.phase='hooked';input.reconcile();
  assert.equal(messages.at(-1).action,'reel-start');assert.equal(input.state().held,null);
});
test('a held right press is never buffered into a later attack; a fresh strike binds its current encounter',()=>{
  const {input,c,messages,reply}=rig();
  assert.equal(input.press(2),false);
  Object.assign(c.lure,{phase:'strikeable',encounterId:encounter});
  assert.equal(input.press(2),false);input.buttons(0);assert.equal(input.press(2),true);
  assert.deepEqual([messages[0].action,messages[0].encounterId,messages[0].sessionId],['strike',encounter,session]);
  assert.equal(input.strike(),false);reply(input,'lure-hooked');input.buttons(0);assert.equal(input.press(2),false);
  Object.assign(c.lure,{phase:'hooked'});input.reconcile();assert.equal(input.state().strikePending,false);
  c.allowed=false;assert.equal(input.strike(),false);assert.equal(messages.length,1);
});
test('R plus wheel changes one detent and waits for the published level, without leaking modifier state across sessions',()=>{
  const {input,c,messages,reply}=rig();assert.equal(input.wheel(-100),false);
  input.speedKey(true);assert.equal(input.wheel(-1),true);assert.equal(messages[0].value,3);
  reply(input,'lure-speed');assert.equal(input.wheel(-1),false);
  c.lure.speedLevel=3;assert.equal(input.wheel(-1),true);assert.equal(messages[1].value,4);
  c.lure.speedLevel=4;reply(input,'lure-speed');assert.equal(input.wheel(-1),false);
  input.speedKey(false);assert.equal(input.wheel(1),false);
  input.speedKey(true);c.allowed=false;input.reconcile();c.allowed=true;
  assert.equal(input.wheel(1),false);assert.equal(input.speedKey(true,true),false);
});
test('the 56-silver purchase, water selection and later exit wait for host confirmation and expire with the menu',()=>{
  const {shop,c,messages,reply,offered}=rig();offered(1);c.walletMinor='5599';assert.equal(shop.offer(),false);
  c.walletMinor='5600';assert.equal(shop.submit('buy'),false);assert.equal(shop.offer(),true);
  c.allowed=false;shop.reconcile();c.allowed=true;assert.equal(shop.submit('buy'),false);
  shop.offer();shop.submit('buy');assert.deepEqual(Object.keys(messages[0]).sort(),['action','documentId','expectedSequence','kind','lease','transportId']);
  reply(shop,'lure-kit-purchased');assert.equal(shop.pending,'buy');
  c.sequence='7';c.walletMinor='0';offered(2,{owned:true,allowed:false});
  assert.equal(shop.pending,null);assert.equal(shop.can('select'),false);
  offered(3,{owned:true,allowed:true});shop.submit('select');offered(4,{owned:true,selected:true});
  assert.equal(shop.pending,'select');reply(shop,'lure-method-selected');assert.equal(shop.pending,null);
  shop.submit('leave');assert.equal(messages.at(-1).action,'leave');reply(shop,'lure-unavailable');
  assert.equal(shop.state().selected,true);assert.equal(messages.length,3);
});

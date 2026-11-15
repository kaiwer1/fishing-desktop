import test from 'node:test';
import assert from 'node:assert/strict';
import { WaterConditions, showWaterConditions } from './water-conditions.mjs';

const lake='development-still-water',sea='coastal-bay';
const morning={waterId:lake,clockMinute:451,weatherCode:'cloudy',cloudPercent:40,windDeciMs:32,tempDeciC:177,lightPermille:410,rainPermille:0};
const packet=(revision,change={})=>({kind:'game-conditions-state',documentId:'here',revision,state:{...morning,...change}});
test('published site weather freezes in pause and clears immediately on another site or unsupported page',()=>{
  const v=new WaterConditions('here');
  v.apply(packet(1));const original=v.forWater(lake);
  v.apply(packet(2));assert.equal(v.forWater(lake),original);
  v.apply(packet(3,{clockMinute:453}));assert.equal(v.forWater(lake,true),original);
  assert.equal(v.forWater(lake).clockMinute,453);
  assert.equal(v.forWater(sea),null);v.apply(packet(4,{waterId:sea,clockMinute:1}));
  assert.equal(v.forWater(sea).clockMinute,1);assert.equal(v.forWater(null),null);
  assert.equal(v.forWater(lake),null);
});
test('only a newer complete, bounded host snapshot updates the actual HUD values',()=>{
  const v=new WaterConditions('here'),el={hidden:false,dataset:{},textContent:''};
  showWaterConditions(el,v.forWater(lake));assert.equal(el.hidden,true);
  assert.equal(v.apply(packet(1,{weatherCode:'mist',clockMinute:0,tempDeciC:-12})),true);
  showWaterConditions(el,v.forWater(lake));
  assert.match(el.textContent,/00:00.*薄雾.*-1\.2 °C.*3\.2 m\/s/);
  assert.deepEqual(el.dataset,{clockMinute:'0',weatherCode:'mist',waterId:lake});
  for(const diff of [{waterId:'test-lake'},{clockMinute:1440},{weatherCode:'toString'},{lightPermille:-1},{rainPermille:1001},{windDeciMs:-1},{tempDeciC:1.2},{cloudPercent:undefined}]){
    assert.equal(v.apply(packet(2,diff)),false);
  }
  assert.equal(v.apply({...packet(2),documentId:'other'}),false);assert.equal(v.apply(packet(1)),false);
  showWaterConditions(el,null);assert.equal(el.textContent,'');assert.deepEqual(el.dataset,{});
});

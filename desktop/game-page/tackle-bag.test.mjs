import test from 'node:test';
import assert from 'node:assert/strict';
import {equipmentBag,bagDetails,matchBag} from './tackle-bag.mjs';

const id=n=>'ac146410-7282-4d14-9518-'+String(n).padStart(12,'0');
const r=(n,kind,key,place='rig-slot',group='float',owner=null,facts=[])=>({id:id(n),kind,key,place,group,owner,facts});
const f=(key,value)=>({key,value:String(value)});
const gear=[r(1,'rod','starter-rod','rig-slot','float',null,[f('minLoadMg',1000),f('maxLoadMg',25000),f('massMg',150000)]),
  r(2,'reel','starter-reel'),r(3,'line','nylon-018','reel-spool','float',id(2),[f('remainingMm',32000),f('diameterUm',180)]),
  r(4,'sinker','sinker-2_5g'),r(5,'leader','bottom-016-leader','rig-slot','bottom-2',null,[f('lengthMm',400)]),
  r(6,'spoon','coast-spoon-12g','rig-slot','lure',null,[f('massMg',12000)]),r(7,'hook','spoon-hook','integral','lure',id(6)),
  r(8,'net','landing-net','equipped','tools'),r(9,'stand','three-rod-stand','owned','tools',null,[f('slots',3)])];
test('bag shows confirmed attributes and the owner of spool lines and integral hooks',()=>{
  const rows=equipmentBag({bag:gear});
  assert.equal(rows.length,9);const details=rows.map(row=>bagDetails(row,rows));
  assert.deepEqual(details[0].facts,[['重量','150 克'],['适配抛重','1–25 克']]);
  assert.match(details[2].location,/基础纺车轮.*线杯/);assert.deepEqual(details[2].facts,[['线径','0.18 毫米'],['剩余线长','32 米']]);
  assert.match(details[6].location,/勺形亮片.*钩位/);
  assert.match(details[4].location,/底钓套装 2/);assert.deepEqual(details[4].facts,[['长度','0.4 米']]);
  assert.equal(details[5].art,'spoon');assert.deepEqual(details[8].facts,[['竿位','3 个']]);
  assert.ok(matchBag(details[1],'reel','鱼轮'));assert.ok(matchBag(details[3],'sinker','铅坠'));
  assert.ok(matchBag(details[4],'line','子线  底钓'));assert.ok(matchBag(details[5],'spoon','１２ 克'));
  assert.equal(matchBag(details[5],'line','亮片'),false);
});
test('old projection retains surviving instances only, with exact legacy line lengths',()=>{
  const old={items:[{id:id(1),definitionKey:'old-reel',kind:'reel',location:'inventory',reelId:null},
    {id:id(2),definitionKey:'old-line',kind:'line',location:'reel-spool',reelId:id(1),remainingLineMm:0+14000,initialLineMm:50000},
    {id:id(3),kind:'hook',location:'lost'}]};
  const rows=equipmentBag(old);
  assert.equal(rows.length,2);assert.match(bagDetails(rows[1],rows).location,/old-reel/);
  const long=equipmentBag({bag:[gear[1],r(10,'line','fixed-line','reel-spool','float',id(2),[f('remainingMm','9223372036854775807')])]});
  assert.deepEqual(bagDetails(long[1],long).facts,[['剩余线长','9,223,372,036,854,775.807 米']]);
});
test('duplicate, cross-rig and detached owner IDs and malformed dimensions are rejected',()=>{
  for(const bag of [[gear[0],gear[0]],[gear[2]],[gear[1],{...gear[2],group:'lure'}],[{...gear[0],owner:id(4)}],
    [{...gear[0],facts:[f('massMg',1),f('massMg',2)]}],[{...gear[0],facts:[f('massMg','1.1')]}],[{...gear[0],kind:'constructor'}]]){
    assert.equal(equipmentBag({bag}),null);
  }
  assert.deepEqual(equipmentBag({bag:[]}),[]);
});

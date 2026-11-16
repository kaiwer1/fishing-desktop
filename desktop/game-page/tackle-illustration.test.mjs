import test from 'node:test';
import assert from 'node:assert/strict';
import {tackleFigure} from './tackle-illustration.mjs';

const kinds=['float-rod','bottom-rod','lure-rod','reel','line','hook','float','sinker','leader','spoon','net','stand','worm','bread-bait'];
function fixture(){
  return {createElementNS(ns,tag){
    assert.equal(ns,'http://www.w3.org/2000/svg');
    return {tag,attrs:{},children:[],setAttribute(key,val){this.attrs[key]=val;},append(child){this.children.push(child);}};
  }};
}
const all=root=>[root,...root.children.flatMap(all)];

test('all equipment plates have standalone artwork, a distinct geometry and locally scoped gradients and clips',()=>{
  const doc=fixture(),shapes=new Set(),identities=new Set();
  for(const kind of kinds){
    const card=tackleFigure(doc,kind),nodes=all(card);
    assert.equal(card.attrs['data-tackle'],kind);
    assert.equal(card.attrs.viewBox,'0 0 160 100');
    assert.equal(card.attrs['aria-hidden'],'true');
    assert.equal(card.attrs.focusable,'false');
    const ids=new Set(nodes.map(n=>n.attrs.id).filter(Boolean));
    for(const id of ids){assert.ok(!identities.has(id));identities.add(id);}
    for(const n of nodes){
      assert.ok(!['text','image','script','foreignObject','use'].includes(n.tag));
      for(const v of Object.values(n.attrs)){
        assert.doesNotMatch(v,/NaN|Infinity|undefined|https?:/);
        const fragment=v.match(/^url\(#(.+)\)$/);
        if(fragment)assert.ok(ids.has(fragment[1]),'missing paint or clipping shape in '+kind);
      }
    }
    const shape=nodes.filter(n=>n.tag==='path'||n.tag==='ellipse').map(n=>n.attrs.d??[n.attrs.cx,n.attrs.cy,n.attrs.rx,n.attrs.ry].join(',')).join(' ');
    assert.ok(!shapes.has(shape),'duplicate artwork: '+kind);shapes.add(shape);
  }
  const original=tackleFigure(doc,'net'),duplicate=tackleFigure(doc,'net');
  const clipId=svg=>all(svg).find(n=>n.tag==='clipPath').attrs.id;
  assert.notEqual(clipId(original),clipId(duplicate));
});

test('missing SVG support and unknown artwork allow the existing item label to stand alone',()=>{
  assert.equal(tackleFigure({},'reel'),null);
  for(const key of [null,'missing','__proto__','<svg>'])assert.equal(tackleFigure(fixture(),key),null);
});

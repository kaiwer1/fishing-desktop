import test from 'node:test';
import assert from 'node:assert/strict';
import { fishName, fishFigure, installFishIllustrations, newCreelCatch } from './fish-illustration.mjs';
import { SpotMenu } from './spot-menu.mjs';

function fixture() {
  class Node {
    constructor(tag, doc) { this.tag = tag; this.ownerDocument = doc; }
    children = []; attributes = {}; dataset = {};
    setAttribute(k,v) { this.attributes[k] = v; }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.children = children; }
    addEventListener() {}
    all(tag) { return [...(this.tag === tag ? [this] : []), ...this.children.flatMap(c => c.all(tag))]; }
  }
  const doc = { createElementNS: (ns,tag) => { assert.equal(ns, 'http://www.w3.org/2000/svg'); return new Node(tag,doc); },
    createElement: tag => new Node(tag,doc) };
  return {doc, bank:doc.createElementNS('http://www.w3.org/2000/svg','svg')};
}
const species = ['carp','crucian','grass-carp','blunt-snout-bream','black-seabream','yellowfin-seabream','spotted-seabass'];
test('each species has an independent body and dorsal shape, local pigments, distinct fins and a reusable accessible plate', () => {
  const {doc,bank} = fixture(); installFishIllustrations(doc,bank); installFishIllustrations(doc,bank);
  const symbols = bank.all('symbol'), bodies=bank.all('clipPath').map(c=>c.children[0].attributes.d);
  assert.equal(symbols.length,7); assert.equal(new Set(bodies).size,7);
  assert.equal(new Set(symbols.map(s=>s.children[1].attributes.d)).size,7);
  assert.equal(new Set(symbols.map(s=>s.attributes.id)).size,7);
  const seabass = symbols.find(s=>s.attributes.id==='fd-fish-spotted-seabass'); assert.ok(seabass.all('ellipse').length >= 20);
  const byId = id => symbols.find(s=>s.attributes.id==='fd-fish-'+id);
  assert.notEqual(byId('black-seabream').children[2].attributes.fill, byId('yellowfin-seabream').children[2].attributes.fill);
  for (const key of species) {
    const art=fishFigure(doc,key), localRef=art.children[0].attributes.href;
    assert.ok(symbols.some(s=>'#'+s.attributes.id===localRef)); assert.equal(art.attributes['aria-hidden'],'true');
    assert.equal(fishFigure(doc,fishName(key)).children[0].attributes.href,localRef);
  }
  assert.equal(fishFigure(doc,'constructor'),null); assert.equal(fishFigure(doc,'<svg>鲫鱼'),null);
});
test('a coastal fishing spot shows the host-listed fish even with an empty creel and in read-only mode', () => {
  const {doc}=fixture(), container=doc.createElement('section');
  const menu = new SpotMenu({documentId:'page',container,canSelect:()=>false,send:()=>assert.fail(),fishArt:fishFigure});
  menu.apply({kind:'game-spot-state',documentId:'page',revision:1,state:{activeId:'coastal-bay',spots:[{
    id:'coastal-bay',name:'海湾',region:'中国近海',description:'海岸礁坡',species:['黑鲷','花鲈','黄鳍鲷'],
  }]}});
  assert.deepEqual(container.all('figcaption').map(n=>n.textContent),['黑鲷','花鲈','黄鳍鲷']);
  assert.deepEqual(container.all('use').map(n=>n.attributes.href),['#fd-fish-black-seabream','#fd-fish-spotted-seabass','#fd-fish-yellowfin-seabream']);
});
test('a catch card identifies only the appended confirmed creel row from the just-finished cast', () => {
  const caught={Id:'fish-42',SpeciesKey:'spotted-seabass',MassMg:'8744000',SaleValueMinor:'96'};
  const before={HasConfirmedState:true,SessionId:'cast-12',CreelOffset:0,CreelCount:0,Sequence:'9',Creel:[]};
  const after={...before,SessionId:null,CreelCount:1,Sequence:'10',Creel:[caught]};
  assert.deepEqual(newCreelCatch(before,after),{id:'fish-42',speciesKey:'spotted-seabass',massMg:'8744000'});
  for(const current of [{...after,PendingResult:true},{...after,Faulted:true},{...after,CreelOffset:1,Creel:[]},
    {...after,Creel:[{...caught,MassMg:'1.5'}]},{...after,CreelCount:0},{...after,SessionId:'cast-13'},
    {...after,Sequence:'9'},{...after,Creel:[{...caught,SpeciesKey:'unlisted'}]}]) assert.equal(newCreelCatch(before,current),null);
  assert.equal(newCreelCatch(null,after),null); assert.equal(newCreelCatch(after,after),null);
  assert.equal(newCreelCatch({...before,Creel:[caught]},after),null);
});

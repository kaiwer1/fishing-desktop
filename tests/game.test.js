import { test } from 'node:test';
import assert from 'node:assert/strict';
import { FishingGame, freshProgress, restoreProgress } from '../src/game.js';
import { MAPS, SPECIES, FISH_CAPACITY } from '../src/content.js';
import { SPOTS } from '../src/ecosystem.js';

function bite(game, point) {
  assert.equal(game.cast(point), true);
  for (let i=0; i<600 && game.phase !== 'bite'; i++) game.tick(.05);
  assert.equal(game.phase, 'bite');
}
function catchFish(game) {
  bite(game); assert.equal(game.strike(), true);
  let time = 0;
  for (let i=0; i<2000 && game.phase === 'fighting'; i++) {
    if (game.session.tension > 68) game.setReeling(false);
    if (game.session.tension < 35) game.setReeling(true);
    game.tick(.05); time += .05;
  }
  assert.equal(game.phase, 'caught');
  return time;
}

test('a real cast consumes one bait, lands one fish, and selling pays exactly once', () => {
  const game = new FishingGame(null, () => .05);
  catchFish(game);
  assert.equal(game.progress.baits.worm, 11);
  assert.equal(game.progress.energy, 96);
  assert.equal(game.progress.fish.length, 1);
  assert.ok(game.progress.xp > 0);
  const fish = game.session.fish, coins = game.progress.coins;
  assert.equal(game.land(), false);
  assert.equal(game.sell(fish.id), true);
  assert.equal(game.progress.coins, coins + fish.value);
  assert.equal(game.sell(fish.id), false);
  assert.equal(game.progress.coins, coins + fish.value);
  assert.equal(game.progress.book[fish.species].count, 1);
});

test('missed bites, excessive tension, and prolonged slack lose the fish without granting rewards', () => {
  const missed = new FishingGame(null, () => .2);
  bite(missed); for (let i=0;i<160;i++) missed.tick(.05);
  assert.equal(missed.phase, 'escaped');
  assert.equal(missed.progress.fish.length, 0);
  const tight = new FishingGame(null, () => .2);
  bite(tight); tight.strike(); for (let i=0;i<300;i++) tight.tick(.05);
  assert.equal(tight.phase, 'escaped'); assert.match(tight.message, /张力/);
  const slack = new FishingGame(null, () => .2);
  bite(slack); slack.strike(); slack.setReeling(false); for (let i=0;i<300;i++) slack.tick(.05);
  assert.equal(slack.phase, 'escaped'); assert.match(slack.message, /松/);
  for (const game of [missed,tight,slack]) { assert.equal(game.progress.caught,0); assert.equal(game.progress.xp,0); }
});

test('all eight species can be hooked in an appropriate map with compatible bait', () => {
  const observed = new Set();
  for (const [map, place] of Object.entries(MAPS)) {
    for (const bait of ['worm','corn','minnow']) {
      const pool = place.species.filter(id => SPECIES[id].bait.includes(bait));
      if (!pool.length) continue;
      for (const spot of SPOTS[map]) for (let sample=0;sample<=20;sample++) {
        const progress = freshProgress(); progress.map=map;progress.bait=bait;progress.baits[bait]=10;progress.caught=1;
        let randomCalls=0;
        const game = new FishingGame(progress,()=>randomCalls++===0 ? .05 : randomCalls===2 ? Math.min(.999999,sample/20) : .3);
        bite(game, spot.target);observed.add(game.session.species);
        assert.ok(pool.includes(game.session.species));
      }
    }
  }
  assert.deepEqual([...observed].sort(),Object.keys(SPECIES).sort());
});

test('incompatible bait, insufficient stamina, full creel and duplicate casts do not charge the player', () => {
  const progress=freshProgress();progress.map='pine';progress.bait='minnow';progress.baits.minnow=5;
  const game=new FishingGame(progress);
  assert.equal(game.cast(),false);assert.equal(game.progress.baits.minnow,5);
  game.chooseBait('worm');game.progress.energy=0;
  assert.equal(game.cast(),false);assert.equal(game.progress.baits.worm,12);
  game.progress.energy=100;
  game.progress.fish=Array.from({length:FISH_CAPACITY},(_,i)=>({id:String(i+1),species:'roach',weight:.2,value:4,map:'pine'}));
  assert.equal(game.cast(),false);assert.equal(game.progress.baits.worm,12);
  game.progress.fish=[];assert.equal(game.cast(),true);assert.equal(game.cast(),false);
  assert.equal(game.progress.baits.worm,11);assert.equal(game.travel('river'),false);
});

test('gear upgrades shorten fights; clothing reduces stamina cost; food and bait purchases have effects', () => {
  const first = new FishingGame(null,()=>.2);
  const baseTime = catchFish(first);
  const progress = freshProgress();progress.coins=1000;
  const upgraded = new FishingGame(progress,()=>.2);
  assert.equal(upgraded.buy('gear','reedRod'),true);
  assert.equal(upgraded.buy('gear','windcoat'),true);
  const improvedTime = catchFish(upgraded);
  assert.ok(improvedTime < baseTime);
  assert.equal(upgraded.progress.energy,97);
  upgraded.finish();assert.equal(upgraded.buy('food','tea'),true);assert.equal(upgraded.progress.energy,100);
  const coins=upgraded.progress.coins;
  assert.equal(upgraded.buy('food','tea'),false);assert.equal(upgraded.progress.coins,coins);
  const count=upgraded.progress.baits.worm;
  assert.equal(upgraded.buy('bait','worm'),true);assert.equal(upgraded.progress.baits.worm,count+10);
  upgraded.progress.coins=0;assert.equal(upgraded.buy('gear','riverRod'),false);
  assert.equal(upgraded.progress.gear.rod,'reedRod');
});

test('durable progress survives reload, but an unfinished cast cannot produce a duplicated fish', () => {
  const game=new FishingGame(null,()=>.05);catchFish(game);
  const restored=new FishingGame(JSON.parse(JSON.stringify(game.progress)));
  assert.equal(restored.phase,'idle');assert.deepEqual(restored.progress.fish,game.progress.fish);
  assert.equal(restored.progress.xp,game.progress.xp);
  game.finish();game.cast();
  const inFlight=new FishingGame(JSON.parse(JSON.stringify(game.progress)));
  assert.equal(inFlight.phase,'idle');assert.equal(inFlight.progress.fish.length,1);assert.equal(inFlight.progress.baits.worm,10);
});

test('corrupt saves and hostile identifiers are sanitized; emergency supplies prevent a resource soft lock', () => {
  const state=restoreProgress({version:1,coins:-99,energy:Infinity,map:'__proto__',gear:{rod:'unknown'},owned:['constructor'],fish:[{id:'<img>',species:'roach',weight:.4,value:99999,map:'fake'}]});
  assert.equal(state.coins,0);assert.equal(state.energy,100);assert.equal(state.map,'pine');
  assert.match(state.fish[0].id,/^\d+$/);assert.equal(state.fish[0].value,9);
  const progress=freshProgress();progress.coins=0;progress.energy=0;progress.baits={worm:0,corn:0,minnow:0};
  const game=new FishingGame(progress);assert.equal(game.supply(),true);assert.equal(game.cast(),true);
  assert.equal(game.supply(),false);
});

test('invalid water casts do not consume resources; moving and reloading preserve local fishing pressure', () => {
  const game=new FishingGame(null,()=>.05), point={x:-21,z:-12};
  for (const bad of [{x:Infinity,z:-9},{x:0,z:4},{x:90,z:-10},null]) assert.equal(game.cast(bad),false);
  assert.equal(game.progress.baits.worm,12);assert.equal(game.progress.energy,100);
  assert.equal(game.cast(point),true);assert.equal(game.moveSpot(1),false);game.cancel();
  const pressure=JSON.stringify(game.progress.world.pressure);
  assert.equal(game.moveSpot(1),true);assert.equal(game.travel('river'),true);assert.equal(game.travel('pine'),true);
  assert.equal(JSON.stringify(game.progress.world.pressure),pressure);
  const reload=new FishingGame(JSON.parse(JSON.stringify(game.progress)));
  assert.deepEqual(reload.progress.world,game.progress.world);
});

test('new catches record their actual landing point, bait, time and depth without replacing old progress', () => {
  const game=new FishingGame(null,()=>.05);catchFish(game);
  assert.equal(game.progress.journal.length,1);
  const record=game.progress.journal[0];assert.deepEqual(record.point,game.session.point);
  assert.ok(record.depth>0);assert.ok(record.hour>=0&&record.hour<24);assert.equal(record.bait,'worm');
  const reload=new FishingGame(JSON.parse(JSON.stringify(game.progress)));
  assert.deepEqual(reload.progress.journal,game.progress.journal);
  const old=freshProgress();delete old.world;delete old.spot;delete old.journal;old.coins=543;old.xp=123;
  const migrated=new FishingGame(old);assert.equal(migrated.progress.coins,543);assert.equal(migrated.progress.xp,123);
  assert.equal(migrated.progress.world.elapsed,0);assert.deepEqual(migrated.progress.journal,[]);
});

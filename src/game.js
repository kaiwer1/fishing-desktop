import { BAITS, MAPS, SPECIES, GEAR, FOOD, FISH_CAPACITY, levelFor, fishPrice } from './content.js';
import { freshWorld, restoreWorld, advanceWorld, isWater, SPOTS, fishingConditions, pickWeighted, disturb } from './ecosystem.js';

const own = (object, key) => typeof key === 'string' && Object.hasOwn(object, key);
const clamp = (n, min, max) => Math.min(max, Math.max(min, n));
const number = (n, fallback, max) => typeof n === 'number' && Number.isFinite(n) ? clamp(n, 0, max) : fallback;
const integer = (n, fallback, max) => Math.floor(number(n, fallback, max));

export function freshProgress() {
  return {
    version: 1, coins: 120, energy: 100, map: 'pine', bait: 'worm',
    baits: { worm: 12, corn: 8, minnow: 0 },
    gear: { rod: 'starter', line: 'basic', clothes: 'everyday' },
    owned: ['starter', 'basic', 'everyday'], fish: [], book: {}, xp: 0, caught: 0, sold: 0, nextId: 1,
    world: freshWorld(), spot: 0, journal: [],
  };
}

/** Only durable progress is restored; in-flight casts never become free fish. */
export function restoreProgress(raw) {
  const state = freshProgress();
  if (!raw || typeof raw !== 'object' || raw.version !== 1) return state;
  state.coins = integer(raw.coins, 120, 1e9);
  state.energy = number(raw.energy, 100, 100);
  if (own(MAPS, raw.map)) state.map = raw.map;
  state.world = restoreWorld(raw.world);
  state.spot = integer(raw.spot, 0, SPOTS[state.map].length - 1);
  if (own(BAITS, raw.bait)) state.bait = raw.bait;
  for (const key of Object.keys(BAITS)) state.baits[key] = integer(raw.baits?.[key], state.baits[key], 9999);
  state.owned = [...new Set([...state.owned, ...(Array.isArray(raw.owned) ? raw.owned.filter(id => own(GEAR, id)) : [])])];
  for (const type of ['rod', 'line', 'clothes']) {
    const id = raw.gear?.[type];
    if (state.owned.includes(id) && GEAR[id].type === type) state.gear[type] = id;
  }
  const ids = new Set();
  for (const candidate of Array.isArray(raw.fish) ? raw.fish.slice(0, FISH_CAPACITY) : []) {
    if (!candidate || !own(SPECIES, candidate.species) || typeof candidate.weight !== 'number' || !Number.isFinite(candidate.weight) || candidate.weight <= 0 || candidate.weight > SPECIES[candidate.species].max) continue;
    const id = String(Math.max(1, integer(Number(candidate.id), state.nextId, 1e9)));
    if (ids.has(id)) continue;
    ids.add(id);
    state.fish.push({ id, species: candidate.species, weight: candidate.weight, value: fishPrice(candidate.species, candidate.weight), map: own(MAPS, candidate.map) ? candidate.map : state.map });
    state.nextId = Math.max(state.nextId + 1, integer(Number(id), 0, 1e9) + 1);
  }
  state.nextId = Math.max(state.nextId, integer(raw.nextId, 1, 1e9));
  for (const id of Object.keys(SPECIES)) {
    if (raw.book?.[id] && typeof raw.book[id] === 'object') state.book[id] = { count: integer(raw.book[id].count, 0, 1e6), record: number(raw.book[id].record, 0, SPECIES[id].max) };
  }
  state.xp = integer(raw.xp, 0, 1e8);
  state.caught = integer(raw.caught, 0, 1e6);
  state.sold = integer(raw.sold, 0, 1e6);
  for (const entry of Array.isArray(raw.journal) ? raw.journal.slice(-30) : []) {
    if (!entry || !own(SPECIES, entry.species) || !own(MAPS, entry.map) || !own(BAITS, entry.bait) || !isWater(entry.map, entry.point)) continue;
    state.journal.push({ species: entry.species, map: entry.map, bait: entry.bait, point: {x: entry.point.x, z: entry.point.z}, weight: number(entry.weight, .1, SPECIES[entry.species].max), hour: number(entry.hour, 6, 24), depth: number(entry.depth, 1, 12) });
  }
  return state;
}

export class FishingGame {
  constructor(progress, rng = Math.random) {
    this.progress = restoreProgress(progress);
    this.rng = rng;
    this.phase = 'idle';
    this.session = null;
    this.revision = 0;
    this.saveRevision = 0;
    this.worldCheckpoint = 0;
    this.message = '选好鱼饵，点击抛竿开始。浮漂下沉时提竿。';
  }

  get busy() { return ['casting', 'waiting', 'bite', 'fighting'].includes(this.phase); }
  get level() { return levelFor(this.progress.xp); }
  get totalValue() { return this.progress.fish.reduce((sum, fish) => sum + fish.value, 0); }
  get spot() { return SPOTS[this.progress.map][this.progress.spot]; }
  random() { const n = this.rng(); return Number.isFinite(n) ? clamp(n, 0, .999999) : .5; }
  changed(message, save = false) {
    if (message) this.message = message;
    this.revision++;
    if (save) this.saveRevision++;
    return true;
  }
  reject(message) { this.changed(message); return false; }

  chooseBait(id) {
    if (this.busy) return this.reject('先收竿，再更换鱼饵。');
    if (!own(BAITS, id)) return false;
    this.progress.bait = id;
    return this.changed(`已装上${BAITS[id].name}。`, true);
  }

  cast(point = this.spot.target) {
    if (this.busy || this.phase === 'caught') return false;
    const p = this.progress;
    if (!isWater(p.map, point)) return this.reject('落点不在可垂钓水域，朝水面重新瞄准。');
    const cost = GEAR[p.gear.clothes].energyCost;
    if (p.fish.length >= FISH_CAPACITY) return this.reject('鱼护已满，先到鱼市卖鱼。');
    if (p.baits[p.bait] < 1) return this.reject('这种鱼饵用完了。换一种，或去渔具店补充。');
    if (p.energy < cost) return this.reject('体力不足，先去食品店喝茶或吃点东西。');
    const candidates = MAPS[p.map].species.filter(id => SPECIES[id].bait.includes(p.bait));
    if (!candidates.length) return this.reject('这里的鱼不适合这种鱼饵。查看地图鱼种，或换用蚯蚓。');
    this.session = {
      species: null, weight: 0, elapsed: 0, totalTime: 0,
      point: { x: point.x, z: point.z }, bait: p.bait,
      exposure: 0, threshold: .35 - Math.log(Math.max(.000001, 1 - this.random())),
      biteWindow: p.caught === 0 ? 7 : 5, tension: 35, progress: 0, looseTime: 0, reeling: false,
      difficulty: 1, fish: null,
    };
    p.baits[p.bait]--;
    p.energy -= cost;
    disturb(p.world, p.map, point, .055);
    this.phase = 'casting';
    return this.changed('正在抛竿……', true);
  }

  strike() {
    if (this.phase !== 'bite') return this.reject('浮漂还没完全下沉，等咬钩提示再提竿。');
    this.phase = 'fighting';
    this.session.elapsed = 0;
    this.session.reeling = true;
    return this.changed('中鱼！正在收线。张力接近红区时点“松线”，降下来后继续收线。');
  }

  setReeling(value) {
    if (this.phase !== 'fighting') return false;
    this.session.reeling = Boolean(value);
    return this.changed(value ? '正在收线，把鱼慢慢带回来。' : '正在松线，等张力回到绿区。');
  }

  cancel() {
    if (!this.busy) return false;
    this.phase = 'idle'; this.session = null;
    return this.changed('已收竿，本次鱼饵已消耗。重新选饵后可再抛竿。');
  }

  finish() {
    if (this.busy) return false;
    this.phase = 'idle'; this.session = null;
    return this.changed('继续找一个好钓位吧。');
  }

  escape(reason) {
    disturb(this.progress.world, this.progress.map, this.session.point, .08);
    this.phase = 'escaped'; this.session.reeling = false;
    return this.changed(reason, true);
  }

  tick(seconds) {
    if (!Number.isFinite(seconds) || seconds <= 0) return;
    const dt = Math.min(seconds, .25), p = this.progress;
    advanceWorld(p.world, dt);
    this.worldCheckpoint += dt;
    if (this.worldCheckpoint >= 5) { this.worldCheckpoint = 0; this.changed(null, true); }
    if (!this.busy) return;
    const s = this.session;
    s.elapsed += dt; s.totalTime += dt;
    if (this.phase === 'casting' && s.elapsed >= 1) {
      this.phase = 'waiting'; s.elapsed = 0; this.changed('鱼饵已入水。观察浮漂，等它完全下沉。');
    } else if (this.phase === 'waiting') {
      const conditions = fishingConditions(p.world, p.map, s.point, s.bait);
      if (s.elapsed > 2) s.exposure += dt * (p.caught === 0 ? Math.max(.22, conditions.rate) : conditions.rate) / GEAR[p.gear.clothes].wait;
      if (s.exposure >= s.threshold) {
        let pool = conditions.weights;
        if (p.caught === 0 && pool.some(fish => SPECIES[fish.id].strength <= 1.35)) pool = pool.filter(fish => SPECIES[fish.id].strength <= 1.35);
        s.species = pickWeighted(pool, this.random());
        if (!s.species) return this.escape('暂时没有鱼问饵，换个落点或鱼饵再试。');
        const definition = SPECIES[s.species], size = p.caught === 0 ? this.random() * .22 : this.random() ** 1.8;
        s.weight = Math.round((definition.min + (definition.max - definition.min) * size) * 1000) / 1000;
        s.difficulty = definition.strength * (.8 + size * .5);
        s.conditions = { hour: conditions.environment.hour, depth: conditions.depth };
        this.phase = 'bite'; s.elapsed = 0; this.changed('浮漂沉了！右键提竿，或点击“提竿”！');
      } else if (s.elapsed >= 65) this.escape('这一竿暂时没有鱼问饵，试试别的远近、水草边或钓点。');
    } else if (this.phase === 'bite' && s.elapsed > s.biteWindow) {
      this.escape('提竿晚了，鱼吃掉鱼饵游走了。再试一次。');
    } else if (this.phase === 'fighting') {
      const pull = s.difficulty * (.75 + .25 * Math.sin(s.elapsed * 2.1));
      const linePower = GEAR[this.progress.gear.line].power;
      s.tension = Math.max(0, s.tension + dt * (s.reeling ? (15 + pull * 7) / linePower : -25 + pull * 3));
      s.looseTime = s.tension < 8 ? s.looseTime + dt : 0;
      if (s.tension >= 100) return this.escape('鱼线张力过高，鱼挣脱了。下次接近红区就松线。');
      if (s.looseTime > 4) return this.escape('鱼线松得太久，鱼脱钩了。记得及时收线。');
      if (s.reeling) {
        const power = GEAR[this.progress.gear.rod].power * (1 + Math.min(.2, (this.level - 1) * .025));
        const control = s.tension >= 18 && s.tension <= 80 ? 1 : .25;
        s.progress = clamp(s.progress + dt * .125 * power * control / (.7 + s.difficulty * .3), 0, 1);
      }
      if (s.progress >= 1) this.land();
    }
  }

  land() {
    if (this.phase !== 'fighting') return false;
    const p = this.progress, s = this.session;
    const fish = { id: String(p.nextId++), species: s.species, weight: s.weight, value: fishPrice(s.species, s.weight), map: p.map };
    p.fish.push(fish); p.caught++;
    p.journal.push({ species: s.species, weight: s.weight, map: p.map, point: { ...s.point }, bait: s.bait, ...s.conditions });
    p.journal = p.journal.slice(-30);
    disturb(p.world, p.map, s.point, .16);
    const entry = p.book[s.species] ?? { count: 0, record: 0 };
    const record = fish.weight > entry.record;
    p.book[s.species] = { count: entry.count + 1, record: Math.max(entry.record, fish.weight) };
    s.xp = 10 + Math.round(fish.weight * 6); p.xp += s.xp;
    s.fish = fish; s.record = record; s.reeling = false;
    this.phase = 'caught';
    return this.changed(`钓到了${SPECIES[fish.species].name}！已放入鱼护。`, true);
  }

  travel(id) {
    if (this.busy || this.phase === 'caught') return this.reject('先结束这次垂钓，再切换地图。');
    if (!own(MAPS, id)) return false;
    this.progress.map = id; this.phase = 'idle'; this.session = null;
    this.progress.spot = 0;
    return this.changed(`已到达${MAPS[id].name}。换一种鱼饵，看看这里的鱼。`, true);
  }

  moveSpot(index) {
    if (this.busy || this.phase === 'caught') return this.reject('先收竿，再换钓位。');
    if (!Number.isInteger(index) || !SPOTS[this.progress.map][index]) return false;
    this.progress.spot = index; this.phase = 'idle'; this.session = null;
    return this.changed(`来到${this.spot.name}。${this.spot.note}`, true);
  }

  buy(kind, id) {
    if (this.busy) return this.reject('先收竿，再购买物品。');
    const table = kind === 'bait' ? BAITS : kind === 'food' ? FOOD : kind === 'gear' ? GEAR : null;
    if (!table || !own(table, id)) return false;
    const item = table[id], p = this.progress;
    if (kind === 'gear' && p.owned.includes(id)) return this.equip(id);
    if (kind === 'food' && p.energy >= 100) return this.reject('体力已满，先留着银币吧。');
    if (p.coins < item.price) return this.reject('银币不够，先卖几条鱼。');
    if (kind === 'bait' && p.baits[id] > 9999 - item.quantity) return this.reject('这种鱼饵已经很多了。');
    p.coins -= item.price;
    if (kind === 'bait') p.baits[id] += item.quantity;
    if (kind === 'food') p.energy = Math.min(100, p.energy + item.energy);
    if (kind === 'gear') { p.owned.push(id); p.gear[item.type] = id; }
    return this.changed(`已购买${item.name}${kind === 'food' ? '，体力恢复了' : kind === 'gear' ? '并装备' : ''}。`, true);
  }

  equip(id) {
    if (this.busy) return this.reject('先收竿，再更换装备。');
    if (!own(GEAR, id) || !this.progress.owned.includes(id)) return false;
    this.progress.gear[GEAR[id].type] = id;
    return this.changed(`已装备${GEAR[id].name}。`, true);
  }

  sell(id = null) {
    if (this.busy) return this.reject('先收竿，再出售渔获。');
    const p = this.progress, selected = id === null ? p.fish : p.fish.filter(fish => fish.id === id);
    if (!selected.length) return this.reject('鱼护还没有鱼，先去钓一条吧。');
    const value = selected.reduce((sum, fish) => sum + fish.value, 0);
    const ids = new Set(selected.map(fish => fish.id));
    p.fish = p.fish.filter(fish => !ids.has(fish.id)); p.coins += value; p.sold += selected.length;
    return this.changed(`卖出 ${selected.length} 条鱼，获得 ${value} 银币。`, true);
  }

  supply() {
    if (this.busy) return false;
    const p = this.progress;
    if (p.coins >= 20 || (p.energy >= 4 && Object.values(p.baits).some(n => n > 0))) return this.reject('现有补给还能继续用；需要时可到商店补充。');
    p.energy = Math.max(p.energy, 35); p.baits.worm += 5; p.bait = 'worm';
    return this.changed('营地送来 5 份蚯蚓和一份简餐，可以继续钓鱼了。', true);
  }
}

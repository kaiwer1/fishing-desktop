import { MAPS, SPECIES } from './content.js';

const clamp = (n, a, b) => Math.max(a, Math.min(b, n));
const finite = (n, fallback, max) => typeof n === 'number' && Number.isFinite(n) ? clamp(n, 0, max) : fallback;
const hash = n => { const v = Math.sin(n * 127.1 + 311.7) * 43758.5453; return v - Math.floor(v); };
const COLUMNS = 6, ROWS = 5;

export const SPOTS = {
  pine: [
    { name: '西岸草边', x: -18, target: { x: -21, z: -12 }, note: '近岸浅水与水草，先试试蚯蚓。' },
    { name: '湖心栈桥', x: 0, target: { x: 0, z: -28 }, note: '朝远处深水抛投，留意水面动静。' },
    { name: '东岸浅滩', x: 18, target: { x: 23, z: -15 }, note: '浅滩靠着深浅交界，可以换着远近试钓。' },
  ],
  reed: [
    { name: '西侧芦苇', x: -18, target: { x: -23, z: -13 }, note: '水草边缘适合尝试近距离落点。' },
    { name: '湾口深槽', x: 0, target: { x: -2, z: -34 }, note: '远处水更深，玉米与蚯蚓各试一竿。' },
    { name: '东侧草窝', x: 18, target: { x: 24, z: -15 }, note: '草窝和开阔水面之间可能有鱼活动。' },
  ],
  river: [
    { name: '上游缓流', x: -9, target: { x: -11, z: -15 }, note: '岸边流速较缓，先观察鱼口。' },
    { name: '河心深槽', x: 0, target: { x: 0, z: -32 }, note: '深水与流水交界，可以用活饵寻找掠食鱼。' },
    { name: '下游草边', x: 9, target: { x: 11, z: -18 }, note: '活饵或蚯蚓，注意草边的动静。' },
  ],
};

// Original gameplay approximations; these are not RF4 parameters or ecological claims.
const NICHES = {
  crucian: { x: -21, z: -12, depth: 2, temperature: 19, peak: 7 },
  roach: { x: -7, z: -18, depth: 2.8, temperature: 16, peak: 9 },
  carp: { x: 9, z: -28, depth: 4.5, temperature: 22, peak: 19 },
  tench: { x: 23, z: -14, depth: 2.6, temperature: 21, peak: 5 },
  bream: { x: -2, z: -35, depth: 5, temperature: 19, peak: 21 },
  perch: { x: -10, z: -23, depth: 3.5, temperature: 16, peak: 8 },
  pike: { x: 12, z: -18, depth: 3.2, temperature: 15, peak: 17 },
  chub: { x: 5, z: -29, depth: 3.8, temperature: 17, peak: 10 },
};

export function freshWorld() {
  return { version: 1, seed: 7341, elapsed: 0, pressure: Object.fromEntries(Object.keys(MAPS).map(id => [id, Array(COLUMNS * ROWS).fill(0)])) };
}

export function restoreWorld(raw) {
  const world = freshWorld();
  if (!raw || raw.version !== 1) return world;
  world.seed = Math.floor(finite(raw.seed, world.seed, 0xffffffff));
  world.elapsed = finite(raw.elapsed, 0, 1e9);
  for (const id of Object.keys(MAPS)) {
    const cells = raw.pressure?.[id];
    if (Array.isArray(cells)) world.pressure[id] = world.pressure[id].map((_, i) => finite(cells[i], 0, 1));
  }
  return world;
}

export function waterLimit(map) { return map === 'river' ? 18 : 36; }
export function isWater(map, point) {
  return Object.hasOwn(MAPS, map) && point && Number.isFinite(point.x) && Number.isFinite(point.z) && Math.abs(point.x) <= waterLimit(map) && point.z <= -3 && point.z >= -50;
}
export function advanceWorld(world, dt) {
  if (!Number.isFinite(dt) || dt <= 0) return;
  world.elapsed = Math.min(1e9, world.elapsed + dt);
  const decay = Math.exp(-dt / 240);
  for (const cells of Object.values(world.pressure)) for (let i = 0; i < cells.length; i++) cells[i] *= decay;
}
function cellIndex(map, point) {
  const x = clamp(Math.floor((point.x + waterLimit(map)) / (waterLimit(map) * 2) * COLUMNS), 0, COLUMNS - 1);
  const z = clamp(Math.floor((-point.z - 3) / 47 * ROWS), 0, ROWS - 1);
  return z * COLUMNS + x;
}
export function pressureAt(world, map, point) { return isWater(map, point) ? world.pressure[map][cellIndex(map, point)] : 0; }
export function disturb(world, map, point, amount = .10) {
  if (!isWater(map, point)) return;
  const cells = world.pressure[map], limit = waterLimit(map);
  for (let i = 0; i < cells.length; i++) {
    const x = -limit + (i % COLUMNS + .5) / COLUMNS * limit * 2;
    const z = -3 - (Math.floor(i / COLUMNS) + .5) / ROWS * 47;
    const influence = Math.exp(-((x - point.x) ** 2 + (z - point.z) ** 2) / 100);
    cells[i] = clamp(cells[i] + amount * influence, 0, 1);
  }
}
export function environmentAt(world, map) {
  const hour = (6.3 + world.elapsed / 35) % 24;
  const daylight = clamp((Math.sin((hour - 6) * Math.PI / 12) + .12) / 1.1, .13, 1);
  const cloud = .5 + .4 * Math.sin(world.elapsed / 95 + world.seed * .01);
  return {
    hour, daylight, cloud,
    temperature: (map === 'river' ? 14 : map === 'reed' ? 20 : 17) + Math.sin((hour - 8) * Math.PI / 12) * 4,
    wind: 1.5 + (1 + Math.sin(world.elapsed / 71)) * 1.7,
    weather: cloud > .65 ? '阴天' : cloud < .35 ? '晴朗' : '多云',
  };
}
export function depthAt(map, point) {
  const edge = 1 - Math.abs(point.x) / waterLimit(map);
  return Math.max(.8, .9 + (-point.z - 3) * .075 + edge * (map === 'river' ? 2 : 1.5));
}
export function schoolsAt(world, map) {
  const limit = waterLimit(map) - 3;
  return MAPS[map].species.map((id, index) => {
    const niche = NICHES[id], phase = hash(world.seed + index * 91 + id.length * 17) * Math.PI * 2;
    return {
      species: id,
      x: clamp(niche.x + Math.sin(world.elapsed / (75 + index * 8) + phase) * 7, -limit, limit),
      z: clamp(niche.z + Math.cos(world.elapsed / (95 + index * 11) + phase) * 6, -46, -7),
      radius: 8 + index % 3,
    };
  });
}
export function fishingConditions(world, map, point, bait) {
  if (!isWater(map, point)) return { weights: [], rate: 0, depth: 0, pressure: 0 };
  const environment = environmentAt(world, map), depth = depthAt(map, point), pressure = pressureAt(world, map, point);
  const weights = schoolsAt(world, map).filter(school => SPECIES[school.species].bait.includes(bait)).map(school => {
    const niche = NICHES[school.species], distance2 = (school.x - point.x) ** 2 + (school.z - point.z) ** 2;
    const abundance = .06 + 2.8 * Math.exp(-distance2 / (2 * school.radius ** 2));
    const depthFit = .35 + .65 * Math.exp(-(((depth - niche.depth) / 2.4) ** 2));
    const temperatureFit = .45 + .55 * Math.exp(-(((environment.temperature - niche.temperature) / 8) ** 2));
    const timeFit = .45 + .55 * ((1 + Math.cos((environment.hour - niche.peak) * Math.PI / 12)) / 2);
    const windFit = .85 + .15 * Math.cos(environment.wind - 2);
    const cloudFit = school.species === 'perch' || school.species === 'pike' ? .8 + environment.cloud * .4 : 1.05 - environment.cloud * .1;
    return { id: school.species, weight: abundance * depthFit * temperatureFit * timeFit * windFit * cloudFit * (1 - .8 * pressure) };
  });
  const rate = weights.length ? clamp(weights.reduce((sum, fish) => sum + fish.weight, 0) / weights.length * .22, .018, .45) : 0;
  return { weights, rate, depth, pressure, environment };
}
export function pickWeighted(weights, roll) {
  const total = weights.reduce((sum, item) => sum + item.weight, 0);
  if (!total) return null;
  let remaining = clamp(roll, 0, .999999) * total;
  for (const item of weights) { remaining -= item.weight; if (remaining < 0) return item.id; }
  return weights.at(-1).id;
}

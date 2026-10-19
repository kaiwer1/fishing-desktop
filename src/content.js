export const BAITS = {
  worm: { name: '蚯蚓', note: '适合多数淡水鱼', price: 8, quantity: 10 },
  corn: { name: '甜玉米', note: '适合鲤鱼、鲫鱼和丁鱥', price: 8, quantity: 10 },
  minnow: { name: '活饵鱼', note: '到白桦河寻找鲈鱼和狗鱼', price: 18, quantity: 6 },
};

// Original prototype balance values, not ecological simulation or RF4 data.
export const SPECIES = {
  crucian: { name: '鲫鱼', min: .15, max: 1.8, strength: .7, price: 24, color: '#b9ac83', shape: 'round', bait: ['worm', 'corn'] },
  roach: { name: '拟鲤', min: .12, max: 1.2, strength: .6, price: 22, color: '#b4c0bb', shape: 'slim', bait: ['worm'] },
  carp: { name: '鲤鱼', min: .6, max: 8, strength: 1.9, price: 30, color: '#b39a56', shape: 'round', bait: ['worm', 'corn'] },
  tench: { name: '丁鱥', min: .3, max: 3.5, strength: 1.3, price: 32, color: '#829766', shape: 'round', bait: ['worm', 'corn'] },
  bream: { name: '鳊鱼', min: .35, max: 4.8, strength: 1.35, price: 29, color: '#b5b6a6', shape: 'round', bait: ['worm', 'corn'] },
  perch: { name: '河鲈', min: .18, max: 2.5, strength: 1.15, price: 36, color: '#a0a16b', shape: 'slim', bait: ['worm', 'minnow'] },
  pike: { name: '白斑狗鱼', min: .8, max: 9, strength: 2.2, price: 43, color: '#819c77', shape: 'long', bait: ['minnow'] },
  chub: { name: '雅罗鱼', min: .25, max: 3.2, strength: 1.35, price: 34, color: '#a8b7ad', shape: 'slim', bait: ['worm'] },
};

export const MAPS = {
  pine: { name: '松风湖', subtitle: '松林环抱的平静湖面', weather: '清晨 · 微风 · 湖泊', species: ['crucian', 'roach', 'carp', 'tench'], water: 0x244f43, ground: 0x536854, forest: 0x2c4b3d },
  reed: { name: '芦苇湾', subtitle: '浅湾藏着鳊鱼和丁鱥', weather: '午后 · 晴朗 · 浅湾', species: ['crucian', 'bream', 'tench', 'carp'], water: 0x4b603c, ground: 0x747148, forest: 0x536345 },
  river: { name: '白桦河', subtitle: '用活饵寻找凶猛的狗鱼', weather: '傍晚 · 轻风 · 河湾', species: ['roach', 'perch', 'chub', 'pike'], water: 0x305c68, ground: 0x63725e, forest: 0x37594e },
};

export const GEAR = {
  starter: { type: 'rod', name: '湖畔入门竿', price: 0, power: 1, note: '可靠的入门鱼竿', color: '#52665b' },
  reedRod: { type: 'rod', name: '芦岸轻量竿', price: 120, power: 1.4, note: '收鱼速度提升 40%', color: '#8c7653' },
  riverRod: { type: 'rod', name: '溪流强力竿', price: 280, power: 1.9, note: '收鱼速度提升 90%', color: '#487d89' },
  basic: { type: 'line', name: '标准鱼线', price: 0, power: 1, note: '常规张力控制' },
  strong: { type: 'line', name: '韧性鱼线', price: 70, power: 1.35, note: '收线时张力增长降低约 26%' },
  everyday: { type: 'clothes', name: '日常外套', price: 0, energyCost: 4, wait: 1, note: '轻装来到水边', color: '#b9aa8b' },
  windcoat: { type: 'clothes', name: '防风钓鱼服', price: 80, energyCost: 3, wait: 1, note: '每次抛竿消耗体力从 4 降至 3', color: '#758868' },
  hat: { type: 'clothes', name: '遮阳钓鱼套装', price: 100, energyCost: 4, wait: .8, note: '原型加成：等待咬钩时间缩短 20%', color: '#b9a674' },
};

export const FOOD = {
  tea: { name: '一杯热茶', price: 12, energy: 35, note: '立即恢复 35 点体力' },
  sandwich: { name: '营地三明治', price: 22, energy: 65, note: '立即恢复 65 点体力' },
};

export const FISH_CAPACITY = 50;
export function levelFor(xp) { return 1 + Math.floor(Math.sqrt(xp / 35)); }
export function fishPrice(species, weight) { return Math.max(1, Math.round(SPECIES[species].price * weight)); }

import { FishingGame } from './game.js';
import { BAITS, MAPS, SPECIES, GEAR, FOOD, FISH_CAPACITY } from './content.js';

const SAVE_KEY = 'fishing-desktop.progress.v1';
const $ = selector => document.querySelector(selector);
const kg = n => n < 1 ? `${Math.round(n * 1000)} 克` : `${n.toFixed(2)} 公斤`;
const text = (selector, value) => { const node = $(selector); if (node.textContent !== String(value)) node.textContent = value; };

function fishArt(id) {
  const fish = SPECIES[id];
  const height = fish.shape === 'round' ? 30 : fish.shape === 'long' ? 14 : 22;
  return `<svg class="fish-art" viewBox="0 0 220 110" role="img" aria-label="${fish.name}示意图"><g fill="${fish.color}" stroke="#283a35" stroke-width="1.5"><path d="M162 55 L202 29 L195 55 L202 82 Z"/><path d="M82 ${55-height+4} L102 ${21-height*.3} L135 ${55-height+5} Z"/><ellipse cx="106" cy="55" rx="70" ry="${height}"/><path d="M103 59 L125 ${73+height*.3} L98 69Z"/><path d="M63 ${49-height*.2} Q76 55 66 ${58+height*.3}" fill="none"/><circle cx="51" cy="50" r="4" fill="#111e1c" stroke="#d3d1ad"/><path d="M39 57 L48 59" fill="none"/></g><path d="M80 ${49-height*.3} Q119 ${40-height*.25} 159 52" stroke="#e2dfbf" stroke-width="2" opacity=".4" fill="none"/></svg>`;
}

export function mountGameUI(callbacks = {}) {
  let saved;
  try { saved = JSON.parse(localStorage.getItem(SAVE_KEY) ?? 'null'); } catch { saved = null; }
  const game = new FishingGame(saved);
  const hub = $('#hub'), result = $('#catch-dialog');
  let tab = 'maps', revision = -1, saveRevision = 0, previousPhase = 'idle';
  let map = null, audio = null;
  let castPoint = { x: 1.4, z: -9 };

  function save() {
    if (saveRevision === game.saveRevision) return;
    saveRevision = game.saveRevision;
    try { localStorage.setItem(SAVE_KEY, JSON.stringify(game.progress)); text('#save-status', '进度自动保存'); }
    catch { text('#save-status', '当前环境无法保存，关闭页面会丢失本次进度'); }
  }

  function prepareAudio() {
    if (!$('#bite-sound').checked) return;
    try { audio ??= new AudioContext(); audio.resume().catch(() => {}); } catch { /* Audio is optional. */ }
  }
  function biteSound() {
    if (!audio || audio.state !== 'running' || !$('#bite-sound').checked) return;
    const oscillator = audio.createOscillator(), gain = audio.createGain();
    oscillator.type = 'sine'; oscillator.frequency.setValueAtTime(760, audio.currentTime);
    oscillator.frequency.setValueAtTime(980, audio.currentTime + .14);
    gain.gain.setValueAtTime(.05, audio.currentTime); gain.gain.exponentialRampToValueAtTime(.001, audio.currentTime + .42);
    oscillator.connect(gain); gain.connect(audio.destination); oscillator.start(); oscillator.stop(audio.currentTime + .43);
  }

  function act() {
    if (game.phase === 'bite') game.strike();
    else if (game.phase === 'fighting') game.setReeling(!game.session.reeling);
    else if (!game.busy && game.phase !== 'caught') {
      prepareAudio();
      if (game.cast()) callbacks.onCast?.(castPoint);
    }
    render();
  }

  function render() {
    const p = game.progress, s = game.session;
    text('#coins', p.coins); text('#player-level', game.level); text('#energy-value', Math.round(p.energy));
    text('#creel-count', `${p.fish.length}/${FISH_CAPACITY}`);
    text('#game-message', game.message);
    text('#map-title', MAPS[p.map].name); text('#map-copy', MAPS[p.map].subtitle); text('#map-weather', MAPS[p.map].weather);
    text('#equipped-rod', GEAR[p.gear.rod].name);
    if (map !== p.map) {
      map = p.map;
      $('#lake').setAttribute('aria-label', `${MAPS[p.map].name}动态三维场景`);
      callbacks.onMapChange?.(MAPS[p.map], p.map);
    }
    callbacks.onEquipment?.(GEAR[p.gear.rod], GEAR[p.gear.clothes]);
    const phaseTitles = { idle: '准备抛竿', casting: '抛竿入水', waiting: '等待咬钩', bite: '鱼咬钩了！', fighting: '控线遛鱼', caught: '渔获入护', escaped: '再试一竿' };
    text('#phase-title', phaseTitles[game.phase]);
    const action = $('#fishing-action');
    const actionText = game.phase === 'bite' ? '提竿！' : game.phase === 'fighting' ? (s.reeling ? '松线' : '收线') : game.phase === 'casting' ? '正在抛竿…' : game.phase === 'waiting' ? '等待咬钩…' : '抛竿';
    if (action.textContent !== actionText) action.textContent = actionText;
    action.disabled = ['casting', 'waiting', 'caught'].includes(game.phase);
    action.classList.toggle('bite-action', game.phase === 'bite');
    $('#cancel-cast').hidden = !game.busy;
    $('#bait-select').disabled = game.busy;
    $('#observe').disabled = game.busy;
    $('#fight-meters').hidden = game.phase !== 'fighting';
    $('#bite-window').hidden = game.phase !== 'bite';
    if (game.phase === 'bite') text('#bite-countdown', Math.max(0, Math.ceil(s.biteWindow - s.elapsed)));
    if (game.phase === 'fighting') {
      const tension = Math.round(s.tension);
      $('#tension-fill').style.transform = `scaleX(${Math.min(1, s.tension/100)})`;
      $('#catch-fill').style.transform = `scaleX(${s.progress})`;
      $('#tension-meter').setAttribute('aria-valuenow', tension);
      $('#catch-meter').setAttribute('aria-valuenow', Math.round(s.progress*100));
      text('#tension-number', tension); text('#catch-number', `${Math.round(s.progress*100)}%`);
      text('#fight-advice', s.tension > 75 ? '张力高了，松线！' : s.tension < 18 ? '鱼线偏松，及时收线' : s.reeling ? '保持张力，稳稳收线' : '回到绿区后点击收线');
      $('#tension-meter').classList.toggle('danger', s.tension > 75);
    }
    if (revision !== game.revision) {
      revision = game.revision;
      $('#bait-select').innerHTML = Object.entries(BAITS).map(([id, bait]) => `<option value="${id}" ${p.bait === id ? 'selected' : ''}>${bait.name} · ${p.baits[id]} 份</option>`).join('');
      if (hub.open) renderHub();
    }
    if (previousPhase !== game.phase) {
      if (game.phase === 'bite') biteSound();
      if (game.phase === 'caught') showCatch();
      previousPhase = game.phase;
    }
    document.body.classList.toggle('is-fishing', game.busy);
    save();
  }

  function showCatch() {
    const s = game.session, fish = s.fish;
    $('#catch-content').innerHTML = `<p class="eyebrow">${s.record ? '新的个人纪录' : '这一竿有收获'}</p><h2 id="catch-title">${SPECIES[fish.species].name}</h2>${fishArt(fish.species)}<div class="catch-weight">${kg(fish.weight)}</div><p>估价 <strong>${fish.value} 银币</strong> · 经验 +${s.xp}</p><p class="muted">已放入鱼护，进度已保存。</p><div class="catch-actions"><button type="button" data-sell-catch="${fish.id}">卖出这条鱼 · ${fish.value} 银币</button><button type="button" class="primary" data-keep-catch>放进鱼护，继续钓鱼</button></div>`;
    result.showModal();
  }

  const tabNames = { maps: '钓点地图', tackle: '渔具商店', food: '食品商店', clothes: '服装商店', creel: '鱼护与鱼市', bag: '我的背包', book: '鱼类图鉴' };
  function openHub(next) {
    if (game.busy) { game.reject('先收竿，再打开地图或营地。'); render(); return; }
    tab = next; renderHub(); hub.showModal();
  }
  function product(kind, id, item) {
    const p = game.progress, owned = kind === 'gear' && p.owned.includes(id), equipped = owned && p.gear[item.type] === id;
    const label = equipped ? '已装备' : owned ? '装备' : kind === 'food' ? '购买并食用' : '购买';
    return `<article class="item-card"><span class="item-kind">${kind === 'bait' ? '鱼饵补给' : kind === 'food' ? '体力补给' : item.type === 'rod' ? '鱼竿' : item.type === 'line' ? '鱼线' : '服装'}</span><h3>${item.name}</h3><p>${item.note}</p>${kind === 'bait' ? `<small>每包 ${item.quantity} 份 · 持有 ${p.baits[id]} 份</small>` : ''}<div class="item-bottom"><strong>${owned ? '已拥有' : `${item.price} 银币`}</strong><button type="button" data-buy-kind="${kind}" data-buy-id="${id}" ${equipped ? 'disabled' : ''}>${label}</button></div></article>`;
  }
  function renderHub() {
    const p = game.progress;
    text('#hub-title', tabNames[tab]); text('#hub-wallet', `${p.coins} 银币 · 体力 ${Math.round(p.energy)}/100`);
    text('#hub-message', game.message);
    for (const button of document.querySelectorAll('#hub-nav button')) button.setAttribute('aria-pressed', String(button.dataset.tab === tab));
    let html = '';
    if (tab === 'maps') html = `<p class="section-note">选择钓点后再选饵。三处钓点均可免费前往，鱼种与画面各有不同。</p><div class="item-grid">${Object.entries(MAPS).map(([id, place]) => `<article class="item-card map-card map-${id}"><span class="item-kind">${place.weather}</span><h3>${place.name}</h3><p>${place.subtitle}</p><p class="species-list">${place.species.map(fish => SPECIES[fish].name).join(' · ')}</p><button type="button" data-travel="${id}" ${p.map === id ? 'disabled' : ''}>${p.map === id ? '当前钓点' : '前往垂钓'}</button></article>`).join('')}</div>`;
    if (tab === 'tackle') html = `<h3 class="section-title">鱼饵</h3><div class="item-grid">${Object.entries(BAITS).map(([id, item]) => product('bait', id, item)).join('')}</div><h3 class="section-title">鱼竿与鱼线</h3><div class="item-grid">${Object.entries(GEAR).filter(([, item]) => item.type !== 'clothes').map(([id, item]) => product('gear', id, item)).join('')}</div>`;
    if (tab === 'food') html = `<p class="section-note">每次抛竿消耗体力。食物购买后立即使用，体力上限为 100。</p><div class="item-grid">${Object.entries(FOOD).map(([id, item]) => product('food', id, item)).join('')}</div><div class="supply"><p>补给用尽、银币不足 20 时，可向营地领取应急补给，继续垂钓。</p><button type="button" data-supply>领取应急补给</button></div>`;
    if (tab === 'clothes') html = `<p class="section-note">服装加成会实际影响体力或等待时间。当前版本用钓鱼服袖口颜色展示穿搭。</p><div class="item-grid">${Object.entries(GEAR).filter(([, item]) => item.type === 'clothes').map(([id, item]) => product('gear', id, item)).join('')}</div>`;
    if (tab === 'bag') html = `<h3 class="section-title">当前钓组</h3><div class="equipment-summary">${Object.values(p.gear).map(id => `<div><span>${GEAR[id].name}</span><small>${GEAR[id].note}</small></div>`).join('')}</div><h3 class="section-title">鱼饵</h3><div class="item-grid">${Object.entries(BAITS).map(([id, item]) => `<article class="item-card"><h3>${item.name} × ${p.baits[id]}</h3><p>${item.note}</p><button type="button" data-bait="${id}">${p.bait === id ? '当前使用' : '装上鱼饵'}</button></article>`).join('')}</div><h3 class="section-title">已拥有装备</h3><div class="item-grid">${p.owned.map(id => product('gear', id, GEAR[id])).join('')}</div>`;
    if (tab === 'creel') html = `<div class="creel-heading"><p>鱼护 ${p.fish.length}/${FISH_CAPACITY} · 共 ${kg(p.fish.reduce((sum, fish) => sum + fish.weight, 0))}<span>已出售 ${p.sold} 条 · 累计钓获 ${p.caught} 条</span></p><button type="button" class="primary" data-sell-all ${p.fish.length ? '' : 'disabled'}>全部卖出 · ${game.totalValue} 银币</button></div>${p.fish.length ? `<div class="item-grid fish-grid">${p.fish.map(fish => `<article class="item-card">${fishArt(fish.species)}<h3>${SPECIES[fish.species].name}</h3><p>${kg(fish.weight)} · ${MAPS[fish.map].name}</p><button type="button" data-sell="${fish.id}">卖出 · ${fish.value} 银币</button></article>`).join('')}</div>` : '<div class="empty-state"><h3>鱼护里还没有鱼</h3><p>先回到水边抛一竿。钓到的鱼会自动进入鱼护。</p></div>'}`;
    if (tab === 'book') html = `<p class="section-note">已发现 ${Object.values(p.book).filter(entry => entry.count > 0).length}/${Object.keys(SPECIES).length} 种 · 等级 ${game.level} · 经验 ${p.xp}。每次升级略微提升控鱼效率。</p><div class="item-grid fish-grid">${Object.entries(SPECIES).map(([id, fish]) => `<article class="item-card ${p.book[id]?.count ? '' : 'undiscovered'}">${fishArt(id)}<span class="item-kind">${p.book[id]?.count ? '已发现' : '尚未钓获'}</span><h3>${fish.name}</h3><p>${Object.values(MAPS).filter(place => place.species.includes(id)).map(place => place.name).join(' · ')}</p><p>适用：${fish.bait.map(bait => BAITS[bait].name).join('、')}</p><small>钓获 ${p.book[id]?.count ?? 0} 条 · 纪录 ${kg(p.book[id]?.record ?? 0)}</small></article>`).join('')}</div>`;
    $('#hub-content').innerHTML = html;
  }

  $('#fishing-action').addEventListener('click', act);
  $('#cancel-cast').addEventListener('click', () => { game.cancel(); render(); });
  $('#bait-select').addEventListener('change', event => { game.chooseBait(event.target.value); render(); });
  for (const button of document.querySelectorAll('[data-open-hub]')) button.addEventListener('click', () => openHub(button.dataset.openHub));
  $('#hub-close').addEventListener('click', () => hub.close());
  $('#hub-nav').addEventListener('click', event => { const button = event.target.closest('[data-tab]'); if (button) { tab = button.dataset.tab; renderHub(); } });
  $('#hub-content').addEventListener('click', event => {
    const button = event.target.closest('button'); if (!button) return;
    if (button.dataset.travel) { if (game.travel(button.dataset.travel)) hub.close(); }
    else if (button.dataset.buyKind) game.buy(button.dataset.buyKind, button.dataset.buyId);
    else if (button.dataset.bait) game.chooseBait(button.dataset.bait);
    else if (button.hasAttribute('data-sell-all')) game.sell();
    else if (button.dataset.sell) game.sell(button.dataset.sell);
    else if (button.hasAttribute('data-supply')) game.supply();
    render();
  });
  $('#catch-content').addEventListener('click', event => {
    const button = event.target.closest('button'); if (!button) return;
    if (button.dataset.sellCatch) game.sell(button.dataset.sellCatch);
    if (button.dataset.sellCatch || button.hasAttribute('data-keep-catch')) { result.close(); game.finish(); render(); }
  });
  result.addEventListener('close', () => { if (game.phase === 'caught') { game.finish(); render(); } });
  document.addEventListener('keydown', event => {
    if (event.code !== 'Space' || hub.open || result.open || $('#settings').open || $('#interface').hidden || /INPUT|SELECT|TEXTAREA|BUTTON/.test(event.target.tagName)) return;
    event.preventDefault();
    if (game.phase === 'fighting') { if (!game.session.reeling) { game.setReeling(true); render(); } }
    else if (!event.repeat) act();
  });
  document.addEventListener('keyup', event => { if (event.code === 'Space' && game.phase === 'fighting') { game.setReeling(false); render(); } });
  render();
  return {
    game,
    tick(dt) {
      if (!hub.open && !result.open && !$('#settings').open && !$('#interface').hidden) {
        let remaining = Math.min(1, dt);
        while (remaining > 0) { const step = Math.min(.05, remaining); game.tick(step); remaining -= step; }
      }
      render();
    },
    castAt(point) { if (game.busy || game.phase === 'caught') return; castPoint = { x: point.x, z: point.z }; act(); },
  };
}

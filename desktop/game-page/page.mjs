import { LeaseSession } from './lease-session.mjs';
import { CastInput, FlightNotices } from './cast-input.mjs';
import { FlightView } from './flight-view.mjs';
import { BiteView, StrikeInput } from './bite-view.mjs';
import { FightView, FightInput } from './fight-view.mjs';
import { ShopView, ShopInput, formatMinor } from './shop-view.mjs';

const documentId = crypto.randomUUID();
const authority = new LeaseSession(documentId);
let snapshot = null, revision = 0, hostPolicy = null, lastReply = null;
let lastActivation = null, lastActivationReply = null, lastRevoke = null;
let inflight = null, readOnly = true;
let castState = null, castRevision = 0;
const flightNotices = new FlightNotices();
const flightView = new FlightView(documentId);
const biteView = new BiteView(documentId);
const fightView = new FightView(documentId);
const shopView = new ShopView(documentId);
let commerceFaulted = false;
let speedKeyHeld = false;
const selected = new Set();
const $ = id => document.getElementById(id);
const send = message => window.chrome.webview.postMessage({ ...message, documentId });
const landscape = () => innerWidth > innerHeight;
const canControl = () => !readOnly && !commerceFaulted && landscape() && Boolean(authority.state().lease) && snapshot?.HasConfirmedState && !snapshot.Faulted && !snapshot.HostPaused && !snapshot.ShutdownConfirmed;
const canPrepare = () => canControl() && !inflight && !shopBusy() && !snapshot.SessionId && !snapshot.MenuOpen;
const castInput = new CastInput({ readContext: () => ({ lease: authority.state().lease, allowed: canPrepare() }), post: send, newTransportId: () => crypto.randomUUID() });
function strikeContext() {
  const bite = biteView.state();
  return { lease: authority.state().lease, allowed: canControl() && !inflight && !snapshot.MenuOpen && !snapshot.PendingResult &&
    Boolean(bite?.sessionId) && bite.sessionId === snapshot.SessionId && !castInput.state(),
    sessionId: bite?.sessionId, encounterId: bite?.encounterId, phase: bite?.phase };
}
const strikeInput = new StrikeInput({ readContext: strikeContext, post: send, newTransportId: () => crypto.randomUUID() });
function fightContext() {
  const fight = fightView.state();
  return { lease: authority.state().lease, allowed: canControl() && !inflight && !snapshot.MenuOpen && !snapshot.PendingResult &&
    Boolean(fight?.sessionId) && fight.sessionId === snapshot.SessionId,
    sessionId: fight?.sessionId, encounterId: fight?.encounterId, phase: fight?.phase, canRetryLanding: fight?.canRetryLanding };
}
const fightInput = new FightInput({ readContext: fightContext, post: send, newTransportId: () => crypto.randomUUID() });
function shopContext() {
  const shop = shopView.state();
  return { lease: authority.state().lease, allowed: canControl() && !inflight && Boolean(snapshot.MenuOpen) && !snapshot.SessionId && shop?.available === true,
    schemaVersion: shop?.schemaVersion, netOwned: shop?.netOwned, netEquipped: shop?.netEquipped, foodCount: shop?.foodCount, maxPacks: shop?.maxPacks };
}
const shopInput = new ShopInput({ readContext: shopContext, post: send, newTransportId: () => crypto.randomUUID() });
function shopBusy() { const s = shopInput.state(); return Boolean(s.inflight || s.awaitingState); }
const names = { carp: '鲤鱼', crucian: '鲫鱼', worm: '蚯蚓' };
function decimal(value, scale = 2) {
  if (typeof value !== 'string' || !/^\d+$/.test(value)) return '—';
  const full = value.padStart(scale + 1, '0');
  return `${full.slice(0, -scale)}.${full.slice(-scale)}`;
}
function render() {
  castInput.reconcile();
  strikeInput.reconcile();
  fightInput.reconcile();
  const previousShopLease = shopInput.state().lease;
  shopInput.reconcile();
  if (!shopContext().allowed || previousShopLease !== shopInput.state().lease) shopView.clearReferences();
  if (!fightContext().allowed) speedKeyHeld = false;
  $('status').textContent = snapshot?.Faulted || commerceFaulted ? '保存结果待核验，请退出后重新打开原存档。' : snapshot?.HostPaused ? '当前暂停' : readOnly ? '观赏窗口' : canControl() ? '交互已开启' : '请从窗口顶部开启交互';
  if (!snapshot?.HasConfirmedState) {
    $('stats').replaceChildren(); $('session').textContent = '尚无可确认的游戏进度'; $('creel').replaceChildren(); $('capacity').textContent = '';
  } else {
    $('stats').replaceChildren(...[
      `银币 ${decimal(snapshot.WalletMinor)}`, `体力 ${snapshot.Stamina}`, `${names[snapshot.BaitDefinitionKey] ?? snapshot.BaitDefinitionKey} ${snapshot.BaitCount} 份`,
    ].map(text => { const span = document.createElement('span'); span.textContent = text; return span; }));
    const pending = snapshot.PendingFish;
    $('session').textContent = snapshot.PendingResult && pending ? `鱼护已满：${names[pending.SpeciesKey] ?? pending.SpeciesKey} · ${decimal(pending.MassMg, 6)} kg 已保留，需明确放流后才能继续。` : snapshot.SessionId ? '这一竿已出手，请观察轨迹与浮漂。' : '可以出手';
    $('capacity').textContent = `${snapshot.CreelCount} / ${snapshot.CreelCapacity}`;
    const rows = snapshot.Creel ?? [];
    for (const id of selected) if (!rows.some(f => f.Id === id)) selected.delete(id);
    $('creel').replaceChildren(...rows.map(fish => {
      const item = document.createElement('li'); item.className = 'fish';
      const label = document.createElement('label'), checkbox = document.createElement('input'), text = document.createElement('span');
      checkbox.type = 'checkbox'; checkbox.checked = selected.has(fish.Id); checkbox.disabled = !canControl() || !snapshot.MenuOpen || Boolean(snapshot.SessionId) || Boolean(inflight);
      checkbox.addEventListener('change', () => { if (checkbox.checked) selected.add(fish.Id); else selected.delete(fish.Id); updateButtons(); });
      text.textContent = `${names[fish.SpeciesKey] ?? fish.SpeciesKey} · ${decimal(fish.MassMg, 6)} kg · ${decimal(fish.SaleValueMinor)} 银币`;
      label.append(checkbox, text); item.append(label); return item;
    }));
    if (!rows.length) { const item = document.createElement('li'); item.textContent = '鱼护为空'; $('creel').append(item); }
  }
  renderCast(); renderBite(); renderFight(); renderShop(); updateButtons();
}
function renderCast() {
  const labels = { idle: '尚未蓄力', charging: '正在蓄力，松开左键出手', prepared: '已松开，等待确认出手', committed: '已确认出手', cancelled: '蓄力已取消，重新按下才能开始', rejected: '本次出手未完成', unknown: '保存结果待核验，请退出后重新打开原存档' };
  const phase = castState?.phase ?? 'idle';
  $('cast-status').textContent = labels[phase] ?? '等待宿主确认';
  const power = ['charging', 'prepared', 'committed'].includes(phase) ? castState.powerPermille : 0;
  $('cast-power').value = power; $('cast-percent').textContent = `${(power / 10).toFixed(1)}%`;
  $('cast-target').setAttribute('aria-disabled', String(!canPrepare() && !strikeContext().allowed && !fightContext().allowed));
}
function renderFlight() {
  const graph = flightView.diagram(), state = flightView.state();
  const labels = { idle: '等待确认出手', flying: '钓组飞行中', waiting: '已落水，观察下方浮漂', invalid: '本次未进入有效水面', ended: '这一竿已结束' };
  const reasons = { flying: '飞行中', 'water-contact': '已接触水面', 'outside-water': '落在水域外', 'line-limit': '所需线长已达可用上限', 'flight-timeout': '飞行超时', ended: '已结束' };
  const redundant = { flying: 'flying', waiting: 'water-contact', ended: 'ended' };
  const reason = state?.reason && state.reason !== redundant[state.phase] ? reasons[state.reason] ?? '未能继续本次轨迹' : '';
  $('flight-status').textContent = `${labels[state?.phase ?? 'idle']}${reason ? ` · ${reason}` : ''}`;
  $('flight-marks').setAttribute('visibility', graph ? 'visible' : 'hidden');
  $('flight-clipped').hidden = !graph?.clipped;
  if (!graph) { $('flight-metrics').textContent = '距离 — · 所需线长 —'; return; }
  const setPoint = (element, p) => { element.setAttribute('cx', p.x.toFixed(2)); element.setAttribute('cy', p.y.toFixed(2)); };
  setPoint($('flight-origin'), graph.origin); setPoint($('flight-bobber'), graph.current);
  $('flight-bobber').setAttribute('fill', graph.phase === 'invalid' ? '#d47a66' : graph.phase === 'waiting' ? '#c4d985' : '#e8a85b');
  $('flight-trail').setAttribute('points', graph.recent.map(p => `${p.x.toFixed(2)},${p.y.toFixed(2)}`).join(' '));
  const line = $('flight-tether');
  for (const [attribute, value] of Object.entries({ x1: graph.origin.x, y1: graph.origin.y, x2: graph.current.x, y2: graph.current.y })) line.setAttribute(attribute, value.toFixed(2));
  $('flight-metrics').textContent = `距离 ${(graph.distanceMm / 1000).toFixed(2)} 米 · 所需线长 ${(graph.lineUsedMm / 1000).toFixed(2)} 米 · 活动 ${(graph.elapsedMilliseconds / 1000).toFixed(2)} 秒`;
}
function renderBite() {
  const state = biteView.state(), diagram = biteView.diagram();
  const labels = { idle: '有效落水后开始观察浮漂', settling: '钓组正在沉稳', searching: '静候鱼口', approaching: '留意浮漂变化',
    nibbling: '浮漂轻动，鱼仍在试探', strikeable: '浮漂明显下沉', hooked: '已中鱼，请手动收线',
    'retrieve-required': '请收回这一竿，重新挂饵', ended: '这一竿已结束' };
  const reasons = { 'strike-too-early': '提竿过早，未能中鱼', 'strike-too-late': '鱼口已错过', 'empty-strike': '本次空提',
    'bite-missed': '错过鱼口', 'bait-depleted': '钩饵已经脱落', 'environment-interrupted': '当前水域条件已变化', 'environment-unavailable': '当前水域条件不可用' };
  $('bite-status').textContent = labels[state?.phase ?? 'idle'] ?? '等待浮漂状态';
  $('bite-detail').textContent = reasons[state?.reason] ?? (state?.phase === 'hooked' ? '按住左键收线，滚轮调泄力；留意下方遛鱼状态。' : '右键提竿；过早提竿可能空提。浮漂变化由宿主活动时间推进。');
  $('bite-bait').textContent = !state || state.phase === 'idle' || state.phase === 'ended' ? '钩饵 —' : state.baitAttached ? '钩饵：在钩上' : '钩饵：已用尽';
  $('bite-marks').setAttribute('visibility', diagram ? 'visible' : 'hidden');
  if (diagram) {
    $('bite-float').setAttribute('transform', `translate(${diagram.x.toFixed(2)} ${diagram.y.toFixed(2)})`);
    $('bite-float').setAttribute('opacity', diagram.submerged ? '.55' : '1');
  }
}
function renderFight() {
  const state = fightView.state(), graph = fightView.diagram();
  const labels = { idle: '中鱼后开始遛鱼', fighting: '遛鱼中', 'landing-eligible': '已进入可抄范围，按空格抄鱼',
    'landing-pending': '正在确认收鱼结果', landed: '已确认收鱼入护', 'capacity-blocked': '鱼护已满，请明确放流当前鱼',
    faulted: '遛鱼已暂停，当前结果未确认', ended: '本次遛鱼已结束' };
  const reasons = { 'fish-too-far': '鱼距岸还远', 'fish-too-deep': '鱼所在水层超出抄网范围', 'fish-moving': '鱼还在快速游动', 'fish-active': '鱼仍有较多体力',
    'net-unavailable': '没有可用抄网', eligible: '可抄鱼', 'landing-not-written': '尚未写入，点击下方按钮重试原收鱼结果',
    'landing-save-unknown': '保存结果待核验，请退出后重新打开原存档', 'solver-unresolved': '当前受力求解超出支持范围，请收回这一竿',
    'load-outside-supported-range': '当前负载超出简化模型支持范围，请收回这一竿', 'speed-outside-supported-range': '当前速度超出简化模型支持范围，请收回这一竿' };
  $('fight-status').textContent = labels[state?.phase ?? 'idle'];
  $('fight-detail').textContent = state?.phase === 'faulted' || state?.phase === 'landing-pending' ? reasons[state.reason] ?? '等待宿主确认，不能重复发鱼。' :
    state && ['fighting', 'landing-eligible'].includes(state.phase) ? `${state.reeling ? '正在主动收线' : '尚未主动收线'} · ${reasons[state.landingReason] ?? '观察鱼的位置与线张力'}` : '左键按住收线；松开停止绞入，鱼仍可能向外拉线。';
  $('fight-marks').setAttribute('visibility', graph ? 'visible' : 'hidden');
  $('fight-clipped').hidden = !graph?.clipped;
  $('fight-energy').value = state?.energyPermille ?? 0;
  $('fight-energy-value').textContent = state && state.phase !== 'idle' ? `${(state.energyPermille / 10).toFixed(1)}%` : '—';
  $('fight-reeling').textContent = state?.reeling ? '收线中' : '未收线';
  $('drag-level').textContent = `${state?.dragLevel ?? 0} / 10`;
  $('reel-level').textContent = `${state?.reelLevel ?? 0} / 10`;
  if (!graph) { $('fight-metrics').textContent = '出线 — · 线杯余量 — · 张力 —'; return; }
  $('fight-fish').setAttribute('cx', graph.x.toFixed(2)); $('fight-fish').setAttribute('cy', graph.y.toFixed(2));
  $('fight-line').setAttribute('x2', graph.x.toFixed(2)); $('fight-line').setAttribute('y2', graph.y.toFixed(2));
  $('fight-fish').setAttribute('fill', state.phase === 'landing-eligible' ? '#c4d985' : state.phase === 'faulted' ? '#d47a66' : '#e8a85b');
  $('fight-metrics').textContent = `距岸 ${(graph.distanceMm / 1000).toFixed(2)} 米 · 深度 ${(graph.depthMm / 1000).toFixed(2)} 米 · 出线 ${(state.outLineMm / 1000).toFixed(2)} 米 · 线杯余量 ${(state.spoolRemainingMm / 1000).toFixed(2)} 米 · 松线 ${(state.slackMm / 1000).toFixed(2)} 米 · 张力 ${(state.tensionMilliN / 1000).toFixed(2)} N · 泄力 ${(state.dragMilliN / 1000).toFixed(2)} N`;
}
function renderShop() {
  const state = shopView.state(), input = shopInput.state();
  $('shop').hidden = !snapshot?.MenuOpen;
  const reasons = { 'commerce-upgrade-required': '这份旧版进度尚未启用商店。请先生成并核验升级副本，原档不会自动改动。',
    'control-required': '请从窗口顶部开启交互后购买或使用物品。', 'session-busy': '请先收回当前一竿或处理当前鱼，再购买、换装和进食。',
    'save-outcome-unknown': '保存结果待核验，已停止交易。请退出后重新打开原存档。', 'owner-stopped': '进度已关闭。' };
  $('shop-status').textContent = commerceFaulted ? reasons['save-outcome-unknown'] : shopBusy() ? '正在等待宿主确认，请勿重复操作。' :
    state?.reason ? reasons[state.reason] ?? '商店暂时不可用。' : !canControl() ? reasons['control-required'] : input.retry ? '上次操作确认未写入。可以重试原操作，或取消。' : '先获取报价，再确认购买；所有变化以保存结果为准。';
  $('shop-wallet').textContent = `余额 ${formatMinor(state?.walletMinor)} 银币`;
  $('shop-net-price').textContent = `${formatMinor(state?.netPriceMinor)} 银币 / 件`;
  $('shop-net-owned').textContent = state?.netEquipped ? '已拥有 · 已装备' : state?.netOwned ? '已拥有 · 未装备' : '尚未拥有';
  $('shop-bait-price').textContent = `${formatMinor(state?.baitPackPriceMinor)} 银币 / 包${state?.baitPackUnits ? ` · 每包 ${state.baitPackUnits} 份` : ''}`;
  $('shop-bait-count').textContent = `现有 ${state?.baitCount ?? 0} 份`;
  $('shop-food-price').textContent = `${formatMinor(state?.foodPriceMinor)} 银币 / 份${state?.foodStaminaRestore ? ` · 每份恢复最多 ${state.foodStaminaRestore} 体力` : ''}`;
  $('shop-food-count').textContent = `现有 ${state?.foodCount ?? 0} 份`;
  for (const id of ['bait-packs', 'food-packs']) $(id).max = String(state?.maxPacks || 1);
  $('shop-quote').hidden = !input.quote;
  if (input.quote) {
    const q = input.quote, product = { net: '抄网', bait: '蚯蚓鱼饵', food: '面包' }[q.product];
    $('shop-quote-text').textContent = `${product} · ${q.packs} ${q.product === 'bait' ? '包' : '份'}（共 ${q.units} ${q.product === 'net' ? '件' : '份'}）· 合计 ${formatMinor(q.totalPriceMinor)} 银币。确认后才购买。`;
  } else $('shop-quote-text').textContent = '';
  $('shop-retry').hidden = !input.retry;
  $('shop-retry-text').textContent = input.retry ? `上次${{ buy: '购买', equip: '装备调整', eat: '食用' }[input.retry.action]}确认未写入；重试沿用原操作，不会换成新的购买。` : '';
  $('shop-cancel').hidden = !input.quote && !input.retry;
}
function updateButtons() {
  const active = canControl() && !inflight && !shopBusy();
  const input = castInput.state();
  $('cast').disabled = !canPrepare() || Boolean(input);
  $('cast-release').disabled = !canPrepare() || input?.phase !== 'charging' || input.source !== 'button';
  $('cast-cancel').disabled = !canControl() || !input;
  const strike = strikeContext();
  $('strike').disabled = !strike.allowed || !['settling', 'searching', 'approaching', 'nibbling', 'strikeable'].includes(strike.phase) || Boolean(strikeInput.state().pending);
  const fight = fightContext(), fighting = fight.allowed && ['fighting', 'landing-eligible'].includes(fight.phase);
  $('reel-hold').disabled = !fighting;
  for (const id of ['drag-minus', 'drag-plus', 'reel-minus', 'reel-plus']) $(id).disabled = !fighting;
  const landingPending = fightInput.state().pending.some(p => ['land', 'retry-land'].includes(p.action) && p.sessionId === fight.sessionId && p.encounterId === fight.encounterId && p.lease === fight.lease);
  $('land').disabled = !fight.allowed || fight.phase !== 'landing-eligible' || landingPending;
  $('retry-land').hidden = !fight.canRetryLanding;
  $('retry-land').disabled = !fight.allowed || fight.phase !== 'landing-pending' || !fight.canRetryLanding || landingPending;
  $('recall').disabled = !active || !snapshot.SessionId || snapshot.PendingResult;
  $('release').disabled = !active || !snapshot.PendingResult;
  $('menu').disabled = !active;
  $('menu').textContent = snapshot?.MenuOpen ? '关闭鱼护与商店' : '打开鱼护与商店';
  $('sell').disabled = !active || !snapshot.MenuOpen || Boolean(snapshot.SessionId) || !selected.size;
  $('previous').disabled = !snapshot?.HasConfirmedState || snapshot.CreelOffset <= 0;
  $('next').disabled = !snapshot?.HasConfirmedState || snapshot.CreelOffset + (snapshot.Creel?.length ?? 0) >= snapshot.CreelCount;
  const shop = shopContext(), shopState = shopInput.state(), shopping = shop.allowed && shopState.ready && !shopBusy() && !shopState.faulted;
  const newShopAction = shopping && !shopState.retry;
  $('quote-net').disabled = !newShopAction || shop.netOwned;
  $('quote-bait').disabled = !newShopAction; $('quote-food').disabled = !newShopAction;
  $('bait-packs').disabled = !newShopAction; $('food-packs').disabled = !newShopAction;
  $('net-equip').disabled = !newShopAction || !shop.netOwned || shop.netEquipped;
  $('net-unequip').disabled = !newShopAction || !shop.netOwned || !shop.netEquipped;
  $('food-eat').disabled = !newShopAction || shop.foodCount < 1;
  $('shop-buy').disabled = !shopping || !shopState.quote;
  $('shop-retry-confirm').disabled = !shopping || !shopState.retry;
  $('shop-cancel').disabled = !shopping || !shopState.quote && !shopState.retry;
}
function revoke(reason) {
  castInput.cancel();
  fightInput.cancel(); speedKeyHeld = false;
  const cancellation = authority.cancel();
  send({ kind: 'revoke', ...cancellation, reason, activationRequestId: lastActivation?.requestId ?? '', transportId: crypto.randomUUID() });
  render();
}
function intent(action) {
  if (!canControl() || inflight || shopBusy()) return;
  fightInput.cancel();
  const command = { action, requestId: crypto.randomUUID(), eventId: crypto.randomUUID(), sessionId: action === 'sell' ? '' : snapshot.SessionId,
    expectedSequence: snapshot.Sequence, rigRevision: snapshot.RigRevision, rulesVersion: snapshot.RulesVersion };
  if (action === 'sell') command.fishIds = [...selected];
  inflight = crypto.randomUUID();
  send({ kind: 'game-intent', lease: authority.state().lease, transportId: inflight, intent: command });
  render();
}
const codes = { 'cast-candidate': '这一竿已保存，已使用一份鱼饵。', 'recall-candidate': '已收回，扣饵保持。', 'released-candidate': '当前鱼已放流。', 'sale-candidate': '出售已确认。', 'menu-updated': '菜单状态已更新。', 'control-required': '请重新开启交互。', 'invalid-intent': '请求未识别。', 'version-conflict': '进度已更新，请重新操作。', 'bait-empty': '鱼饵已用完。', 'stamina-insufficient': '体力不足。', 'session-busy': '请先结束当前一竿。' };
Object.assign(codes, { charging: '已开始蓄力，松开出手。', prepared: '已松开，等待宿主确认。', cancelled: '蓄力已取消。', 'stale-input': '旧操作已忽略，请重新按下。', 'gesture-not-current': '本次操作已结束，请重新按下。', 'domain-paused': '当前暂停，请恢复后重新按下。', 'cast-preparation-required': '请先按住蓄力，再松开出手。', 'save-outcome-unknown': '保存结果待核验，请退出后重新打开原存档。' });
Object.assign(codes, { hooked: '已中鱼，请手动收线。', 'strike-too-early': '提竿过早，请收回后重新挂饵。', 'strike-too-late': '已错过鱼口，请收回后重试。',
  'empty-strike': '本次空提，请收回后重新挂饵。', 'encounter-not-current': '这次鱼口已变化，请观察当前浮漂。', 'bite-not-ready': '钓组尚未进入有效等鱼状态。', 'bite-not-active': '当前不能提竿。', 'session-not-current': '这一竿已变化，旧操作已忽略。' });
Object.assign(codes, { 'reel-held': '开始手动收线。', 'reel-released': '已停止主动收线。', 'drag-updated': '泄力已调整。', 'speed-updated': '收线速度已调整。',
  'fight-not-current': '本次遛鱼已变化，旧操作已忽略。', 'fight-not-active': '当前不能继续收线。', 'fight-not-ready': '当前还未进入遛鱼。',
  'fish-too-far': '鱼还没有进入抄网范围。', 'fish-too-deep': '鱼所在水层还不能抄鱼。', 'fish-moving': '鱼还在快速游动。', 'fish-active': '鱼还没有疲劳到可抄状态。',
  'net-unavailable': '没有可用抄网。', 'landing-pending': '正在确认收鱼结果。', landed: '已确认收鱼入护。', 'landed-candidate': '已确认收鱼入护。', 'creel-capacity-exceeded': '鱼护已满，请明确放流当前鱼。',
  'landing-not-written': '本次结果尚未写入，可重试原收鱼结果。', 'landing-retry-unavailable': '当前不能重试收鱼。', 'landing-save-unknown': '保存结果待核验，请退出后重新打开原存档。' });
Object.assign(codes, { 'shop-quoted': '报价已确认，请检查总价后选择购买。', 'purchase-candidate': '购买已确认，物品已入背包。', 'equip-candidate': '抄网装备状态已保存。', 'food-candidate': '已食用一份食品，体力变化已保存。',
  'shop-cancelled': '本次报价或未写入操作已取消。', 'shop-not-written': '操作确认未写入，可重试原操作或取消。', 'shop-retry-required': '请先重试或取消上次未写入的操作。', 'shop-intent-not-current': '报价或操作已失效，请重新获取报价。',
  'commerce-upgrade-required': '旧版进度需先核验升级副本才能使用商店。', 'wallet-insufficient': '银币不足。', 'net-not-owned': '请先购买抄网。', 'net-already-owned': '已拥有抄网。', 'net-equipment-unchanged': '抄网装备状态没有变化。',
  'food-empty': '食品已用完。', 'stamina-full': '体力已满，本次没有消耗食品。', 'bait-limit-exceeded': '鱼饵数量已达到上限。', 'food-limit-exceeded': '食品数量已达到上限。', 'quote-stale': '进度已变化，请重新获取报价。', 'save-capacity': '保存容量已满，未执行本次交易。' });
window.chrome.webview.addEventListener('message', event => {
  const m = event.data; if (!m || m.documentId !== documentId) return;
  if (m.kind === 'game-flight-state') {
    // Host positions can arrive frequently. Update the diagram alone; neither
    // rebuild the creel nor start an independent page animation clock.
    if (flightView.apply(m)) renderFlight();
    return;
  }
  if (m.kind === 'game-bite-state') {
    if (biteView.apply(m)) { strikeInput.reconcile(); renderBite(); renderCast(); updateButtons(); }
    return;
  }
  if (m.kind === 'game-fight-state') {
    if (fightView.apply(m)) { fightInput.reconcile(); renderFight(); renderCast(); updateButtons(); }
    return;
  }
  if (m.kind === 'game-shop-state') {
    if (shopView.apply(m)) {
      if (m.state.reason === 'save-outcome-unknown') commerceFaulted = true;
      shopInput.hostState(shopView.state()); renderShop(); updateButtons();
    }
    return;
  }
  if (m.kind === 'prepare-activation') {
    if (!landscape() || (!m.allowUnfocused && (document.hidden || !document.hasFocus()))) return;
    lastActivation = { requestId: m.requestId };
    send({ kind: 'activation-ready', requestId: m.requestId, cancellationEpoch: authority.state().epoch, lease: '', transportId: m.requestId });
  }
  if (m.kind === 'lease') {
    const before = authority.state().revision, accepted = authority.apply(m);
    if (!accepted && m.lease && authority.state().revision === m.revision && m.revision > before)
      send({ kind: 'revoke', lease: m.lease, cancellationEpoch: authority.state().epoch, transportId: crypto.randomUUID(), reason: 'late-grant' });
  }
  if (m.kind === 'game-snapshot' && Number.isSafeInteger(m.revision) && m.revision > revision) { snapshot = m.snapshot; revision = m.revision; }
  if (m.kind === 'game-reply') {
    lastReply = m; castInput.reply(m); strikeInput.reply(m); fightInput.reply(m); shopInput.reply(m);
    if (m.code === 'save-outcome-unknown') commerceFaulted = true;
    if (inflight === m.transportId) inflight = null; $('notice').textContent = codes[m.code] ?? `操作未完成：${m.code}`;
  }
  if (m.kind === 'game-cast-state' && Number.isSafeInteger(m.revision) && m.revision > castRevision &&
      ['idle', 'charging', 'prepared', 'committed', 'cancelled', 'rejected', 'unknown'].includes(m.phase) &&
      Number.isInteger(m.powerPermille) && m.powerPermille >= 0 && m.powerPermille <= 1000 &&
      Number.isInteger(m.elapsedMilliseconds) && m.elapsedMilliseconds >= 0) {
    castState = m; castRevision = m.revision; castInput.hostState(m);
  }
  if (m.kind === 'game-flight' && flightNotices.apply(m)) {
    $('notice').textContent = '已确认出手并保存消耗。下方显示固定平面的飞行与落水状态。';
  }
  if (m.kind === 'activation-reply') lastActivationReply = m;
  if (m.kind === 'revoke-reply') lastRevoke = m;
  if (m.kind === 'role') { readOnly = m.role.includes('只读'); document.body.dataset.role = readOnly ? 'observer' : 'interaction'; }
  if (m.kind === 'host-policy' && (!hostPolicy || m.revision >= hostPolicy.revision)) hostPolicy = m;
  render();
});
$('cast').addEventListener('click', () => { castInput.begin(); render(); });
$('cast-release').addEventListener('click', () => { castInput.release(); render(); });
$('cast-cancel').addEventListener('click', () => { castInput.cancel(); render(); });
$('strike').addEventListener('click', () => { strikeInput.strike(); updateButtons(); });
$('land').addEventListener('click', () => { fightInput.land(); updateButtons(); });
$('retry-land').addEventListener('click', () => { fightInput.retryLanding(); updateButtons(); });
function changeFightLevel(action, direction) {
  const state = fightView.state(); if (!state) return false;
  const value = action === 'drag' ? state.dragLevel : state.reelLevel;
  const next = Math.max(0, Math.min(10, value + direction));
  return next !== value && fightInput.adjust(action, next);
}
for (const [id, action, direction] of [['drag-minus', 'drag', -1], ['drag-plus', 'drag', 1], ['reel-minus', 'speed', -1], ['reel-plus', 'speed', 1]])
  $(id).addEventListener('click', () => changeFightLevel(action, direction));
$('recall').addEventListener('click', () => intent('recall'));
$('release').addEventListener('click', () => intent('release-current'));
$('sell').addEventListener('click', () => intent('sell'));
$('menu').addEventListener('click', () => {
  if (!canControl() || inflight || shopBusy()) return;
  castInput.cancel();
  fightInput.cancel(); speedKeyHeld = false;
  if (snapshot.MenuOpen) shopInput.cancel();
  inflight = crypto.randomUUID(); send({ kind: 'game-menu', lease: authority.state().lease, transportId: inflight, open: !snapshot.MenuOpen }); render();
});
function requestShopQuote(product) {
  const packs = product === 'net' ? 1 : $(product === 'bait' ? 'bait-packs' : 'food-packs').valueAsNumber;
  if (!Number.isInteger(packs) || packs < 1 || packs > (shopView.state()?.maxPacks ?? 0)) { $('notice').textContent = '请输入当前上限以内的整数数量。'; return; }
  shopInput.quote(product, packs); renderShop(); updateButtons();
}
for (const [id, action] of [
  ['quote-net', () => requestShopQuote('net')], ['quote-bait', () => requestShopQuote('bait')], ['quote-food', () => requestShopQuote('food')],
  ['shop-buy', () => shopInput.buy()], ['shop-cancel', () => shopInput.cancel()], ['shop-retry-confirm', () => shopInput.retry()],
  ['net-equip', () => shopInput.equip(true)], ['net-unequip', () => shopInput.equip(false)], ['food-eat', () => shopInput.eat()],
]) {
  $(id).addEventListener('click', event => { if (event.detail > 1) return; action(); renderShop(); updateButtons(); });
  $(id).addEventListener('keydown', event => { if (event.repeat && ['Space', 'Enter'].includes(event.code)) event.preventDefault(); });
}
function page(offset) { send({ kind: 'game-page', lease: '', transportId: crypto.randomUUID(), offset }); }
$('previous').addEventListener('click', () => page(Math.max(0, snapshot.CreelOffset - 50)));
$('next').addEventListener('click', () => page(snapshot.CreelOffset + 50));
const target = $('cast-target');
const reelPointers = new Map(), reelKeys = new Map();
target.addEventListener('pointerdown', event => {
  if (event.pointerType !== 'mouse') return;
  if (event.button === 0 && ['fighting', 'landing-eligible'].includes(fightContext().phase)) {
    beginReelPointer(event, target); return;
  }
  const changed = castInput.pointer(event.pointerId, event.buttons, true);
  if (changed) {
    event.preventDefault();
    if (castInput.state()?.source === `pointer:${event.pointerId}`) {
      try { target.setPointerCapture(event.pointerId); } catch { castInput.pointerCancelled(event.pointerId); }
    }
    render();
  }
});
target.addEventListener('pointermove', event => { if (!(event.buttons & 1)) endReelPointer(event); if (event.pointerType === 'mouse' && castInput.pointer(event.pointerId, event.buttons)) render(); });
target.addEventListener('pointerup', event => { endReelPointer(event); if (event.pointerType === 'mouse' && castInput.pointer(event.pointerId, event.buttons)) render(); });
target.addEventListener('pointercancel', event => { endReelPointer(event); if (castInput.pointerCancelled(event.pointerId)) render(); });
target.addEventListener('lostpointercapture', event => { endReelPointer(event); if (castInput.pointerLost(event.pointerId)) render(); });
target.addEventListener('contextmenu', event => { event.preventDefault(); if (castInput.cancel()) render(); });
for (const element of [target, $('bite-diagram')]) {
  // mousedown fires for each newly pressed button, including a right-button
  // chord that Pointer Events reports as pointermove. Preparation cancellation
  // has priority and this physical press is then marked held, never buffered.
  element.addEventListener('mousedown', event => {
    if (event.button !== 2) return;
    event.preventDefault();
    if (castInput.state()) { castInput.cancel(); strikeInput.mouseButtons(event.buttons); render(); return; }
    if (strikeInput.mouseDown(event.button, event.buttons)) updateButtons();
  });
}
$('bite-diagram').addEventListener('contextmenu', event => event.preventDefault());
function beginReelPointer(event, element) {
  if (event.button !== 0 || !(event.buttons & 1) || !fightInput.begin(`pointer:${event.pointerId}`)) return;
  const held = fightInput.state().held;
  reelPointers.set(event.pointerId, { element, at: event.timeStamp, startTransportId: held.startTransportId });
  event.preventDefault();
  try { element.setPointerCapture(event.pointerId); } catch { fightInput.end(`pointer:${event.pointerId}`, held.startTransportId); reelPointers.delete(event.pointerId); }
  updateButtons();
}
function endReelPointer(event) {
  const held = reelPointers.get(event.pointerId);
  if (!held || event.timeStamp < held.at || event.type === 'lostpointercapture' && held.element.hasPointerCapture(event.pointerId)) return;
  fightInput.end(`pointer:${event.pointerId}`, held.startTransportId); reelPointers.delete(event.pointerId);
}
for (const element of [$('fight-diagram'), $('reel-hold')]) {
  element.addEventListener('pointerdown', event => beginReelPointer(event, element));
  element.addEventListener('pointermove', event => { if (!(event.buttons & 1)) endReelPointer(event); });
  for (const type of ['pointerup', 'pointercancel', 'lostpointercapture']) element.addEventListener(type, endReelPointer);
  element.addEventListener('contextmenu', event => event.preventDefault());
}
// Keyboard fallback for the hold button: no click toggle and no repeated down.
$('reel-hold').addEventListener('keydown', event => {
  if (['Space', 'Enter'].includes(event.code)) {
    event.preventDefault();
    if (!event.repeat && fightInput.begin(`key:${event.code}`)) reelKeys.set(event.code, { at: event.timeStamp, startTransportId: fightInput.state().held.startTransportId });
  }
});
$('reel-hold').addEventListener('keyup', event => {
  if (!['Space', 'Enter'].includes(event.code)) return; event.preventDefault();
  const held = reelKeys.get(event.code); if (!held || event.timeStamp < held.at) return;
  fightInput.end(`key:${event.code}`, held.startTransportId); reelKeys.delete(event.code);
});
$('reel-hold').addEventListener('blur', () => fightInput.cancel());
for (const element of [target, $('fight-diagram')]) element.addEventListener('wheel', event => {
  if (!fightContext().allowed || !['fighting', 'landing-eligible'].includes(fightContext().phase) || event.deltaY === 0) return;
  event.preventDefault(); changeFightLevel(speedKeyHeld ? 'speed' : 'drag', event.deltaY < 0 ? 1 : -1);
}, { passive: false });
window.addEventListener('pointerup', endReelPointer);
window.addEventListener('mouseup', event => strikeInput.mouseButtons(event.buttons));
window.addEventListener('mousemove', event => strikeInput.mouseButtons(event.buttons));
window.addEventListener('blur', () => revoke('blur'));
window.addEventListener('pagehide', () => revoke('pagehide'));
window.addEventListener('resize', () => { if (!landscape()) revoke('portrait'); render(); });
document.addEventListener('visibilitychange', () => { if (document.hidden) revoke('hidden'); });
document.addEventListener('keydown', event => {
  if (event.key === 'Escape') { revoke('escape'); return; }
  if (event.target instanceof Element && (event.target.isContentEditable || event.target.closest('input,textarea,select,button'))) return;
  if (event.code === 'KeyR' && !event.repeat && fightContext().allowed) speedKeyHeld = true;
  if (event.code === 'Space' && !event.repeat && fightContext().allowed) { event.preventDefault(); fightInput.land(); updateButtons(); }
});
document.addEventListener('keyup', event => { if (event.code === 'KeyR') speedKeyHeld = false; });
// No page render/simulation loop: the SVG only projects host snapshots.
const visual = { start() {}, stop() {}, pause() {}, resume() {}, state: () => ({ running: false, frames: 0, kind: 'host-flight-projection' }) };
window.dualProbe = { documentId, visual, state: () => ({ documentId, ...authority.state(), snapshot, revision, lastReply, lastActivationReply, lastRevoke, hostPolicy, castState, lastFlight: flightNotices.last, flightCount: flightNotices.count, inputState: castInput.state(), flightState: flightView.state(), flightRevision: flightView.revision,
  biteState: biteView.state(), biteRevision: biteView.revision, strikeInputState: strikeInput.state(),
  fightState: fightView.state(), fightRevision: fightView.revision, fightInputState: fightInput.state(),
  shopState: shopView.state(), shopRevision: shopView.revision, shopInputState: shopInput.state() }),
  sendRaw: message => window.chrome.webview.postMessage(message), cancel: () => revoke('test-cancel') };
render(); renderFlight();

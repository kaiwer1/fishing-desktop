import { LeaseSession } from './lease-session.mjs';
import { CastInput, FlightNotices } from './cast-input.mjs';
import { FlightView } from './flight-view.mjs';
import { BiteView, StrikeInput } from './bite-view.mjs';
import { FightView, FightInput } from './fight-view.mjs';
import { ShopView, ShopInput, formatMinor } from './shop-view.mjs';
import { EquipmentView, EquipmentInput } from './equipment-view.mjs';
import { EquipmentPanel } from './equipment-panel.mjs';
import { SparesView, SparesInput } from './spares-view.mjs';
import { SparesPanel } from './spares-panel.mjs';
import { CampView, CampInput } from './camp-view.mjs';
import { CampPanel } from './camp-panel.mjs';
import { WaterView } from './water-view.mjs';
import { pickAim } from './water-aim.mjs';
import { ScenePresentation } from './scene-presentation.mjs';
import { SpotMenu } from './spot-menu.mjs';
import { Wardrobe, wardrobeMessages } from './wardrobe.mjs';
import { fishName, fishFigure, installFishIllustrations, newCreelCatch } from './fish-illustration.mjs';
import { createSpecimenView } from './specimen-view.mjs';
import { AnglerLog, createAnglerLogPanel } from './angler-log.mjs';
import { BankWalk } from './bank-walk.mjs';
import { BottomView, bottomPrompt, createBottomTipPanel } from './bottom-view.mjs';
import { BottomShop, BottomStrike, confirmedLineEnd } from './bottom-controls.mjs';
import { StandView, createStandPanel } from './stand-view.mjs';
import { BankKit, bindBankShop } from './bank-kit.mjs';
import { LureView, createLurePanel, lureMetrics, lurePrompt } from './lure-view.mjs';
import { LureShop, LureInput } from './lure-controls.mjs';
import { WaterConditions, showWaterConditions } from './water-conditions.mjs';

const documentId = crypto.randomUUID();
const authority = new LeaseSession(documentId);
let snapshot = null, revision = 0, hostPolicy = null, lastReply = null;
let lastActivation = null, lastActivationReply = null, lastRevoke = null;
let inflight = null, readOnly = true;
let spotRequest = null;
let campOpening = null, campOpenShown = false;
let castState = null, castRevision = 0;
const flightNotices = new FlightNotices();
const flightView = new FlightView(documentId);
const biteView = new BiteView(documentId);
const bottomView = new BottomView(documentId);
const lureView = new LureView(documentId);
let bottomLineEnd = null, bottomPanel = null;
const fightView = new FightView(documentId);
const shopView = new ShopView(documentId);
const equipmentView = new EquipmentView(documentId);
const sparesView = new SparesView(documentId);
const campView = new CampView(documentId);
const waterView = new WaterView(documentId);
const conditionsView=new WaterConditions(documentId);
const anglerLog = new AnglerLog(documentId);
let selectedAim = null;
let saveFaulted = false;
let speedKeyHeld = false;
const selected = new Set();
const $ = id => document.getElementById(id);
installFishIllustrations(document, $('fish-symbol-bank'));
let catchTimer = null;
let specimen = null;
const send = message => window.chrome.webview.postMessage({ ...message, documentId });
const landscape = () => innerWidth > innerHeight;
const canControl = () => !readOnly && !saveFaulted && landscape() && Boolean(authority.state().lease) && snapshot?.HasConfirmedState && !snapshot.Faulted && !snapshot.HostPaused && !snapshot.ShutdownConfirmed;
const freeAtBank = () => canControl() && !inflight && !spotRequest && !gearBusy() && !wardrobe.busy && currentSpotWater() && !shopBusy() && !equipmentBusy() && !sparesBusy() && !campBusy() && !campEngaged() && !snapshot.MenuOpen;
const canPrepare = () => {
  if (!freeAtBank() || heldSession()) return false;
  if (lureMethod()) return waterView.state()?.waterId==='coastal-bay' && !bankKit.selected && !bottomMethod();
  if (!bankKit.selected) return true;
  const hand = stand.state()?.heldRig;
  return handReady() && (hand.phase === 'idle' || (hand.phase === 'charging' && Boolean(castInput.state())));
};
const castInput = new CastInput({ readContext: () => {
  const water = waterView.state();
  return { lease:authority.state().lease, allowed:canPrepare() && bankWalk.canCast(water),
    requiresAim:water?.enabled === true, geometryVersion:water?.geometryVersion, poseRevision:bankWalk.forWater(water)?.poseRevision, bankHand:handGeneration() };
}, post: send, newTransportId: () => crypto.randomUUID() });
function buttonAim() {
  const water = waterView.state();
  if (!water?.enabled) return null;
  const remembered = selectedAim?.geometryVersion === water.geometryVersion;
  return bankWalk.aim(water, { u:remembered ? selectedAim.u : 5000, v:remembered ? selectedAim.v : 5000, geometryVersion:water.geometryVersion });
}
function strikeContext() {
  const bite = biteView.state();
  return { lease: authority.state().lease, allowed: !lureMethod() && !bottomMethod() && canControl() && !inflight && !snapshot.MenuOpen && !snapshot.PendingResult &&
    Boolean(bite?.sessionId) && bite.sessionId === snapshot.SessionId && !castInput.state(),
    sessionId: bite?.sessionId, encounterId: bite?.encounterId, phase: bite?.phase };
}
const strikeInput = new StrikeInput({ readContext: strikeContext, post: send, newTransportId: () => crypto.randomUUID() });
const bottomStrike = new BottomStrike({ documentId, post:send, newTransportId:() => crypto.randomUUID(), readContext:() => ({
  allowed:handReady() && canControl() && !inflight && !snapshot.MenuOpen && !snapshot.PendingResult && !castInput.state() && !document.hidden,
  sessionId:heldSession(), bottom:currentBottom(), lease:authority.state().lease, bankHand:handGeneration() }) });
function heldSession(){return bankKit.selected ? stand.state()?.heldRig?.sessionId??null : snapshot?.SessionId;}
function handGeneration(){return bankKit.selected ? stand.state()?.handGeneration : undefined;}
function handReady(){
  if(!bankKit.selected)return true;
  const s=stand.state();
  return Boolean(s?.heldRig && !s.pending && !stand.pending && (snapshot?.SessionId??null)===s.heldRig.sessionId);
}
function currentBottom() { return bottomView.forSession(heldSession()); }
function bottomMethod() { return bankKit.selected || bottomShop.state()?.method === 'bottom' || Boolean(currentBottom()); }
function lureMethod() { return lureShop.state()?.selected===true; }
function currentLure() { return lureMethod() ? lureView.forSession(heldSession()) : null; }
function canRecallLure() {
  const lure=currentLure();
  return !lure || lure.phase==='hooked' || (lure.phase==='retrieve-pending' && lureView.canRetryRetrieve);
}
function lureContext() {
  const sessionId=heldSession(), lure=currentLure();
  return {lease:authority.state().lease,sessionId,lure,allowed:canControl() && !inflight && !snapshot.MenuOpen &&
    !snapshot.PendingResult && !document.hidden && document.hasFocus() && !visualPaused && hostPolicy?.canRun!==false &&
    !bottomMethod() && waterView.state()?.waterId==='coastal-bay' && !castInput.state()};
}
const lureInput=new LureInput({documentId,readContext:lureContext,post:send,newTransportId:()=>crypto.randomUUID()});
function fightContext() {
  const fight = fightView.state();
  return { lease: authority.state().lease, bankHand:handGeneration(), allowed: handReady() && canControl() && !inflight && !snapshot.MenuOpen && !snapshot.PendingResult &&
    Boolean(fight?.sessionId) && fight.sessionId === heldSession(),
    sessionId: fight?.sessionId, encounterId: fight?.encounterId, phase: fight?.phase, canRetryLanding: fight?.canRetryLanding, canRetryFailure: fight?.canRetryFailure };
}
const fightInput = new FightInput({ readContext: fightContext, post: send, newTransportId: () => crypto.randomUUID() });
function failurePending() {
  const fight = fightView.state();
  return fight?.phase === 'failure-pending' && fight.sessionId === heldSession();
}
function shopContext() {
  const shop = shopView.state();
  return { lease: authority.state().lease, allowed: canControl() && !inflight && Boolean(snapshot.MenuOpen) && !snapshot.SessionId && shop?.available === true,
    schemaVersion: shop?.schemaVersion, netOwned: shop?.netOwned, netEquipped: shop?.netEquipped, foodCount: shop?.foodCount, maxPacks: shop?.maxPacks };
}
const shopInput = new ShopInput({ readContext: shopContext, post: send, newTransportId: () => crypto.randomUUID() });
function shopBusy() { const s = shopInput.state(); return Boolean(s.inflight || s.awaitingState); }
function equipmentContext() {
  return { documentId, lease: authority.state().lease, allowed: canControl() && !inflight,
    schemaVersion: equipmentView.state()?.schemaVersion, menuOpen: Boolean(snapshot?.MenuOpen), sessionId: snapshot?.SessionId ?? null,
    pendingResult: Boolean(snapshot?.PendingResult), faulted: Boolean(saveFaulted || snapshot?.Faulted) };
}
const equipmentInput = new EquipmentInput({ readContext: equipmentContext, post: send, newTransportId: () => crypto.randomUUID() });
function equipmentBusy() { const s = equipmentInput.state(); return Boolean(s.inflight || s.awaitingState); }
function sparesContext() {
  return { ...equipmentContext(), schemaVersion: sparesView.state()?.schemaVersion };
}
const sparesInput = new SparesInput({ readContext: sparesContext, post: send, newTransportId: () => crypto.randomUUID() });
function sparesBusy() { const s = sparesInput.state(); return Boolean(s.inflight || s.awaitingState); }
const campInput = new CampInput({ readContext: equipmentContext, post: send, newTransportId: () => crypto.randomUUID() });
function campBusy() { const s = campInput.state(); return Boolean(s.inflight || s.awaitingState); }
function campEngaged() { return ['working', 'ready', 'retry'].includes(campInput.state().phase); }
// Another menu operation blocks new intent, not delivery of a current host
// projection. Otherwise the fixed shop/equipment send order loses readiness.
const gearFree = () => !spotRequest && !gearBusy() && !occupiedBank() && !wardrobe.busy;
const canUseShop = () => gearFree() && !equipmentBusy() && !equipmentInput.state().retry && !sparesBusy() && !sparesInput.state().retry && !campBusy() && !campEngaged();
const canUseEquipment = () => gearFree() && !shopBusy() && !shopInput.state().retry && !sparesBusy() && !sparesInput.state().retry && !campBusy() && !campEngaged();
const canUseSpares = () => gearFree() && !shopBusy() && !shopInput.state().retry && !equipmentBusy() && !equipmentInput.state().retry && !campBusy() && !campEngaged();
const canUseCamp = () => gearFree() && !shopBusy() && !shopInput.state().retry && !equipmentBusy() && !equipmentInput.state().retry && !sparesBusy() && !sparesInput.state().retry;
const equipmentPanel = new EquipmentPanel({ view: equipmentView, input: equipmentInput, snapshot: () => snapshot,
  canAct: canUseEquipment, changed: () => updateButtons() });
const sparesPanel = new SparesPanel({ view: sparesView, input: sparesInput, snapshot: () => snapshot,
  canAct: canUseSpares, changed: () => updateButtons() });
const campPanel = new CampPanel({ view: campView, input: campInput, snapshot: () => snapshot,
  canAct: canUseCamp, changed: () => updateButtons() });
const spotMenu = new SpotMenu({ documentId, container: $('spot-menu'), canSelect: spotAccess, send: selectSpot, fishArt: fishFigure });
const wardrobe = new Wardrobe({ documentId, container: $('wardrobe'), readContext: () => tradeContext(!gearBusy()), post: send,
  newTransportId: () => crypto.randomUUID(), changed: updateButtons });
const bottomShop = new BottomShop({ documentId, post:send, newTransportId:() => crypto.randomUUID(),
  readContext:() => tradeContext(!wardrobe.busy && !bankKit.busy && !bankKit.selected && !lureShop.busy && !lureMethod()) });
const bankKit = new BankKit({ documentId, post:send, newTransportId:() => crypto.randomUUID(),
  readContext:() => tradeContext(!wardrobe.busy && !bottomShop.busy && !lureShop.busy && !lureMethod()) });
const lureShop=new LureShop({documentId,post:send,newTransportId:()=>crypto.randomUUID(),
  readContext:()=>tradeContext(!wardrobe.busy && !bankKit.busy && !bankKit.selected && !bottomShop.busy)});
const lurePanel=createLurePanel(document);$('lure-instruments').append(lurePanel.element);
const renderBankShop = bindBankShop(bankKit,$,updateButtons);
const stand = new StandView({documentId,post:send,newTransportId:()=>crypto.randomUUID(),readContext:()=>{
  const w=waterView.state();
  return {waterId:w?.waterId,spotId:w?.spotId,geometryVersion:w?.geometryVersion,lease:authority.state().lease,
    menuOpen:snapshot?.MenuOpen, focused:!document.hidden && document.hasFocus(), allowed:bankKit.selected && canControl() &&
      !inflight && !castInput.state() && !visualPaused && hostPolicy?.canRun!==false && bankWalk.canCast(w)};
}});
const standPanels=['map-stand','scene-stand'].map(id=>{
  const panel=createStandPanel(document), container=$(id);
  container.append(panel.element);return {panel,container};
});
function gearBusy(){return bottomShop.busy || bankKit.busy || lureShop.busy;}
function occupiedBank(){return bankKit.selected && stand.state()?.slots.some(row=>row.rig?.sessionId);}
function tradeContext(otherShopFree) {
  const shop = shopInput.state(), equipment = equipmentInput.state(), spares = sparesInput.state();
  return { lease: authority.state().lease, sequence: snapshot?.Sequence, walletMinor: snapshot?.WalletMinor,
    allowed: otherShopFree && canControl() && snapshot.MenuOpen && !snapshot.SessionId && !occupiedBank() && !snapshot.PendingResult && !castInput.state() &&
      !['charging','prepared'].includes(castState?.phase) && !spotRequest && !inflight && !shopBusy() && !shop.retry && !shop.quote &&
      !equipmentBusy() && !equipment.retry && !sparesBusy() && !spares.retry && !spares.quote && !campBusy() && !campEngaged() };
}
function currentSpotWater() {
  return !spotMenu.revision || Boolean(spotMenu.activeId && waterView.state()?.waterId === spotMenu.activeId);
}
function currentConditions(){
  if(!snapshot?.HasConfirmedState || snapshot.Faulted || snapshot.ShutdownConfirmed || saveFaulted)return conditionsView.forWater(null);
  const water=waterView.state();
  const frozen=document.hidden || visualPaused || snapshot.HostPaused || snapshot.MenuOpen || hostPolicy?.canRun===false;
  return conditionsView.forWater(water?.enabled && currentSpotWater() ? water.waterId : null,frozen);
}
function renderConditions(){
  const weather=currentConditions();
  showWaterConditions($('water-conditions'),weather);
  showWaterConditions($('map-weather'),weather);
}
function spotAccess() {
  if (!canControl()) return '开启交互后可以切换钓点。';
  if (!snapshot.MenuOpen) return '打开菜单后可以切换钓点。';
  if (bankKit.selected) return '请先收回各竿，再到渔具店退出竿架模式。';
  if (snapshot.PendingResult) return '请先处理当前渔获。';
  if (snapshot.SessionId || castInput.state() || ['charging','prepared'].includes(castState?.phase)) return '请先收回这一竿或取消蓄力。';
  if (spotRequest || !currentSpotWater()) return '正在同步钓场…';
  if (inflight || !canUseShop() || !canUseCamp() || shopInput.state().quote || sparesInput.state().quote) return '请先完成或取消当前菜单操作。';
  return true;
}
function selectSpot(spotId) {
  if (spotAccess() !== true) return false;
  const lease = authority.state().lease, transportId = crypto.randomUUID();
  spotRequest = { spotId, lease, transportId, acknowledged: false };
  try { send({ kind: 'game-spot-select', lease, transportId, spotId, expectedVersion: waterView.state().geometryVersion }); }
  catch { spotRequest = null; return false; }
  updateButtons(); return true;
}
function reconcileSpot() {
  if (!spotRequest) return;
  if (!canControl() || !snapshot.MenuOpen || spotRequest.lease !== authority.state().lease) {
    spotRequest = null; spotMenu.cancelPending();
  } else if (spotRequest.acknowledged && spotMenu.activeId === spotRequest.spotId && currentSpotWater()) {
    spotRequest = null;
  }
}
function renderSpotLocation() {
  const atlas = spotMenu.state(), active = atlas?.spots.find(s => s.id === atlas.activeId);
  $('scene-spot-name').textContent = active?.name ?? '岸边钓位';
  $('scene-spot-region').textContent = active?.region ?? '';
  $('camp-location').textContent = active?.name ?? '';
  document.body.dataset.campWater = active?.id ?? '';
  $('map-spot-name').textContent = active ? `${active.name} · 俯视钓位` : '钓鱼桌面 · 俯视钓位';
}
let visualPaused = false;
const scene = new ScenePresentation({ canvas: $('scene-canvas'), load: () => import('./game-scene.mjs'),
  projection: sceneProjection, canRender: () => landscape() && !document.hidden && hostPolicy?.canRun === true &&
    !visualPaused && snapshot?.HasConfirmedState && !snapshot.HostPaused && !snapshot.ShutdownConfirmed &&
    !snapshot.Faulted && !saveFaulted && (readOnly || !snapshot.MenuOpen),
  cancelHeld: () => { bankWalk.stop(); castInput.cancel(); lureInput.reset(); fightInput.cancel(); speedKeyHeld = false; }, changed: renderSceneShell });
const bankWalk = new BankWalk({ documentId, post:send, newTransportId:() => crypto.randomUUID(), readContext:() => {
  const water = waterView.state();
  return { lease:authority.state().lease, waterId:water?.waterId, geometryVersion:water?.geometryVersion,
    allowed:scene.visible && freeAtBank() && !heldSession() && !stand.pending && !snapshot?.PendingResult && !castInput.state() &&
      !['charging','prepared'].includes(castState?.phase) && !document.hidden && document.hasFocus() && !visualPaused && hostPolicy?.canRun === true };
} });
function sceneProjection() {
  const session = heldSession(), water = waterView.state(), bottom = currentBottom();
  const current = s => s?.sessionId && s.sessionId === session ? s : null;
  if (water?.landing && (!session || water.landing.sessionId !== session)) water.landing = null;
  const cast = castState && (session || castState.phase !== 'committed') ? { phase: castState.phase, powerPermille: castState.powerPermille,
    elapsedMilliseconds: castState.elapsedMilliseconds, inputSequence: castState.inputSequence } : null;
  const canAim = canPrepare() && bankWalk.canCast(water) && !castInput.state() && !document.hidden && !visualPaused && hostPolicy?.canRun === true;
  return { water, cast, flight: current(flightView.state()), bite: current(biteView.state()), fight: current(fightView.state()),
    bottom:bottom ? { ...bottom, lineEnd:bottomLineEnd && { ...bottomLineEnd } } : null,
    weather:currentConditions(),lureSelected:lureMethod(),lure:currentLure(),canAim,stand:bankKit.selected ? stand.state() : null,bank:bankWalk.forWater(water),sleeveColor:wardrobe.sleeveColor };
}
function renderSceneShell() {
  const state = scene.state();
  document.body.dataset.sceneMode = state.loading ? 'loading' : state.mode;
  document.body.dataset.sceneMenu = !readOnly && snapshot?.MenuOpen ? 'open' : 'closed';
  $('scene-stage').hidden = !state.loading && state.mode !== 'scene';
  $('scene-map-tools').hidden = !state.enabled || state.mode === 'scene';
  $('scene-to-water').hidden = Boolean(state.fault); $('scene-to-water').disabled = state.loading;
  $('scene-map-status').hidden = !state.fault;
  if (state.fault) $('scene-map-status').textContent = '岸边画面暂不可用，可以继续在俯视图钓鱼。重新打开窗口后可再进入岸边视角。';
  renderStand();
  if (state.mode !== 'scene') { setSceneHelp(false); return; }
  renderSceneHud();
}
function setSceneHelp(open) {
  $('scene-help-sheet').hidden = !open;
  $('scene-help-toggle').setAttribute('aria-expanded', String(open));
}
let shoreMessageTimer;
function showShoreMessage(code) {
  const element = $('scene-message');
  clearTimeout(shoreMessageTimer);
  element.hidden = true;
  if (readOnly || !scene.visible || !canControl() || snapshot.MenuOpen) return;
  if (['charging','prepared','cast-candidate','reel-held','reel-released','menu-updated','lure-reeling','lure-stopped','lure-speed','bottom-hooked','lure-hooked'].includes(code)) return;
  const text = replyText(code);
  if (!text) return;
  element.textContent = text; element.hidden = false;
  shoreMessageTimer = setTimeout(() => { element.hidden = true; }, 3600);
}
function renderSceneHud() {
  document.body.dataset.sceneMenu = !readOnly && snapshot?.MenuOpen ? 'open' : 'closed';
  const helpAvailable = scene.visible && canControl() && !snapshot.MenuOpen && !document.hidden && !visualPaused && document.hasFocus();
  $('scene-help-toggle').disabled = !helpAvailable;
  if (!helpAvailable) { setSceneHelp(false); $('scene-message').hidden = true; }
  if (readOnly || !scene.visible) return;
  const session = heldSession(), bite = biteView.state(), fight = fightView.state(), flight = flightView.state();
  const bank = bankWalk.forWater(waterView.state());
  $('scene-help-walk').hidden = $('scene-help-walk-keys').hidden = !bank;
  $('bank-position').hidden = !bank;
  if (bank) {
    const side = bank.xMm < 0 ? '左侧' : '右侧';
    $('bank-position').textContent = bank.xMm === 0 ? '岸线中央' : `岸线${side} ${(Math.abs(bank.xMm) / 1000).toFixed(1)} 米`;
  }
  let action = '对准水面', hint = '按住左键蓄力，松开抛竿。', cue = 'quiet';
  if (bank && !bankWalk.canCast(waterView.state())) { action = '沿岸走位'; hint = '松开走位键，站稳后按住水面抛竿。'; }
  const charging = castState?.phase === 'charging' && !session && helpAvailable;
  const lure = currentLure(), bottom = currentBottom();
  if (!canControl()) { action = $('status').textContent; hint = ''; }
  else if (snapshot.PendingResult) { action = '鱼护已满'; hint = $('session').textContent; }
  else if (bankKit.selected && !stand.state()?.heldRig) {
    action = '从竿架取竿'; hint = '按 1、2、3 取对应竿位的竿，或按 E 取最近的竿。';
  }
  else if (session && fight?.sessionId === session && fight.phase !== 'idle') {
    action = $('fight-status').textContent;
    if (fight.phase === 'fighting') {
      hint = `${fight.reeling ? '收线中' : '按住左键收线'} · 张力 ${(fight.tensionMilliN / 1000).toFixed(1)} N · 泄力 ${fight.dragLevel}/10`;
    } else if (fight.phase === 'landing-eligible') {
      cue = 'land'; action = '可抄鱼'; hint = '按空格抄鱼。';
    } else {
      hint = ['landing-pending','failure-pending','faulted','escaped'].includes(fight.phase) ? $('fight-detail').textContent : '';
    }
  } else if (lure) {
    [action,hint] = lurePrompt(lure.phase);
    if (['in-water','following'].includes(lure.phase)) { action = lure.reeling ? '正在收饵' : '亮片下沉'; hint = lure.reeling ? '' : '按住左键收饵'; }
    if (lure.phase === 'strikeable') cue = 'strike';
  } else if (bottom) {
    [action,hint] = bottomPrompt(bottom.phase);
    if (['sinking','settling','settled','touch','retrieved'].includes(bottom.phase)) hint = '';
    if (bottom.phase === 'bite') cue = 'strike';
  } else if (session && !lureMethod() && !bottomMethod() && bite?.sessionId === session && bite.phase !== 'idle') {
    action = $('bite-status').textContent; hint = '';
    if (bite.phase === 'strikeable') { cue = 'strike'; hint = '现在按鼠标右键提竿。'; }
    else if (bite.phase === 'hooked') hint = '按住左键收线。';
    else if (bite.phase === 'retrieve-required') hint = $('bite-detail').textContent;
  } else if (session) {
    const current = flight?.sessionId === session ? flight : null;
    action = current?.phase === 'waiting' ? '钓组已落水' : current ? $('flight-status').textContent : '钓组已出手';
    hint = current?.phase === 'invalid' ? '收回这一竿后可重新选择水面落点。' : '';
  } else if (charging) { action = '松开左键抛竿'; hint = ''; }
  const method=lureMethod()?'亮片路亚':bottomMethod()?'底钓':'';
  const title = method && !session && canControl() ? method + ' · ' + action : action;
  if ($('scene-action').textContent !== title) $('scene-action').textContent = title;
  $('scene-readout').dataset.cue = cue;
  const inputCue = cue === 'strike' || cue === 'land' ? hint : '';
  if ($('scene-input-cue').textContent !== inputCue) $('scene-input-cue').textContent = inputCue;
  $('scene-hint').hidden = !hint;
  if ($('scene-hint').textContent !== hint) $('scene-hint').textContent = hint;
  const showLure = lure && !['hooked','ended'].includes(lure.phase) && !snapshot.PendingResult;
  $('scene-lure-readout').hidden=!showLure;
  if(showLure) {
    $('scene-lure-readout').textContent = `水下 ${(lure.depthMm / 1000).toFixed(1)} 米 · ${lure.speedLevel+1}/5 档`;
    $('scene-lure-readout').setAttribute('title',lureMetrics(lure));
  }
  $('scene-charge').hidden = $('scene-power').hidden = !charging;
  $('scene-power').value = charging ? castState.powerPermille : 0;
  if (charging) $('scene-power-percent').textContent = Math.round(castState.powerPermille / 10) + '%';
  $('scene-menu').disabled = $('menu').disabled;
  for (const key of ['recall','release','land','retry-land','retry-escape']) {
    const source = $(key), proxy = $(`scene-${key}`);
    proxy.disabled = source.disabled; proxy.hidden = source.disabled;
    if(key==='recall')proxy.textContent=source.textContent;
  }
}
const names = { worm: '蚯蚓' };
function decimal(value, scale = 2) {
  if (typeof value !== 'string' || !/^\d+$/.test(value)) return '—';
  const full = value.padStart(scale + 1, '0');
  return `${full.slice(0, -scale)}.${full.slice(-scale)}`;
}
function hideCatch() {
  if (catchTimer !== null) clearTimeout(catchTimer);
  catchTimer = null; $('catch-card').hidden = true;
}
function showCatch(caught) {
  if (!caught || !canControl() || snapshot.MenuOpen || document.hidden) return;
  hideCatch(); const plate = fishFigure(document,caught.speciesKey);
  $('catch-art').replaceChildren(...(plate ? [plate] : [])); $('catch-name').textContent = fishName(caught.speciesKey);
  $('catch-mass').textContent = `${decimal(caught.massMg,6).replace(/\.?0+$/, '')} kg`;
  $('catch-card').hidden = false; catchTimer = setTimeout(hideCatch, 6500);
}
function canInspectFish() {
  return canControl() && snapshot.MenuOpen && !inflight && !document.hidden && !visualPaused && hostPolicy?.canRun !== false;
}
function specimenButton(fish) {
  if (readOnly || !snapshot.MenuOpen || !fishName(fish.SpeciesKey)) return null;
  specimen ??= createSpecimenView(document, { readSnapshot: () => snapshot, canOpen: canInspectFish,
    findButton: id => $('creel').querySelector(`[data-specimen-id="${id}"]`) });
  const button = specimen.buttonFor(fish.Id);
  if (button) button.disabled = !canInspectFish();
  return button;
}
function render() {
  castInput.reconcile();
  strikeInput.reconcile();
  fightInput.reconcile();
  const previousShopLease = shopInput.state().lease;
  shopInput.reconcile();
  equipmentInput.reconcile();
  sparesInput.reconcile();
  campInput.reconcile();
  if (!shopContext().allowed || previousShopLease !== shopInput.state().lease) shopView.clearReferences();
  if (!fightContext().allowed) speedKeyHeld = false;
  $('status').textContent = snapshot?.Faulted || saveFaulted ? '保存结果待核验，请退出后重新打开原存档。' : snapshot?.HostPaused ? '当前暂停' : readOnly ? '观赏窗口' : canControl() ? '交互已开启' : '请从窗口顶部开启交互';
  if (!snapshot?.HasConfirmedState) {
    $('stats').replaceChildren(); $('session').textContent = '尚无可确认的游戏进度'; $('creel').replaceChildren(); $('capacity').textContent = '';
  } else {
    $('stats').replaceChildren(...[
      `银币 ${decimal(snapshot.WalletMinor)}`, `体力 ${snapshot.Stamina}`, `${names[snapshot.BaitDefinitionKey] ?? snapshot.BaitDefinitionKey} ${snapshot.BaitCount} 份`,
    ].map(text => { const span = document.createElement('span'); span.textContent = text; return span; }));
    const pending = snapshot.PendingFish;
    $('session').textContent = snapshot.PendingResult && pending ? `鱼护已满：${fishName(pending.SpeciesKey) ?? pending.SpeciesKey} · ${decimal(pending.MassMg, 6)} kg 已保留，需明确放流后才能继续。` : heldSession() ? (lureMethod() ? '亮片已出手，入水后按住左键匀收。' : bottomMethod() ? '底钓钓组已出手，请观察竿尖。' : '这一竿已出手，请观察轨迹与浮漂。') : bankKit.selected && !stand.state()?.heldRig ? '请先从竿架取竿。' : '可以出手';
    $('capacity').textContent = `${snapshot.CreelCount} / ${snapshot.CreelCapacity}`;
    const rows = snapshot.Creel ?? [];
    for (const id of selected) if (!rows.some(f => f.Id === id)) selected.delete(id);
    $('creel').replaceChildren(...rows.map(fish => {
      const item = document.createElement('li'); item.className = 'fish';
      const label = document.createElement('label'), checkbox = document.createElement('input'), text = document.createElement('span');
      checkbox.type = 'checkbox'; checkbox.checked = selected.has(fish.Id); checkbox.disabled = !canControl() || !snapshot.MenuOpen || Boolean(snapshot.SessionId) || Boolean(inflight) || occupiedBank();
      checkbox.addEventListener('change', () => { if (checkbox.checked) selected.add(fish.Id); else selected.delete(fish.Id); updateButtons(); });
      const plate = fishFigure(document,fish.SpeciesKey); if (plate) item.append(plate);
      text.textContent = `${fishName(fish.SpeciesKey) ?? fish.SpeciesKey} · ${decimal(fish.MassMg, 6)} kg · ${decimal(fish.SaleValueMinor)} 银币`;
      label.append(checkbox, text); item.append(label);
      const inspect = specimenButton(fish); if (inspect) item.append(inspect);
      return item;
    }));
    if (!rows.length) { const item = document.createElement('li'); item.textContent = '鱼护为空'; $('creel').append(item); }
  }
  renderCast(); renderBite(); renderFight(); renderShop(); equipmentPanel.render(); updateButtons();
}
function renderCast() {
  const labels = { idle: '尚未蓄力', charging: '正在蓄力，松开左键出手', prepared: '已松开，等待确认出手', committed: '已确认出手', cancelled: '蓄力已取消，重新按下才能开始', rejected: '本次出手未完成', unknown: '保存结果待核验，请退出后重新打开原存档' };
  const phase = castState?.phase ?? 'idle';
  $('cast-status').textContent = labels[phase] ?? '等待宿主确认';
  const power = ['charging', 'prepared', 'committed'].includes(phase) ? castState.powerPermille : 0;
  $('cast-power').value = power; $('cast-percent').textContent = `${(power / 10).toFixed(1)}%`;
  $('cast-target').setAttribute('aria-disabled', String(!canPrepare() && !strikeContext().allowed && !bottomStrike.available && !(currentLure() && lureContext().allowed) && !fightContext().allowed));
  renderWater();
}
function renderWater() {
  const state = waterView.state(), graph = waterView.diagram(), enabled = state?.enabled === true;
  $('cast-legacy').hidden = enabled; $('water-aim-panel').hidden = !enabled;
  if (!enabled) return;
  const metres = value => (value / 1000).toFixed(2);
  $('water-far').textContent = `远端 ${metres(state.bounds.maxZmm)} 米`;
  $('water-near').textContent = `近端 ${metres(state.bounds.minZmm)} 米`;
  $('water-horizontal').textContent = `横向 ${metres(state.bounds.minXmm)} 至 ${metres(state.bounds.maxXmm)} 米`;
  const depths = state.bed.map(row => row.depthMm);
  const methodDepth = bottomMethod() ? '铅坠沉至底部后观察竿尖' : `漂深 ${metres(state.floatDepthMm)} 米 · 最低净空 ${metres(state.clearanceMm)} 米`;
  $('water-depth').textContent = `已配置河床水深 ${metres(Math.min(...depths))}–${metres(Math.max(...depths))} 米 · ${methodDepth}`;
  const direction = { near: '近岸方向', far: '远岸方向', left: '左侧', right: '右侧' };
  const outside = p => p?.visible === false ? ` · 图外（${p.outside.map(value => direction[value]).join('、')}）` : '';
  const position = p => `横向 ${metres(p.xMm)} 米，离岸 ${metres(p.zMm)} 米，${p.depthMm === 0 ? '无有效水深' : `水深 ${metres(p.depthMm)} 米`}`;
  for (const name of ['target', 'prediction', 'landing']) {
    const marker = $(`water-${name}-mark`), p = graph[name];
    marker.setAttribute('visibility', p?.visible ? 'visible' : 'hidden');
    if (p?.visible) marker.setAttribute('transform', `translate(${p.x.toFixed(2)} ${p.y.toFixed(2)})`);
  }
  $('water-target-status').textContent = state.target ? `宿主瞄准目标：${position(state.target)} · 需线 ${metres(state.target.requiredLineMm)} 米` :
    '在图内按住左键瞄准蓄力。按钮使用上次选择，首次使用图中心；目标等待宿主确认。';
  $('water-prediction-status').textContent = state.prediction ? `预计落点（${(state.prediction.powerPermille / 10).toFixed(1)}% 蓄力）：${position(state.prediction)}${outside(graph.prediction)}` : '预计落点：等待宿主计算';
  const reasons = { 'water-sampled': '水域检查通过', 'outside-water': '水域外', 'float-clearance-insufficient': '漂深净空不足' };
  $('water-landing-status').textContent = state.landing ? `实际落点：${state.landing.accepted ? '水域检查通过' : reasons[state.landing.reason] ?? '未通过水域检查'} · ${position(state.landing)}${outside(graph.landing)}` : '实际落点：尚未落水';
  $('water-landing-circle').setAttribute('fill', state.landing?.accepted ? '#a8c37d' : '#de997d');
  $('water-outside').hidden = ![graph.prediction, graph.landing].some(p => p?.visible === false);
}
function renderFlight() {
  const graph = flightView.diagram(), state = flightView.state();
  const labels = { idle: '等待确认出手', flying: '钓组飞行中', waiting: lureMethod() ? '亮片已入水，可以开始收饵' : bottomMethod() ? '底钓钓组已落水，观察竿尖' : '已落水，观察下方浮漂', invalid: '本次未进入有效水面', ended: '这一竿已结束' };
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
  const lineBroken = state?.phase === 'escaped' && state.reason === 'line-overloaded';
  const labels = { idle: '中鱼后开始遛鱼', fighting: '遛鱼中', 'landing-eligible': '已进入可抄范围，按空格抄鱼',
    'landing-pending': '正在确认收鱼结果', landed: '已确认收鱼入护', 'capacity-blocked': '鱼护已满，请明确放流当前鱼',
    'failure-pending': '正在确认本竿失败结果', escaped: lineBroken ? '已确认主线过载断线，损失已保存' : state?.reason === 'slack-unhooked' ? '已确认松线脱钩，本竿结束' : '已确认本竿失败，本竿结束',
    faulted: '遛鱼已暂停，当前结果未确认', ended: '本次遛鱼已结束' };
  const reasons = { 'fish-too-far': '鱼距岸还远', 'fish-too-deep': '鱼所在水层超出抄网范围', 'fish-moving': '鱼还在快速游动', 'fish-active': '鱼仍有较多体力',
    'net-unavailable': '没有可用抄网', eligible: '可抄鱼', 'landing-not-written': '尚未写入，点击下方按钮重试原收鱼结果',
    'landing-save-unknown': '保存结果待核验，请退出后重新打开原存档', 'solver-unresolved': '当前受力求解超出支持范围，请收回这一竿',
    'failure-pending': '结果尚未保存确认，暂不能收回、抄鱼或继续收线。',
    'failure-not-written': '失败结果确认未写入。请明确点击下方按钮重试原结果，不能用收回来跳过确认。',
    'failure-save-unknown': '保存结果待核验，已停止全部操作。请退出后重新打开原存档。',
    'load-outside-supported-range': '当前负载超出简化模型支持范围，请收回这一竿', 'speed-outside-supported-range': '当前速度超出简化模型支持范围，请收回这一竿' };
  $('fight-status').textContent = labels[state?.phase ?? 'idle'];
  $('fight-detail').textContent = state && ['faulted', 'landing-pending', 'failure-pending'].includes(state.phase) ? reasons[state.reason] ?? '等待宿主确认，当前结果尚未保存。' :
    lineBroken ? '本次没有鱼入护；断线损失已确认，已使用的鱼饵和体力不退还。请打开菜单检查并补齐钓组，再抛竿。' :
    state?.phase === 'escaped' && lureMethod() ? '本次渔获已脱钩，结束这一竿后可再次抛出亮片。' :
    state?.phase === 'escaped' ? state.reason === 'slack-unhooked' ? '本次没有鱼入护；已使用的鱼饵和体力不退还，装备没有损失。可重新抛竿。' : '本次没有鱼入护；已使用的鱼饵和体力不退还，请检查当前钓组。' :
    state && ['fighting', 'landing-eligible'].includes(state.phase) ? `${state.reeling ? '正在主动收线' : '尚未主动收线'} · ${reasons[state.landingReason] ?? '观察鱼的位置与线张力'}` : '左键按住收线；松开停止绞入，鱼仍可能向外拉线。';
  $('fight-marks').setAttribute('visibility', graph ? 'visible' : 'hidden');
  $('fight-clipped').hidden = !graph?.clipped;
  $('fight-energy').value = state?.energyPermille ?? 0;
  $('fight-energy-value').textContent = state && state.phase !== 'idle' ? `${(state.energyPermille / 10).toFixed(1)}%` : '—';
  $('fight-reeling').textContent = state?.reeling ? '收线中' : '未收线';
  $('fight-exposure').textContent = state && !['idle', 'ended', 'escaped'].includes(state.phase) ? `宿主记录的松线暴露：${state.unhookExposureSteps} 步` : '松线暴露 —';
  $('fight-overload').hidden = !state?.breakEnabled || ['idle', 'ended'].includes(state.phase);
  $('fight-overload').textContent = state?.breakEnabled ? `主线断裂阈值 ${(state.breakThresholdMilliN / 1000).toFixed(2)} N · 过载累计 ${state.overloadExposureSteps} / ${state.overloadRequiredSteps} 步` : '主线过载 —';
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
  $('shop-status').textContent = saveFaulted ? reasons['save-outcome-unknown'] : shopBusy() ? '正在等待宿主确认，请勿重复操作。' :
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
  specimen?.reconcile();
  logPanel.reconcile();
  bankWalk.reconcile();
  if (catchTimer !== null && (!canControl() || hostPolicy?.canRun === false || snapshot.MenuOpen || snapshot.SessionId || castInput.state() || document.hidden)) hideCatch();
  reconcileSpot();
  wardrobe.reconcile();
  bankKit.reconcile(); stand.reconcile(); bottomShop.reconcile(); bottomStrike.reconcile(); renderBottom();
  lureShop.reconcile(); lureInput.reconcile(); renderLure();
  renderBankShop(snapshot?.MenuOpen); renderStand();
  renderConditions();
  const active = canControl() && !inflight && !spotRequest && !gearBusy() && !wardrobe.busy && !shopBusy() && !equipmentBusy() && !sparesBusy() && !campBusy();
  const input = castInput.state();
  $('cast').disabled = !canPrepare() || !bankWalk.canCast(waterView.state()) || Boolean(input);
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
  const failureRetryPending = fightInput.state().pending.some(p => p.action === 'retry-escape' && p.sessionId === fight.sessionId && p.encounterId === fight.encounterId && p.lease === fight.lease);
  $('retry-escape').hidden = fight.phase !== 'failure-pending' || !fight.canRetryFailure;
  $('retry-escape').disabled = !fight.allowed || fight.phase !== 'failure-pending' || !fight.canRetryFailure || failureRetryPending;
  $('recall').disabled = !active || !handReady() || !heldSession() || snapshot.PendingResult || failurePending() || !canRecallLure();
  $('recall').textContent=currentLure()?.phase==='retrieve-pending' && lureView.canRetryRetrieve ? '重试收回亮片' : '收回这一竿';
  $('release').disabled = !active || !handReady() || !snapshot.PendingResult;
  $('menu').disabled = !active;
  $('menu').textContent = snapshot?.MenuOpen ? '返回钓位' : spotMenu.revision ? '打开钓点、鱼护与商店' : '打开鱼护与商店';
  $('sell').disabled = !active || campEngaged() || !snapshot.MenuOpen || Boolean(snapshot.SessionId) || occupiedBank() || !selected.size;
  $('previous').disabled = !snapshot?.HasConfirmedState || snapshot.CreelOffset <= 0;
  $('next').disabled = !snapshot?.HasConfirmedState || snapshot.CreelOffset + (snapshot.Creel?.length ?? 0) >= snapshot.CreelCount;
  const shop = shopContext(), shopState = shopInput.state(), shopping = shop.allowed && shopState.ready && !shopBusy() && !shopState.faulted && canUseShop();
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
  equipmentPanel.render();
  sparesPanel.render();
  campPanel.render();
  $('spot-menu').hidden = !snapshot?.MenuOpen || !spotMenu.revision;
  if (!$('spot-menu').hidden) spotMenu.render();
  $('wardrobe').hidden = !snapshot?.MenuOpen || !wardrobe.revision;
  if (!$('wardrobe').hidden) wardrobe.render();
  if (scene.visible) renderSceneHud();
  renderCampNavigation();
  scene.refresh();
}
function renderBottom() {
  const shop = bottomShop.state();
  $('bottom-shop').hidden = !shop || !snapshot?.MenuOpen;
  if (shop && snapshot?.MenuOpen) {
    const affordable = typeof snapshot.WalletMinor === 'string' && /^\d+$/.test(snapshot.WalletMinor) && BigInt(snapshot.WalletMinor)>=6400n;
    const method = shop.method === 'bottom' ? '底钓' : '浮漂';
    $('bottom-shop-status').textContent = !shop.available ? '当前存档的底钓套装暂不可用。' : bottomShop.pending ? '正在确认本次操作…' :
      bottomShop.confirming ? '支付 64.00 银币购入一套单钩底钓装备？' :
      [shop.owned ? '已拥有' : affordable ? '64.00 银币 / 套' : '64.00 银币 / 套 · 余额不足', '当前钓法 · '+method,
        shop.allowed ? '' : '请前往静水湾的底钓区使用'].filter(Boolean).join('　');
    $('bottom-buy').hidden = shop.owned || bottomShop.confirming;
    $('bottom-buy').disabled = !bottomShop.can('buy');
    $('bottom-confirm').hidden = $('bottom-cancel').hidden = !bottomShop.confirming;
    $('bottom-confirm').disabled = !bottomShop.confirming;
    $('select-bottom').hidden = $('select-float').hidden = !shop.owned;
    $('select-bottom').disabled = !bottomShop.can('select-bottom');
    $('select-float').disabled = !bottomShop.can('select-float');
  }
  const bottom = currentBottom();
  $('bite').hidden = bottomMethod() || lureMethod();
  $('bottom-observation').hidden = !bottom || Boolean(snapshot?.MenuOpen);
  if (bottom && !bottomPanel) {
    bottomPanel = createBottomTipPanel(document); $('bottom-diagram').append(bottomPanel.element);
  }
  bottomPanel?.render(bottomView,heldSession());
  $('bottom-strike').disabled = !bottomStrike.available;
}
function renderLure(){
  const s=lureShop.state(), menu=Boolean(snapshot?.MenuOpen);
  $('lure-shop').hidden=!s || !menu;
  if(s && menu){
    $('lure-shop-status').textContent=lureShop.pending?'正在确认操作…':lureShop.confirming?'支付 56.00 银币购买亮片、路亚竿和纺车轮？':
      s.selected?'已装备亮片路亚':!s.available?'此存档暂未开放亮片路亚。':s.owned?
      (s.allowed?'已拥有，可在此钓场使用。':'已拥有，在海湾岩岸可以使用。'):'56.00 银币 / 套';
    $('lure-buy').hidden=s.owned || lureShop.confirming;
    $('lure-buy').disabled=!lureShop.can('buy');
    $('lure-confirm').hidden=$('lure-cancel').hidden=!lureShop.confirming;
    $('lure-confirm').disabled=!lureShop.confirming;
    $('lure-select').hidden=!s.owned || s.selected;
    $('lure-select').disabled=!lureShop.can('select');
    $('lure-leave').hidden=!s.selected;$('lure-leave').disabled=!lureShop.can('leave');
  }
  const lure=currentLure();
  $('lure-observation').hidden=!lure || menu || scene.visible || lure.phase==='hooked';
  if(!$('lure-observation').hidden)lurePanel.render(lureView,heldSession());
}
function renderStand() {
  const visible = bankKit.selected && landscape() && !snapshot?.MenuOpen && Boolean(stand.state());
  for (const {panel,container} of standPanels) {
    container.hidden = !visible || (container === $('scene-stand') ? !scene.visible : scene.visible);
    if (!container.hidden) panel.render(stand);
  }
}
const campAreas = { map: 'spot-menu', creel: 'creel-section', log:'angler-log-dialog', gear: 'equipment', tackle: 'shop-net',
  food: 'shop-food', clothes: 'wardrobe', work: 'camp' };
const campKeys = { KeyM: 'map', KeyC: 'creel', KeyO:'log', KeyI: 'gear', KeyT: 'food' };
const campTitles = {home:'岸边营地',map:'选择水域',creel:'鱼护',gear:'钓组与背包',tackle:'渔具店',food:'食品补给',clothes:'服装商店',work:'营地帮工'};
const playerCamp = () => !readOnly && spotMenu.revision > 0 && scene.state().enabled;
const logPanel = createAnglerLogPanel(document,anglerLog,() => playerCamp() && canControl() &&
  snapshot.MenuOpen && !document.hidden && !visualPaused && hostPolicy?.canRun !== false);
function areaAvailable(area) {
  switch (area) {
    case 'map': return Boolean(spotMenu.activeId);
    case 'creel': return Boolean(snapshot?.HasConfirmedState);
    case 'log': return !snapshot?.MenuOpen || (anglerLog.sequence !== null && String(anglerLog.sequence) === snapshot.Sequence);
    case 'gear': return equipmentView.state()?.schemaVersion >= 3;
    case 'tackle':
    case 'food': return shopView.state()?.schemaVersion >= 2;
    case 'clothes': return wardrobe.state()?.available === true;
    case 'work': return Boolean(campView.state() && campView.state().reason !== 'camp-schema-required');
    default: return area === 'home';
  }
}
function visitCampArea(area) {
  if (!playerCamp() || !canControl() || !snapshot.MenuOpen || !areaAvailable(area)) return false;
  if (!canLeaveCampPage(area)) return false;
  if (campOpening?.area === 'log' && area !== 'log') { campOpening = null; campOpenShown = true; }
  if (area === 'log') return logPanel.open();
  const node = $(area === 'home' ? 'camp-home' : campAreas[area]);
  if (!node || node.hidden) return false;
  document.body.dataset.campPage = area;
  $('camp-section-title').textContent = campTitles[area];
  for (const key of Object.keys(campAreas)) $('camp-go-'+key).setAttribute('aria-current',key===area?'page':'false');
  $('camp-panels').scrollTop=0;
  if(area!=='home') { node.setAttribute('tabindex','-1'); node.focus?.({preventScroll:true}); }
  node.scrollIntoView?.({ block: 'start', inline: 'nearest', behavior: 'instant' });
  return true;
}
function canLeaveCampPage(destination) {
  const current=document.body.dataset.campPage;
  if(current===destination || !['tackle','food'].includes(current)) return true;
  const shop=shopInput.state();
  return !shop.quote && !shop.retry && !shopBusy();
}
function renderCampNavigation() {
  const enabled = playerCamp(), open = Boolean(enabled && snapshot?.MenuOpen && landscape());
  document.body.dataset.camp = open ? 'open' : 'closed';
  $('scene-menu').textContent = enabled ? '营地 · Esc' : '钓点与补给';
  $('camp-home').hidden = $('camp-overview').hidden = !open;
  if (enabled) $('menu').textContent = snapshot?.MenuOpen ? '返回钓位 · Esc' : '进入营地 · Esc';
  if (campOpening && (!enabled || !canControl() || campOpening.lease !== authority.state().lease)) campOpening = null;
  if (!open) { campOpenShown = false; document.body.dataset.campPage='home'; return; }
  $('camp-balance').textContent = formatMinor(snapshot.WalletMinor)+' 银币';
  $('camp-creel-count').textContent = '已入护 '+snapshot.CreelCount+' / '+snapshot.CreelCapacity+' 条';
  for (const area of Object.keys(campAreas)) $('camp-go-' + area).disabled = !canControl() || !areaAvailable(area) || !canLeaveCampPage(area);
  $('camp-overview').disabled = !canControl() || !canLeaveCampPage('home');
  if (!campOpenShown) {
    // Opening from shore can deliver the menu before its first journal projection.
    if (campOpening?.area === 'log' && !areaAvailable('log')) return;
    campOpenShown = true;
    const destination = campOpening?.area ?? 'home';
    campOpening = null;
    if (!visitCampArea(destination) && destination !== 'home') visitCampArea('home');
  }
}
function revoke(reason) {
  stand.clearInput();
  lureInput.reset();
  bankWalk.stop();
  castInput.cancel();
  fightInput.cancel(); speedKeyHeld = false;
  const cancellation = authority.cancel();
  send({ kind: 'revoke', ...cancellation, reason, activationRequestId: lastActivation?.requestId ?? '', transportId: crypto.randomUUID() });
  render();
}
function intent(action) {
  if (!canControl() || inflight || spotRequest || gearBusy() || wardrobe.busy || shopBusy() || equipmentBusy() || sparesBusy() || campBusy() || campEngaged() || action === 'recall' && failurePending()) return;
  if (bankKit.selected && (action === 'sell' ? occupiedBank() : !handReady() || !heldSession())) return;
  if(action==='recall' && !canRecallLure())return;
  lureInput.reset();
  fightInput.cancel();
  const command = { action, requestId: crypto.randomUUID(), eventId: crypto.randomUUID(), sessionId: action === 'sell' ? '' : heldSession(),
    expectedSequence: snapshot.Sequence, rigRevision: snapshot.RigRevision, rulesVersion: snapshot.RulesVersion };
  if (action === 'sell') command.fishIds = [...selected];
  inflight = crypto.randomUUID();
  send({ kind: 'game-intent', lease: authority.state().lease, transportId: inflight, intent: command });
  render();
}
const codes = { 'cast-candidate': '这一竿已保存，已使用一份鱼饵。', 'recall-candidate': '已收回，扣饵保持。', 'released-candidate': '当前鱼已放流。', 'sale-candidate': '出售已确认。', 'menu-updated': '菜单状态已更新。', 'control-required': '请重新开启交互。', 'invalid-intent': '请求未识别。', 'version-conflict': '进度已更新，请重新操作。', 'bait-empty': '鱼饵已用完。', 'stamina-insufficient': '体力不足。', 'session-busy': '请先结束当前一竿。' };
Object.assign(codes, { 'bottom-kit-purchased':'底钓套装购买已确认。', 'bottom-method-selected':'钓法已切换。',
  'bottom-hooked':'底钓已中鱼，请手动收线。', 'bottom-shop-busy':'请先结束当前一竿或菜单操作。', 'bottom-shop-stale':'存档进度已变化，请重新选择。',
  'bottom-unavailable':'当前未开放底钓。', 'bottom-kit-not-owned':'请先购买底钓套装。', 'bottom-kit-owned':'已经拥有底钓套装。',
  'bottom-spot-unavailable':'请前往静水湾的底钓区。', 'bottom-strike-missed':'已经错过这次鱼口。',
  'outside-bottom-patch':'落点离开底钓区，请收回并重新瞄准。', 'bed-too-steep':'落点底坡过陡，请换个落点。' });
Object.assign(codes, { 'save-settlement-reserve': '存档空间不足以安全保存下一竿，已取消操作，未扣除鱼饵或体力。请保留当前存档。' });
Object.assign(codes, {'lure-kit-purchased':'亮片套装已购入。','lure-method-selected':'钓法已切换。','lure-hooked':'已中鱼，请重新按左键收线。',
  'lure-unavailable':'暂时不能使用路亚套装。','lure-spot-unavailable':'亮片路亚请前往海湾岩岸。','lure-not-current':'这次收饵已结束。',
  'lure-shop-stale':'存档进度已变化，请重新选择。','lure-shop-busy':'请先收回钓组或完成当前菜单操作。','lure-kit-owned':'已拥有亮片套装。',
  'lure-kit-not-owned':'请先在渔具店购买亮片套装。','lure-not-written':'购买尚未写入，可在柜台重新确认。',
  'lure-retrieved':'亮片已收回，可以重新抛投。','lure-retrieval-not-written':'进度尚未写入，请点「重试收回亮片」。','lure-retrieve-required':'请按住左键把亮片收回岸边。',
  'lure-not-active':'亮片已不在收饵状态。','lure-reeling':'正在匀收亮片。','lure-stopped':'已停止收饵。','lure-speed':'收线速度档已更新。'});
const lureReplies={'cast-candidate':'亮片已出手，消耗 2 点体力。','recall-candidate':'已收回亮片。','empty-strike':'本次未能中鱼，可以继续收饵。',
  'encounter-not-current':'这次攻击已经结束，继续观察亮片。','slack-unhooked':'已确认脱钩，本竿结束。','unhooked-candidate':'脱钩结果已保存。'};
function replyText(code){return (lureMethod() && lureReplies[code]) || codes[code];}
Object.assign(codes, { 'bank-purchased':'竿架装备购买已确认。', 'bank-mode-selected':'钓竿模式已切换。', 'bank-busy':'请先收回各竿，完成当前操作。',
  'bank-stale':'装备或进度已变化，请重新选择。', 'bank-unavailable':'当前无法使用竿架。','bank-kit-required':'请先购买底竿和竿架。',
  'bank-leave-first':'请先退出竿架模式。','stand-updated':'取放已确认。','slot-unreachable':'请走到竿架附近。',
  'ownership-stale':'竿位已变化，请重新选择。','slot-empty':'该竿位没有竿。','slot-occupied':'该竿位已占用。','hands-empty':'请先取竿。' });
for (const [code, text] of Object.entries(wardrobeMessages)) if (code.startsWith('wardrobe-')) codes[code] = text;
Object.assign(codes, { 'spot-selected': '钓点已切换。', 'spot-unchanged': '已在所选钓点。', 'spot-unavailable': '该钓点暂未开放。',
  'spot-switch-busy': '请先结束当前一竿或处理菜单中的操作。', 'spot-stale': '水域已更新，请重新选择钓点。' });
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
Object.assign(codes, { 'failure-pending': '失败结果尚未保存确认，请等待当前结果。', 'failure-not-written': '失败结果确认未写入，可明确重试原结果。',
  'failure-save-unknown': '失败结果的保存状态未知，已停止全部操作，请退出后核验原存档。', 'failure-retry-unavailable': '当前不能重试失败结果。',
  'failure-result-required': '请先确认当前失败结果，不能用收回来跳过。', 'slack-unhooked': '已确认松线脱钩，本竿结束；不退饵、不退体力，装备无损失。',
  'unhooked-candidate': '已确认松线脱钩，本竿结束；没有鱼入护，装备无损失。' });
Object.assign(codes, { 'assembly-candidate': '装备变更已保存。', 'line-move-candidate': '鱼线位置已保存，长度保持不变。',
  'equipment-archive-candidate': '损失记录已整理，名额已腾出；历史仍保留，没有返还装备或银币。',
  'equipment-not-written': '装备操作确认未写入，请重试原操作或取消。', 'equipment-retry-required': '请先处理上一次未写入的装备操作。', 'equipment-cancelled': '已取消未写入的装备操作。',
  'equipment-schema-required': '此存档尚未支持装备编辑。', 'equipment-intent-not-current': '这次装备操作已失效，请重新选择。',
  'menu-required': '请先打开菜单。', 'result-pending': '请先处理当前尚未确认的结果。',
  'equipment-version-conflict': '装备已更新，请重新选择。', 'missing-component': '钓组缺少部件，请先在菜单装配。', 'missing-line': '当前渔轮没有鱼线，请先装线。',
  'insufficient-line': '已装鱼线长度不足。', 'assembly-unchanged': '当前装备未变化。', 'line-unchanged': '鱼线位置未变化。' });
Object.assign(codes, { 'spares-quoted': '备件报价已确认，请核对后购买。', 'equipment-purchase-candidate': '备件已购买，可到钓组中装配。',
  'spares-not-configured': '此存档尚未配置备件商店。', 'spares-not-written': '购买确认未写入，请重试原购买或取消。',
  'spares-retry-required': '请先处理上一次未写入的备件购买。', 'spares-cancelled': '已取消备件报价或未写入操作。',
  'spares-intent-not-current': '备件报价已失效，请重新查看报价。', 'equipment-quote-stale': '装备或进度已变化，请重新查看备件报价。',
  'equipment-capacity-exceeded': '装备数量已达当前存档上限，本次未扣款。', 'equipment-offers-missing': '此存档尚未配置备件商店。' });
Object.assign(codes, { 'camp-work-started': '帮工已开始，请保持当前窗口和菜单开启。', 'camp-work-ready': '帮工已完成，请领取报酬。',
  'camp-work-not-written': '领取确认未写入，请重试领取原报酬。', 'camp-work-save-unknown': '领取结果待核验，请退出后重新打开原存档。',
  'camp-work-cancelled': '本轮帮工已取消，未结算时间不保留。', 'camp-work-not-ready': '本轮尚未完成，暂不能领取。',
  'camp-work-busy': '请先完成或取消当前帮工。', 'camp-work-state-changed': '当前进度已变化，本轮帮工已取消。',
  'camp-work-candidate': '帮工报酬已确认入账：8.00 银币。', 'camp-wallet-ineligible': '可先用现有银币购买补给。',
  'camp-creel-not-empty': '可以先出售鱼护里的渔获。', 'camp-schema-required': '此存档尚未支持营地帮工。' });
window.chrome.webview.addEventListener('message', event => {
  const m = event.data; if (!m || m.documentId !== documentId) return;
  if(m.kind==='game-conditions-state'){
    if(conditionsView.apply(m)){renderConditions();scene.refresh();}
    return;
  }
  if (m.kind === 'game-angler-log') {
    if (anglerLog.apply(m)) { logPanel.reconcile(); renderCampNavigation(); }
    return;
  }
  if(m.kind==='game-lure-shop-state'){if(lureShop.apply(m))render();return;}
  if(m.kind==='game-lure-state'){
    const previous=lureView.state(),retry=lureView.canRetryRetrieve;
    if(lureView.apply(m)){
      lureInput.reconcile();
      if(previous?.phase!==m.state.phase || previous?.sessionId!==m.state.sessionId || retry!==lureView.canRetryRetrieve){
        renderCast();updateButtons();
      }else if(m.state.sessionId && currentLure()){
        if(scene.visible)renderSceneHud();else if(!$('lure-observation').hidden)lurePanel.render(lureView,heldSession());
        scene.refresh();
      }
    }
    return;
  }
  if (m.kind === 'game-bank-kit-state') {
    if (bankKit.apply(m)) render();
    return;
  }
  if (m.kind === 'game-stand-state') {
    if (stand.apply(m)) { castInput.reconcile(); fightInput.reconcile(); renderCast(); renderFight(); updateButtons(); }
    return;
  }
  if (m.kind === 'game-bottom-shop-state') {
    if (bottomShop.apply(m)) { renderFlight(); renderCast(); updateButtons(); }
    return;
  }
  if (m.kind === 'game-bottom-state') {
    if (bottomView.apply(m)) {
      bottomLineEnd = m.state.phase === 'idle' ? null : confirmedLineEnd(m.lineEnd);
      renderFlight(); renderCast(); updateButtons();
    }
    return;
  }
  if (m.kind === 'game-bank-state') {
    if (bankWalk.apply(m)) { castInput.reconcile(); updateButtons(); }
    return;
  }
  if (m.kind === 'game-wardrobe-state') {
    if (wardrobe.apply(m)) updateButtons();
    return;
  }
  if (m.kind === 'game-spot-state') {
    if (spotMenu.apply(m)) { renderSpotLocation(); updateButtons(); }
    return;
  }
  if (m.kind === 'game-water-state') {
    const movable = Boolean(bankWalk.forWater(waterView.state()));
    const previousGeometry = waterView.geometryKey(movable);
    if (waterView.apply(m)) {
      if (waterView.geometryKey(movable) !== previousGeometry) { bankWalk.stop(); castInput.cancel(); selectedAim = null; }
      const water = waterView.state();
      scene.configure(Boolean(m.sceneEnabled === true && water?.enabled && water.waterBounds && water.anchor));
      castInput.reconcile(); renderCast(); updateButtons();
    }
    return;
  }
  if (m.kind === 'game-flight-state') {
    // Host positions can arrive frequently. Update the diagram alone; neither
    // rebuild the creel nor start an independent page animation clock.
    if (flightView.apply(m)) { renderFlight(); if (scene.visible) renderSceneHud(); scene.refresh(); }
    return;
  }
  if (m.kind === 'game-bite-state') {
    if (biteView.apply(m)) { strikeInput.reconcile(); renderBite(); renderCast(); updateButtons(); }
    return;
  }
  if (m.kind === 'game-fight-state') {
    if (fightView.apply(m)) {
      if (['failure-save-unknown', 'landing-save-unknown'].includes(m.state.reason)) saveFaulted = true;
      fightInput.reconcile();
      if (saveFaulted) render(); else { renderFight(); renderCast(); updateButtons(); }
    }
    return;
  }
  if (m.kind === 'game-shop-state') {
    if (shopView.apply(m)) {
      if (m.state.reason === 'save-outcome-unknown') saveFaulted = true;
      shopInput.hostState(shopView.state()); renderShop(); updateButtons();
    }
    return;
  }
  if (m.kind === 'game-equipment-state') {
    if (equipmentView.apply(m)) {
      if (m.state.reason === 'save-outcome-unknown') saveFaulted = true;
      equipmentInput.hostState(m); equipmentPanel.render(); updateButtons();
    }
    return;
  }
  if (m.kind === 'game-spares-state') {
    if (sparesView.apply(m)) {
      if (m.state.reason === 'save-outcome-unknown') saveFaulted = true;
      sparesInput.hostState(m); updateButtons();
    }
    return;
  }
  if (m.kind === 'game-camp-state') {
    if (campView.apply(m)) {
      if (m.state.phase === 'unknown' || ['save-outcome-unknown', 'camp-work-save-unknown'].includes(m.state.reason)) saveFaulted = true;
      campInput.hostState(m); updateButtons();
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
  if (m.kind === 'game-snapshot' && Number.isSafeInteger(m.revision) && m.revision > revision) {
    const caught = newCreelCatch(snapshot, m.snapshot); snapshot = m.snapshot; revision = m.revision; showCatch(caught);
  }
  if (m.kind === 'game-reply') {
    const walkReply = bankWalk.reply(m);
    if (['walk-updated','walk-stopped'].includes(m.code)) { if (walkReply) updateButtons(); return; }
    if (campOpening && campOpening.transportId === m.transportId && m.code !== 'menu-updated') campOpening = null;
    wardrobe.reply(m); bottomShop.reply(m); bottomStrike.reply(m); bankKit.reply(m); stand.reply(m); lureShop.reply(m); lureInput.reply(m);
    if (spotRequest && m.transportId === spotRequest.transportId) {
      if (m.code === 'spot-selected' || m.code === 'spot-unchanged') spotRequest.acknowledged = true;
      else { spotRequest = null; spotMenu.reject(codes[m.code]); }
    }
    lastReply = m; showShoreMessage(m.code); castInput.reply(m); strikeInput.reply(m); fightInput.reply(m); shopInput.reply(m); equipmentInput.reply(m); sparesInput.reply(m); campInput.reply(m);
    if (['save-outcome-unknown', 'failure-save-unknown', 'landing-save-unknown', 'camp-work-save-unknown'].includes(m.code)) saveFaulted = true;
    if (inflight === m.transportId) inflight = null;
    $('notice').textContent = m.code === 'wardrobe-updated' || (m.code === 'menu-updated' && playerCamp()) ? '' : replyText(m.code) ?? `操作未完成：${m.code}`;
  }
  if (m.kind === 'game-cast-state' && Number.isSafeInteger(m.revision) && m.revision > castRevision &&
      ['idle', 'charging', 'prepared', 'committed', 'cancelled', 'rejected', 'unknown'].includes(m.phase) &&
      Number.isInteger(m.powerPermille) && m.powerPermille >= 0 && m.powerPermille <= 1000 &&
      Number.isInteger(m.elapsedMilliseconds) && m.elapsedMilliseconds >= 0) {
    castState = m; castRevision = m.revision; castInput.hostState(m);
  }
  if (m.kind === 'game-flight' && flightNotices.apply(m)) {
    $('notice').textContent = lureMethod() ? '亮片已出手，消耗 2 点体力。' : '已确认出手并保存消耗。下方显示固定平面的飞行与落水状态。';
  }
  if (m.kind === 'activation-reply') lastActivationReply = m;
  if (m.kind === 'revoke-reply') lastRevoke = m;
  if (m.kind === 'role') { readOnly = m.role.includes('只读'); document.body.dataset.role = readOnly ? 'observer' : 'interaction'; }
  if (m.kind === 'host-policy' && (!hostPolicy || m.revision >= hostPolicy.revision)) { hostPolicy = m; visualPaused = !m.canRun; }
  render();
});
$('cast').addEventListener('click', () => {
  bankWalk.stop();
  const aim = buttonAim(); if (castInput.begin('button', aim) && aim) selectedAim = Object.freeze({ ...aim }); render();
});
$('cast-release').addEventListener('click', () => { castInput.release(); render(); });
$('cast-cancel').addEventListener('click', () => { castInput.cancel(); render(); });
$('strike').addEventListener('click', () => { strikeInput.strike(); updateButtons(); });
for (const [id, act] of [
  ['bottom-buy', () => bottomShop.offer()], ['bottom-confirm', () => bottomShop.submit('buy')],
  ['bottom-cancel', () => bottomShop.cancel()], ['select-bottom', () => bottomShop.submit('select-bottom')],
  ['select-float', () => bottomShop.submit('select-float')], ['bottom-strike', () => bottomStrike.strike()],
  ['lure-buy',()=>lureShop.offer()],['lure-confirm',()=>lureShop.submit('buy')],['lure-cancel',()=>lureShop.cancel()],
  ['lure-select',()=>lureShop.submit('select')],['lure-leave',()=>lureShop.submit('leave')],
]) {
  $(id).addEventListener('click', e => { if (e.detail<=1 && !$(id).disabled) { act(); updateButtons(); } });
  $(id).addEventListener('keydown', e => { if (e.repeat && ['Space','Enter'].includes(e.code)) e.preventDefault(); });
}
$('land').addEventListener('click', () => { fightInput.land(); updateButtons(); });
$('retry-land').addEventListener('click', () => { fightInput.retryLanding(); updateButtons(); });
$('retry-escape').addEventListener('click', event => { if (event.detail > 1) return; fightInput.retryFailure(); updateButtons(); });
$('retry-escape').addEventListener('keydown', event => { if (event.repeat && ['Space', 'Enter'].includes(event.code)) event.preventDefault(); });
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
function toggleMenu(area = 'home') {
  if (!canControl() || inflight || spotRequest || gearBusy() || wardrobe.busy || shopBusy() || equipmentBusy() || sparesBusy() || campBusy()) return;
  stand.clearInput();
  lureInput.reset();
  bankWalk.stop();
  castInput.cancel();
  fightInput.cancel(); speedKeyHeld = false;
  if (snapshot.MenuOpen) shopInput.cancel();
  inflight = crypto.randomUUID();
  const open = !snapshot.MenuOpen, lease = authority.state().lease;
  campOpening = open && playerCamp() ? { area, lease, transportId: inflight } : null;
  send({ kind: 'game-menu', lease, transportId: inflight, open }); render();
}
$('menu').addEventListener('click', () => toggleMenu());
$('camp-overview').addEventListener('click', () => visitCampArea('home'));
for (const area of Object.keys(campAreas)) $('camp-go-' + area).addEventListener('click', () => visitCampArea(area));
function requestShopQuote(product) {
  if (!canUseShop()) return;
  const packs = product === 'net' ? 1 : $(product === 'bait' ? 'bait-packs' : 'food-packs').valueAsNumber;
  if (!Number.isInteger(packs) || packs < 1 || packs > (shopView.state()?.maxPacks ?? 0)) { $('notice').textContent = '请输入当前上限以内的整数数量。'; return; }
  shopInput.quote(product, packs); renderShop(); updateButtons();
}
for (const [id, action] of [
  ['quote-net', () => requestShopQuote('net')], ['quote-bait', () => requestShopQuote('bait')], ['quote-food', () => requestShopQuote('food')],
  ['shop-buy', () => shopInput.buy()], ['shop-cancel', () => shopInput.cancel()], ['shop-retry-confirm', () => shopInput.retry()],
  ['net-equip', () => shopInput.equip(true)], ['net-unequip', () => shopInput.equip(false)], ['food-eat', () => shopInput.eat()],
]) {
  $(id).addEventListener('click', event => { if (event.detail > 1 || !canUseShop()) return; action(); renderShop(); updateButtons(); });
  $(id).addEventListener('keydown', event => { if (event.repeat && ['Space', 'Enter'].includes(event.code)) event.preventDefault(); });
}
function page(offset) { send({ kind: 'game-page', lease: '', transportId: crypto.randomUUID(), offset }); }
$('previous').addEventListener('click', () => page(Math.max(0, snapshot.CreelOffset - 50)));
$('next').addEventListener('click', () => page(snapshot.CreelOffset + 50));
const target = $('cast-target');
const canvas = $('scene-canvas');
const reelPointers = new Map(), reelKeys = new Map();
const surfaceAvailable = surface => surface === canvas ? scene.visible && !snapshot?.MenuOpen : !scene.visible;
function markMouseButtons(buttons){
  strikeInput.mouseButtons(buttons);bottomStrike.buttons(buttons);lureInput.buttons(buttons);
}
function rightPress(event,surface){
  if(event.button!==2 || !(event.buttons&2) || !surfaceAvailable(surface))return false;
  event.preventDefault();
  if(castInput.state()){castInput.cancel();markMouseButtons(event.buttons);render();return true;}
  if(lureMethod()){
    strikeInput.mouseButtons(event.buttons);bottomStrike.buttons(event.buttons);
    if(lureInput.press(event.buttons))updateButtons();
  }else if(bottomMethod()){
    strikeInput.mouseButtons(event.buttons);
    if(bottomStrike.press(event.buttons))updateButtons();
  }else{
    bottomStrike.buttons(event.buttons);
    if(strikeInput.mouseDown(event.button,event.buttons))updateButtons();
  }
  return true;
}
function bindCastSurface(surface) {
surface.addEventListener('pointerdown', event => {
  if (!surfaceAvailable(surface) || event.pointerType !== 'mouse') return;
  if(rightPress(event,surface))return;
  if (surface === canvas && event.button === 0) bankWalk.stop();
  const fight=fightContext();
  if (event.button === 0 && fight.allowed && ['fighting', 'landing-eligible'].includes(fight.phase)) {
    beginReelPointer(event, surface); return;
  }
  if(event.button===0 && currentLure()){
    const source=`pointer:${event.pointerId}`;
    if((event.buttons&1) && lureInput.begin(source)){
      const held=lureInput.state().held;
      reelPointers.set(event.pointerId,{element:surface,at:event.timeStamp,startTransportId:held.transportId,lure:true});
      event.preventDefault();
      try{surface.setPointerCapture(event.pointerId);}catch{lureInput.stop(source,held.transportId);reelPointers.delete(event.pointerId);}
      updateButtons();
    }
    return;
  }
  const water = waterView.state();
  const aiming = water?.enabled && event.button === 0 && !(event.buttons & 2);
  const selected = !aiming ? null : surface === canvas ? scene.pickAim(event.clientX, event.clientY) : pickAim($('water-aim-svg'), event, water.geometryVersion);
  const aim = bankWalk.aim(water, selected);
  if (aiming && !aim) return;
  const changed = castInput.pointer(event.pointerId, event.buttons, true, aim);
  if (changed) {
    if (aim && castInput.state()?.source === `pointer:${event.pointerId}`) selectedAim = Object.freeze({ ...aim });
    event.preventDefault();
    if (castInput.state()?.source === `pointer:${event.pointerId}`) {
      try { surface.setPointerCapture(event.pointerId); } catch { castInput.pointerCancelled(event.pointerId); }
    }
    render();
  }
});
surface.addEventListener('pointermove', event => {
  if (!(event.buttons & 1)) endReelPointer(event);
  if(event.pointerType!=='mouse' || !surfaceAvailable(surface))return;
  // A second physical button on an active pointer arrives as a move.
  // Normal motion has button=-1 and cannot turn a held press into a strike.
  if(rightPress(event,surface))return;
  if(!(event.buttons&2))markMouseButtons(event.buttons);
  if(castInput.pointer(event.pointerId,event.buttons))render();
});
surface.addEventListener('pointerup', event => { endReelPointer(event); if (surfaceAvailable(surface) && event.pointerType === 'mouse' && castInput.pointer(event.pointerId, event.buttons)) render(); });
surface.addEventListener('pointercancel', event => { endReelPointer(event); if (surfaceAvailable(surface) && castInput.pointerCancelled(event.pointerId)) render(); });
surface.addEventListener('lostpointercapture', event => { endReelPointer(event); if (surfaceAvailable(surface) && castInput.pointerLost(event.pointerId)) render(); });
surface.addEventListener('contextmenu', event => { event.preventDefault(); if (surfaceAvailable(surface) && castInput.cancel()) render(); });
}
bindCastSurface(target); bindCastSurface(canvas);
for (const element of [target, canvas, $('bite-diagram'), $('bottom-diagram')]) {
  element.addEventListener('mousedown', event=>rightPress(event,element));
}
$('bite-diagram').addEventListener('contextmenu', event => event.preventDefault());
$('bottom-diagram').addEventListener('contextmenu', event => event.preventDefault());
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
  const source=`pointer:${event.pointerId}`;
  if(held.lure)lureInput.stop(source,held.startTransportId);else fightInput.end(source,held.startTransportId);
  reelPointers.delete(event.pointerId);
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
for (const element of [target, canvas, $('fight-diagram')]) element.addEventListener('wheel', event => {
  if(surfaceAvailable(element) && lureInput.adjustingSpeed && event.deltaY){
    event.preventDefault();if(lureInput.wheel(event.deltaY))updateButtons();return;
  }
  if (!surfaceAvailable(element) || !fightContext().allowed || !['fighting', 'landing-eligible'].includes(fightContext().phase) || event.deltaY === 0) return;
  event.preventDefault(); changeFightLevel(speedKeyHeld ? 'speed' : 'drag', event.deltaY < 0 ? 1 : -1);
}, { passive: false });
window.addEventListener('pointerup', event=>{endReelPointer(event);if(event.pointerType==='mouse')markMouseButtons(event.buttons);});
window.addEventListener('pointercancel', event=>{if(event.pointerType==='mouse')markMouseButtons(0);});
for (const type of ['mouseup','mousemove']) window.addEventListener(type, event => {
  markMouseButtons(event.buttons);
});
window.addEventListener('blur', () => revoke('blur'));
window.addEventListener('pagehide', () => { revoke('pagehide'); scene.dispose(); specimen?.dispose(); });
window.addEventListener('resize', () => { if (!landscape()) revoke('portrait'); render(); scene.resize(); });
document.addEventListener('visibilitychange', () => { if (document.hidden) revoke('hidden'); else scene.refresh(); });
const typing = target => target instanceof Element && (target.isContentEditable || target.closest('input,textarea,select'));
document.addEventListener('focusin', event => { if (typing(event.target)) bankWalk.stop(); });
document.addEventListener('keydown', event => {
  if (!$('scene-help-toggle').disabled && scene.visible && !snapshot?.MenuOpen &&
      !typing(event.target) && !event.isComposing && !event.defaultPrevented && !event.repeat && !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey &&
      (event.code === 'F1' || event.key === 'Escape' && !$('scene-help-sheet').hidden)) {
    event.preventDefault();
    const open = event.code === 'F1' && $('scene-help-sheet').hidden;
    setSceneHelp(open);
    if (!open && document.activeElement === $('scene-help-toggle')) $('scene-canvas').focus();
    return;
  }
  if (bankKit.selected && stand.key(event,true)) { event.preventDefault(); updateButtons(); return; }
  if (!typing(event.target) && !event.isComposing && !event.defaultPrevented && !event.altKey && !event.ctrlKey && !event.metaKey &&
      bankWalk.key(event,true)) { event.preventDefault(); return; }
  const isEscape = event.key === 'Escape' || event.code === 'Escape', area = campKeys[event.code];
  if (playerCamp() && (isEscape || area)) {
    if (event.defaultPrevented || event.isComposing || event.repeat || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey ||
        document.hidden || !document.hasFocus() || !canControl() ||
        (event.target instanceof Element && (event.target.isContentEditable || event.target.closest('input,textarea,select')))) return;
    if (area && !areaAvailable(area)) return;
    event.preventDefault();
    if (isEscape || !snapshot.MenuOpen) toggleMenu(area ?? 'home'); else visitCampArea(area);
    return;
  }
  if (event.key === 'Escape') { revoke('escape'); return; }
  if (typing(event.target)) return;
  if (event.code === 'KeyR' && !event.repeat && !event.isComposing && !event.altKey && !event.ctrlKey && !event.metaKey) {
    if(lureInput.speedKey(true))event.preventDefault();
    else if(fightContext().allowed)speedKeyHeld=true;
  }
  if (event.target instanceof Element && event.target.closest('button')) return;
  if (event.code === 'Space' && !event.repeat && fightContext().allowed) { event.preventDefault(); fightInput.land(); updateButtons(); }
});
document.addEventListener('keyup', event => {
  stand.key(event,false);
  if (!typing(event.target) && bankWalk.key(event,false)) event.preventDefault();
  if (event.code === 'KeyR') { speedKeyHeld=false;lureInput.speedKey(false); }
});
$('scene-to-map').addEventListener('click', () => scene.showScene(false));
$('scene-help-toggle').addEventListener('click', () => {
  if ($('scene-help-toggle').disabled || !scene.visible) return;
  const open = $('scene-help-sheet').hidden;
  setSceneHelp(open);
  if (!open) $('scene-canvas').focus();
});
$('scene-to-water').addEventListener('click', () => scene.showScene(true));
for (const key of ['menu','recall','release','land','retry-land','retry-escape']) {
  $(`scene-${key}`).addEventListener('click', event => { if (event.detail <= 1 && !$(key).disabled) $(key).click(); });
}
function pauseVisual(value) { visualPaused = value; if (value) bankWalk.stop();specimen?.reconcile();logPanel.reconcile();renderConditions();scene.refresh();if(scene.visible)renderSceneHud(); }
const visual = { start: () => pauseVisual(false), stop: () => pauseVisual(true), pause: () => pauseVisual(true), resume: () => pauseVisual(false),
  state: () => scene.state().renderer ?? { running: false, frames: 0, kind: 'host-flight-projection' } };
window.dualProbe = { documentId, visual, state: () => ({ documentId, ...authority.state(), snapshot, revision, lastReply, lastActivationReply, lastRevoke, hostPolicy, castState, lastFlight: flightNotices.last, flightCount: flightNotices.count, inputState: castInput.state(), flightState: flightView.state(), flightRevision: flightView.revision,
  biteState: biteView.state(), biteRevision: biteView.revision, strikeInputState: strikeInput.state(),
  specimenState: specimen?.state() ?? { open:false, loading:false, error:null, fish:null, viewer:null },
  anglerLogState: anglerLog.state(), anglerLogOpen: logPanel.shown,
  bottomState:bottomView.state(), bottomRevision:bottomView.revision, bottomLineEnd:bottomLineEnd && { ...bottomLineEnd },
  bottomShopState:bottomShop.state(), bottomShopPending:bottomShop.pending,
  lureShopState:lureShop.state(),lureShopPending:lureShop.pending,lureState:lureView.state(),lureRevision:lureView.revision,lureInputState:lureInput.state(),lureCanRetryRetrieve:lureView.canRetryRetrieve,
  bankKitState:bankKit.state(), bankKitPending:bankKit.pending, standState:stand.state(), standRevision:stand.revision, standPending:stand.pending,
  fightState: fightView.state(), fightRevision: fightView.revision, fightInputState: fightInput.state(),
  shopState: shopView.state(), shopRevision: shopView.revision, shopInputState: shopInput.state(),
  equipmentState: equipmentView.state(), equipmentRevision: equipmentView.revision, equipmentInputState: equipmentInput.state(),
  sparesState: sparesView.state(), sparesRevision: sparesView.revision, sparesInputState: sparesInput.state(),
  campState: campView.state(), campRevision: campView.revision, campInputState: campInput.state(),
  waterState: waterView.state(), waterRevision: waterView.revision, weatherState:currentConditions(),weatherRevision:conditionsView.revision,sceneState: scene.state(),
  bankState:bankWalk.state(), bankRevision:bankWalk.revision, bankPending:bankWalk.pending,
  spotState: spotMenu.state(), spotRevision: spotMenu.revision, spotPending: spotRequest?.spotId ?? spotMenu.pending,
  wardrobeState: wardrobe.state(), wardrobeRevision: wardrobe.revision, wardrobePending: wardrobe.pending }),
  pickSceneAim: (x, y) => scene.pickAim(x, y), projectSceneAim: (u, v) => scene.projectAim(u, v),
  sendRaw: message => window.chrome.webview.postMessage(message), cancel: () => revoke('test-cancel') };
render(); renderFlight();

import { LeaseSession } from './lease-session.mjs';
import { CastInput, FlightNotices } from './cast-input.mjs';
import { FlightView } from './flight-view.mjs';
import { BiteView, StrikeInput } from './bite-view.mjs';

const documentId = crypto.randomUUID();
const authority = new LeaseSession(documentId);
let snapshot = null, revision = 0, hostPolicy = null, lastReply = null;
let lastActivation = null, lastActivationReply = null, lastRevoke = null;
let inflight = null, readOnly = true;
let castState = null, castRevision = 0;
const flightNotices = new FlightNotices();
const flightView = new FlightView(documentId);
const biteView = new BiteView(documentId);
const selected = new Set();
const $ = id => document.getElementById(id);
const send = message => window.chrome.webview.postMessage({ ...message, documentId });
const landscape = () => innerWidth > innerHeight;
const canControl = () => !readOnly && landscape() && Boolean(authority.state().lease) && snapshot?.HasConfirmedState && !snapshot.Faulted && !snapshot.HostPaused && !snapshot.ShutdownConfirmed;
const canPrepare = () => canControl() && !inflight && !snapshot.SessionId && !snapshot.MenuOpen;
const castInput = new CastInput({ readContext: () => ({ lease: authority.state().lease, allowed: canPrepare() }), post: send, newTransportId: () => crypto.randomUUID() });
function strikeContext() {
  const bite = biteView.state();
  return { lease: authority.state().lease, allowed: canControl() && !inflight && !snapshot.MenuOpen && !snapshot.PendingResult &&
    Boolean(bite?.sessionId) && bite.sessionId === snapshot.SessionId && !castInput.state(),
    sessionId: bite?.sessionId, encounterId: bite?.encounterId, phase: bite?.phase };
}
const strikeInput = new StrikeInput({ readContext: strikeContext, post: send, newTransportId: () => crypto.randomUUID() });
const names = { carp: '鲤鱼', crucian: '鲫鱼', worm: '蚯蚓' };
function decimal(value, scale = 2) {
  if (typeof value !== 'string' || !/^\d+$/.test(value)) return '—';
  const full = value.padStart(scale + 1, '0');
  return `${full.slice(0, -scale)}.${full.slice(-scale)}`;
}
function render() {
  castInput.reconcile();
  strikeInput.reconcile();
  $('status').textContent = snapshot?.Faulted ? '保存结果待核验，请退出后重新打开原存档。' : snapshot?.HostPaused ? '当前暂停' : readOnly ? '观赏窗口' : canControl() ? '交互已开启' : '请从窗口顶部开启交互';
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
  renderCast(); renderBite(); updateButtons();
}
function renderCast() {
  const labels = { idle: '尚未蓄力', charging: '正在蓄力，松开左键出手', prepared: '已松开，等待确认出手', committed: '已确认出手', cancelled: '蓄力已取消，重新按下才能开始', rejected: '本次出手未完成', unknown: '保存结果待核验，请退出后重新打开原存档' };
  const phase = castState?.phase ?? 'idle';
  $('cast-status').textContent = labels[phase] ?? '等待宿主确认';
  const power = ['charging', 'prepared', 'committed'].includes(phase) ? castState.powerPermille : 0;
  $('cast-power').value = power; $('cast-percent').textContent = `${(power / 10).toFixed(1)}%`;
  $('cast-target').setAttribute('aria-disabled', String(!canPrepare() && !strikeContext().allowed));
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
    nibbling: '浮漂轻动，鱼仍在试探', strikeable: '浮漂明显下沉', hooked: '已中鱼，遛鱼接线中',
    'retrieve-required': '请收回这一竿，重新挂饵', ended: '这一竿已结束' };
  const reasons = { 'strike-too-early': '提竿过早，未能中鱼', 'strike-too-late': '鱼口已错过', 'empty-strike': '本次空提',
    'bite-missed': '错过鱼口', 'bait-depleted': '钩饵已经脱落', 'environment-interrupted': '当前水域条件已变化', 'environment-unavailable': '当前水域条件不可用' };
  $('bite-status').textContent = labels[state?.phase ?? 'idle'] ?? '等待浮漂状态';
  $('bite-detail').textContent = reasons[state?.reason] ?? (state?.phase === 'hooked' ? '目标保持为这次遇鱼；尚未收鱼入护。' : '右键提竿；过早提竿可能空提。浮漂变化由宿主活动时间推进。');
  $('bite-bait').textContent = !state || state.phase === 'idle' || state.phase === 'ended' ? '钩饵 —' : state.baitAttached ? '钩饵：在钩上' : '钩饵：已用尽';
  $('bite-marks').setAttribute('visibility', diagram ? 'visible' : 'hidden');
  if (diagram) {
    $('bite-float').setAttribute('transform', `translate(${diagram.x.toFixed(2)} ${diagram.y.toFixed(2)})`);
    $('bite-float').setAttribute('opacity', diagram.submerged ? '.55' : '1');
  }
}
function updateButtons() {
  const active = canControl() && !inflight;
  const input = castInput.state();
  $('cast').disabled = !canPrepare() || Boolean(input);
  $('cast-release').disabled = !canPrepare() || input?.phase !== 'charging' || input.source !== 'button';
  $('cast-cancel').disabled = !canControl() || !input;
  const strike = strikeContext();
  $('strike').disabled = !strike.allowed || !['settling', 'searching', 'approaching', 'nibbling', 'strikeable'].includes(strike.phase) || Boolean(strikeInput.state().pending);
  $('recall').disabled = !active || !snapshot.SessionId || snapshot.PendingResult;
  $('release').disabled = !active || !snapshot.PendingResult;
  $('menu').disabled = !active;
  $('menu').textContent = snapshot?.MenuOpen ? '关闭鱼护菜单' : '打开鱼护菜单';
  $('sell').disabled = !active || !snapshot.MenuOpen || Boolean(snapshot.SessionId) || !selected.size;
  $('previous').disabled = !snapshot?.HasConfirmedState || snapshot.CreelOffset <= 0;
  $('next').disabled = !snapshot?.HasConfirmedState || snapshot.CreelOffset + (snapshot.Creel?.length ?? 0) >= snapshot.CreelCount;
}
function revoke(reason) {
  castInput.cancel();
  const cancellation = authority.cancel();
  send({ kind: 'revoke', ...cancellation, reason, activationRequestId: lastActivation?.requestId ?? '', transportId: crypto.randomUUID() });
  render();
}
function intent(action) {
  if (!canControl() || inflight) return;
  const command = { action, requestId: crypto.randomUUID(), eventId: crypto.randomUUID(), sessionId: action === 'sell' ? '' : snapshot.SessionId,
    expectedSequence: snapshot.Sequence, rigRevision: snapshot.RigRevision, rulesVersion: snapshot.RulesVersion };
  if (action === 'sell') command.fishIds = [...selected];
  inflight = crypto.randomUUID();
  send({ kind: 'game-intent', lease: authority.state().lease, transportId: inflight, intent: command });
  render();
}
const codes = { 'cast-candidate': '这一竿已保存，已使用一份鱼饵。', 'recall-candidate': '已收回，扣饵保持。', 'released-candidate': '当前鱼已放流。', 'sale-candidate': '出售已确认。', 'menu-updated': '菜单状态已更新。', 'control-required': '请重新开启交互。', 'invalid-intent': '请求未识别。', 'version-conflict': '进度已更新，请重新操作。', 'bait-empty': '鱼饵已用完。', 'stamina-insufficient': '体力不足。', 'session-busy': '请先结束当前一竿。' };
Object.assign(codes, { charging: '已开始蓄力，松开出手。', prepared: '已松开，等待宿主确认。', cancelled: '蓄力已取消。', 'stale-input': '旧操作已忽略，请重新按下。', 'gesture-not-current': '本次操作已结束，请重新按下。', 'domain-paused': '当前暂停，请恢复后重新按下。', 'cast-preparation-required': '请先按住蓄力，再松开出手。', 'save-outcome-unknown': '保存结果待核验，请退出后重新打开原存档。' });
Object.assign(codes, { hooked: '已中鱼，遛鱼接线中。', 'strike-too-early': '提竿过早，请收回后重新挂饵。', 'strike-too-late': '已错过鱼口，请收回后重试。',
  'empty-strike': '本次空提，请收回后重新挂饵。', 'encounter-not-current': '这次鱼口已变化，请观察当前浮漂。', 'bite-not-ready': '钓组尚未进入有效等鱼状态。', 'bite-not-active': '当前不能提竿。', 'session-not-current': '这一竿已变化，旧操作已忽略。' });
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
  if (m.kind === 'game-reply') { lastReply = m; castInput.reply(m); strikeInput.reply(m); if (inflight === m.transportId) inflight = null; $('notice').textContent = codes[m.code] ?? `操作未完成：${m.code}`; }
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
$('recall').addEventListener('click', () => intent('recall'));
$('release').addEventListener('click', () => intent('release-current'));
$('sell').addEventListener('click', () => intent('sell'));
$('menu').addEventListener('click', () => {
  if (!canControl() || inflight) return;
  castInput.cancel();
  inflight = crypto.randomUUID(); send({ kind: 'game-menu', lease: authority.state().lease, transportId: inflight, open: !snapshot.MenuOpen }); render();
});
function page(offset) { send({ kind: 'game-page', lease: '', transportId: crypto.randomUUID(), offset }); }
$('previous').addEventListener('click', () => page(Math.max(0, snapshot.CreelOffset - 50)));
$('next').addEventListener('click', () => page(snapshot.CreelOffset + 50));
const target = $('cast-target');
target.addEventListener('pointerdown', event => {
  if (event.pointerType !== 'mouse') return;
  const changed = castInput.pointer(event.pointerId, event.buttons, true);
  if (changed) {
    event.preventDefault();
    if (castInput.state()?.source === `pointer:${event.pointerId}`) {
      try { target.setPointerCapture(event.pointerId); } catch { castInput.pointerCancelled(event.pointerId); }
    }
    render();
  }
});
target.addEventListener('pointermove', event => { if (event.pointerType === 'mouse' && castInput.pointer(event.pointerId, event.buttons)) render(); });
target.addEventListener('pointerup', event => { if (event.pointerType === 'mouse' && castInput.pointer(event.pointerId, event.buttons)) render(); });
target.addEventListener('pointercancel', event => { if (castInput.pointerCancelled(event.pointerId)) render(); });
target.addEventListener('lostpointercapture', event => { if (castInput.pointerLost(event.pointerId)) render(); });
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
window.addEventListener('mouseup', event => strikeInput.mouseButtons(event.buttons));
window.addEventListener('mousemove', event => strikeInput.mouseButtons(event.buttons));
window.addEventListener('blur', () => revoke('blur'));
window.addEventListener('pagehide', () => revoke('pagehide'));
window.addEventListener('resize', () => { if (!landscape()) revoke('portrait'); render(); });
document.addEventListener('visibilitychange', () => { if (document.hidden) revoke('hidden'); });
document.addEventListener('keydown', event => { if (event.key === 'Escape') revoke('escape'); });
// No page render/simulation loop: the SVG only projects host snapshots.
const visual = { start() {}, stop() {}, pause() {}, resume() {}, state: () => ({ running: false, frames: 0, kind: 'host-flight-projection' }) };
window.dualProbe = { documentId, visual, state: () => ({ documentId, ...authority.state(), snapshot, revision, lastReply, lastActivationReply, lastRevoke, hostPolicy, castState, lastFlight: flightNotices.last, flightCount: flightNotices.count, inputState: castInput.state(), flightState: flightView.state(), flightRevision: flightView.revision,
  biteState: biteView.state(), biteRevision: biteView.revision, strikeInputState: strikeInput.state() }),
  sendRaw: message => window.chrome.webview.postMessage(message), cancel: () => revoke('test-cancel') };
render(); renderFlight();

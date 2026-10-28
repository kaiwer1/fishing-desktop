import { LeaseSession } from './lease-session.mjs';
import { CastInput, FlightNotices } from './cast-input.mjs';

const documentId = crypto.randomUUID();
const authority = new LeaseSession(documentId);
let snapshot = null, revision = 0, hostPolicy = null, lastReply = null;
let lastActivation = null, lastActivationReply = null, lastRevoke = null;
let inflight = null, readOnly = true;
let castState = null, castRevision = 0;
const flightNotices = new FlightNotices();
const selected = new Set();
const $ = id => document.getElementById(id);
const send = message => window.chrome.webview.postMessage({ ...message, documentId });
const landscape = () => innerWidth > innerHeight;
const canControl = () => !readOnly && landscape() && Boolean(authority.state().lease) && snapshot?.HasConfirmedState && !snapshot.Faulted && !snapshot.HostPaused && !snapshot.ShutdownConfirmed;
const canPrepare = () => canControl() && !inflight && !snapshot.SessionId && !snapshot.MenuOpen;
const castInput = new CastInput({ readContext: () => ({ lease: authority.state().lease, allowed: canPrepare() }), post: send, newTransportId: () => crypto.randomUUID() });
const names = { carp: '鲤鱼', crucian: '鲫鱼', worm: '蚯蚓' };
function decimal(value, scale = 2) {
  if (typeof value !== 'string' || !/^\d+$/.test(value)) return '—';
  const full = value.padStart(scale + 1, '0');
  return `${full.slice(0, -scale)}.${full.slice(-scale)}`;
}
function render() {
  castInput.reconcile();
  $('status').textContent = snapshot?.Faulted ? '保存结果待核验，请退出后重新打开原存档。' : snapshot?.HostPaused ? '当前暂停' : readOnly ? '观赏窗口' : canControl() ? '交互已开启' : '请从窗口顶部开启交互';
  if (!snapshot?.HasConfirmedState) {
    $('stats').replaceChildren(); $('session').textContent = '尚无可确认的游戏进度'; $('creel').replaceChildren(); $('capacity').textContent = '';
  } else {
    $('stats').replaceChildren(...[
      `银币 ${decimal(snapshot.WalletMinor)}`, `体力 ${snapshot.Stamina}`, `${names[snapshot.BaitDefinitionKey] ?? snapshot.BaitDefinitionKey} ${snapshot.BaitCount} 份`,
    ].map(text => { const span = document.createElement('span'); span.textContent = text; return span; }));
    const pending = snapshot.PendingFish;
    $('session').textContent = snapshot.PendingResult && pending ? `鱼护已满：${names[pending.SpeciesKey] ?? pending.SpeciesKey} · ${decimal(pending.MassMg, 6)} kg 已保留，需明确放流后才能继续。` : snapshot.SessionId ? '这一竿已出手；本接入预览尚未运行咬钩和遛鱼模拟。' : '可以出手';
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
  renderCast(); updateButtons();
}
function renderCast() {
  const labels = { idle: '尚未蓄力', charging: '正在蓄力，松开左键出手', prepared: '已松开，等待确认出手', committed: '已确认出手', cancelled: '蓄力已取消，重新按下才能开始', rejected: '本次出手未完成', unknown: '保存结果待核验，请退出后重新打开原存档' };
  const phase = castState?.phase ?? 'idle';
  $('cast-status').textContent = labels[phase] ?? '等待宿主确认';
  const power = ['charging', 'prepared', 'committed'].includes(phase) ? castState.powerPermille : 0;
  $('cast-power').value = power; $('cast-percent').textContent = `${(power / 10).toFixed(1)}%`;
  $('cast-target').setAttribute('aria-disabled', String(!canPrepare()));
}
function updateButtons() {
  const active = canControl() && !inflight;
  const input = castInput.state();
  $('cast').disabled = !canPrepare() || Boolean(input);
  $('cast-release').disabled = !canPrepare() || input?.phase !== 'charging' || input.source !== 'button';
  $('cast-cancel').disabled = !canControl() || !input;
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
window.chrome.webview.addEventListener('message', event => {
  const m = event.data; if (!m || m.documentId !== documentId) return;
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
  if (m.kind === 'game-reply') { lastReply = m; castInput.reply(m); if (inflight === m.transportId) inflight = null; $('notice').textContent = codes[m.code] ?? `操作未完成：${m.code}`; }
  if (m.kind === 'game-cast-state' && Number.isSafeInteger(m.revision) && m.revision > castRevision &&
      ['idle', 'charging', 'prepared', 'committed', 'cancelled', 'rejected', 'unknown'].includes(m.phase) &&
      Number.isInteger(m.powerPermille) && m.powerPermille >= 0 && m.powerPermille <= 1000 &&
      Number.isInteger(m.elapsedMilliseconds) && m.elapsedMilliseconds >= 0) {
    castState = m; castRevision = m.revision; castInput.hostState(m);
  }
  if (m.kind === 'game-flight' && flightNotices.apply(m)) {
    $('notice').textContent = '已确认出手并保存消耗。飞行轨迹与水面尚未接入。';
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
window.addEventListener('blur', () => revoke('blur'));
window.addEventListener('pagehide', () => revoke('pagehide'));
window.addEventListener('resize', () => { if (!landscape()) revoke('portrait'); render(); });
document.addEventListener('visibilitychange', () => { if (document.hidden) revoke('hidden'); });
document.addEventListener('keydown', event => { if (event.key === 'Escape') revoke('escape'); });
// Static integration page. These hooks let the existing host pause/capture its
// own view without pretending that a render loop or fishing simulation exists.
const visual = { start() {}, stop() {}, pause() {}, resume() {}, state: () => ({ running: false, frames: 0, kind: 'static-game-data' }) };
window.dualProbe = { documentId, visual, state: () => ({ documentId, ...authority.state(), snapshot, revision, lastReply, lastActivationReply, lastRevoke, hostPolicy, castState, lastFlight: flightNotices.last, flightCount: flightNotices.count, inputState: castInput.state() }),
  sendRaw: message => window.chrome.webview.postMessage(message), cancel: () => revoke('test-cancel') };
render();

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { WaterView } from './water-view.mjs';

const state = extra => ({ enabled: true, geometryVersion: 1, waterId: 'development-still-water', spotId: 'bank-01',
  bounds: { minXmm: -12000, maxXmm: 12000, minZmm: 12000, maxZmm: 40000 }, surfaceYmm: 0, floatDepthMm: 200, clearanceMm: 100,
  bed: [{ zMm: 3000, depthMm: 100 }, { zMm: 12000, depthMm: 1200 }, { zMm: 40000, depthMm: 3000 }, { zMm: 80000, depthMm: 3500 }],
  target: { u: 5000, v: 5000, xMm: 0, zMm: 26000, depthMm: 2100, requiredLineMm: 28000 },
  prediction: { xMm: 0, zMm: 15000, depthMm: 1400, powerPermille: 400 }, landing: null, ...extra });
const packet = (revision = 1, extra = {}) => ({ kind: 'game-water-state', documentId: 'current', revision, state: state(extra) });

test('water projection accepts current newer host data, keeps legacy disabled and copies only display fields', () => {
  const view = new WaterView('current'); assert.equal(view.state(), null); assert.equal(view.diagram(), null);
  assert.equal(view.apply({ ...packet(), state: { enabled: false } }), true); assert.deepEqual(view.state(), { enabled: false });
  for (const p of [packet(1), packet(0), { ...packet(2), documentId: 'old' }, { ...packet(2), kind: 'game-flight-state' }]) assert.equal(view.apply(p), false);
  const p = packet(2, { fishIds: ['private'], velocity: 99 }); assert.equal(view.apply(p), true);
  p.state.bounds.minXmm = 0; p.state.bed[0].depthMm = 999; p.state.target.u = 0;
  const snapshot = view.state(); snapshot.prediction.xMm = 999; snapshot.bed.push({ zMm: 100000, depthMm: 2 });
  assert.equal(view.state().bounds.minXmm, -12000); assert.equal(view.state().bed[0].depthMm, 100);
  assert.equal(view.state().target.u, 5000); assert.equal(view.state().prediction.xMm, 0); assert.equal(view.state().bed.length, 4);
  assert.equal('fishIds' in snapshot, false); assert.equal('velocity' in snapshot, false);
  assert.equal(view.apply({ ...packet(3), state: { enabled: false } }), true); assert.equal(view.diagram(), null);
});

test('geometry and all optional measurement shapes are bounded and malformed snapshots do not consume revision', () => {
  const view = new WaterView('current'), base = state();
  const invalid = [
    { enabled: 1 }, { enabled: false, geometryVersion: 1 }, { geometryVersion: 0 }, { geometryVersion: 1.5 }, { geometryVersion: '1' },
    { waterId: '' }, { spotId: 'x'.repeat(97) }, { bounds: { ...base.bounds, minXmm: 12000 } }, { bounds: { ...base.bounds, maxZmm: Infinity } },
    { surfaceYmm: 0.1 }, { floatDepthMm: 0 }, { clearanceMm: -1 }, { bed: [] }, { bed: [base.bed[0]] },
    { bed: Array.from({ length: 129 }, (_, i) => ({ zMm: i * 1000, depthMm: 100 })) },
    { bed: [base.bed[1], base.bed[0], base.bed[3]] }, { bed: [base.bed[0], base.bed[0], base.bed[3]] },
    { bed: [{ zMm: 13000, depthMm: 1000 }, base.bed[3]] }, { bed: [base.bed[0], { zMm: 39000, depthMm: 1000 }] },
    { bed: [{ zMm: 3000, depthMm: 0 }, base.bed[3]] }, { bed: [{ zMm: 3000, depthMm: 100001 }, base.bed[3]] },
    { target: undefined }, { target: { ...base.target, u: 10001 } }, { target: { ...base.target, xMm: 12001 } },
    { target: { ...base.target, requiredLineMm: '28000' } }, { target: { ...base.target, requiredLineMm: 100000001 } },
    { target: { ...base.target, depthMm: 299 } }, { prediction: undefined }, { prediction: { ...base.prediction, powerPermille: 1001 } },
    { prediction: { ...base.prediction, xMm: NaN } }, { landing: undefined },
    { landing: { xMm: 0, zMm: 3000, depthMm: 299, accepted: true, reason: 'water-sampled' } },
    { landing: { xMm: 0, zMm: 1000, depthMm: 0, accepted: false, reason: null } }
  ];
  for (const values of invalid) assert.equal(view.apply(packet(1, values)), false, JSON.stringify(values));
  assert.equal(view.revision, 0); assert.equal(view.apply(packet(1, { target: null, prediction: null, landing: null })), true);
});

test('diagram preserves left/right and near/far orientation without inventing an in-chart hit', () => {
  const view = new WaterView('current'); view.apply(packet(1, {
    target: { u: 0, v: 0, xMm: -12000, zMm: 40000, depthMm: 3000, requiredLineMm: 41798 },
    prediction: { xMm: 13000, zMm: 41000, depthMm: 3200, powerPermille: 1000 },
    landing: { xMm: 0, zMm: 5000, depthMm: 0, accepted: false, reason: 'float-clearance-insufficient' }
  }));
  const before = view.diagram(); assert.deepEqual(before.target, { x: 0, y: 0, visible: true, outside: [] });
  assert.ok(before.prediction.x > 480 && before.prediction.y < 0); assert.equal(before.prediction.visible, false);
  assert.deepEqual(before.prediction.outside, ['right', 'far']); assert.ok(before.landing.y > 280);
  assert.equal(before.landing.visible, false); assert.deepEqual(before.landing.outside, ['near']);
  for (let i = 0; i < 30; i++) assert.deepEqual(view.diagram(), before);
  const key = view.geometryKey(); view.apply(packet(2)); assert.equal(view.geometryKey(), key);
  view.apply(packet(3, { geometryVersion: 2 })); assert.notEqual(view.geometryKey(), key);
});

// Minimal DOM harness loads the actual complete page and dispatches its real
// handlers. CTM is controlled here; native browser input remains a separate check.
async function pageFixture(sceneModule) {
  const nodes = new Map(), scrolls = [];
  class TestElement {
    constructor(tag = 'div') { this.tagName = tag.toUpperCase(); this.children = []; this.listeners = new Map(); this.attributes = {}; this.dataset = {}; this.value = ''; this.disabled = false; this.captured = new Set(); }
    get options() { return this.children; }
    get valueAsNumber() { return Number(this.value); }
    setAttribute(key, value) { this.attributes[key] = String(value); }
    append(...children) { for (const child of children) { child.parentElement = this; this.children.push(child); if (child.id) nodes.set(child.id, child); } }
    replaceChildren(...children) { this.children = []; this.append(...children); }
    addEventListener(type, callback) { if (!this.listeners.has(type)) this.listeners.set(type, []); this.listeners.get(type).push(callback); }
    closest(selector) { return selector.split(',').includes(this.tagName.toLowerCase()) ? this : this.parentElement?.closest(selector) ?? null; }
    setPointerCapture(id) { this.captured.add(id); }
    hasPointerCapture(id) { return this.captured.has(id); }
    click() { this.dispatch('click'); }
    scrollIntoView() { scrolls.push(this.id); }
    dispatch(type, values = {}) {
      const event = { type, target: this, detail: 1, repeat: false, timeStamp: 10, button: 0, pointerId: 3, pointerType: 'mouse', preventDefault() {}, ...values };
      if (type !== 'click' || !this.disabled) for (const callback of this.listeners.get(type) ?? []) callback(event);
      return event;
    }
  }
  const html = await readFile(new URL('./game.html', import.meta.url), 'utf8');
  for (const [, tag, id] of html.matchAll(/<([a-z]+)\b[^>]*\bid="([^"]+)"[^>]*>/g)) { const node = new TestElement(tag); node.id = id; nodes.set(id, node); }
  const aimSvg = nodes.get('water-aim-svg'); aimSvg.viewBox = { baseVal: { x: 0, y: 0, width: 480, height: 280 } };
  aimSvg.getScreenCTM = () => ({ a: 1.5, b: 0, c: 0, d: 1.5, e: 100, f: 50,
    inverse: () => ({ a: 2 / 3, b: 0, c: 0, d: 2 / 3, e: -100 / 1.5, f: -50 / 1.5 }) });
  const windowEvents = new TestElement(), documentEvents = new TestElement(), messages = []; let receive;
  globalThis.Element = TestElement; globalThis.innerWidth = 1200; globalThis.innerHeight = 800;
  globalThis.document = { body: new TestElement('body'), hidden: false, hasFocus: () => true, createElement: tag => new TestElement(tag),
    getElementById: id => { assert.ok(nodes.has(id), `Missing DOM id ${id}`); return nodes.get(id); }, addEventListener: (...args) => documentEvents.addEventListener(...args) };
  globalThis.window = { chrome: { webview: { postMessage: message => messages.push(message), addEventListener: (type, callback) => { receive = callback; } } }, addEventListener: (...args) => windowEvents.addEventListener(...args) };
  let source = await readFile(new URL('./page.mjs', import.meta.url), 'utf8');
  if (sceneModule) source = source.replace("import('./game-scene.mjs')", 'Promise.resolve(window.testSceneModule)');
  window.testSceneModule = sceneModule;
  source = source.replace(/from '([^']+)'/g, (_, path) => `from '${new URL(path === './lease-session.mjs' ? '../../diagnostics/state-owner/lease-session.mjs' : path, import.meta.url).href}'`);
  await import(`data:text/javascript;base64,${Buffer.from(`${source}\n// fresh water page ${crypto.randomUUID()}`).toString('base64')}`);
  const probe = window.dualProbe, emit = message => receive({ data: { documentId: probe.documentId, ...message } });
  const snapshot = { HasConfirmedState: true, Faulted: false, HostPaused: false, ShutdownConfirmed: false, MenuOpen: false, SessionId: null, PendingResult: false,
    Sequence: '1', RigRevision: '1', RulesVersion: '1', WalletMinor: '12000', Stamina: 100, BaitDefinitionKey: 'worm', BaitCount: 12, Creel: [], CreelCount: 0, CreelCapacity: 2, CreelOffset: 0 };
  emit({ kind: 'role', role: '交互' }); emit({ kind: 'game-snapshot', revision: 1, snapshot }); emit({ kind: 'lease', revision: 1, cancellationEpoch: 0, lease: 'first' });
  return { nodes, messages, emit, probe, snapshot, scrolls, click: id => nodes.get(id).dispatch('click'),
    key: (code, values = {}) => documentEvents.dispatch('keydown', { code, key: code === 'Escape' ? code : code.slice(3).toLowerCase(), ...values }),
    pointer: (type, values) => nodes.get('cast-target').dispatch(type, { target: aimSvg, ...values }),
    water: (revision, extra = {}) => emit({ ...packet(revision, extra), documentId: probe.documentId }) };
}

const extended = () => ({ waterBounds: { minXmm: -40000, maxXmm: 40000, minZmm: 3000, maxZmm: 80000 },
  anchor: { xMm: 0, yMm: 1500, zMm: 0 } });
const sessionId = '10000000-0000-0000-0000-000000000019';
test('scene geometry contains the complete near-to-far bed, bank anchor and a copied landing identity', () => {
  const view = new WaterView('current'), geometry = extended();
  const landed = { xMm: 0, zMm: 15000, depthMm: 1500, accepted: true, reason: 'water-sampled', sessionId };
  for (const invalid of [{ anchor: geometry.anchor }, { waterBounds: geometry.waterBounds },
    { ...geometry, anchor: { ...geometry.anchor, yMm: 0 } }, { ...geometry, anchor: { ...geometry.anchor, zMm: 4000 } },
    { ...geometry, waterBounds: { ...geometry.waterBounds, maxZmm: 80001 } }, { ...geometry, landing: { ...landed, sessionId: '' } }]) {
    assert.equal(view.apply(packet(1, invalid)), false);
  }
  assert.equal(view.apply({ ...packet(1, geometry), sceneEnabled: 1 }), false);
  assert.equal(view.apply({ ...packet(1, { ...geometry, landing: landed }), sceneEnabled: true }), true);
  const a = view.state(); a.waterBounds.minXmm = 1; a.anchor.xMm = 7; a.landing.sessionId = '';
  assert.deepEqual(view.state().waterBounds, geometry.waterBounds); assert.deepEqual(view.state().anchor, geometry.anchor);
  assert.equal(view.state().landing.sessionId, sessionId);
});

function fakeScene() {
  const log = { active: false, updates: [], disposed: false, created: 0 };
  return { log, createGameScene(canvas, { onFault }) {
    assert.equal(canvas.id, 'scene-canvas'); log.created++; log.fault = onFault;
    return { state: () => ({ active: log.active, running: log.active }), resize() {}, update: data => log.updates.push(data),
      setActive: active => { log.active = active; }, dispose: () => { log.disposed = true; log.active = false; },
      projectAim: (u, v) => ({ clientX: u, clientY: v }), pickAim: (x, y) => x === 4000 && y === 8000 ? { u: x, v: y, geometryVersion: 1 } : null };
  } };
}
async function enableScene(f) {
  f.emit({ kind: 'host-policy', revision: 1, canRun: true });
  f.emit({ kind: 'game-water-state', revision: 1, sceneEnabled: true, state: state(extended()) });
  await Promise.resolve();
  assert.equal(f.probe.state().sceneState.mode, 'scene');
}
function enableCampCatalog(f) {
  const lake = {id:'development-still-water',name:'静水湾',region:'中国内陆',description:'岸边芦苇',species:['鲤鱼']};
  f.emit({kind:'game-spot-state',revision:1,state:{activeId:lake.id,spots:[lake]}});
}
test('player camp keyboard waits for the host open transition then scrolls once, keeps every destination and returns to the lake', async () => {
  const f=await pageFixture(fakeScene()); await enableScene(f); enableCampCatalog(f);
  f.key('KeyM'); assert.equal(f.messages.length,1); assert.equal(f.messages[0].kind,'game-menu'); assert.equal(f.messages[0].open,true);
  f.key('KeyC'); assert.equal(f.messages.length,1); assert.deepEqual(f.scrolls,[]); assert.equal(f.nodes.get('camp-home').hidden,true);
  f.emit({kind:'game-reply',transportId:f.messages[0].transportId,code:'menu-updated'});
  f.emit({kind:'game-snapshot',revision:2,snapshot:{...f.snapshot,MenuOpen:true}});
  assert.deepEqual(f.scrolls,['spot-menu']); assert.equal(f.nodes.get('camp-home').hidden,false);
  f.emit({kind:'game-snapshot',revision:3,snapshot:{...f.snapshot,MenuOpen:true}}); assert.equal(f.scrolls.length,1);
  f.key('KeyC'); f.click('camp-overview'); f.click('camp-go-map'); assert.deepEqual(f.scrolls,['spot-menu','creel-section','camp-home','spot-menu']);
  f.key('KeyI'); assert.equal(f.scrolls.length,4); assert.equal(f.nodes.get('camp-go-gear').disabled,true);
  f.emit({kind:'game-shop-state',revision:1,state:{schemaVersion:3,available:true,reason:null,walletMinor:'12000',baitCount:12,foodCount:2,netOwned:false,netEquipped:false,catalogVersion:'1',netPriceMinor:'2400',baitPackPriceMinor:'20',foodPriceMinor:'20',baitPackUnits:5,foodStaminaRestore:20,maxPacks:100,quote:null,retry:null}});
  f.key('KeyT'); assert.equal(f.scrolls.at(-1),'shop-food'); f.click('camp-go-tackle'); assert.equal(f.scrolls.at(-1),'shop-net');
  f.key('Escape'); assert.equal(f.messages.length,2); assert.equal(f.messages[1].open,false); assert.equal(f.probe.state().lease,'first');
  f.emit({kind:'game-snapshot',revision:4,snapshot:{...f.snapshot,MenuOpen:false}}); assert.equal(f.nodes.get('camp-home').hidden,true);
});
test('player camp ignores editable targets, repeats, modifiers, portrait and revoked leases; old input keeps its escape semantics', async () => {
  const f=await pageFixture(fakeScene()); await enableScene(f); enableCampCatalog(f);
  for (const code of ['KeyM','Escape']) for (const extra of [{target:f.nodes.get('food-packs')},{target:f.nodes.get('equipment-line')},{ctrlKey:true},{repeat:true},{isComposing:true}]) f.key(code,extra);
  globalThis.innerHeight=1600; f.key('KeyC'); globalThis.innerHeight=800;
  assert.equal(f.messages.length,0); assert.equal(f.probe.state().lease,'first');
  f.emit({kind:'lease',revision:2,cancellationEpoch:0,lease:null}); f.key('Escape'); f.key('KeyM'); assert.equal(f.messages.length,0);
  const legacy=await pageFixture(); legacy.water(1); legacy.key('KeyC'); assert.equal(legacy.messages.length,0); legacy.key('Escape');
  assert.equal(legacy.messages.at(-1).kind,'revoke'); assert.equal(legacy.messages.at(-1).reason,'escape');
});
test('real page shares cast input across camera and map, cancelling a held gesture before a switch or graphics fault', async () => {
  const renderer = fakeScene(), f = await pageFixture(renderer); await enableScene(f);
  const c = f.nodes.get('scene-canvas'), press = () => c.dispatch('pointerdown', { clientX: 4000, clientY: 8000, buttons: 1 });
  c.dispatch('pointerdown', { clientX: -1, clientY: 8000, buttons: 1 }); assert.equal(f.messages.length, 0);
  press(); assert.deepEqual(f.messages[0].aim, { u: 4000, v: 8000, geometryVersion: 1 });
  f.click('scene-to-map'); c.dispatch('pointerup', { buttons: 0 });
  assert.equal(renderer.log.active, false); assert.equal(f.probe.pickSceneAim(4000,8000), null);
  assert.deepEqual(f.messages.map(m => m.action), ['begin','cancel']);
  f.click('scene-to-water'); assert.equal(renderer.log.created, 1); assert.equal(renderer.log.active, true);
  press(); c.dispatch('pointerup', { buttons: 0 });
  assert.equal(f.messages.at(-1).action, 'release'); assert.equal('aim' in f.messages.at(-1), false);
  f.emit({ kind: 'game-cast-state', revision: 1, phase: 'committed', inputSequence: '2', elapsedMilliseconds: 250, powerPermille: 400 });
  press(); renderer.log.fault('webgl-context-lost'); c.dispatch('pointerup', { buttons: 0 });
  assert.equal(renderer.log.disposed, true); assert.equal(f.probe.state().sceneState.fault, 'webgl-context-lost');
  assert.deepEqual(f.messages.map(m => m.action), ['begin','cancel','begin','release','begin','cancel']);
  f.click('scene-to-water'); assert.equal(f.probe.state().sceneState.mode, 'map');
  f.pointer('pointerdown', { clientX: 280, clientY: 365, buttons: 1 }); assert.deepEqual(f.messages.at(-1).aim, { u: 2500, v: 7500, geometryVersion: 1 });
});
test('observer renders current-session facts without sending controls and obeys native pause', async () => {
  const renderer = fakeScene(), f = await pageFixture(renderer); f.emit({ kind: 'role', role: '只读' }); await enableScene(f);
  const c = f.nodes.get('scene-canvas'); c.dispatch('pointerdown', { clientX: 4000, clientY: 8000, buttons: 1 });
  assert.equal(f.messages.length, 0); assert.equal(renderer.log.active, true);
  const landing = { sessionId, xMm: 0, zMm: 15000, depthMm: 1400, accepted: true, reason: 'water-sampled' };
  f.emit({ kind: 'game-water-state', revision: 2, sceneEnabled: true, state: state({ ...extended(), landing }) });
  assert.equal(renderer.log.updates.at(-1).water.landing, null);
  f.emit({ kind: 'game-snapshot', revision: 2, snapshot: { ...f.snapshot, SessionId: sessionId } });
  assert.deepEqual(renderer.log.updates.at(-1).water.landing, landing);
  f.emit({ kind: 'host-policy', revision: 2, canRun: false }); assert.equal(renderer.log.active, false);
  f.probe.visual.resume(); assert.equal(renderer.log.active, false);
  f.emit({ kind: 'host-policy', revision: 3, canRun: true }); assert.equal(renderer.log.active, true);
  assert.equal(f.messages.length, 0);
});

test('actual page freezes SVG pointer aim, shows host-only markers and hides near off-chart landing', async () => {
  const f = await pageFixture(); f.water(1, { target: null, prediction: null });
  assert.equal(f.nodes.get('cast-legacy').hidden, true); assert.equal(f.nodes.get('water-aim-panel').hidden, false);
  f.pointer('pointerdown', { clientX: 280, clientY: 20, buttons: 1 }); assert.equal(f.messages.length, 0);
  f.pointer('pointerdown', { clientX: 280, clientY: 365, buttons: 1 });
  assert.deepEqual(f.messages[0].aim, { u: 2500, v: 7500, geometryVersion: 1 });
  assert.equal(f.nodes.get('water-target-mark').attributes.visibility, 'hidden');
  f.pointer('pointermove', { clientX: 800, clientY: 500, buttons: 1 });
  f.pointer('pointerup', { clientX: 800, clientY: 500, buttons: 0 });
  assert.deepEqual(f.messages.map(m => m.action), ['begin', 'release']); assert.equal('aim' in f.messages[1], false);
  assert.deepEqual(f.probe.state().inputState.aim, { u: 2500, v: 7500, geometryVersion: 1 });
  f.water(2, { landing: { xMm: 0, zMm: 5000, depthMm: 0, accepted: false, reason: 'float-clearance-insufficient' } });
  assert.equal(f.nodes.get('water-target-mark').attributes.visibility, 'visible');
  assert.equal(f.nodes.get('water-landing-mark').attributes.visibility, 'hidden');
  assert.match(f.nodes.get('water-landing-status').textContent, /净空不足.*图外（近岸方向）/);
  assert.equal(f.nodes.get('water-outside').hidden, false); assert.equal(f.messages.length, 2);
  f.emit({ kind: 'game-cast-state', revision: 1, inputSequence: '1', phase: 'committed', powerPermille: 400, elapsedMilliseconds: 400 });
  f.click('cast'); assert.deepEqual(f.messages.at(-1).aim, { u: 2500, v: 7500, geometryVersion: 1 });
});

test('actual page keeps old mode, defaults aimed buttons to center and cancels stale geometry before release', async () => {
  const f = await pageFixture(); f.click('cast'); assert.equal('aim' in f.messages[0], false); f.click('cast-cancel');
  f.water(1, { target: null, prediction: null }); f.click('cast');
  assert.deepEqual(f.messages.at(-1).aim, { u: 5000, v: 5000, geometryVersion: 1 });
  const count = f.messages.length; f.water(2, { geometryVersion: 2, target: null, prediction: null });
  assert.equal(f.messages.length, count + 1); assert.equal(f.messages.at(-1).action, 'cancel'); assert.equal('aim' in f.messages.at(-1), false);
  f.click('cast-release'); assert.equal(f.messages.length, count + 1); assert.equal(f.probe.state().inputState, null);
  f.click('cast'); assert.equal(f.messages.at(-1).aim.geometryVersion, 2);
  f.emit({ kind: 'game-water-state', revision: 3, state: { enabled: false } });
  assert.equal(f.messages.at(-1).action, 'cancel'); assert.equal(f.nodes.get('cast-legacy').hidden, false);
  f.click('cast'); assert.equal(f.messages.at(-1).action, 'begin'); assert.equal('aim' in f.messages.at(-1), false);
  const before = f.messages.length; f.emit({ kind: 'game-water-state', revision: 4, state: { enabled: true } });
  assert.equal(f.messages.length, before); assert.equal(f.probe.state().waterRevision, 3);
});

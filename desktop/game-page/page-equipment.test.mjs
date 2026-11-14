import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const id = n => `10000000-0000-0000-0000-${String(n).padStart(12, '0')}`;
const row = (n, kind, extra = {}) => ({ id: id(n), definitionKey: `starter-${kind}`, kind, revision: '1', location: 'rig-slot', slot: kind,
  reelId: null, initialLineMm: 0, remainingLineMm: 0, ...extra });
const inventory = () => [row(1, 'rod'), row(2, 'reel'), row(3, 'line', { location: 'reel-spool', slot: null, reelId: id(2), initialLineMm: 50000, remainingLineMm: 32000 }),
  row(4, 'hook'), row(5, 'bobber'), row(6, 'sinker'), row(7, 'rod', { location: 'inventory', slot: null })];

// This small DOM surface exercises the real page module, event handlers and
// host-message order. It does not claim browser layout or native input coverage.
async function pageFixture() {
  const nodes = new Map();
  class TestElement {
    constructor(tag = 'div') { this.tagName = tag.toUpperCase(); this.id = ''; this.children = []; this.listeners = new Map(); this.attributes = {}; this.dataset = {}; this.value = ''; this.disabled = false; }
    get options() { return this.children; }
    get valueAsNumber() { return Number(this.value); }
    setAttribute(key, value) { this.attributes[key] = String(value); }
    append(...children) { for (const child of children) { child.parentElement = this; this.children.push(child); if (child.id) nodes.set(child.id, child); } }
    replaceChildren(...children) { this.children = []; this.append(...children); if (this.tagName === 'SELECT') this.value = this.children[0]?.value ?? ''; }
    addEventListener(type, callback) { if (!this.listeners.has(type)) this.listeners.set(type, []); this.listeners.get(type).push(callback); }
    closest(selector) { return selector.split(',').includes(this.tagName.toLowerCase()) ? this : this.parentElement?.closest(selector) ?? null; }
    dispatch(type, values = {}, force = false) {
      const event = { target: this, detail: 1, repeat: false, prevented: false, preventDefault() { this.prevented = true; }, ...values };
      if (type !== 'click' || !this.disabled || force) for (const callback of this.listeners.get(type) ?? []) callback(event);
      return event;
    }
  }
  const html = await readFile(new URL('./game.html', import.meta.url), 'utf8');
  for (const match of html.matchAll(/<([a-z]+)\b[^>]*\bid="([^"]+)"[^>]*>/g)) { const node = new TestElement(match[1]); node.id = match[2]; nodes.set(node.id, node); }
  nodes.get('bait-packs').value = '1'; nodes.get('food-packs').value = '1';
  const windowEvents = new TestElement(), documentEvents = new TestElement(), messages = []; let receive;
  globalThis.Element = TestElement; globalThis.innerWidth = 1200; globalThis.innerHeight = 800;
  globalThis.document = { body: new TestElement('body'), hidden: false, hasFocus: () => true, createElement: tag => new TestElement(tag),
    getElementById: id => { assert.ok(nodes.has(id), `Missing DOM id ${id}`); return nodes.get(id); }, addEventListener: (...args) => documentEvents.addEventListener(...args) };
  globalThis.window = { chrome: { webview: { postMessage: message => messages.push(message), addEventListener: (type, callback) => { assert.equal(type, 'message'); receive = callback; } } },
    addEventListener: (...args) => windowEvents.addEventListener(...args) };
  let source = await readFile(new URL('./page.mjs', import.meta.url), 'utf8');
  source = source.replace(/from '([^']+)'/g, (_, path) => `from '${new URL(path === './lease-session.mjs' ? '../../diagnostics/state-owner/lease-session.mjs' : path, import.meta.url).href}'`);
  await import(`data:text/javascript;base64,${Buffer.from(`${source}\n// fresh page ${crypto.randomUUID()}`).toString('base64')}`);
  const probe = window.dualProbe, documentId = probe.documentId;
  const snapshot = { HasConfirmedState: true, Faulted: false, HostPaused: false, ShutdownConfirmed: false, MenuOpen: true, SessionId: null, PendingResult: false,
    Sequence: '1', RigRevision: '1', RulesVersion: '1', WalletMinor: '12000', Stamina: 80, BaitDefinitionKey: 'worm', BaitCount: 12, Creel: [], CreelCount: 0, CreelCapacity: 2, CreelOffset: 0 };
  const shop = { schemaVersion: 3, available: true, reason: null, walletMinor: '12000', baitCount: 12, foodCount: 2, netOwned: false, netEquipped: false,
    catalogVersion: '1', netPriceMinor: '2400', baitPackPriceMinor: '20', foodPriceMinor: '20', baitPackUnits: 5, foodStaminaRestore: 20, maxPacks: 100, quote: null, retry: null };
  const equipment = { schemaVersion: 3, available: true, reason: null, lease: 'first', stateSequence: '1', rigRevision: '1', inventoryRevision: '1', equipmentVersion: '1', archivedItemsCount: 0, archivedLineMm: '0', items: inventory(), issues: [], retry: null };
  const spares = { schemaVersion: 3, available: true, reason: null, lease: 'first', stateSequence: '1', catalogVersion: '1', walletMinor: '12000', instancesCount: 7,
    items: [{ sku: 'hook-single', definitionKey: 'single-hook', kind: 'hook', priceMinor: '35', lineLengthMm: 0 }], quote: null, retry: null };
  const camp = { available: false, reason: 'camp-balance-sufficient', lease: 'first', phase: 'idle', elapsedMilliseconds: 0, requiredMilliseconds: 60000, rewardMinor: '800', stateSequence: '1' };
  let snapshotRevision = 0, shopRevision = 0, equipmentRevision = 0, sparesRevision = 0, campRevision = 0, leaseRevision = 0;
  const emit = message => receive({ data: { documentId, ...message } });
  const broadcast = () => {
    emit({ kind: 'game-snapshot', revision: ++snapshotRevision, snapshot: { ...snapshot } });
    // Deliberately mirror the real host: shop state precedes equipment state.
    emit({ kind: 'game-shop-state', revision: ++shopRevision, state: structuredClone(shop) });
    emit({ kind: 'game-equipment-state', revision: ++equipmentRevision, state: structuredClone(equipment) });
    emit({ kind: 'game-spares-state', revision: ++sparesRevision, state: structuredClone(spares) });
    emit({ kind: 'game-camp-state', revision: ++campRevision, state: structuredClone(camp) });
  };
  const grant = lease => emit({ kind: 'lease', revision: ++leaseRevision, cancellationEpoch: 0, lease });
  emit({ kind: 'role', role: '交互' }); emit({ kind: 'game-snapshot', revision: ++snapshotRevision, snapshot: { ...snapshot } }); grant('first'); broadcast();
  return { nodes, messages, snapshot, shop, equipment, spares, camp, probe, broadcast, grant, emit,
    click: (id, event, force) => nodes.get(id).dispatch('click', event, force),
    select(id, value) { nodes.get(id).value = value; nodes.get(id).dispatch('change'); },
    reply(code, transportId = messages.at(-1).transportId) { emit({ kind: 'game-reply', transportId, code }); },
    advance() { snapshot.Sequence = String(Number(snapshot.Sequence) + 1); equipment.stateSequence = spares.stateSequence = camp.stateSequence = snapshot.Sequence; },
    replaceRod() { equipment.items = equipment.items.map(item => item.id === id(1) ? { ...item, location: 'inventory', slot: null, revision: '2' } : item.id === id(7) ? { ...item, location: 'rig-slot', slot: 'rod', revision: '2' } : item); this.advance(); }
  };
}

test('page initializes after empty placeholders with current equipment selected and no accidental unload action', async () => {
  const f = await pageFixture();
  for (const [slot, n] of [['rod', 1], ['reel', 2], ['hook', 4], ['bobber', 5], ['sinker', 6]]) {
    assert.equal(f.nodes.get(`equipment-slot-${slot}`).value, id(n)); assert.equal(f.nodes.get(`equipment-apply-${slot}`).disabled, true);
    f.click(`equipment-apply-${slot}`);
  }
  assert.equal(f.nodes.get('equipment-line').value, id(3)); assert.equal(f.nodes.get('equipment-reel').value, id(2));
  assert.equal(f.nodes.get('equipment-move-line').disabled, true); assert.equal(f.messages.length, 0);
});

const spots = [
  { id: 'development-still-water', name: '静水湾', region: '江南', description: '湖湾芦苇', species: ['鲫鱼','鲤鱼'] },
  { id: 'river-bend', name: '江湾', region: '长江', description: '江岸回水', species: ['草鱼','鳊鱼'] },
  { id: 'coastal-bay', name: '海湾', region: '东海', description: '海岸礁湾', species: ['黑鲷','花鲈','黄鳍鲷'] },
];
function spotState(f, revision, activeId) { f.emit({ kind: 'game-spot-state', revision, state: { activeId, spots } }); }
function spotWater(f, revision, geometryVersion, waterId) {
  f.emit({ kind: 'game-water-state', revision, state: { enabled: true, geometryVersion, waterId, spotId: 'bank-01',
    bounds: { minXmm: -12000, maxXmm: 12000, minZmm: 12000, maxZmm: 40000 }, surfaceYmm: 0, floatDepthMm: 200, clearanceMm: 100,
    bed: [{ zMm: 3000, depthMm: 1000 }, { zMm: 80000, depthMm: 3500 }], target: null, prediction: null, landing: null } });
}

test('shared spot menu uses only the current geometry version and waits for confirmed location, water and matching reply', async () => {
  const f = await pageFixture(); assert.equal(f.nodes.get('spot-menu').hidden, true);
  spotState(f, 1, spots[0].id); assert.equal(f.nodes.get('spot-menu').hidden, false);
  assert.equal(f.nodes.get('spot-select-river-bend').disabled, true);
  spotWater(f, 1, 1, spots[0].id); f.click('spot-select-river-bend');
  const request = f.messages.at(-1);
  assert.deepEqual(Object.keys(request).sort(), ['documentId','expectedVersion','kind','lease','spotId','transportId']);
  assert.deepEqual([request.kind,request.spotId,request.lease,request.expectedVersion], ['game-spot-select','river-bend','first',1]);
  assert.equal(f.nodes.get('scene-spot-name').textContent, '静水湾');
  f.click('quote-bait', {}, true); f.click('menu', {}, true); assert.equal(f.messages.length, 1);
  f.reply('spot-selected', request.transportId); assert.equal(f.probe.state().spotPending, 'river-bend');
  spotState(f, 2, 'river-bend'); assert.equal(f.nodes.get('scene-spot-name').textContent, '江湾');
  assert.equal(f.nodes.get('map-spot-name').textContent, '江湾 · 俯视钓位'); assert.equal(f.probe.state().spotPending, 'river-bend');
  spotWater(f, 2, 2, 'river-bend'); assert.equal(f.probe.state().spotPending, null);
  f.click('spot-select-coastal-bay'); const second = f.messages.at(-1); assert.equal(second.expectedVersion, 2);
  f.reply('spot-stale', request.transportId); assert.equal(f.probe.state().spotPending, 'coastal-bay');
  spotWater(f, 3, 3, 'coastal-bay'); spotState(f, 3, 'coastal-bay');
  assert.equal(f.probe.state().spotPending, 'coastal-bay'); f.reply('spot-selected', second.transportId);
  assert.equal(f.probe.state().spotPending, null);
  f.snapshot.MenuOpen = false; f.broadcast(); f.click('cast'); assert.equal(f.messages.at(-1).aim.geometryVersion, 3);
});

test('spot menu cannot send during casts, another transaction or after losing its lease; failures leave the confirmed HUD intact', async () => {
  const f = await pageFixture(); spotWater(f, 1, 1, spots[0].id); spotState(f, 1, spots[0].id);
  f.snapshot.SessionId = id(42); f.broadcast(); f.click('spot-select-river-bend', {}, true); assert.equal(f.messages.length, 0);
  f.snapshot.SessionId = null; f.broadcast(); f.click('food-eat'); f.click('spot-select-river-bend', {}, true); assert.equal(f.messages.length, 1);
  f.reply('food-candidate'); f.broadcast(); f.click('spot-select-river-bend'); f.reply('spot-switch-busy');
  assert.equal(f.probe.state().spotPending, null); assert.equal(f.nodes.get('scene-spot-name').textContent, '静水湾');
  assert.match(f.nodes.get('spot-menu').children[1].textContent, /先结束当前一竿/);
  f.click('spot-select-river-bend'); const old = f.messages.at(-1).transportId;
  f.grant(null); assert.equal(f.probe.state().spotPending, null); f.click('spot-select-river-bend', {}, true);
  f.reply('spot-selected', old); assert.equal(f.messages.length, 3);
  f.emit({ kind: 'role', role: '只读' }); f.grant('new'); f.click('spot-select-river-bend', {}, true); assert.equal(f.messages.length, 3);
});

test('camp holds all trading menus through work and retry, then unlocks immediately after confirmed explicit payment', async () => {
  const f = await pageFixture(); f.snapshot.WalletMinor = f.shop.walletMinor = f.spares.walletMinor = '0';
  Object.assign(f.camp, { available: true, reason: null }); f.broadcast(); f.select('equipment-slot-rod', id(7));
  f.click('camp-start'); assert.equal(f.messages.at(-1).action, 'start'); f.reply('camp-work-started');
  Object.assign(f.camp, { phase: 'working', elapsedMilliseconds: 20000 }); f.broadcast();
  assert.equal(f.nodes.get('camp-elapsed').textContent, '20 / 60 秒'); assert.equal(f.nodes.get('menu').disabled, false);
  for (const button of ['quote-bait', 'spares-quote-hook-single', 'equipment-apply-rod']) {
    assert.equal(f.nodes.get(button).disabled, true); f.click(button, {}, true);
  }
  assert.equal(f.messages.length, 1); Object.assign(f.camp, { phase: 'ready', elapsedMilliseconds: 60000 }); f.broadcast();
  assert.equal(f.messages.length, 1); assert.equal(f.nodes.get('camp-claim').disabled, false); f.click('camp-claim');
  f.reply('camp-work-not-written'); f.camp.phase = 'retry'; f.broadcast();
  assert.equal(f.nodes.get('camp-claim').textContent, '重试领取'); assert.equal(f.nodes.get('quote-bait').disabled, true); assert.equal(f.messages.length, 2);
  f.click('camp-claim'); f.snapshot.WalletMinor = f.shop.walletMinor = f.spares.walletMinor = '800'; f.advance();
  f.reply('camp-work-candidate'); f.camp.phase = 'confirmed'; f.broadcast();
  assert.equal(f.nodes.get('camp-status').textContent, '报酬已确认入账：8.00 银币。');
  for (const button of ['quote-bait', 'spares-quote-hook-single', 'equipment-apply-rod']) assert.equal(f.nodes.get(button).disabled, false);
  assert.deepEqual(f.messages.map(m => m.action), ['start', 'claim', 'claim']);
});

test('camp respects a busy shop, permits menu exit, gives recovery guidance and freezes all menus after unknown payment', async () => {
  const f = await pageFixture(); assert.equal(f.nodes.get('camp-start').disabled, true); assert.match(f.nodes.get('camp-status').textContent, /现有银币/);
  Object.assign(f.camp, { available: true, reason: null }); f.broadcast(); f.click('food-eat');
  f.click('camp-start', {}, true); assert.equal(f.messages.length, 1); f.reply('food-candidate'); f.broadcast();
  f.click('camp-start'); f.reply('camp-work-started'); f.camp.phase = 'working'; f.broadcast(); f.click('menu');
  assert.equal(f.messages.at(-1).kind, 'game-menu'); assert.equal(f.messages.at(-1).open, false);
  f.snapshot.MenuOpen = false; Object.assign(f.camp, { available: false, reason: 'menu-required', phase: 'cancelled' }); f.reply('menu-updated'); f.broadcast();
  assert.equal(f.probe.state().campInputState.phase, null); assert.equal(f.nodes.get('camp').hidden, true);
  f.snapshot.MenuOpen = true; f.snapshot.CreelCount = 1; Object.assign(f.camp, { reason: 'camp-creel-not-empty' }); f.broadcast();
  assert.match(f.nodes.get('camp-status').textContent, /出售渔获/);
  f.snapshot.CreelCount = 0; Object.assign(f.camp, { available: true, reason: null, phase: 'ready', elapsedMilliseconds: 60000 }); f.broadcast(); f.click('camp-claim');
  f.reply('camp-work-save-unknown'); f.camp.phase = 'confirmed'; f.broadcast();
  for (const button of ['camp-start', 'camp-claim', 'quote-bait', 'spares-quote-hook-single']) assert.equal(f.nodes.get(button).disabled, true);
  assert.match(f.nodes.get('camp-status').textContent, /待核验/);
});

test('archive button confirms freed slots and exact archived line totals without changing owned gear or money', async () => {
  const f = await pageFixture(); assert.equal(f.nodes.get('equipment-archive-lost').disabled, true);
  f.equipment.items.push(row(8, 'hook', { location: 'lost', slot: null })); f.broadcast();
  assert.equal(f.nodes.get('equipment-archive-lost').disabled, false); assert.match(f.nodes.get('equipment-capacity').textContent, /当前名额 8 \/ 128/);
  f.click('equipment-archive-lost'); assert.equal(f.messages.at(-1).action, 'archive-lost');
  assert.deepEqual(Object.keys(f.messages.at(-1)).sort(), ['kind', 'documentId', 'lease', 'transportId', 'action', 'inputSequence', 'expectedSequence'].sort());
  f.equipment.items = f.equipment.items.filter(item => item.location !== 'lost');
  f.equipment.archivedItemsCount = 1; f.equipment.archivedLineMm = '9007199254740993'; f.advance(); f.reply('equipment-archive-candidate'); f.broadcast();
  assert.equal(f.nodes.get('equipment-archive-lost').disabled, true);
  assert.equal(f.nodes.get('equipment-capacity').textContent, '当前名额 7 / 128 · 已归档 1 件 · 归档累计损失鱼线 9,007,199,254,740.993 米');
  assert.equal(f.equipment.items.length, 7); assert.equal(f.shop.walletMinor, '12000'); assert.equal(f.spares.walletMinor, '12000');
});

test('archive known-not-written blocks both shops and explicit retry releases all three menus', async () => {
  const f = await pageFixture(); f.equipment.items.push(row(8, 'hook', { location: 'lost', slot: null })); f.broadcast();
  f.click('equipment-archive-lost'); f.equipment.retry = { operationId: id(300), action: 'archive-lost' }; f.reply('equipment-not-written'); f.broadcast();
  assert.equal(f.nodes.get('equipment-archive-lost').disabled, true); assert.equal(f.nodes.get('equipment-retry').disabled, false);
  for (const button of ['quote-bait', 'spares-quote-hook-single', 'equipment-archive-lost']) {
    assert.equal(f.nodes.get(button).disabled, true); f.click(button, {}, true);
  }
  assert.equal(f.messages.length, 1); f.click('equipment-retry'); assert.equal(f.messages.at(-1).operationId, id(300));
  f.equipment.retry = null; f.equipment.items.pop(); f.equipment.archivedItemsCount = 1; f.advance(); f.reply('equipment-archive-candidate'); f.broadcast();
  assert.equal(f.nodes.get('quote-bait').disabled, false); assert.equal(f.nodes.get('spares-quote-hook-single').disabled, false);
});

test('archiving obeys competing menu activity, revoked authority and an unknown-save latch', async () => {
  const f = await pageFixture(); f.equipment.items.push(row(8, 'hook', { location: 'lost', slot: null })); f.broadcast();
  f.click('spares-quote-hook-single'); f.click('equipment-archive-lost', {}, true); assert.equal(f.messages.length, 1);
  f.reply('spares-cancelled'); f.broadcast(); f.grant(null);
  assert.equal(f.nodes.get('equipment-archive-lost').disabled, true); f.click('equipment-archive-lost', {}, true); assert.equal(f.messages.length, 1);
  f.equipment.lease = f.spares.lease = 'second'; f.grant('second'); f.broadcast(); f.click('equipment-archive-lost');
  assert.equal(f.messages.length, 2); f.reply('save-outcome-unknown'); f.broadcast();
  assert.equal(f.nodes.get('equipment-archive-lost').disabled, true); f.click('equipment-archive-lost', {}, true); assert.equal(f.messages.length, 2);
});

test('equipment completion enables an immediate shop action in the real shop-before-equipment message order', async () => {
  const f = await pageFixture(); f.select('equipment-slot-rod', id(7)); f.click('equipment-apply-rod');
  assert.equal(f.messages.at(-1).action, 'replace-slot'); assert.equal(f.nodes.get('quote-bait').disabled, true);
  f.click('quote-bait', {}, true); assert.equal(f.messages.length, 1);
  f.replaceRod(); f.reply('assembly-candidate'); f.broadcast();
  assert.equal(f.probe.state().shopInputState.ready, true); assert.equal(f.nodes.get('quote-bait').disabled, false);
  f.click('quote-bait'); assert.equal(f.messages.length, 2); assert.equal(f.messages.at(-1).action, 'quote');
});

test('shop completion enables an immediate equipment action using the resulting sequence', async () => {
  const f = await pageFixture(); f.select('equipment-slot-rod', id(7)); f.click('food-eat');
  assert.equal(f.messages.at(-1).action, 'eat'); assert.equal(f.nodes.get('equipment-apply-rod').disabled, true);
  f.click('equipment-apply-rod', {}, true); assert.equal(f.messages.length, 1);
  f.shop.foodCount = 1; f.snapshot.Stamina = 100; f.advance(); f.reply('food-candidate'); f.broadcast();
  assert.equal(f.nodes.get('equipment-apply-rod').disabled, false); f.click('equipment-apply-rod');
  assert.equal(f.messages.length, 2); assert.equal(f.messages.at(-1).action, 'replace-slot'); assert.equal(f.messages.at(-1).expectedSequence, '2');
});

test('known equipment retry blocks shop intent without discarding its host state, cancellation releases it', async () => {
  const f = await pageFixture(); f.select('equipment-slot-rod', id(7)); f.click('equipment-apply-rod');
  f.equipment.retry = { operationId: id(100), action: 'replace-slot' }; f.reply('equipment-not-written'); f.broadcast();
  assert.equal(f.nodes.get('equipment-retry').disabled, false); assert.equal(f.nodes.get('quote-bait').disabled, true);
  assert.equal(f.probe.state().shopInputState.ready, true); f.click('quote-bait', {}, true); assert.equal(f.messages.length, 1);
  f.click('equipment-cancel'); assert.equal(f.messages.at(-1).action, 'cancel'); f.equipment.retry = null; f.reply('equipment-cancelled'); f.broadcast();
  f.click('quote-bait'); assert.equal(f.messages.at(-1).action, 'quote'); assert.equal(f.messages.length, 3);
});

test('known shop retry blocks equipment intent and cancellation releases the already current projection', async () => {
  const f = await pageFixture(); f.select('equipment-slot-rod', id(7)); f.click('food-eat');
  f.shop.retry = { operationId: id(100), action: 'eat' }; f.reply('shop-not-written'); f.broadcast();
  assert.equal(f.nodes.get('equipment-apply-rod').disabled, true); assert.equal(f.probe.state().equipmentInputState.ready, true);
  f.click('equipment-apply-rod', {}, true); assert.equal(f.messages.length, 1);
  f.click('shop-cancel'); f.shop.retry = null; f.reply('shop-cancelled'); f.broadcast();
  f.click('equipment-apply-rod'); assert.equal(f.messages.at(-1).action, 'replace-slot'); assert.equal(f.messages.length, 3);
});

test('selection preserves only a user choice and follows a new equipped item when untouched', async () => {
  const f = await pageFixture(); f.replaceRod(); f.broadcast(); assert.equal(f.nodes.get('equipment-slot-rod').value, id(7));
  f.select('equipment-slot-rod', ''); f.equipment.items = f.equipment.items.map(item => ({ ...item, revision: '3' })); f.broadcast();
  assert.equal(f.nodes.get('equipment-slot-rod').value, ''); assert.equal(f.nodes.get('equipment-apply-rod').disabled, false);
});

test('lease revocation and unknown persistence still disable both menu paths', async () => {
  const f = await pageFixture(); f.select('equipment-slot-rod', id(7)); f.grant(null);
  assert.equal(f.nodes.get('equipment-apply-rod').disabled, true); assert.equal(f.nodes.get('quote-bait').disabled, true);
  f.click('equipment-apply-rod', {}, true); f.click('quote-bait', {}, true); assert.equal(f.messages.length, 0);
  f.equipment.lease = 'second'; f.grant('second'); f.broadcast(); f.click('equipment-apply-rod');
  f.reply('save-outcome-unknown'); f.broadcast();
  assert.equal(f.nodes.get('equipment-apply-rod').disabled, true); assert.equal(f.nodes.get('quote-bait').disabled, true);
  f.click('equipment-apply-rod', {}, true); f.click('quote-bait', {}, true); assert.equal(f.messages.length, 1);
});

test('equipment button double clicks and native repeated-key defaults are suppressed', async () => {
  const f = await pageFixture(); f.select('equipment-slot-rod', id(7));
  assert.equal(f.nodes.get('equipment-apply-rod').dispatch('keydown', { code: 'Enter', repeat: true }).prevented, true);
  f.click('equipment-apply-rod', { detail: 2 }); assert.equal(f.messages.length, 0);
  f.click('equipment-apply-rod', { detail: 1 }); assert.equal(f.messages.length, 1);
});

test('host schema, pending-result and assembly issues display the matching Chinese explanation', async () => {
  const f = await pageFixture(); const original = structuredClone(f.equipment);
  Object.assign(f.equipment, { schemaVersion: 2, available: false, reason: 'equipment-schema-required', stateSequence: null,
    rigRevision: null, inventoryRevision: null, equipmentVersion: null, items: [], issues: [] }); f.broadcast();
  assert.equal(f.nodes.get('equipment-status').textContent, '此存档尚未支持装备编辑。');
  Object.assign(f.equipment, original, { available: false, reason: 'result-pending', issues: [
    { code: 'line-capacity-exceeded', slot: 'reel', itemId: id(2), relatedId: id(3) },
    { code: 'bobber-load-out-of-range', slot: 'sinker', itemId: id(6), relatedId: id(5) }] }); f.broadcast();
  assert.equal(f.nodes.get('equipment-status').textContent, '请先处理当前尚未确认的结果。');
  assert.equal(f.nodes.get('equipment-readiness').textContent, '渔轮：鱼线超过线杯容量；沉子：沉子重量不适合浮漂');
});

test('equipment completion accepts the third spares projection and the real quote and buy buttons work', async () => {
  const f = await pageFixture(); f.select('equipment-slot-rod', id(7)); f.click('equipment-apply-rod');
  assert.equal(f.nodes.get('spares-quote-hook-single').disabled, true);
  f.replaceRod(); f.reply('assembly-candidate'); f.broadcast();
  assert.equal(f.nodes.get('spares-quote-hook-single').disabled, false); f.click('spares-quote-hook-single');
  assert.equal(f.messages.at(-1).kind, 'game-spares'); f.spares.quote = { offerId: id(200), ...f.spares.items[0] }; f.reply('spares-quoted'); f.broadcast();
  assert.equal(f.nodes.get('spares-offer').hidden, false); f.click('spares-buy'); assert.equal(f.messages.at(-1).offerId, id(200));
  f.spares.quote = null; f.spares.walletMinor = f.shop.walletMinor = '11965'; f.advance(); f.reply('equipment-purchase-candidate'); f.broadcast();
  assert.equal(f.nodes.get('quote-bait').disabled, false); assert.equal(f.probe.state().equipmentInputState.ready, true);
});

test('known spares retry blocks both other menus until explicit cancellation', async () => {
  const f = await pageFixture(); f.select('equipment-slot-rod', id(7)); f.click('spares-quote-hook-single');
  f.spares.retry = { operationId: id(200) }; f.reply('spares-not-written'); f.broadcast();
  assert.equal(f.nodes.get('quote-bait').disabled, true); assert.equal(f.nodes.get('equipment-apply-rod').disabled, true);
  assert.equal(f.nodes.get('spares-retry').disabled, false); f.click('quote-bait', {}, true); f.click('equipment-apply-rod', {}, true); assert.equal(f.messages.length, 1);
  f.click('spares-cancel'); f.spares.retry = null; f.reply('spares-cancelled'); f.broadcast();
  assert.equal(f.nodes.get('quote-bait').disabled, false); assert.equal(f.nodes.get('equipment-apply-rod').disabled, false);
});

test('known equipment retry retains spares projection but blocks a new spare quote', async () => {
  const f = await pageFixture(); f.select('equipment-slot-rod', id(7)); f.click('equipment-apply-rod');
  f.equipment.retry = { operationId: id(200), action: 'replace-slot' }; f.reply('equipment-not-written'); f.broadcast();
  assert.equal(f.probe.state().sparesInputState.ready, true); assert.equal(f.nodes.get('spares-quote-hook-single').disabled, true);
  f.click('spares-quote-hook-single', {}, true); assert.equal(f.messages.length, 1);
  f.click('equipment-cancel'); f.equipment.retry = null; f.reply('equipment-cancelled'); f.broadcast();
  f.click('spares-quote-hook-single'); assert.equal(f.messages.at(-1).kind, 'game-spares');
});

test('unknown spares save disables all three menus even if the following snapshots appear healthy', async () => {
  const f = await pageFixture(); f.select('equipment-slot-rod', id(7)); f.click('spares-quote-hook-single'); f.reply('save-outcome-unknown'); f.broadcast();
  for (const button of ['spares-quote-hook-single', 'equipment-apply-rod', 'quote-bait']) {
    assert.equal(f.nodes.get(button).disabled, true); f.click(button, {}, true);
  }
  assert.equal(f.messages.length, 1);
});

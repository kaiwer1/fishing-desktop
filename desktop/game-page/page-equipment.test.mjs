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
  const equipment = { schemaVersion: 3, available: true, reason: null, lease: 'first', stateSequence: '1', rigRevision: '1', inventoryRevision: '1', equipmentVersion: '1', items: inventory(), issues: [], retry: null };
  let snapshotRevision = 0, shopRevision = 0, equipmentRevision = 0, leaseRevision = 0;
  const emit = message => receive({ data: { documentId, ...message } });
  const broadcast = () => {
    emit({ kind: 'game-snapshot', revision: ++snapshotRevision, snapshot: { ...snapshot } });
    // Deliberately mirror the real host: shop state precedes equipment state.
    emit({ kind: 'game-shop-state', revision: ++shopRevision, state: structuredClone(shop) });
    emit({ kind: 'game-equipment-state', revision: ++equipmentRevision, state: structuredClone(equipment) });
  };
  const grant = lease => emit({ kind: 'lease', revision: ++leaseRevision, cancellationEpoch: 0, lease });
  emit({ kind: 'role', role: '交互' }); emit({ kind: 'game-snapshot', revision: ++snapshotRevision, snapshot: { ...snapshot } }); grant('first'); broadcast();
  return { nodes, messages, snapshot, shop, equipment, probe, broadcast, grant, emit,
    click: (id, event, force) => nodes.get(id).dispatch('click', event, force),
    select(id, value) { nodes.get(id).value = value; nodes.get(id).dispatch('change'); },
    reply(code, transportId = messages.at(-1).transportId) { emit({ kind: 'game-reply', transportId, code }); },
    advance() { snapshot.Sequence = String(Number(snapshot.Sequence) + 1); equipment.stateSequence = snapshot.Sequence; },
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

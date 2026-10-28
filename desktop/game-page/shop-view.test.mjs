import test from 'node:test';
import assert from 'node:assert/strict';
import { ShopView, ShopInput, formatMinor } from './shop-view.mjs';
const quoteId = '10000000-0000-0000-0000-000000000001', operationId = '20000000-0000-0000-0000-000000000001';
const quote = extra => ({ id: quoteId, product: 'bait', packs: 2, units: 10, totalPriceMinor: '40', ...extra });
const state = extra => ({ schemaVersion: 2, available: true, reason: null, walletMinor: '12000', baitCount: 12, foodCount: 2, netOwned: true, netEquipped: false,
  catalogVersion: '1', netPriceMinor: '2400', baitPackPriceMinor: '20', foodPriceMinor: '20', baitPackUnits: 5, foodStaminaRestore: 20, maxPacks: 100, quote: null, retry: null, ...extra });
const packet = (revision = 1, extra = {}) => ({ kind: 'game-shop-state', documentId: 'current', revision, state: state(extra) });
function fixture() {
  const context = { lease: 'first', allowed: true, schemaVersion: 2, netOwned: true, netEquipped: false, foodCount: 2, maxPacks: 100 }, messages = [];
  let next = 0; const input = new ShopInput({ readContext: () => context, post: m => messages.push(m), newTransportId: () => `shop-${++next}` });
  input.hostState(state());
  return { input, context, messages, complete(extra = {}, code = 'ok') { input.reply({ transportId: messages.at(-1).transportId, code }); input.hostState(state(extra)); } };
}
test('shop accepts only current document newer validated revisions', () => {
  const view = new ShopView('current'); assert.equal(view.apply(packet()), true);
  for (const p of [packet(), packet(0), { ...packet(2), documentId: 'old' }, { ...packet(2), kind: 'game-snapshot' }]) assert.equal(view.apply(p), false);
  assert.equal(view.revision, 1); assert.equal(view.apply(packet(2)), true);
});
test('schema3 equipment profiles retain the same supply menu authority rules', () => {
  const view = new ShopView('current'); assert.equal(view.apply(packet(1, { schemaVersion: 3 })), true);
  const f = fixture(); f.context.schemaVersion = 3; f.input.hostState(state({ schemaVersion: 3 }));
  assert.equal(f.input.quote('bait', 1), true); assert.equal(f.input.quote('bait', 1), false);
  f.complete({ schemaVersion: 3, quote: quote({ packs: 1, units: 5, totalPriceMinor: '20' }) });
  f.context.allowed = false; assert.equal(f.input.buy(), false);
});
test('all money and catalogue versions keep exact Int64 strings', () => {
  const view = new ShopView('current'); assert.equal(view.apply(packet(1, { walletMinor: '9223372036854775807', catalogVersion: '9007199254740993', netPriceMinor: '9223372036854775807' })), true);
  assert.equal(view.state().walletMinor, '9223372036854775807'); assert.equal(formatMinor(view.state().walletMinor), '92233720368547758.07');
  assert.equal(formatMinor('0'), '0.00'); assert.equal(formatMinor('1'), '0.01'); assert.equal(formatMinor('01'), '—');
});
test('malformed schema values strings counts ownership and pricing refuse without consuming revision', () => {
  const view = new ShopView('current');
  for (const bad of [{ schemaVersion: 4 }, { available: 1 }, { available: false }, { reason: 'oops' }, { walletMinor: 100 }, { walletMinor: '00' },
    { walletMinor: '9223372036854775808' }, { netPriceMinor: '0' }, { foodPriceMinor: null }, { catalogVersion: '0' },
    { baitCount: -1 }, { foodCount: 2147483648 }, { netOwned: false, netEquipped: true }, { baitPackUnits: 0 }, { foodStaminaRestore: NaN }, { maxPacks: 101 }]) assert.equal(view.apply(packet(1, bad)), false);
  assert.equal(view.revision, 0); assert.equal(view.apply(packet()), true);
});
test('legacy and unavailable states show reasons without quote or commerce permission', () => {
  const view = new ShopView('current'); const legacy = { schemaVersion: 1, available: false, reason: 'commerce-upgrade-required', catalogVersion: null,
    netPriceMinor: null, baitPackPriceMinor: null, foodPriceMinor: null, baitPackUnits: 0, foodStaminaRestore: 0, maxPacks: 0 };
  assert.equal(view.apply(packet(1, legacy)), true); assert.equal(view.apply(packet(2, { ...legacy, quote: quote() })), false);
  assert.equal(view.apply(packet(2, { ...legacy, schemaVersion: 0, reason: 'save-outcome-unknown', walletMinor: null })), true);
});
test('quote and retry envelopes are bounded mutually exclusive and require authorization', () => {
  const view = new ShopView('current');
  for (const bad of [{ quote: quote({ id: 'bad' }) }, { quote: quote({ product: 'gold' }) }, { quote: quote({ packs: 0 }) }, { quote: quote({ totalPriceMinor: 40 }) },
    { quote: quote({ product: 'net', packs: 2, units: 2 }) }, { retry: { operationId, action: 'refund' } }, { retry: { operationId: 'bad', action: 'eat' } },
    { quote: quote(), retry: { operationId, action: 'buy' } }, { available: false, reason: 'control-required', quote: quote() }]) assert.equal(view.apply(packet(1, bad)), false);
  assert.equal(view.apply(packet(1, { quote: quote() })), true); assert.equal(view.apply(packet(2, { retry: { operationId, action: 'buy' } })), true);
});
test('projection copies only display fields and isolates nested reference mutations', () => {
  const view = new ShopView('current'), p = packet(1, { quote: quote(), profileId: 'private', netId: 'private' }); view.apply(p);
  p.state.quote.id = 'mutated'; const read = view.state(); read.quote.units = 900;
  assert.equal(view.state().quote.id, quoteId); assert.equal(view.state().quote.units, 10); assert.equal('profileId' in view.state(), false); assert.equal('netId' in view.state(), false);
});
test('requesting a quote never sends money and buying requires an explicit host quote', () => {
  const f = fixture(); assert.equal(f.input.buy(), false); assert.equal(f.input.quote('bait', 2), true);
  assert.deepEqual(f.messages[0], { kind: 'game-shop', lease: 'first', transportId: 'shop-1', action: 'quote', product: 'bait', packs: 2 });
  f.complete({ quote: quote() }, 'shop-quoted'); assert.equal(f.messages.length, 1); assert.equal(f.input.buy(), true);
  assert.deepEqual(f.messages[1], { kind: 'game-shop', lease: 'first', transportId: 'shop-2', action: 'buy', quoteId });
});
test('duplicate clicks cannot queue more requests before reply and fresh projection', () => {
  const f = fixture(); f.input.quote('bait', 1); assert.equal(f.input.quote('food', 1), false); assert.equal(f.input.eat(), false);
  f.input.reply({ transportId: f.messages[0].transportId, code: 'shop-quoted' }); assert.equal(f.input.quote('food', 1), false);
  f.input.hostState(state({ quote: quote() })); assert.equal(f.input.buy(), true); assert.equal(f.input.buy(), false);
});
test('equip and eat share one monotone sequence without asset identifiers', () => {
  const f = fixture(); assert.equal(f.input.equip(true), true); f.context.netEquipped = true; f.complete({ netEquipped: true });
  assert.equal(f.input.eat(), true); f.complete({ netEquipped: true }); assert.equal(f.input.equip(false), true);
  assert.deepEqual(f.messages.map(m => m.inputSequence), ['1', '2', '3']);
  assert.deepEqual(Object.keys(f.messages[0]).sort(), ['kind', 'lease', 'transportId', 'action', 'equipped', 'inputSequence'].sort());
  assert.deepEqual(Object.keys(f.messages[1]).sort(), ['kind', 'lease', 'transportId', 'action', 'inputSequence'].sort());
});
test('packs respect native maximum and a net is always one piece', () => {
  const f = fixture(); f.context.maxPacks = 3;
  for (const args of [['bait', 4], ['food', 0], ['net', 2], ['bait', 1.5], ['unknown', 1]]) assert.equal(f.input.quote(...args), false);
  assert.equal(f.input.quote('food', 3), true);
});
test('empty food absent net unchanged equipment observer and legacy all refuse new intent', () => {
  const f = fixture(); f.context.foodCount = 0; assert.equal(f.input.eat(), false); f.context.netOwned = false; assert.equal(f.input.equip(true), false);
  f.context.netOwned = true; assert.equal(f.input.equip(false), false);
  for (const change of [{ allowed: false }, { allowed: true, schemaVersion: 1 }]) { Object.assign(f.context, change); assert.equal(f.input.quote('bait', 1), false); }
  assert.deepEqual(f.messages, []);
});
test('known-not-written offers only original opaque retry or explicit cancellation', () => {
  const f = fixture(); f.input.eat(); f.complete({ retry: { operationId, action: 'eat' } }, 'shop-not-written');
  assert.equal(f.input.eat(), false); assert.equal(f.input.quote('bait', 1), false); assert.equal(f.input.retry(), true);
  assert.deepEqual(f.messages[1], { kind: 'game-shop', lease: 'first', transportId: 'shop-2', action: 'retry', operationId });
  f.complete({ retry: { operationId, action: 'eat' } }, 'shop-not-written'); assert.equal(f.input.cancel(), true);
  assert.deepEqual(f.messages[2], { kind: 'game-shop', lease: 'first', transportId: 'shop-3', action: 'cancel' });
});
test('unknown save outcome latches all actions off even before a later fault snapshot', () => {
  const f = fixture(); f.input.eat(); f.input.reply({ transportId: f.messages[0].transportId, code: 'save-outcome-unknown' });
  f.input.hostState(state({ quote: quote(), retry: null }));
  assert.equal(f.input.state().faulted, true); assert.equal(f.input.state().quote, null);
  assert.equal(f.input.quote('net', 1), false); assert.equal(f.input.eat(), false); assert.equal(f.input.retry(), false); assert.equal(f.input.cancel(), false);
});
test('loss of authority removes quote retry and pending transport references', () => {
  const f = fixture(); f.input.hostState(state({ quote: quote() })); f.context.allowed = false; f.input.reconcile();
  assert.equal(f.input.state().quote, null); f.context.allowed = true; f.context.lease = 'second'; f.input.reconcile(); assert.equal(f.input.buy(), false);
  f.input.hostState(state({ retry: { operationId, action: 'buy' } })); f.context.lease = 'third'; f.input.reconcile(); assert.equal(f.input.retry(), false);
});
test('late reply from old grant cannot clear new pending operation or cause automatic retry', () => {
  const f = fixture(); f.input.eat(); const old = f.messages[0].transportId; f.context.lease = 'second'; f.input.reconcile(); f.input.hostState(state()); f.input.eat();
  assert.equal(f.input.reply({ transportId: old, code: 'shop-not-written' }), false); assert.equal(f.input.state().inflight.transportId, f.messages[1].transportId);
  assert.equal(f.messages[1].inputSequence, '2'); assert.equal(f.messages.length, 2);
});
test('new page or grant needs a fresh current host handle before confirmation', () => {
  const f = fixture(); assert.equal(f.input.buy(), false); assert.equal(f.input.retry(), false); assert.equal(f.input.cancel(), false);
  f.context.allowed = false; f.input.hostState(state({ quote: quote() })); f.context.allowed = true; f.input.reconcile(); assert.equal(f.input.buy(), false);
});
test('unavailable projection blocks intents and new grant waits for a fresh shop projection', () => {
  const f = fixture(); f.input.hostState(state({ available: false, reason: 'session-busy' })); assert.equal(f.input.eat(), false);
  f.context.lease = 'second'; f.input.reconcile(); assert.equal(f.input.quote('bait', 1), false); f.input.hostState(state()); assert.equal(f.input.quote('bait', 1), true);
});

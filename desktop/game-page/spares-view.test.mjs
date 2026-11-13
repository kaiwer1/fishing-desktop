import test from 'node:test';
import assert from 'node:assert/strict';
import { SparesView, SparesInput } from './spares-view.mjs';

const id = n => `10000000-0000-0000-0000-${String(n).padStart(12, '0')}`;
const hook = { sku: 'hook-single', definitionKey: 'single-hook', kind: 'hook', priceMinor: '35', lineLengthMm: 0 };
const line = { sku: 'nylon-018-50m', definitionKey: 'nylon-018', kind: 'line', priceMinor: '400', lineLengthMm: 50000 };
const quote = () => ({ offerId: id(1), ...hook });
const state = extra => ({ schemaVersion: 3, available: true, reason: null, lease: 'first', stateSequence: '1', catalogVersion: '1',
  walletMinor: '12000', instancesCount: 6, items: [hook, line], quote: null, retry: null, ...extra });
const packet = (revision = 1, extra = {}, documentId = 'current') => ({ kind: 'game-spares-state', documentId, revision, state: state(extra) });
function fixture() {
  const context = { documentId: 'current', lease: 'first', allowed: true, menuOpen: true, sessionId: null, pendingResult: false, faulted: false, schemaVersion: 3 };
  const messages = []; let next = 0, revision = 1;
  const input = new SparesInput({ readContext: () => context, post: m => messages.push(m), newTransportId: () => `spares-${++next}` });
  input.hostState(packet());
  return { context, messages, input,
    project(extra = {}) { return input.hostState(packet(++revision, { lease: context.lease, ...extra }, context.documentId)); },
    reply(code = 'spares-quoted') { return input.reply({ kind: 'game-reply', documentId: context.documentId, transportId: messages.at(-1).transportId, code }); }
  };
}

test('spares projections accept only a current document and newer valid revision', () => {
  const v = new SparesView('current'); assert.equal(v.apply(packet()), true);
  for (const p of [packet(), packet(2, {}, 'old'), packet(-1), { ...packet(2), kind: 'game-shop-state' }]) assert.equal(v.apply(p), false);
  assert.equal(v.revision, 1); assert.equal(v.apply(packet(2)), true);
});
test('prices and revisions retain full Int64 precision and reject unsafe representations', () => {
  const v = new SparesView('current'); assert.equal(v.apply(packet(1, { walletMinor: '9223372036854775807', stateSequence: '9007199254740993' })), true);
  assert.equal(v.state().stateSequence, '9007199254740993');
  for (const bad of [{ walletMinor: 12000 }, { walletMinor: '01' }, { stateSequence: '-1' }, { catalogVersion: '0' }, { walletMinor: '9223372036854775808' }]) assert.equal(v.apply(packet(2, bad)), false);
});
test('old and unconfigured profiles show unavailable or stopped without manufactured goods', () => {
  const v = new SparesView('current'); const empty = { available: false, lease: null, items: [], catalogVersion: null };
  assert.equal(v.apply(packet(1, { ...empty, reason: 'spares-not-configured' })), true);
  assert.equal(v.apply(packet(2, { ...empty, reason: 'owner-stopped' })), true);
  assert.equal(v.apply(packet(3, { ...empty, reason: 'spares-schema-required', schemaVersion: 2, stateSequence: null, walletMinor: null, instancesCount: 0 })), true);
  assert.equal(v.apply(packet(4, { ...empty, reason: 'owner-stopped', quote: quote() })), false);
});
test('catalogue bounds reject duplicate SKUs unsupported gear and invalid line lengths', () => {
  const v = new SparesView('current');
  for (const items of [[hook, hook], [{ ...hook, kind: 'rod' }], [{ ...hook, priceMinor: '0' }], [{ ...hook, lineLengthMm: 1 }],
    [{ ...line, lineLengthMm: 0 }], [{ ...line, lineLengthMm: 100000001 }], Array.from({ length: 17 }, (_, i) => ({ ...hook, sku: `hook-${i}` }))]) assert.equal(v.apply(packet(1, { items })), false);
  assert.equal(v.apply(packet(1, { instancesCount: 129 })), false);
});
test('quotes must match the actual product and cannot coexist with retry or observer state', () => {
  const v = new SparesView('current'); assert.equal(v.apply(packet(1, { quote: quote() })), true);
  for (const bad of [{ quote: { ...quote(), priceMinor: '1' } }, { quote: quote(), retry: { operationId: id(2) } },
    { available: false, reason: 'control-required', quote: quote() }, { retry: { operationId: 'invalid' } }]) assert.equal(v.apply(packet(2, bad)), false);
  const copy = v.state(); copy.quote.priceMinor = '1'; copy.items[0].sku = 'fake'; assert.equal(v.state().quote.priceMinor, '35'); assert.equal(v.state().items[0].sku, 'hook-single');
});
test('quotation and purchase emit only opaque identity fields once', () => {
  const f = fixture(); assert.equal(f.input.quote('unknown'), false); assert.equal(f.input.quote(hook.sku), true); assert.equal(f.input.quote(hook.sku), false);
  assert.deepEqual(Object.keys(f.messages[0]).sort(), ['kind', 'documentId', 'lease', 'transportId', 'action', 'sku'].sort());
  f.reply(); f.project({ quote: quote() }); assert.equal(f.input.buy(), true); assert.equal(f.input.buy(), false);
  assert.equal(f.messages[1].offerId, id(1)); assert.equal('priceMinor' in f.messages[1], false); assert.equal('newItemId' in f.messages[1], false);
});
test('reply and subsequent projection are both required with no automatic retry', () => {
  const f = fixture(); f.input.quote(hook.sku); f.project({ quote: quote() }); assert.equal(f.input.buy(), false);
  f.reply(); assert.equal(f.input.buy(), false); f.project({ quote: quote() }); assert.equal(f.input.buy(), true);
  assert.equal(f.input.reply({ kind: 'game-reply', documentId: 'old', transportId: f.messages.at(-1).transportId, code: 'equipment-purchase-candidate' }), false);
  assert.equal(f.messages.length, 2);
});
test('known failure permits only explicit same-handle retry or cancellation', () => {
  const f = fixture(); f.project({ retry: { operationId: id(4) } });
  assert.equal(f.input.quote(hook.sku), false); assert.equal(f.input.buy(), false); assert.equal(f.input.retry(), true);
  assert.equal(f.messages[0].operationId, id(4)); assert.equal(f.messages[0].action, 'retry');
  f.reply('spares-not-written'); f.project({ retry: { operationId: id(4) } }); assert.equal(f.input.cancel(), true);
  assert.deepEqual(Object.keys(f.messages[1]).sort(), ['kind', 'documentId', 'lease', 'transportId', 'action'].sort());
});
test('menu session focus and lease changes remove old purchase references', () => {
  for (const changes of [{ allowed: false }, { menuOpen: false }, { sessionId: id(3) }, { pendingResult: true }, { lease: 'second' }]) {
    const f = fixture(); f.project({ quote: quote() }); Object.assign(f.context, changes); f.input.reconcile();
    assert.equal(f.input.buy(), false); assert.equal(f.input.state().quote, null); assert.equal(f.messages.length, 0);
  }
});
test('uncertain write latches the page even after a later healthy-looking projection', () => {
  const f = fixture(); f.project({ quote: quote() }); f.input.buy(); f.reply('save-outcome-unknown'); f.project({ quote: quote() });
  assert.equal(f.input.state().faulted, true); assert.equal(f.input.buy(), false); assert.equal(f.input.quote(hook.sku), false); assert.equal(f.messages.length, 1);
});
test('unknown owner projection freezes without requiring an outstanding request', () => {
  const f = fixture(); f.project({ schemaVersion: 0, available: false, reason: 'save-outcome-unknown', lease: null, stateSequence: null, catalogVersion: null, walletMinor: null, instancesCount: 0, items: [] });
  assert.equal(f.input.state().faulted, true); assert.equal(f.input.quote(hook.sku), false);
});
test('double clicks and repeated keys never create extra quote requests', () => {
  const f = fixture(); assert.equal(f.input.quote(hook.sku, { detail: 2 }), false); assert.equal(f.input.quote(hook.sku, { repeat: true }), false);
  assert.equal(f.input.quote(hook.sku, { detail: 1 }), true); assert.equal(f.messages.length, 1);
});

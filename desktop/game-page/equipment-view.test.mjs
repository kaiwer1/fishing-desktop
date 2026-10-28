import test from 'node:test';
import assert from 'node:assert/strict';
import { EquipmentView, EquipmentInput } from './equipment-view.mjs';

const id = n => `10000000-0000-0000-0000-${String(n).padStart(12, '0')}`;
const item = (n, kind, extra = {}) => ({ id: id(n), definitionKey: `starter-${kind}`, kind, revision: '1', location: 'inventory',
  slot: null, reelId: null, initialLineMm: kind === 'line' ? 50000 : 0, remainingLineMm: kind === 'line' ? 32000 : 0, ...extra });
const items = () => [item(1, 'rod', { location: 'rig-slot', slot: 'rod' }), item(2, 'reel', { location: 'rig-slot', slot: 'reel' }),
  item(3, 'line', { location: 'reel-spool', reelId: id(2) }), item(4, 'hook', { location: 'rig-slot', slot: 'hook' }),
  item(5, 'bobber', { location: 'rig-slot', slot: 'bobber' }), item(6, 'sinker', { location: 'rig-slot', slot: 'sinker' }),
  item(7, 'rod'), item(8, 'reel'), item(9, 'line'), item(10, 'hook', { location: 'lost' }),
  item(11, 'line', { location: 'lost', remainingLineMm: 0 }), item(12, 'reel', { location: 'lost' })];
const state = extra => ({ schemaVersion: 3, available: true, reason: null, lease: 'first', stateSequence: '12', rigRevision: '2',
  inventoryRevision: '3', equipmentVersion: '1', items: items(), issues: [], retry: null, ...extra });
const packet = (revision = 1, extra = {}, documentId = 'current') => ({ kind: 'game-equipment-state', documentId, revision, state: state(extra) });
const retry = () => ({ operationId: id(100), action: 'replace-slot' });
function fixture() {
  const context = { documentId: 'current', lease: 'first', allowed: true, menuOpen: true, sessionId: null, pendingResult: false, faulted: false, schemaVersion: 3 };
  const messages = []; let next = 0, revision = 1;
  const input = new EquipmentInput({ readContext: () => context, post: m => messages.push(m), newTransportId: () => `equipment-${++next}` });
  input.hostState(packet(revision));
  return { input, context, messages,
    project(extra = {}) { return input.hostState(packet(++revision, { lease: context.lease, ...extra }, context.documentId)); },
    reply(code = 'assembly-candidate') { return input.reply({ kind: 'game-reply', documentId: context.documentId, transportId: messages.at(-1).transportId, code }); },
    complete(extra = {}, code) { this.reply(code); this.project(extra); }
  };
}

test('equipment accepts only current document and strictly newer valid host projections', () => {
  const view = new EquipmentView('current'); assert.equal(view.apply(packet()), true);
  for (const bad of [packet(), packet(0), packet(2, {}, 'old'), { ...packet(2), kind: 'game-snapshot' }, packet(1.5)]) assert.equal(view.apply(bad), false);
  assert.equal(view.revision, 1); assert.equal(view.apply(packet(2)), true);
});

test('versions and sequence remain exact Int64 strings, including values above JavaScript precision', () => {
  const view = new EquipmentView('current');
  assert.equal(view.apply(packet(1, { stateSequence: '9223372036854775807', inventoryRevision: '9007199254740993' })), true);
  assert.equal(view.state().stateSequence, '9223372036854775807'); assert.equal(view.state().inventoryRevision, '9007199254740993');
  for (const bad of [{ stateSequence: 12 }, { stateSequence: '01' }, { stateSequence: '-1' }, { stateSequence: '9223372036854775808' },
    { rigRevision: '0' }, { inventoryRevision: 1 }, { equipmentVersion: null }]) assert.equal(view.apply(packet(2, bad)), false);
});

test('projection rejects inconsistent availability, missing fields and unsupported schema without advancing revision', () => {
  const view = new EquipmentView('current');
  for (const bad of [{ schemaVersion: 4 }, { available: 1 }, { available: false }, { reason: 'blocked' }, { lease: null }, { lease: '' },
    { items: null }, { issues: null }, { retry: undefined }, { stateSequence: undefined }]) assert.equal(view.apply(packet(1, bad)), false);
  assert.equal(view.revision, 0); assert.equal(view.apply(packet()), true);
});

test('legacy and fault projections carry no fake equipment or versions', () => {
  const view = new EquipmentView('current'), unavailable = { schemaVersion: 2, available: false, reason: 'equipment-upgrade-required', lease: null,
    stateSequence: null, rigRevision: null, inventoryRevision: null, equipmentVersion: null, items: [], issues: [] };
  assert.equal(view.apply(packet(1, unavailable)), true);
  for (const extra of [{ items: items() }, { stateSequence: '0' }, { equipmentVersion: '1' }, { retry: retry() }]) assert.equal(view.apply(packet(2, { ...unavailable, ...extra })), false);
  assert.equal(view.apply(packet(2, { ...unavailable, schemaVersion: 0, reason: 'save-outcome-unknown' })), true);
});

test('missing slots and host issues are displayable while equipment repair remains available', () => {
  const view = new EquipmentView('current'); const empty = { items: [], issues: [{ code: 'component-missing', slot: 'hook', itemId: null, relatedId: null }] };
  assert.equal(view.apply(packet(1, empty)), true); assert.equal(view.state().items.length, 0); assert.equal(view.state().issues[0].slot, 'hook');
  const f = fixture(); f.project({ items: [item(7, 'rod')], issues: [{ code: 'component-missing', slot: 'rod', itemId: null, relatedId: null }] });
  assert.equal(f.input.replaceSlot('rod', id(7)), true);
});

test('inventory bounds, identities, kinds, definition keys and revisions are validated', () => {
  const view = new EquipmentView('current');
  assert.equal(view.apply(packet(1, { items: Array.from({ length: 128 }, (_, n) => item(n + 1, 'rod')) })), true);
  for (const badItems of [Array.from({ length: 129 }, (_, n) => item(n + 1, 'rod')), [item(1, 'rod'), item(1, 'reel')],
    [item(1, 'unknown')], [item(1, 'rod', { id: 'invalid' })], [item(1, 'rod', { definitionKey: '<script>' })],
    [item(1, 'rod', { revision: '0' })], [null]]) assert.equal(view.apply(packet(2, { items: badItems })), false);
});

test('line lengths distinguish actual remaining length, exhausted identities and non-line assets', () => {
  const view = new EquipmentView('current'); assert.equal(view.apply(packet()), true);
  assert.equal(view.state().items.find(i => i.id === id(3)).remainingLineMm, 32000);
  for (const bad of [{ initialLineMm: 0 }, { initialLineMm: 100000001 }, { remainingLineMm: -1 }, { remainingLineMm: 50001 },
    { remainingLineMm: 0 }, { remainingLineMm: 1.5 }, { location: 'lost' }]) assert.equal(view.apply(packet(2, { items: [item(9, 'line', bad)] })), false);
  assert.equal(view.apply(packet(2, { items: [item(9, 'line', { location: 'lost', remainingLineMm: 0 })] })), true);
  assert.equal(view.apply(packet(3, { items: [item(9, 'rod', { initialLineMm: 1, remainingLineMm: 1 })] })), false);
});

test('one actual slot and one line per spool are enforced with no independent line slot', () => {
  const view = new EquipmentView('current');
  for (const badItems of [[item(1, 'rod', { location: 'rig-slot', slot: 'reel' })], [item(1, 'line', { location: 'rig-slot', slot: 'line' })],
    [item(1, 'rod', { slot: 'rod' })], [item(1, 'rod', { location: 'rig-slot', slot: 'rod' }), item(2, 'rod', { location: 'rig-slot', slot: 'rod' })],
    [item(1, 'rod', { location: 'reel-spool', reelId: id(2) }), item(2, 'reel')], [item(1, 'line', { location: 'reel-spool', reelId: id(2) })],
    [item(1, 'line', { location: 'reel-spool', reelId: id(2) }), item(2, 'reel', { location: 'lost' })],
    [item(1, 'line', { location: 'reel-spool', reelId: id(2) }), item(2, 'reel'), item(3, 'line', { location: 'reel-spool', reelId: id(2) })]]) assert.equal(view.apply(packet(1, { items: badItems })), false);
  assert.equal(view.apply(packet(1, { items: [item(1, 'line', { location: 'reel-spool', reelId: id(2) }), item(2, 'reel')] })), true);
});

test('issue and retry descriptions are bounded and no retry survives an unavailable projection', () => {
  const view = new EquipmentView('current');
  for (const bad of [{ issues: Array(257).fill({ code: 'missing', slot: null, itemId: null, relatedId: null }) },
    { issues: [{ code: 'missing', slot: 'line', itemId: null, relatedId: null }] }, { issues: [{ code: 'missing', slot: null, itemId: 'bad', relatedId: null }] },
    { retry: { operationId: id(100), action: 'restore-lost' } }, { retry: { operationId: 'bad', action: 'move-line' } },
    { available: false, reason: 'control-required', retry: retry() }]) assert.equal(view.apply(packet(1, bad)), false);
  assert.equal(view.apply(packet(1, { retry: retry() })), true);
});

test('display whitelist deep-copies assets and omits prices, hidden IDs and rule internals', () => {
  const view = new EquipmentView('current'), p = packet(1, { retry: retry(), profileId: 'private', price: '100' });
  p.state.items[0].lossEventId = 'private'; p.state.issues = [{ code: 'missing', slot: null, itemId: null, relatedId: null, internalRule: 'private' }];
  assert.equal(view.apply(p), true); p.state.items[0].definitionKey = 'mutated'; p.state.retry.operationId = 'mutated';
  const read = view.state(); read.items[0].remainingLineMm = 100; read.issues[0].code = 'mutated'; read.retry.action = 'mutated';
  assert.equal(view.state().items[0].definitionKey, 'starter-rod'); assert.equal(view.state().items[0].remainingLineMm, 0);
  assert.equal(view.state().issues[0].code, 'missing'); assert.equal(view.state().retry.operationId, id(100));
  assert.equal('profileId' in read, false); assert.equal('price' in read, false); assert.equal('lossEventId' in read.items[0], false); assert.equal('internalRule' in read.issues[0], false);
  view.clearReferences(); assert.equal(view.state().lease, null); assert.equal(view.state().retry, null); assert.equal(view.state().items.length, 12);
});

test('replacement transmits only the existing item, chosen slot and accepted host sequence', () => {
  const f = fixture(); f.project({ stateSequence: '9007199254740993' }); assert.equal(f.input.replaceSlot('rod', id(7)), true);
  assert.deepEqual(f.messages[0], { kind: 'game-equipment', documentId: 'current', lease: 'first', transportId: 'equipment-1', action: 'replace-slot',
    slot: 'rod', itemId: id(7), expectedSequence: '9007199254740993', inputSequence: '1' });
  assert.equal(f.input.replaceSlot('reel', id(8)), false); assert.equal(f.messages.length, 1);
});

test('whole-line movement and slot removal never send length, prices, new asset IDs or damage', () => {
  const f = fixture(); assert.equal(f.input.moveLine(id(3), null), true);
  assert.deepEqual(f.messages[0], { kind: 'game-equipment', documentId: 'current', lease: 'first', transportId: 'equipment-1', action: 'move-line',
    lineId: id(3), reelId: null, expectedSequence: '12', inputSequence: '1' });
  f.complete({ stateSequence: '13' }); assert.equal(f.input.replaceSlot('hook', null), true);
  assert.equal(f.messages[1].inputSequence, '2'); assert.equal(f.messages[1].expectedSequence, '13'); assert.equal(f.messages[1].itemId, null);
});

test('no missing, wrong-type, unchanged, occupied or lost item can be selected', () => {
  const f = fixture();
  for (const args of [['line', id(9)], ['rod', id(1)], ['rod', id(2)], ['rod', id(99)], ['hook', id(10)], ['rod', undefined]]) assert.equal(f.input.replaceSlot(...args), false);
  for (const args of [[id(11), id(8)], [id(7), id(8)], [id(99), id(8)], [id(9), id(12)], [id(9), id(7)], [id(9), id(99)],
    [id(9), id(2)], [id(3), id(2)], [id(9), null]]) assert.equal(f.input.moveLine(...args), false);
  assert.deepEqual(f.messages, []); assert.equal(f.input.moveLine(id(3), id(8)), true);
});

test('menu, document, schema, active session, pending result and fault guards are explicit', () => {
  for (const patch of [{ allowed: false }, { lease: null }, { documentId: null }, { schemaVersion: 2 }, { menuOpen: false }, { menuOpen: undefined },
    { sessionId: id(90) }, { sessionId: undefined }, { pendingResult: true }, { pendingResult: undefined }, { faulted: true }, { faulted: undefined }]) {
    const f = fixture(); Object.assign(f.context, patch); assert.equal(f.input.replaceSlot('rod', id(7)), false); assert.equal(f.input.moveLine(id(3), null), false); assert.equal(f.messages.length, 0);
  }
});

test('double clicks and repeated keys cannot send any operation even with fresh host state', () => {
  const f = fixture(); assert.equal(f.input.replaceSlot('rod', id(7), { detail: 2 }), false); assert.equal(f.input.moveLine(id(3), null, { repeat: true }), false);
  f.project({ retry: retry() }); assert.equal(f.input.retry({ detail: 2 }), false); assert.equal(f.input.cancel({ repeat: true }), false);
  assert.equal(f.messages.length, 0); assert.equal(f.input.retry({ detail: 0, repeat: false }), true);
});

test('reply alone or a repeated old projection cannot unlock another action', () => {
  const f = fixture(); f.input.replaceSlot('rod', id(7)); assert.equal(f.reply(), true);
  assert.equal(f.input.moveLine(id(3), null), false); assert.equal(f.input.hostState(packet()), false); assert.equal(f.input.moveLine(id(3), null), false);
  f.project(); assert.equal(f.input.moveLine(id(3), null), true);
});

test('a projection that precedes the reply cannot be reused as post-reply confirmation', () => {
  const f = fixture(); f.input.replaceSlot('rod', id(7)); f.project(); assert.equal(f.input.moveLine(id(3), null), false);
  f.reply(); assert.equal(f.input.moveLine(id(3), null), false); f.project(); assert.equal(f.input.moveLine(id(3), null), true);
});

test('known prewrite failure permits only an explicit original operation retry or cancel', () => {
  const f = fixture(); f.input.replaceSlot('rod', id(7)); f.complete({ retry: retry() }, 'equipment-not-written');
  assert.equal(f.messages.length, 1); assert.equal(f.input.replaceSlot('reel', id(8)), false); assert.equal(f.input.moveLine(id(3), null), false);
  assert.equal(f.input.retry(), true); assert.equal(f.input.retry(), false);
  assert.deepEqual(f.messages[1], { kind: 'game-equipment', documentId: 'current', lease: 'first', transportId: 'equipment-2', action: 'retry', operationId: id(100) });
  f.complete({ retry: retry() }, 'equipment-not-written'); assert.equal(f.input.cancel(), true);
  assert.deepEqual(f.messages[2], { kind: 'game-equipment', documentId: 'current', lease: 'first', transportId: 'equipment-3', action: 'cancel' });
});

test('matching unknown reply latches off all later actions, even across apparently healthy projections', () => {
  const f = fixture(); f.input.replaceSlot('rod', id(7)); f.reply('save-outcome-unknown'); f.project({ retry: retry() });
  f.context.lease = 'second'; f.project(); assert.equal(f.input.state().faulted, true);
  assert.equal(f.input.replaceSlot('rod', id(7)), false); assert.equal(f.input.moveLine(id(3), null), false); assert.equal(f.input.retry(), false); assert.equal(f.input.cancel(), false);
});

test('unknown host projection and confirmed fault context independently latch actions off', () => {
  for (const byContext of [false, true]) {
    const f = fixture(); if (byContext) { f.context.faulted = true; f.input.reconcile(); f.context.faulted = false; }
    else f.project({ available: false, reason: 'save-outcome-unknown', retry: null });
    f.project(); assert.equal(f.input.state().faulted, true); assert.equal(f.input.replaceSlot('rod', id(7)), false);
  }
});

test('loss of authority drops retry and in-flight references and requires a new matching host projection', () => {
  const f = fixture(); f.project({ retry: retry() }); f.context.allowed = false; f.input.reconcile();
  assert.equal(f.input.state().retry, null); f.context.allowed = true; f.context.lease = 'second'; f.input.reconcile(); assert.equal(f.input.retry(), false);
  assert.equal(f.input.replaceSlot('rod', id(7)), false); f.project(); assert.equal(f.input.replaceSlot('rod', id(7)), true);
  f.context.menuOpen = false; f.input.reconcile(); assert.equal(f.input.state().inflight, null); f.context.menuOpen = true; assert.equal(f.input.replaceSlot('rod', id(7)), false);
});

test('a delayed state from an old lease cannot revive its operation handle under a new lease', () => {
  const f = fixture(); f.project({ retry: retry() }); f.context.lease = 'second'; f.input.reconcile();
  f.project({ lease: 'first', retry: retry() }); assert.equal(f.input.retry(), false); assert.equal(f.input.state().retry, null);
  f.project(); assert.equal(f.input.replaceSlot('rod', id(7)), true);
});

test('new document rejects prior document state and replies even when their revision is larger', () => {
  const f = fixture(); f.input.replaceSlot('rod', id(7)); const old = f.messages[0]; f.context.documentId = 'new'; f.context.lease = 'second'; f.input.reconcile();
  assert.equal(f.input.hostState(packet(100, { lease: 'second', retry: retry() })), false); assert.equal(f.input.retry(), false);
  f.project(); assert.equal(f.input.moveLine(id(3), null), true);
  assert.equal(f.input.reply({ kind: 'game-reply', documentId: 'current', transportId: old.transportId, code: 'equipment-not-written' }), false);
  assert.equal(f.input.state().inflight.transportId, f.messages[1].transportId); assert.equal(f.messages[1].inputSequence, '2');
});

test('old transport replies cannot release a newer pending request and reply kind is checked', () => {
  const f = fixture(); f.input.replaceSlot('rod', id(7)); const old = f.messages[0]; f.complete(); f.input.moveLine(id(3), null);
  for (const message of [{ kind: 'game-reply', documentId: 'current', transportId: old.transportId },
    { kind: 'game-state', documentId: 'current', transportId: f.messages[1].transportId },
    { kind: 'game-reply', documentId: 'old', transportId: f.messages[1].transportId }]) assert.equal(f.input.reply(message), false);
  assert.equal(f.input.state().inflight.transportId, f.messages[1].transportId);
});

test('unavailable or malformed host state cannot authorize a mutation', () => {
  const f = fixture(); f.project({ available: false, reason: 'session-busy', lease: null }); assert.equal(f.input.moveLine(id(3), null), false);
  assert.equal(f.input.hostState(packet(100, { stateSequence: 12 })), false); assert.equal(f.input.moveLine(id(3), null), false);
  f.project(); assert.equal(f.input.moveLine(id(3), null), true);
});

test('input state snapshots cannot mutate opaque handles or pending transport pairing', () => {
  const f = fixture(); f.project({ retry: retry() }); f.input.state().retry.operationId = id(99); f.input.retry();
  assert.equal(f.messages[0].operationId, id(100)); f.input.state().inflight.transportId = 'changed'; assert.equal(f.reply('equipment-not-written'), true);
});

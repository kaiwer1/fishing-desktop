import test from 'node:test';
import assert from 'node:assert/strict';
import { Wardrobe } from './wardrobe.mjs';

const initial = () => ({ available: true, activeId: null, items: [
  { id: 'moss', name: '苔绿钓鱼服', description: '厚实的织物袖口', priceMinor: 1280, owned: false, equipped: false, color: '#52664b' },
  { id: 'blue', name: '海蓝防风衣', description: '轻便的防风衣料', priceMinor: 1680, owned: false, equipped: false, color: '#376481' },
] });
function fixture() {
  class Node {
    constructor(doc) { this.ownerDocument = doc; }
    children = []; handlers = {}; attributes = {};
    setAttribute(key, value) { this.attributes[key] = value; }
    append(...nodes) { this.children.push(...nodes); }
    replaceChildren(...nodes) { this.children = nodes; }
    addEventListener(type, fn) { this.handlers[type] = fn; }
    find(id) { return this.id === id ? this : this.children.map(n => n.find(id)).find(Boolean); }
    click() { this.handlers.click({ detail: 1 }); }
  }
  const doc = { createElement: () => new Node(doc) }, container = new Node(doc);
  const context = { lease: 'first', allowed: true, sequence: '11', walletMinor: '5000' }, messages = [];
  let next = 0;
  const panel = new Wardrobe({ documentId: 'page', container, readContext: () => context, post: m => messages.push(m),
    newTransportId: () => `transport-${++next}`, changed: () => { panel.reconcile(); panel.render(); } });
  let revision = 0;
  const state = initial();
  const apply = () => { assert.equal(panel.apply({ kind:'game-wardrobe-state', documentId:'page', revision:++revision, state }), true); panel.render(); };
  const reply = (code = 'wardrobe-updated', transportId = messages.at(-1).transportId) => {
    const accepted = panel.reply({ kind:'game-reply', documentId:'page', transportId, code }); panel.render(); return accepted;
  };
  const render = () => { panel.reconcile(); panel.render(); };
  apply(); return { context, state, panel, messages, apply, reply, render, container, button: id => container.find(`wardrobe-${id}`),
    click: id => container.find(`wardrobe-${id}`).click(), note: () => container.find('wardrobe-status').textContent };
}

test('validates colours, unique IDs, exact integer prices and the single confirmed owned sleeve; copies data', () => {
  const f = fixture(), send = (state, documentId = 'page') => f.panel.apply({ kind:'game-wardrobe-state', documentId, revision:2, state });
  for (const edit of [s => s.items[0].priceMinor = 1.5, s => s.items[0].color = 'url(x)', s => s.activeId = 'blue',
    s => { s.activeId = 'blue'; s.items[1].equipped = true; }, s => s.items[1].id = 'moss']) {
    const state = initial(); edit(state); assert.equal(send(state), false);
  }
  assert.equal(send(initial(),'old'), false); assert.equal(f.panel.revision, 1); assert.equal(f.panel.sleeveColor, null);
  f.state.items[0].owned = true; f.state.items[0].equipped = true; f.state.activeId = 'moss'; f.apply();
  f.state.items[0].color = '#ffffff'; f.panel.state().items[0].equipped = false;
  assert.equal(f.panel.sleeveColor, '#52664b'); assert.equal(f.panel.state().items[0].equipped, true);
});
test('purchase needs a separate exact-price confirmation and sends only the seven wire fields', () => {
  const f = fixture(); f.click('buy-moss');
  assert.equal(f.panel.busy, true); assert.equal(f.messages.length, 0);
  assert.match(f.container.children.at(-1).children[0].textContent, /12\.80 银币/);
  f.click('cancel'); assert.equal(f.panel.busy, false);
  f.click('buy-moss'); f.click('confirm'); f.click('confirm');
  assert.deepEqual(f.messages, [{ kind: 'game-wardrobe', documentId:'page', transportId:'transport-1', lease:'first',
    action:'buy', itemId:'moss', expectedSequence:'11' }]);
  f.reply(); assert.equal(f.panel.busy, true); assert.equal(f.panel.sleeveColor, null); assert.match(f.note(), /等待存档/);
  f.state.items[0].owned = true; f.apply(); assert.equal(f.panel.busy, true);
  f.context.sequence = '12'; f.render(); assert.equal(f.panel.busy, false); assert.match(f.note(), /购买已确认/);
  assert.equal(f.panel.sleeveColor, null); assert.equal(f.button('wear-moss').disabled, false);
});
test('wear/remove use the current owned item and the sleeve follows authoritative projection across reply order', () => {
  const f = fixture(); f.state.items[0].owned = true; f.apply(); f.click('wear-moss');
  assert.equal(f.panel.sleeveColor, null); assert.equal(f.reply('wardrobe-updated', 'old'), false);
  f.state.activeId = 'moss'; f.state.items[0].equipped = true; f.context.sequence = '12'; f.apply();
  assert.equal(f.panel.sleeveColor, '#52664b'); assert.equal(f.panel.busy, true); f.reply(); assert.equal(f.panel.busy, false);
  f.click('remove-moss'); assert.deepEqual([f.messages.at(-1).action,f.messages.at(-1).itemId], ['remove','moss']);
  f.reply(); assert.equal(f.panel.sleeveColor, '#52664b'); f.state.activeId = null; f.state.items[0].equipped = false;
  f.context.sequence = '13'; f.apply(); assert.equal(f.panel.sleeveColor, null); assert.match(f.note(), /普通衣袖/);
});
test('unaffordable, read-only, repriced or stale confirmations cannot send; a known rejection needs an explicit new purchase', () => {
  const f = fixture(); f.context.walletMinor = '1279'; f.render(); f.click('buy-moss'); assert.equal(f.panel.busy,false);
  f.context.walletMinor = '5000'; f.context.allowed = false; f.render(); f.click('buy-moss'); assert.equal(f.messages.length,0);
  f.context.allowed = true; f.render(); f.click('buy-moss'); f.state.items[0].priceMinor = 1400; f.apply(); f.click('confirm');
  f.click('buy-moss'); f.context.sequence = '12'; f.render(); f.click('confirm');
  f.click('buy-moss'); f.context.lease = 'other'; f.render(); f.click('confirm'); assert.equal(f.messages.length,0);
  f.click('buy-moss'); f.click('confirm'); f.reply('wardrobe-not-written'); assert.match(f.note(), /未写入/);
  f.apply(); assert.equal(f.messages.length,1); f.click('buy-moss'); assert.equal(f.messages.length,1); f.click('confirm'); assert.equal(f.messages.length,2);
  f.reply('save-outcome-unknown'); f.apply(); f.click('buy-blue'); assert.match(f.note(), /待核验/); assert.equal(f.messages.length,2);
});

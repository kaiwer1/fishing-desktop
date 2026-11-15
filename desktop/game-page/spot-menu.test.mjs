import test from 'node:test';
import assert from 'node:assert/strict';
import { SpotMenu } from './spot-menu.mjs';

function fixture() {
  class Element {
    children = []; attributes = {}; dataset = {}; events = {};
    constructor(tag, doc) { this.tagName = tag; this.ownerDocument = doc; }
    setAttribute(name, value) { this.attributes[name] = value; }
    append(...elements) { this.children.push(...elements); }
    replaceChildren(...elements) { this.children = elements; }
    addEventListener(type, handler) { this.events[type] = handler; }
    click() { this.events.click?.({ detail: 1 }); }
  }
  const doc = { createElement: tag => new Element(tag, doc) };
  const container = doc.createElement('section'), sent = [], context = { gate: true, result: true };
  const menu = new SpotMenu({ container, documentId: 'page', canSelect: () => context.gate,
    send: id => { sent.push(id); return context.result; } });
  const spots = [{ id: 'bank-01', name: '芦苇湾', region: '长江中游', description: '河湾背风，岸边有缓坡。', species: ['鲫鱼','鲤鱼'] },
    { id: 'reef-01', name: '岩礁岸', region: '东海', description: '潮水经过礁石外缘。', species: ['黑鲷'] }];
  const apply = (revision, activeId = 'bank-01', list = spots) => menu.apply({ kind: 'game-spot-state', documentId: 'page', revision, state: { activeId, spots: list } });
  const button = id => container.children[2].children.map(card => card.children.at(-1)).find(b => b.dataset.spotId === id);
  return { menu, sent, context, spots, apply, button, container, status: () => container.children[1].textContent };
}
test('renders regions and species from copied host facts, rejecting stale or incomplete inventories', () => {
  const f = fixture(); assert.equal(f.apply(1), true);
  assert.equal(f.button('bank-01').disabled, true); assert.equal(f.button('reef-01').disabled, false);
  const original = f.button('reef-01'); assert.equal(f.container.children[2].children[1].children[1].textContent, '东海');
  assert.match(f.container.children[2].children[1].children[3].textContent, /黑鲷/);
  assert.equal(f.apply(2), true); assert.equal(f.button('reef-01'), original);
  for (const [activeId, rows] of [['reef-02', f.spots], ['bank-01', [f.spots[0], f.spots[0]]], ['bank-01', [{ ...f.spots[0], species: [false] }]]]) {
    assert.equal(f.apply(3, activeId, rows), false);
  }
  assert.equal(f.menu.apply({ kind:'game-spot-state', documentId:'old', revision:3, state:{activeId:null,spots:[]} }), false);
  assert.equal(f.apply(1), false);
  f.spots[0].name = 'changed'; f.menu.state().spots[1].species.length = 0;
  assert.equal(f.menu.state().spots[0].name, '芦苇湾'); assert.deepEqual(f.menu.state().spots[1].species, ['黑鲷']);
  assert.equal(f.apply(3, null, []), true); assert.match(f.status(), /没有开放/);
});
test('selection waits for an authoritative destination and rechecks live permissions even on forced clicks', () => {
  const f = fixture(); f.apply(1); f.context.gate = '收回这一竿后可切换钓点。';
  f.button('reef-01').click(); f.menu.render(); assert.equal(f.button('reef-01').disabled, true);
  assert.deepEqual(f.sent, []); assert.match(f.status(), /收回/);
  f.context.gate = false; f.menu.render(); f.button('reef-01').click(); assert.deepEqual(f.sent, []);
  f.context.gate = true; f.menu.render(); f.button('bank-01').click(); f.button('reef-01').click(); f.button('reef-01').click();
  assert.deepEqual(f.sent, ['reef-01']); assert.equal(f.menu.pending, 'reef-01'); assert.equal(f.menu.state().activeId, 'bank-01');
  f.apply(2); assert.equal(f.menu.pending, 'reef-01'); f.apply(3,'reef-01'); assert.equal(f.menu.pending, null);
  assert.equal(f.button('reef-01').disabled, true); assert.equal(f.button('bank-01').disabled, false); assert.match(f.status(), /当前钓点 · 岩礁岸/);
});
test('failure, host catalogue replacement and lease cancellation never resend a move', () => {
  const f = fixture(); f.apply(1); f.context.result = false; const old = f.button('reef-01'); old.click();
  assert.equal(f.menu.pending, null); assert.match(f.status(), /未能发送/);
  f.context.result = true; old.click(); f.menu.reject('活动钓竿占用钓位。'); assert.match(f.status(), /活动钓竿/);
  f.apply(2); assert.match(f.status(), /活动钓竿/); f.menu.cancelPending(); old.click(); assert.equal(f.menu.pending, 'reef-01');
  f.apply(3, 'bank-01', [f.spots[0]]); assert.equal(f.menu.pending, null); assert.match(f.status(), /所选钓点已关闭/);
  old.click(); assert.equal(f.sent.length, 3);
  f.apply(4); f.button('reef-01').click(); f.menu.cancelPending(); assert.equal(f.menu.pending, null); assert.equal(f.sent.length, 4);
});

test('station distances follow the angler in the active water while other maps show directions from their own centre', () => {
  const f = fixture();
  const station = (id, name, xMm, fish) => ({id, name, xMm, targetXmm:xMm, targetZmm:19000,
    depthMm:xMm < 0 ? 1210 : 2320, habitat:xMm < 0 ? '浅滩' : '深槽', fish});
  const spots = f.spots.map(s => ({...s, stations:[
    station('west', '左滩', -6000, s.species.slice(0, 1)), station('east', '右槽', 6000, s.species.slice(-1))
  ]}));
  assert.equal(f.apply(1, spots[0].id, spots), true);
  const cards = f.container.children[2].children;
  const lists = cards.map(card => card.children.at(-2).children);
  assert.equal(lists[0][0].dataset.station, 'west');
  assert.equal(lists[0][0].children[3].textContent, '鲫鱼');
  assert.match(lists[0][1].children[2].textContent, /深槽.*离岸 19 米.*水深 2.32 米/);
  f.menu.render({waterId:'bank-01',xMm:-5000});
  assert.equal(lists[0][0].children[1].textContent, '已到达');
  assert.equal(lists[0][1].children[1].textContent, '向右走 11 米');
  assert.equal(lists[1][0].children[1].textContent, '中央向左 6 米');
  f.menu.render({waterId:'bank-01',xMm:-4999});
  assert.equal(lists[0][0].children[1].textContent, '向左走 1.1 米');
  f.button('bank-01').click(); assert.deepEqual(f.sent, []);
  assert.ok(lists.flat().every(row => row.children.every(child => child.tagName !== 'button')));
  const altered = f.menu.state(); altered.spots[0].stations[0].fish[0] = '黑鲷';
  assert.equal(f.menu.state().spots[0].stations[0].fish[0], '鲫鱼');
  const row = spots[0].stations[0];
  for (const invalid of [[row, row], [{...row, depthMm:-1}], [{...row, targetZmm:NaN}], [{...row, fish:['外海鱼']} ]]) {
    assert.equal(f.apply(2, spots[0].id, [{...spots[0], stations:invalid}]), false);
  }
  assert.equal(f.apply(2, f.spots[0].id, f.spots), true);
  assert.equal(f.container.children[2].children[0].children.at(-2).className, undefined);
});

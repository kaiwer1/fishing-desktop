import test from 'node:test';
import assert from 'node:assert/strict';
import { BottomView, createBottomTipPanel } from './bottom-view.mjs';

const sessionId = '10000000-0000-0000-0000-000000000001';
const otherSession = '10000000-0000-0000-0000-000000000002';
const packet = (revision, phase, tipPermille = 0, id = sessionId) => ({
  kind: 'game-bottom-state', documentId: 'bank', revision,
  state: { sessionId: id, phase, tipPermille },
});

function fixture() {
  class Node {
    children = [];
    attributes = {};
    append(...children) { this.children.push(...children); }
    setAttribute(key, value) { this.attributes[key] = value; }
  }
  const doc = {
    createElement: () => new Node(),
    createTextNode: text => text,
    createElementNS: (namespace, tag) => {
      assert.equal(namespace, 'http://www.w3.org/2000/svg');
      const node = new Node(); node.tag = tag; return node;
    },
  };
  const view = new BottomView('bank');
  const panel = createBottomTipPanel(doc);
  const [svg, caption] = panel.element.children;
  const [title, , hint] = caption.children;
  const [, line, , tip] = svg.children;
  return { view, panel, svg, title, hint, tip, line };
}

test('only the current document and newer valid complete snapshots advance the projection', () => {
  const view = new BottomView('bank');
  const good = packet(2, 'touch', 124);
  for (const invalid of [
    { ...good, documentId: 'old' }, { ...good, kind: 'game-bite-state' }, { ...good, revision: 0 },
    packet(2, 'idle', 3, null), packet(2, 'idle'), packet(2, 'settled', 7, null),
    packet(2, 'touch', 7, '00000000-0000-0000-0000-000000000000'), packet(2, 'unknown'),
    packet(2, 'touch', -1), packet(2, 'bite', 1001), packet(2, 'touch', 1.5), packet(2, 'sinking', NaN),
    { ...good, state: { phase: 'settled', tipPermille: 0 } },
    ...['speciesId', 'massMg', 'openStep', 'seed'].map(key => ({ ...good, state: { ...good.state, [key]: 1 } })),
  ]) assert.equal(view.apply(invalid), false);
  assert.equal(view.revision, 0);
  assert.equal(view.apply(good), true);
  good.state.phase = 'hooked';
  view.forSession(sessionId).tipPermille = 999;
  assert.deepEqual(view.state(), { sessionId, phase: 'touch', tipPermille: 124 });
  assert.equal(view.apply(good), false);
  assert.equal(view.apply(packet(1, 'touch')), false);
});

test('non-bottom mode and projections for another physical cast stay hidden', () => {
  const { view, panel } = fixture();
  panel.render(view, sessionId);
  assert.equal(panel.element.hidden, true);
  view.apply(packet(1, 'idle', 0, null)); panel.render(view, null);
  assert.equal(panel.element.hidden, true);
  view.apply(packet(2, 'bite', 922)); panel.render(view, otherSession);
  assert.equal(panel.element.hidden, true);
  panel.render(view, sessionId);
  assert.equal(panel.element.hidden, false);
  view.apply(packet(3, 'idle', 0, null)); panel.render(view, sessionId);
  assert.equal(panel.element.hidden, true);
});

test('each observed bottom phase has its own instruction, only a confirmed bite invites the strike', () => {
  const { view, panel, title, hint, svg } = fixture();
  const phases = [
    ['sinking', '钓组下沉'], ['settling', '触底待稳'], ['settled', '稳底等口'],
    ['touch', '竿尖轻点'], ['bite', '竿尖下压'], ['hooked', '已提竿中鱼'], ['missed', '错过鱼口'],
    ['retrieve-required', '需要收回钓组'], ['retrieved', '钓组已收回'],
  ];
  for (const [index, [phase, label]] of phases.entries()) {
    assert.equal(view.apply(packet(index + 1, phase, 500)), true);
    panel.render(view, sessionId);
    assert.equal(panel.element.hidden, false);
    assert.equal(title.textContent, label);
    assert.equal(hint.textContent.includes('右键提竿'), phase === 'bite');
    assert.equal(svg.attributes.display === 'none', phase === 'retrieved');
  }
});

test('rod tip and attached line follow the observed bend without advancing time or inventing a bite', () => {
  const { view, panel, title, tip, line } = fixture();
  const paths = [];
  for (const [index, bend] of [0, 500, 1000].entries()) {
    view.apply(packet(index + 1, 'settled', bend)); panel.render(view, sessionId);
    const end = tip.attributes.d.split(' ').slice(-2).join(' ');
    assert.ok(line.attributes.d.startsWith('M' + end + ' L'));
    paths.push(tip.attributes.d);
    for (let n = 0; n < 5; n++) panel.render(view, sessionId);
    assert.equal(tip.attributes.d, paths[index]);
    assert.equal(title.textContent, '稳底等口');
  }
  assert.equal(new Set(paths).size, 3);
  const reloaded = fixture();
  reloaded.view.apply(packet(1, 'settled', 1000));
  reloaded.panel.render(reloaded.view, sessionId);
  assert.equal(reloaded.tip.attributes.d, tip.attributes.d);
});

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { InputSession } from './input-session.mjs';
import { PageBridge } from './page-bridge.mjs';

function mode(overrides = {}) {
  return { kind:'mode', documentId:'document-a', revision:1, cancellationEpoch:0,
    enabled:true, reason:'explicit-enable', ...overrides };
}
function fixture(documentId = 'document-a') {
  const session = new InputSession();
  return { session, bridge:new PageBridge(documentId,session) };
}

test('queued activation stays rejected after local cancellation and later refocus', () => {
  const { session:s, bridge:b } = fixture();
  assert.equal(b.apply(mode(),{focused:true}),true);
  s.pointerButtons(1,1,true);
  const queued = mode({revision:2,cancellationEpoch:s.cancellationEpoch});
  s.cancel('blur');
  assert.equal(b.apply(queued,{focused:true}),false);
  assert.equal(s.enabled,false); assert.equal(s.held.size,0);
  assert.equal(s.revision,2);
  assert.equal(b.apply(mode({revision:3,cancellationEpoch:s.cancellationEpoch}),{focused:true}),true);
  assert.deepEqual(s.pointerButtons(1,1),[]);
  assert.equal(s.presses,1);
});

test('reload rejects old-document activation and cancellation without consuming new revisions', () => {
  const { session:s, bridge:b } = fixture('document-b');
  assert.equal(b.apply(mode({revision:999})),false);
  assert.equal(s.revision,0); assert.equal(s.enabled,false);
  assert.equal(b.apply(mode({documentId:'document-b'})),true);
  s.pointerButtons(2,4,true);
  assert.equal(b.apply(mode({revision:1000,enabled:false})),false);
  assert.equal(s.revision,1); assert.equal(s.enabled,true); assert.equal(s.held.size,1);
});

test('out-of-order and duplicate commands cannot override a newer cancellation', () => {
  const { session:s, bridge:b } = fixture(); b.apply(mode());
  assert.equal(b.apply(mode({revision:3,enabled:false,reason:'host-deactivate'})),true);
  assert.equal(b.apply(mode({revision:2})),false);
  assert.equal(b.apply(mode({revision:3,cancellationEpoch:s.cancellationEpoch})),false);
  assert.equal(s.enabled,false); assert.equal(s.revision,3);
  assert.equal(b.apply(mode({revision:4,enabled:false,cancellationEpoch:0})),true);
  assert.equal(s.cancellationEpoch,2);
});

test('unfocused activation is consumed and cannot be replayed after focus returns', () => {
  const { session:s, bridge:b } = fixture(); b.apply(mode());
  s.pointerButtons(1,1,true);
  const queued = mode({revision:2});
  assert.equal(b.apply(queued,{focused:false}),false);
  assert.equal(s.enabled,false); assert.equal(s.held.size,0); assert.equal(s.revision,2);
  assert.equal(s.cancellationEpoch,1);
  assert.equal(b.apply(queued,{focused:true}),false);
  assert.equal(b.apply(mode({revision:3,cancellationEpoch:1}),{focused:true}),true);
});

test('malformed bridge messages cannot change the active session or consume a revision', () => {
  const { session:s, bridge:b } = fixture(); b.apply(mode()); s.pointerButtons(1,1,true);
  const before = s.snapshot();
  const malformed = [null,undefined,[],true,'mode',{},
    mode({kind:'ready',revision:2}), mode({documentId:null,revision:2}),
    mode({enabled:'false',revision:2}), mode({enabled:1,revision:2}),
    mode({revision:NaN}),mode({revision:Infinity}),mode({revision:2.5}),
    mode({revision:0}),mode({revision:-1}),mode({revision:Number.MAX_SAFE_INTEGER+1}),
    mode({revision:'2'}),mode({revision:2,cancellationEpoch:-1}),
    mode({revision:2,cancellationEpoch:NaN}),mode({revision:2,cancellationEpoch:0.5}),
    mode({revision:2,cancellationEpoch:'0'}),
    mode({revision:2,cancellationEpoch:Number.MAX_SAFE_INTEGER+1}),
    mode({revision:2,reason:null}),mode({revision:2,reason:'x'.repeat(129)})];
  for (const message of malformed) {
    assert.equal(b.apply(message),false);
    assert.deepEqual(s.snapshot(),before);
  }
});

test('bridge requires a nonempty bounded document ID', () => {
  for (const id of ['',null,undefined,42,'x'.repeat(129)]) {
    assert.throws(()=>new PageBridge(id,new InputSession()),TypeError);
  }
});

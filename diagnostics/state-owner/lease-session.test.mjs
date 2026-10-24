import assert from 'node:assert/strict';
import test from 'node:test';
import { LeaseSession } from './lease-session.mjs';

const message = (overrides = {}) => ({
  documentId: 'document-a', revision: 1, cancellationEpoch: 0, lease: 'lease-a', ...overrides,
});

test('startup is disabled and an explicit current-epoch grant activates the page', () => {
  const session = new LeaseSession('document-a');
  assert.deepEqual(session.state(), { epoch: 0, lease: null, revision: 0 });
  assert.equal(session.apply(message()), true);
  assert.deepEqual(session.state(), { epoch: 0, lease: 'lease-a', revision: 1 });
  const copy = session.state(); copy.lease = 'altered'; copy.epoch = 100;
  assert.equal(session.lease, 'lease-a'); assert.equal(session.epoch, 0);
});

test('local cancellation returns only the old token and rejects a queued grant', () => {
  const session = new LeaseSession('document-a');
  session.apply(message());
  const queued = message({ revision: 2, lease: 'lease-b' });
  assert.deepEqual(session.cancel(), { lease: 'lease-a', cancellationEpoch: 1 });
  assert.equal(session.apply(queued), false);
  assert.deepEqual(session.state(), { epoch: 1, lease: null, revision: 2 });
  assert.equal(session.apply(queued), false);
  assert.equal(session.apply(message({ revision: 3, cancellationEpoch: 1, lease: 'lease-c' })), true);
  assert.equal(session.lease, 'lease-c');
});

test('cancellation before the first grant still prevents that grant from enabling input', () => {
  const session = new LeaseSession('document-a');
  assert.deepEqual(session.cancel(), { lease: '', cancellationEpoch: 1 });
  assert.equal(session.apply(message()), false);
  assert.equal(session.lease, null); assert.equal(session.revision, 1);
  assert.deepEqual(session.cancel(), { lease: '', cancellationEpoch: 2 });
  assert.equal(session.apply(message({ revision: 2, cancellationEpoch: 1 })), false);
  assert.equal(session.apply(message({ revision: 3, cancellationEpoch: 2 })), true);
});

test('older or repeated messages cannot revoke or replace a newer lease', () => {
  const session = new LeaseSession('document-a');
  assert.equal(session.apply(message({ revision: 5, lease: 'current' })), true);
  assert.equal(session.apply(message({ revision: 4, lease: null })), false);
  assert.equal(session.apply(message({ revision: 5, lease: 'replayed' })), false);
  assert.equal(session.apply(message({ revision: 3 })), false);
  assert.deepEqual(session.state(), { epoch: 0, lease: 'current', revision: 5 });
});

test('messages from another document do not consume revision or affect the current lease', () => {
  const session = new LeaseSession('document-a'); session.apply(message());
  assert.equal(session.apply(message({ documentId: 'document-old', revision: 999, lease: null })), false);
  assert.equal(session.apply(message({ documentId: 'document-old', revision: 1000 })), false);
  assert.deepEqual(session.state(), { epoch: 0, lease: 'lease-a', revision: 1 });
  assert.equal(session.apply(message({ revision: 2, lease: 'new' })), true);
});

test('a newer explicit revoke clears the lease regardless of cancellation epoch', () => {
  const session = new LeaseSession('document-a');
  session.cancel(); session.apply(message({ cancellationEpoch: 1 }));
  assert.equal(session.apply(message({ revision: 2, cancellationEpoch: 0, lease: null })), true);
  assert.deepEqual(session.state(), { epoch: 1, lease: null, revision: 2 });
  session.apply(message({ revision: 3, cancellationEpoch: 1 }));
  assert.equal(session.apply(message({ revision: 4, cancellationEpoch: 99, lease: null })), true);
  assert.deepEqual(session.state(), { epoch: 1, lease: null, revision: 4 });
});

test('a grant with a different epoch fails closed without rewriting the local epoch', () => {
  const session = new LeaseSession('document-a'); session.apply(message());
  assert.equal(session.apply(message({ revision: 2, cancellationEpoch: 2 })), false);
  assert.deepEqual(session.state(), { epoch: 0, lease: null, revision: 2 });
  assert.equal(session.apply(message({ revision: 3 })), true);
});

test('malformed messages neither consume revisions nor change an active lease', () => {
  const session = new LeaseSession('document-a'); session.apply(message());
  const invalid = [null, undefined, [], 'lease', {},
    ...[null, '', undefined, 4].map(documentId => message({ documentId, revision: 2 })),
    ...[null, '2', 0, -1, 1.5, Infinity, NaN, Number.MAX_SAFE_INTEGER + 1].map(revision => message({ revision })),
    ...[null, '0', -1, 0.5, Infinity, NaN, Number.MAX_SAFE_INTEGER + 1].map(cancellationEpoch => message({ revision: 2, cancellationEpoch })),
    ...[undefined, '', 0, false, {}, [], 'x'.repeat(129)].map(lease => message({ revision: 2, lease })),
  ];
  for (const input of invalid) {
    assert.equal(session.apply(input), false);
    assert.deepEqual(session.state(), { epoch: 0, lease: 'lease-a', revision: 1 });
  }
});

test('invalid document identities are rejected and exhausted cancellation fails closed', () => {
  for (const id of [null, undefined, '', 1, {}, 'x'.repeat(129)]) {
    assert.throws(() => new LeaseSession(id), TypeError);
  }
  const session = new LeaseSession('document-a'); session.apply(message());
  session.epoch = Number.MAX_SAFE_INTEGER;
  assert.throws(() => session.cancel(), RangeError);
  assert.equal(session.lease, null);
  assert.equal(session.epoch, Number.MAX_SAFE_INTEGER);
});

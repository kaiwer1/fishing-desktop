// Each page receives leases for its current cancellation epoch only.
// This is an isolated diagnostic contract, not native input authorization.
export class LeaseSession {
  epoch = 0;
  revision = 0;
  lease = null;

  constructor(documentId) {
    if (typeof documentId !== 'string' || documentId.length === 0 || documentId.length > 128) {
      throw new TypeError('A document ID is required');
    }
    this.documentId = documentId;
  }

  cancel() {
    const lease = this.lease ?? '';
    this.lease = null;
    if (this.epoch >= Number.MAX_SAFE_INTEGER) throw new RangeError('Cancellation epoch exhausted');
    return { lease, cancellationEpoch: ++this.epoch };
  }

  apply(message) {
    if (!message || typeof message !== 'object' || Array.isArray(message) ||
        message.documentId !== this.documentId ||
        !Number.isSafeInteger(message.revision) || message.revision <= this.revision ||
        !Number.isSafeInteger(message.cancellationEpoch) || message.cancellationEpoch < 0 ||
        !(message.lease === null || (typeof message.lease === 'string' && message.lease.length > 0 && message.lease.length <= 128))) {
      return false;
    }
    // Even a grant queued before a local cancellation consumes its revision.
    // Replaying it after focus returns must not restore that old activation.
    this.revision = message.revision;
    this.lease = null;
    if (message.lease === null) return true;
    if (message.cancellationEpoch !== this.epoch) return false;
    this.lease = message.lease;
    return true;
  }

  state() {
    return { epoch: this.epoch, lease: this.lease, revision: this.revision };
  }
}

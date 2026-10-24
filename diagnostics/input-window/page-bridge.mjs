// Each loaded document owns one bridge; commands from an earlier document never apply.
export class PageBridge {
  constructor(documentId, session) {
    if (typeof documentId !== 'string' || documentId.length === 0 || documentId.length > 128) {
      throw new TypeError('A document ID is required');
    }
    this.documentId = documentId;
    this.session = session;
  }

  apply(message, { focused = true } = {}) {
    if (!message || typeof message !== 'object' || Array.isArray(message) ||
        message.kind !== 'mode' || message.documentId !== this.documentId ||
        typeof message.enabled !== 'boolean' || typeof message.reason !== 'string' ||
        message.reason.length > 128 ||
        !Number.isSafeInteger(message.revision) || message.revision <= 0 ||
        !Number.isSafeInteger(message.cancellationEpoch) || message.cancellationEpoch < 0) return false;

    if (message.enabled && focused !== true) {
      // Consume the revision and cancel locally. Regaining focus alone cannot replay it.
      this.session.setMode(false, message.revision, 'enable-unfocused');
      return false;
    }
    return this.session.setMode(message.enabled, message.revision, message.reason, message.cancellationEpoch);
  }
}

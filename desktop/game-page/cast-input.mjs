// Page input pairing only. The host owns elapsed time, power, target, costs and
// the opaque preparation token. No page reply can create or replay a cast.
export class CastInput {
  #readContext;
  #post;
  #id;
  #next = 0;
  #active = null;
  #pending = new Map();

  constructor({ readContext, post, newTransportId }) {
    this.#readContext = readContext;
    this.#post = post;
    this.#id = newTransportId;
  }
  state() { return this.#active ? { ...this.#active } : null; }
  #sameContext() {
    const context = this.#readContext();
    return Boolean(this.#active && context.allowed && context.lease === this.#active.lease);
  }
  #send(action, active) {
    const transportId = this.#id();
    this.#pending.set(transportId, { action, inputSequence: active.inputSequence });
    this.#post({ kind: 'game-cast', lease: active.lease, transportId, action, inputSequence: active.inputSequence });
  }
  forget() { this.#active = null; this.#pending.clear(); }
  reconcile() {
    // Host revocation, a new grant or a new document must not retarget an old
    // physical release. The monotonic page-lifetime sequence is never reset.
    if (this.#active && !this.#sameContext()) this.forget();
  }
  begin(source = 'button') {
    this.reconcile();
    const context = this.#readContext();
    if (this.#active || !context.allowed || !context.lease || this.#next === Number.MAX_SAFE_INTEGER) return false;
    this.#pending.clear();
    this.#active = { source, lease: context.lease, inputSequence: String(++this.#next), phase: 'charging' };
    this.#send('begin', this.#active);
    return true;
  }
  release(source = 'button') {
    this.reconcile();
    if (!this.#active || this.#active.source !== source || this.#active.phase !== 'charging') return false;
    // Do not await a begin reply: a quick physical click still has a paired up.
    this.#active.phase = 'prepared';
    this.#send('release', this.#active);
    return true;
  }
  cancel() {
    const active = this.#active;
    if (!active) return false;
    this.forget();
    this.#send('cancel', active); // captured lease, never a replacement grant
    return true;
  }
  pointer(pointerId, buttons, begin = false) {
    if (!Number.isSafeInteger(pointerId) || !Number.isSafeInteger(buttons) || buttons < 0 || buttons > 31) return false;
    // A second mouse button may arrive as pointermove, not pointerdown.
    if (buttons & 2) return this.cancel();
    const source = `pointer:${pointerId}`;
    if (begin && (buttons & 1)) return this.begin(source);
    if (!(buttons & 1)) return this.release(source);
    return false;
  }
  pointerLost(pointerId) {
    // Normal pointerup releases capture after we have sent release. That loss
    // must not enqueue cancellation of the already prepared gesture.
    return this.#active?.source === `pointer:${pointerId}` && this.#active.phase === 'charging' ? this.cancel() : false;
  }
  pointerCancelled(pointerId) {
    return this.#active?.source === `pointer:${pointerId}` ? this.cancel() : false;
  }
  reply(message) {
    const pending = this.#pending.get(message.transportId);
    if (!pending) return false;
    this.#pending.delete(message.transportId);
    const expected = { begin: 'charging', release: 'prepared', cancel: 'cancelled' }[pending.action];
    if (message.code !== expected && this.#active?.inputSequence === pending.inputSequence) this.forget();
    return true;
  }
  hostState(state) {
    if (this.#active?.inputSequence === state.inputSequence && ['committed', 'cancelled', 'rejected', 'unknown'].includes(state.phase)) this.forget();
  }
}

// The observer can outlive multiple interaction documents. Input sequences
// restart in those documents, so presentation uses the owner's emission order.
export class FlightNotices {
  revision = 0;
  count = 0;
  last = null;
  apply(message) {
    if (!Number.isSafeInteger(message.revision) || message.revision <= this.revision ||
        typeof message.inputSequence !== 'string' || !/^[1-9]\d{0,18}$/.test(message.inputSequence) ||
        (message.inputSequence.length === 19 && message.inputSequence > '9223372036854775807') ||
        typeof message.sessionId !== 'string' || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(message.sessionId) ||
        message.sessionId === '00000000-0000-0000-0000-000000000000' ||
        !Number.isInteger(message.powerPermille) || message.powerPermille < 0 || message.powerPermille > 1000) return false;
    this.revision = message.revision; this.count++; this.last = { ...message };
    return true;
  }
}

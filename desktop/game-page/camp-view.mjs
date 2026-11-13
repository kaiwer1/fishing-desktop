const phases = new Set(['idle', 'working', 'ready', 'retry', 'confirmed', 'cancelled', 'unknown']);
const text = value => typeof value === 'string' && value.length > 0 && value.length <= 128;
const counter = value => typeof value === 'string' && /^(0|[1-9]\d{0,18})$/.test(value) &&
  (value.length < 19 || value <= '9223372036854775807');
const copy = state => ({ available: state.available, reason: state.reason, lease: state.lease, phase: state.phase,
  elapsedMilliseconds: state.elapsedMilliseconds, requiredMilliseconds: state.requiredMilliseconds,
  rewardMinor: state.rewardMinor, stateSequence: state.stateSequence });
function validPacket(message) {
  const s = message?.state;
  return message?.kind === 'game-camp-state' && text(message.documentId) && Number.isSafeInteger(message.revision) && message.revision > 0 &&
    s && typeof s.available === 'boolean' && (s.available ? s.reason === null && text(s.lease) && counter(s.stateSequence) : text(s.reason)) &&
    (s.lease === null || text(s.lease)) && (s.stateSequence === null || counter(s.stateSequence)) && phases.has(s.phase) &&
    (s.phase !== 'unknown' || !s.available) && Number.isInteger(s.elapsedMilliseconds) && s.elapsedMilliseconds >= 0 && s.elapsedMilliseconds <= 60000 &&
    s.requiredMilliseconds === 60000 && s.rewardMinor === '800';
}

// No page clock: progress and confirmed income only come from host snapshots.
export class CampView {
  #documentId; #revision = 0; #state = null;
  constructor(documentId) { this.#documentId = documentId; }
  get revision() { return this.#revision; }
  state() { return this.#state ? copy(this.#state) : null; }
  apply(message) {
    if (!validPacket(message) || message.documentId !== this.#documentId || message.revision <= this.#revision) return false;
    this.#state = copy(message.state); this.#revision = message.revision; return true;
  }
}

const singleGesture = event => event == null || (event.detail === undefined || Number.isInteger(event.detail) && event.detail >= 0 && event.detail <= 1) && event.repeat !== true;
export class CampInput {
  #context; #post; #id; #documentId = null; #lease = null; #revision = 0; #state = null;
  #inflight = null; #awaitingState = false; #faulted = false; #next = 0;
  constructor({ readContext, post, newTransportId }) { this.#context = readContext; this.#post = post; this.#id = newTransportId; }
  state() { return { ready: Boolean(this.#state?.available), phase: this.#state?.phase ?? null,
    inflight: this.#inflight ? { ...this.#inflight } : null, awaitingState: this.#awaitingState, faulted: this.#faulted }; }
  #eligible(c) { return Boolean(c && c.allowed === true && text(c.documentId) && text(c.lease) && c.menuOpen === true &&
    c.sessionId === null && c.pendingResult === false && c.faulted === false && !this.#faulted); }
  #forget() { this.#state = null; this.#inflight = null; this.#awaitingState = false; }
  reconcile() {
    const c = this.#context(); if (c?.faulted === true) this.#faulted = true;
    if (this.#documentId !== c?.documentId) { this.#forget(); this.#documentId = c?.documentId ?? null; this.#revision = 0; this.#lease = null; }
    if (!this.#eligible(c) || this.#lease !== c.lease) { this.#forget(); this.#lease = this.#eligible(c) ? c.lease : null; }
  }
  hostState(message) {
    this.reconcile();
    if (!validPacket(message) || message.documentId !== this.#documentId || message.revision <= this.#revision) return false;
    this.#revision = message.revision;
    if (message.state.phase === 'unknown' || ['save-outcome-unknown', 'camp-work-save-unknown'].includes(message.state.reason)) {
      this.#faulted = true; this.#forget(); this.#lease = null; return true;
    }
    if (!this.#eligible(this.#context()) || !message.state.available || message.state.lease !== this.#lease) this.#state = null;
    else this.#state = copy(message.state);
    if (!this.#inflight) this.#awaitingState = false;
    return true;
  }
  #act(action, allowedPhases, event) {
    this.reconcile();
    if (!singleGesture(event) || !this.#eligible(this.#context()) || !this.#state?.available || this.#state.lease !== this.#lease ||
        this.#inflight || this.#awaitingState || this.#next === Number.MAX_SAFE_INTEGER || !allowedPhases.includes(this.#state.phase)) return false;
    const message = { kind: 'game-camp', documentId: this.#documentId, lease: this.#lease, transportId: this.#id(), action, inputSequence: String(++this.#next) };
    this.#inflight = { transportId: message.transportId, documentId: message.documentId, action };
    this.#post(message); return true;
  }
  start(event = null) { return this.#act('start', ['idle', 'cancelled', 'confirmed'], event); }
  claim(event = null) { return this.#act('claim', ['ready', 'retry'], event); }
  cancel(event = null) { return this.#act('cancel', ['working', 'ready', 'retry'], event); }
  reply(message) {
    this.reconcile();
    if (!this.#inflight || message?.kind !== 'game-reply' || message.documentId !== this.#documentId || message.transportId !== this.#inflight.transportId) return false;
    this.#inflight = null; this.#awaitingState = true;
    if (['save-outcome-unknown', 'camp-work-save-unknown'].includes(message.code)) { this.#faulted = true; this.#forget(); this.#lease = null; }
    return true;
  }
}

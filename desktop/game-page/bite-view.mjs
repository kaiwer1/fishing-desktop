const phases = new Set(['idle', 'settling', 'searching', 'approaching', 'nibbling', 'strikeable', 'hooked', 'retrieve-required', 'ended']);
const strikePhases = new Set(['settling', 'searching', 'approaching', 'nibbling', 'strikeable']);
const guid = value => typeof value === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(value) && value !== '00000000-0000-0000-0000-000000000000';
const counter = value => typeof value === 'string' && /^(0|[1-9]\d{0,18})$/.test(value) && (value.length < 19 || value <= '9223372036854775807');
const offset = value => Number.isInteger(value) && value >= -300 && value <= 300;
const copy = s => ({ sessionId: s.sessionId, encounterId: s.encounterId, phase: s.phase, reason: s.reason,
  stepIndex: s.stepIndex, searchEpoch: s.searchEpoch, baitAttached: s.baitAttached,
  bobberOffsetMm: s.bobberOffsetMm, bobberLateralMm: s.bobberLateralMm });

// Host-only timing and target selection. This projection deliberately keeps no
// species, mass, probability, future window, storage identity or local clock.
export class BiteView {
  #documentId;
  #revision = 0;
  #state = null;
  constructor(documentId) { this.#documentId = documentId; }
  get revision() { return this.#revision; }
  state() { return this.#state ? copy(this.#state) : null; }
  apply(message) {
    if (!message || message.kind !== 'game-bite-state' || message.documentId !== this.#documentId ||
        !Number.isSafeInteger(message.revision) || message.revision <= this.#revision) return false;
    const s = message.state;
    if (!s || !phases.has(s.phase) || !(s.reason === null || (typeof s.reason === 'string' && s.reason.length <= 128)) ||
        !counter(s.stepIndex) || !counter(s.searchEpoch) || typeof s.baitAttached !== 'boolean' ||
        !offset(s.bobberOffsetMm) || !offset(s.bobberLateralMm) || !(s.encounterId === null || guid(s.encounterId))) return false;
    if (s.phase === 'idle' ? s.sessionId !== null || s.encounterId !== null : !guid(s.sessionId)) return false;
    if (['approaching', 'nibbling', 'strikeable', 'hooked'].includes(s.phase) && !guid(s.encounterId)) return false;
    this.#state = copy(s); this.#revision = message.revision; return true;
  }
  diagram() {
    const s = this.#state;
    if (!s || s.phase === 'idle' || s.phase === 'ended') return null;
    // Fixed 600 mm wide close-up. Positive host height rises above water;
    // no smoothing or time prediction can move the float into a bite window.
    return { x: 180 + s.bobberLateralMm * 0.4, y: 125 - s.bobberOffsetMm * 0.25,
      submerged: s.bobberOffsetMm < -80, phase: s.phase, baitAttached: s.baitAttached };
  }
}

// A strike is a fresh physical edge or explicit fallback click. Its session,
// encounter and lease are captured together and never retargeted after send.
export class StrikeInput {
  #readContext;
  #post;
  #id;
  #next = 0;
  #pending = null;
  #rightHeld = false;
  constructor({ readContext, post, newTransportId }) { this.#readContext = readContext; this.#post = post; this.#id = newTransportId; }
  state() { return { rightHeld: this.#rightHeld, pending: this.#pending ? { ...this.#pending } : null }; }
  reconcile() {
    if (!this.#pending) return;
    const c = this.#readContext();
    if (!c.allowed || c.lease !== this.#pending.lease || c.sessionId !== this.#pending.sessionId || c.encounterId !== this.#pending.encounterId) this.#pending = null;
    // Do not clear held input on revocation; restoring control while the mouse
    // remains down must not make that same press eligible again.
  }
  strike() {
    this.reconcile();
    const c = this.#readContext();
    if (this.#pending || !c.allowed || !c.lease || !guid(c.sessionId) || !(c.encounterId === null || guid(c.encounterId)) ||
        !strikePhases.has(c.phase) || this.#next === Number.MAX_SAFE_INTEGER) return false;
    const message = { kind: 'game-strike', lease: c.lease, transportId: this.#id(), sessionId: c.sessionId,
      encounterId: c.encounterId, inputSequence: String(++this.#next) };
    this.#pending = { ...message }; this.#post(message); return true;
  }
  mouseDown(button, buttons) {
    if (button !== 2 || !Number.isInteger(buttons) || buttons < 0 || buttons > 31 || !(buttons & 2)) return false;
    const held = this.#rightHeld; this.#rightHeld = true;
    return !held && this.strike();
  }
  mouseButtons(buttons) {
    if (!Number.isInteger(buttons) || buttons < 0 || buttons > 31) return;
    // A move carrying an already-held right button records that hold but does
    // not strike. Up outside the target is observed by the window listener.
    this.#rightHeld = Boolean(buttons & 2);
  }
  reply(message) {
    if (this.#pending?.transportId !== message.transportId) return false;
    this.#pending = null; return true;
  }
}

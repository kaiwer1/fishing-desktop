const phases = new Set(['idle', 'fighting', 'landing-eligible', 'landing-pending', 'failure-pending', 'escaped', 'landed', 'capacity-blocked', 'faulted', 'ended']);
const activePhases = new Set(['fighting', 'landing-eligible']);
const settlementActions = new Set(['land', 'retry-land', 'retry-escape']);
const unknownCodes = new Set(['save-outcome-unknown', 'landing-save-unknown', 'failure-save-unknown']);
const guid = value => typeof value === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(value) && value !== '00000000-0000-0000-0000-000000000000';
const counter = value => typeof value === 'string' && /^(0|[1-9]\d{0,18})$/.test(value) && (value.length < 19 || value <= '9223372036854775807');
const integer = (value, minimum, maximum) => Number.isInteger(value) && value >= minimum && value <= maximum;
const reason = value => value === null || typeof value === 'string' && value.length <= 128;
const fields = ['sessionId', 'encounterId', 'phase', 'reason', 'landingReason', 'stepIndex', 'xMm', 'yMm', 'zMm', 'outLineMm', 'spoolRemainingMm', 'slackMm',
  'tensionMilliN', 'dragMilliN', 'rodDeflectionMm', 'energyPermille', 'reeling', 'dragLevel', 'reelLevel', 'canRetryLanding', 'unhookExposureSteps', 'canRetryFailure'];
const copy = state => Object.fromEntries(fields.map(key => [key, state[key]]));

// Rendering reads only current host measurements. It never integrates fish,
// changes line, predicts a catch or exposes the locked species/body mass.
export class FightView {
  #documentId;
  #revision = 0;
  #state = null;
  constructor(documentId) { this.#documentId = documentId; }
  get revision() { return this.#revision; }
  state() { return this.#state ? copy(this.#state) : null; }
  apply(message) {
    if (!message || message.kind !== 'game-fight-state' || message.documentId !== this.#documentId || !Number.isSafeInteger(message.revision) || message.revision <= this.#revision) return false;
    const s = message.state;
    if (!s || !phases.has(s.phase) || !reason(s.reason) || !reason(s.landingReason) || !counter(s.stepIndex) ||
        !['xMm', 'yMm', 'zMm'].every(key => integer(s[key], -10000000, 10000000)) ||
        !['outLineMm', 'spoolRemainingMm', 'slackMm'].every(key => integer(s[key], 0, 100000000)) ||
        !['tensionMilliN', 'dragMilliN'].every(key => integer(s[key], 0, 10000000)) ||
        !integer(s.rodDeflectionMm, 0, 1000000000) || !integer(s.energyPermille, 0, 1000) ||
        !integer(s.dragLevel, 0, 10) || !integer(s.reelLevel, 0, 10) || typeof s.reeling !== 'boolean' || typeof s.canRetryLanding !== 'boolean' ||
        !integer(s.unhookExposureSteps, 0, 12000) || typeof s.canRetryFailure !== 'boolean') return false;
    if (s.phase === 'idle' ? s.sessionId !== null || s.encounterId !== null : !guid(s.sessionId) || !guid(s.encounterId)) return false;
    if (s.canRetryLanding && s.phase !== 'landing-pending' || s.canRetryFailure && s.phase !== 'failure-pending' ||
        ['failure-pending', 'escaped'].includes(s.phase) && s.reeling) return false;
    this.#state = copy(s); this.#revision = message.revision; return true;
  }
  diagram() {
    const s = this.#state;
    if (!s || ['idle', 'ended', 'escaped'].includes(s.phase)) return null;
    // A fixed top-down training diagram: X +/-40 m and Z 0..80 m.
    // Out-of-range locations are marked clipped instead of moving the world.
    return { x: 240 + Math.max(-40000, Math.min(40000, s.xMm)) * 200 / 40000,
      y: 220 - Math.max(0, Math.min(80000, s.zMm)) * 192 / 80000,
      clipped: Math.abs(s.xMm) > 40000 || s.zMm < 0 || s.zMm > 80000,
      distanceMm: Math.hypot(s.xMm, s.zMm), depthMm: -s.yMm, phase: s.phase,
      energyPermille: s.energyPermille, reeling: s.reeling };
  }
}

export class FightInput {
  #readContext;
  #post;
  #id;
  #next = 0;
  #held = null;
  #pending = new Map();
  #faulted = false;
  constructor({ readContext, post, newTransportId }) { this.#readContext = readContext; this.#post = post; this.#id = newTransportId; }
  state() { return { held: this.#held ? { ...this.#held } : null, pending: [...this.#pending.values()].map(p => ({ ...p })), faulted: this.#faulted }; }
  #same(a, b) { return a.lease === b.lease && a.sessionId === b.sessionId && a.encounterId === b.encounterId; }
  #valid(c) { return Boolean(!this.#faulted && c.allowed && c.lease && guid(c.sessionId) && guid(c.encounterId)); }
  #send(action, c, value) {
    if (this.#next === Number.MAX_SAFE_INTEGER || this.#held && action !== 'reel-stop' && this.#next >= Number.MAX_SAFE_INTEGER - 1) return null;
    const m = { kind: 'game-fight', lease: c.lease, transportId: this.#id(), sessionId: c.sessionId, encounterId: c.encounterId,
      inputSequence: String(++this.#next), action };
    if (value !== undefined) m.value = value;
    // Retain only bounded reply pairing; dropping an old reply record cannot
    // undo a held release or resend any action.
    if (this.#pending.size >= 64) this.#pending.delete(this.#pending.keys().next().value);
    this.#pending.set(m.transportId, { ...m }); this.#post(m); return m;
  }
  reconcile() {
    if (!this.#held) return;
    const c = this.#readContext();
    if (!this.#valid(c) || !activePhases.has(c.phase) || !this.#same(c, this.#held)) this.cancel();
  }
  begin(source = 'button') {
    this.reconcile(); const c = this.#readContext();
    // Reserve one sequence for the paired stop at the artificial JS limit.
    if (this.#held || !this.#valid(c) || !activePhases.has(c.phase) || this.#next >= Number.MAX_SAFE_INTEGER - 1) return false;
    const sent = this.#send('reel-start', c);
    this.#held = { source, lease: c.lease, sessionId: c.sessionId, encounterId: c.encounterId, startTransportId: sent.transportId };
    return true;
  }
  end(source = 'button', startTransportId = null) {
    if (!this.#held || this.#held.source !== source || startTransportId !== null && this.#held.startTransportId !== startTransportId) return false;
    return this.cancel();
  }
  cancel() {
    const held = this.#held; if (!held) return false;
    this.#held = null; this.#send('reel-stop', held); return true;
  }
  adjust(action, value) {
    this.reconcile(); const c = this.#readContext();
    if (!this.#valid(c) || !activePhases.has(c.phase) || !['drag', 'speed'].includes(action) || !integer(value, 0, 10)) return false;
    return Boolean(this.#send(action, c, value));
  }
  #landing(action) {
    this.reconcile(); const c = this.#readContext();
    if (!this.#valid(c) || (action === 'land' ? c.phase !== 'landing-eligible' : c.phase !== 'landing-pending' || !c.canRetryLanding)) return false;
    if ([...this.#pending.values()].some(m => settlementActions.has(m.action) && this.#same(m, c))) return false;
    this.cancel(); return Boolean(this.#send(action, c));
  }
  land() { return this.#landing('land'); }
  retryLanding() { return this.#landing('retry-land'); }
  retryFailure() {
    this.reconcile(); const c = this.#readContext();
    if (!this.#valid(c) || c.phase !== 'failure-pending' || !c.canRetryFailure ||
        [...this.#pending.values()].some(m => settlementActions.has(m.action) && this.#same(m, c))) return false;
    this.cancel(); return Boolean(this.#send('retry-escape', c));
  }
  reply(message) {
    const pending = this.#pending.get(message.transportId); if (!pending) return false;
    this.#pending.delete(message.transportId);
    if (unknownCodes.has(message.code)) { this.#faulted = true; this.#held = null; this.#pending.clear(); return true; }
    if (pending.action === 'reel-start' && message.code !== 'reel-held' && this.#held?.startTransportId === message.transportId) this.cancel();
    return true;
  }
}

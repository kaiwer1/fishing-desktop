const directionOf = code => code === 'KeyA' || code === 'ArrowLeft' ? -1 :
  code === 'KeyD' || code === 'ArrowRight' ? 1 : 0;
const shift = code => code === 'ShiftLeft' || code === 'ShiftRight';
const coordinate = n => Number.isSafeInteger(n) && Math.abs(n) <= 1000000000;
function valid(s) {
  if (!s || typeof s.enabled !== 'boolean') return false;
  if (!s.enabled) return Object.keys(s).length === 1;
  return typeof s.waterId === 'string' && /^[a-z0-9][a-z0-9._-]{0,95}$/.test(s.waterId) &&
    Number.isSafeInteger(s.geometryVersion) && s.geometryVersion > 0 &&
    Number.isSafeInteger(s.poseRevision) && s.poseRevision >= 0 &&
    [s.minXmm,s.xMm,s.maxXmm].every(coordinate) && s.minXmm < s.maxXmm && s.xMm >= s.minXmm && s.xMm <= s.maxXmm &&
    [-1,0,1].includes(s.direction) && typeof s.fast === 'boolean' && (s.direction !== 0 || !s.fast);
}
const copy = s => s && (s.enabled ? { enabled:true, waterId:s.waterId, geometryVersion:s.geometryVersion, xMm:s.xMm,
  minXmm:s.minXmm, maxXmm:s.maxXmm, direction:s.direction, fast:s.fast, poseRevision:s.poseRevision } : { enabled:false });

// Motion is integrated by the host. Only changes to physically held movement
// keys are posted, with the lease and water version captured on that key press.
export class BankWalk {
  #documentId; #read; #post; #id; #state = null; #revision = 0; #sequence = 0;
  #held = new Set(); #scope = null; #direction = 0; #fast = false; #transport = null;
  constructor({ documentId, readContext, post, newTransportId }) {
    this.#documentId = documentId; this.#read = readContext; this.#post = post; this.#id = newTransportId;
  }
  get revision() { return this.#revision; }
  get pending() { return this.#transport !== null; }
  state() { return copy(this.#state); }
  forWater(water) { return this.#state?.enabled && this.#state.waterId === water?.waterId && this.#state.geometryVersion === water?.geometryVersion ? this.state() : null; }
  canCast(water) {
    if (!this.#state?.enabled) return !this.#transport;
    return !this.#transport && this.#direction === 0 && this.#state.direction === 0 &&
      Boolean(this.forWater(water)) && water.anchor?.xMm === this.#state.xMm;
  }
  aim(water, selected) {
    if (!selected || !this.canCast(water)) return null;
    return this.#state?.enabled ? { ...selected, poseRevision:this.#state.poseRevision } : selected;
  }
  apply(m) {
    if (m?.kind !== 'game-bank-state' || m.documentId !== this.#documentId || !Number.isSafeInteger(m.revision) ||
        m.revision <= this.#revision || !valid(m.state)) return false;
    this.#revision = m.revision; this.#state = copy(m.state); this.reconcile(); this.#settle(); return true;
  }
  #live(c) { return this.#state?.enabled && c.allowed && c.lease && c.waterId === this.#state.waterId && c.geometryVersion === this.#state.geometryVersion; }
  reconcile() {
    if (!this.#scope) return;
    const c = this.#read();
    if (!this.#live(c) || c.lease !== this.#scope.lease || c.waterId !== this.#scope.waterId ||
        c.geometryVersion !== this.#scope.geometryVersion) this.stop();
  }
  key(event, down) {
    const code = event.code;
    if (!directionOf(code) && !shift(code)) return false;
    this.reconcile(); const c = this.#read();
    if (!this.#live(c)) return false;
    if (down) {
      if (event.repeat) return !shift(code);
      if (this.#held.has(code)) return !shift(code);
      this.#scope ??= { lease:c.lease, waterId:c.waterId, geometryVersion:c.geometryVersion };
      this.#held.add(code);
    } else if (!this.#held.delete(code)) return false;
    const left = this.#held.has('KeyA') || this.#held.has('ArrowLeft');
    const right = this.#held.has('KeyD') || this.#held.has('ArrowRight');
    const direction = Number(right) - Number(left);
    const fast = direction !== 0 && (this.#held.has('ShiftLeft') || this.#held.has('ShiftRight') || event.shiftKey === true);
    this.#send(direction,fast);
    if (!this.#held.size) this.#scope = null;
    return !shift(code);
  }
  #send(direction, fast) {
    if (direction === this.#direction && fast === this.#fast) return;
    if (!this.#scope || this.#sequence >= Number.MAX_SAFE_INTEGER) return;
    this.#direction = direction; this.#fast = fast;
    this.#transport = { id:this.#id(), revision:this.#revision, direction, fast, acknowledged:false };
    this.#post({ kind:'game-bank-walk', documentId:this.#documentId, lease:this.#scope.lease, transportId:this.#transport.id,
      inputSequence:String(++this.#sequence), geometryVersion:this.#scope.geometryVersion, direction, fast });
  }
  stop() {
    this.#send(0,false); this.#scope = null; this.#held.clear();
  }
  reply(m) {
    if (!this.#transport || m?.documentId !== this.#documentId || m.kind !== 'game-reply' || m.transportId !== this.#transport.id) return false;
    if (['walk-updated','walk-stopped'].includes(m.code)) { this.#transport.acknowledged = true; this.#settle(); }
    else { this.#transport = null; this.stop(); }
    return true;
  }
  #settle() {
    const p = this.#transport;
    if (p?.acknowledged && this.#revision > p.revision && this.#state?.direction === p.direction && this.#state.fast === p.fast) this.#transport = null;
  }
}

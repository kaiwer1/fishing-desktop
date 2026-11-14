const decimal = value => typeof value === 'string' && /^(0|[1-9]\d{0,18})$/.test(value) &&
  (value.length < 19 || value <= '9223372036854775807');

export class BottomShop {
  #documentId; #read; #post; #newId;
  #state = null; #revision = 0; #offer = null; #pending = null;
  constructor({ documentId, readContext, post, newTransportId }) {
    this.#documentId = documentId; this.#read = readContext; this.#post = post; this.#newId = newTransportId;
  }
  get busy() { return Boolean(this.#offer || this.#pending); }
  get confirming() { return Boolean(this.#offer); }
  get pending() { return this.#pending?.action ?? null; }
  state() { return this.#state && { ...this.#state }; }
  apply(m) {
    const s = m?.state;
    if (m?.kind !== 'game-bottom-shop-state' || m.documentId !== this.#documentId ||
        !Number.isSafeInteger(m.revision) || m.revision <= this.#revision || !s ||
        [s.available,s.owned,s.allowed].some(v => typeof v !== 'boolean') || s.priceMinor !== 6400 ||
        !['float','bottom'].includes(s.method) || (s.method === 'bottom' && !s.owned)) return false;
    this.#revision = m.revision;
    this.#state = { available:s.available, owned:s.owned, method:s.method, allowed:s.allowed, priceMinor:s.priceMinor };
    this.reconcile(); return true;
  }
  reconcile() {
    const c = this.#read(), p = this.#pending, offer = this.#offer;
    if (offer && (!this.#eligible('buy',c) || offer.lease !== c.lease || offer.sequence !== c.sequence)) this.#offer = null;
    if (!p) return;
    if (!c.allowed || c.lease !== p.lease) this.#pending = null;
    else if (p.ack && this.#revision > p.revision &&
        (p.action === 'buy' ? this.#state.owned && c.sequence !== p.sequence :
          this.#state.method === (p.action === 'select-bottom' ? 'bottom' : 'float'))) this.#pending = null;
  }
  #eligible(action,c) {
    const s = this.#state;
    if (!s?.available || !c.allowed || !c.lease || !decimal(c.sequence)) return false;
    if (action === 'buy') return !s.owned && decimal(c.walletMinor) && BigInt(c.walletMinor) >= BigInt(s.priceMinor);
    if (action === 'select-bottom') return s.owned && s.allowed && s.method === 'float';
    return action === 'select-float' && s.owned && s.method === 'bottom';
  }
  can(action) { return !this.busy && this.#eligible(action,this.#read()); }
  offer() {
    if (!this.can('buy')) return false;
    const c = this.#read(); this.#offer = { lease:c.lease, sequence:c.sequence }; return true;
  }
  cancel() { this.#offer = null; }
  submit(action) {
    this.reconcile();
    const c = this.#read();
    if (this.#pending || !this.#eligible(action,c) || (action === 'buy') !== this.confirming) return false;
    const transportId = this.#newId(); this.#offer = null;
    this.#pending = { action, transportId, lease:c.lease, sequence:c.sequence, revision:this.#revision, ack:false };
    try { this.#post({ kind:'game-bottom-shop', documentId:this.#documentId, lease:c.lease, transportId, action, expectedSequence:c.sequence }); }
    catch { this.#pending = null; return false; }
    return true;
  }
  reply(m) {
    if (m?.kind !== 'game-reply' || m.documentId !== this.#documentId || !this.#pending || m.transportId !== this.#pending.transportId) return false;
    const success = this.#pending.action === 'buy' ? 'bottom-kit-purchased' : 'bottom-method-selected';
    if (m.code === success) this.#pending.ack = true;
    else this.#pending = null;
    this.reconcile(); return true;
  }
}

export class BottomStrike {
  #documentId; #read; #post; #newId;
  #held = false; #next = 0; #pending = null;
  constructor({ documentId, readContext, post, newTransportId }) {
    this.#documentId = documentId; this.#read = readContext; this.#post = post; this.#newId = newTransportId;
  }
  get available() {
    const c=this.#read();
    return !this.#pending && Boolean(c.allowed && c.lease && c.bottom?.phase === 'bite' && c.bottom.sessionId === c.sessionId);
  }
  reconcile() {
    if (!this.#pending) return;
    const c=this.#read(), p=this.#pending;
    if (!c.allowed || c.sessionId !== p.sessionId || c.bankHand !== p.bankHand || c.lease !== p.lease || c.bottom?.phase !== 'bite') this.#pending=null;
  }
  strike() {
    this.reconcile();
    if (!this.available || this.#next === Number.MAX_SAFE_INTEGER) return false;
    const c=this.#read(), transportId=this.#newId();
    this.#pending={ sessionId:c.sessionId, lease:c.lease, transportId, ...(c.bankHand ? {bankHand:c.bankHand} : {}) };
    try { this.#post({ kind:'game-bottom-strike', documentId:this.#documentId, ...this.#pending, inputSequence:String(++this.#next) }); }
    catch { this.#pending=null; return false; }
    return true;
  }
  buttons(buttons) { if (Number.isInteger(buttons) && buttons >= 0 && buttons <= 31) this.#held=Boolean(buttons & 2); }
  press(buttons) {
    if (!Number.isInteger(buttons) || buttons<0 || buttons>31 || !(buttons&2)) return false;
    const held=this.#held; this.#held=true;
    return !held && this.strike();
  }
  reply(m) {
    if (m?.kind !== 'game-reply' || m.documentId !== this.#documentId || !this.#pending || m.transportId !== this.#pending.transportId) return false;
    // A successful strike waits for the phase to change before permitting a
    // second action, even if the player clicks again before the projection.
    if (m.code !== 'bottom-hooked') this.#pending=null;
    return true;
  }
}

export function confirmedLineEnd(p) {
  if (!p || ![p.xMm,p.yMm,p.zMm].every(n => Number.isInteger(n) && Math.abs(n)<=1000000)) return null;
  return { xMm:p.xMm, yMm:p.yMm, zMm:p.zMm };
}

const products = new Set(['net', 'bait', 'food']);
const guid = value => typeof value === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(value) && value !== '00000000-0000-0000-0000-000000000000';
const integer = (value, min, max) => Number.isInteger(value) && value >= min && value <= max;
const number = (value, positive = false) => typeof value === 'string' && /^(0|[1-9]\d{0,18})$/.test(value) &&
  (value.length < 19 || value <= '9223372036854775807') && (!positive || value !== '0');
const copyQuote = q => q ? { id: q.id, product: q.product, packs: q.packs, units: q.units, totalPriceMinor: q.totalPriceMinor } : null;
const copyRetry = r => r ? { operationId: r.operationId, action: r.action } : null;
const copy = s => ({ schemaVersion: s.schemaVersion, available: s.available, reason: s.reason, walletMinor: s.walletMinor,
  baitCount: s.baitCount, foodCount: s.foodCount, netOwned: s.netOwned, netEquipped: s.netEquipped, catalogVersion: s.catalogVersion,
  netPriceMinor: s.netPriceMinor, baitPackPriceMinor: s.baitPackPriceMinor, foodPriceMinor: s.foodPriceMinor,
  baitPackUnits: s.baitPackUnits, foodStaminaRestore: s.foodStaminaRestore, maxPacks: s.maxPacks, quote: copyQuote(s.quote), retry: copyRetry(s.retry) });

// Strings remain exact all the way to display; no JavaScript Number conversion
// participates in prices, catalogue versions or balances.
export function formatMinor(value) {
  if (!number(value)) return '—';
  const digits = value.padStart(3, '0'); return `${digits.slice(0, -2)}.${digits.slice(-2)}`;
}
export class ShopView {
  #documentId;
  #revision = 0;
  #state = null;
  constructor(documentId) { this.#documentId = documentId; }
  get revision() { return this.#revision; }
  state() { return this.#state ? copy(this.#state) : null; }
  clearReferences() { if (this.#state) { this.#state.quote = null; this.#state.retry = null; } }
  apply(message) {
    if (!message || message.kind !== 'game-shop-state' || message.documentId !== this.#documentId ||
        !Number.isSafeInteger(message.revision) || message.revision <= this.#revision) return false;
    const s = message.state;
    if (!s || !integer(s.schemaVersion, 0, 2) || typeof s.available !== 'boolean' ||
        !(s.reason === null || typeof s.reason === 'string' && s.reason.length > 0 && s.reason.length <= 128) ||
        (s.available ? s.reason !== null : s.reason === null) ||
        !integer(s.baitCount, 0, 2147483647) || !integer(s.foodCount, 0, 2147483647) ||
        typeof s.netOwned !== 'boolean' || typeof s.netEquipped !== 'boolean' || s.netEquipped && !s.netOwned) return false;
    if (s.schemaVersion === 0 ? s.walletMinor !== null : !number(s.walletMinor)) return false;
    if (s.schemaVersion === 2) {
      if (!['catalogVersion', 'netPriceMinor', 'baitPackPriceMinor', 'foodPriceMinor'].every(k => number(s[k], true)) ||
          !integer(s.baitPackUnits, 1, 2147483647) || !integer(s.foodStaminaRestore, 1, 2147483647) || !integer(s.maxPacks, 1, 100)) return false;
    } else if (s.available || ['catalogVersion', 'netPriceMinor', 'baitPackPriceMinor', 'foodPriceMinor'].some(k => s[k] !== null) ||
        s.baitPackUnits !== 0 || s.foodStaminaRestore !== 0 || s.maxPacks !== 0) return false;
    if (s.quote !== null) {
      const q = s.quote;
      if (!s.available || !q || !guid(q.id) || !products.has(q.product) || !integer(q.packs, 1, s.maxPacks) ||
          !integer(q.units, 1, 2147483647) || !number(q.totalPriceMinor, true) || q.product === 'net' && (q.packs !== 1 || q.units !== 1)) return false;
    }
    if (s.retry !== null && (!s.available || !s.retry || !guid(s.retry.operationId) || !['buy', 'equip', 'eat'].includes(s.retry.action))) return false;
    if (s.quote !== null && s.retry !== null) return false;
    this.#state = copy(s); this.#revision = message.revision; return true;
  }
}

// One in-flight intent, with quote/retry handles kept only under the exact
// current grant. All price, resource and durable request decisions are native.
export class ShopInput {
  #context;
  #post;
  #id;
  #lease = null;
  #quote = null;
  #retry = null;
  #inflight = null;
  #awaitingState = false;
  #next = 0;
  #faulted = false;
  #hostAvailable = false;
  constructor({ readContext, post, newTransportId }) { this.#context = readContext; this.#post = post; this.#id = newTransportId; }
  state() { return { lease: this.#lease, quote: copyQuote(this.#quote), retry: copyRetry(this.#retry),
    inflight: this.#inflight ? { ...this.#inflight } : null, awaitingState: this.#awaitingState, faulted: this.#faulted, ready: this.#hostAvailable }; }
  #forget() { this.#quote = null; this.#retry = null; this.#inflight = null; this.#awaitingState = false; this.#hostAvailable = false; }
  reconcile() {
    const c = this.#context();
    if (!c.allowed || !c.lease || this.#faulted || this.#lease !== c.lease) {
      this.#forget(); this.#lease = c.allowed && c.lease && !this.#faulted ? c.lease : null;
    }
  }
  hostState(s) {
    if (s.reason === 'save-outcome-unknown') this.#faulted = true;
    this.reconcile();
    this.#hostAvailable = Boolean(this.#lease && s.available && s.schemaVersion === 2 && !this.#faulted);
    if (!this.#hostAvailable) { this.#quote = null; this.#retry = null; return; }
    this.#quote = copyQuote(s.quote); this.#retry = copyRetry(s.retry); this.#awaitingState = false;
  }
  #ready() {
    this.reconcile(); const c = this.#context();
    return c.allowed && this.#lease === c.lease && this.#hostAvailable && !this.#faulted && !this.#inflight && !this.#awaitingState && c.schemaVersion === 2;
  }
  #send(action, fields = {}) {
    const m = { kind: 'game-shop', lease: this.#lease, transportId: this.#id(), action, ...fields };
    this.#inflight = { transportId: m.transportId, action, lease: m.lease }; this.#quote = null; this.#retry = null; this.#post(m); return true;
  }
  quote(product, packs) {
    if (!this.#ready() || this.#retry) return false;
    const c = this.#context();
    if (!products.has(product) || !integer(packs, 1, Math.min(100, c.maxPacks)) || product === 'net' && packs !== 1) return false;
    return this.#send('quote', { product, packs });
  }
  buy() { if (!this.#ready() || !this.#quote) return false; return this.#send('buy', { quoteId: this.#quote.id }); }
  equip(equipped) {
    if (!this.#ready() || this.#retry || typeof equipped !== 'boolean' || this.#next === Number.MAX_SAFE_INTEGER) return false;
    const c = this.#context();
    if (!c.netOwned || c.netEquipped === equipped) return false;
    return this.#send('equip', { equipped, inputSequence: String(++this.#next) });
  }
  eat() {
    if (!this.#ready() || this.#retry || this.#context().foodCount < 1 || this.#next === Number.MAX_SAFE_INTEGER) return false;
    return this.#send('eat', { inputSequence: String(++this.#next) });
  }
  retry() { if (!this.#ready() || !this.#retry) return false; return this.#send('retry', { operationId: this.#retry.operationId }); }
  cancel() { if (!this.#ready() || !this.#quote && !this.#retry) return false; return this.#send('cancel'); }
  reply(message) {
    if (!this.#inflight || this.#inflight.transportId !== message.transportId) return false;
    this.#inflight = null; this.#awaitingState = true;
    if (message.code === 'save-outcome-unknown') { this.#faulted = true; this.#forget(); this.#lease = null; }
    return true;
  }
}

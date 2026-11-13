export { formatMinor } from './shop-view.mjs';

const kinds = new Set(['hook', 'bobber', 'sinker', 'line']);
const guid = value => typeof value === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(value) && value !== '00000000-0000-0000-0000-000000000000';
const integer = (value, minimum, maximum) => Number.isInteger(value) && value >= minimum && value <= maximum;
const text = value => typeof value === 'string' && value.length > 0 && value.length <= 128;
const key = value => typeof value === 'string' && /^[a-z0-9_.-]{1,96}$/.test(value);
const counter = (value, positive = false) => typeof value === 'string' && /^(0|[1-9]\d{0,18})$/.test(value) &&
  (value.length < 19 || value <= '9223372036854775807') && (!positive || value !== '0');
const itemFields = ['sku', 'definitionKey', 'kind', 'priceMinor', 'lineLengthMm'];
const copyItem = item => Object.fromEntries(itemFields.map(field => [field, item[field]]));
const copyQuote = quote => quote ? { offerId: quote.offerId, ...copyItem(quote) } : null;
const copyRetry = retry => retry ? { operationId: retry.operationId } : null;
const copy = state => ({ schemaVersion: state.schemaVersion, available: state.available, reason: state.reason, lease: state.lease,
  stateSequence: state.stateSequence, catalogVersion: state.catalogVersion, items: state.items.map(copyItem),
  walletMinor: state.walletMinor, instancesCount: state.instancesCount, quote: copyQuote(state.quote), retry: copyRetry(state.retry) });

function validState(state) {
  if (!state || !integer(state.schemaVersion, 0, 3) || typeof state.available !== 'boolean' ||
      (state.available ? state.reason !== null || !text(state.lease) : !text(state.reason)) ||
      !(state.lease === null || text(state.lease)) || !Array.isArray(state.items) || state.items.length > 16 ||
      !integer(state.instancesCount, 0, 128)) return false;
  if (state.schemaVersion !== 3) return !state.available && state.stateSequence === null && state.catalogVersion === null &&
    state.walletMinor === null && state.instancesCount === 0 && state.items.length === 0 && state.quote === null && state.retry === null;
  if (!counter(state.stateSequence) || !counter(state.walletMinor)) return false;
  if (state.catalogVersion === null) return !state.available && ['spares-not-configured', 'owner-stopped'].includes(state.reason) && state.items.length === 0 &&
    state.quote === null && state.retry === null;
  if (!counter(state.catalogVersion, true) || state.items.length === 0) return false;
  const items = new Map();
  for (const item of state.items) {
    if (!item || !key(item.sku) || items.has(item.sku) || !key(item.definitionKey) || !kinds.has(item.kind) ||
        !counter(item.priceMinor, true) || !integer(item.lineLengthMm, item.kind === 'line' ? 1 : 0, item.kind === 'line' ? 100000000 : 0)) return false;
    items.set(item.sku, item);
  }
  if (state.quote !== null) {
    const quote = state.quote, item = quote && items.get(quote.sku);
    if (!state.available || !quote || !guid(quote.offerId) || !item || itemFields.some(field => quote[field] !== item[field])) return false;
  }
  if (state.retry !== null && (!state.available || !state.retry || !guid(state.retry.operationId))) return false;
  return state.quote === null || state.retry === null;
}

const validPacket = message => message && message.kind === 'game-spares-state' && text(message.documentId) &&
  Number.isSafeInteger(message.revision) && message.revision > 0 && validState(message.state);

// The page displays the configured native catalogue. A quote is only an opaque
// reference to one exact host-projected product, never a page-supplied price.
export class SparesView {
  #documentId;
  #revision = 0;
  #state = null;
  constructor(documentId) { this.#documentId = documentId; }
  get revision() { return this.#revision; }
  state() { return this.#state ? copy(this.#state) : null; }
  clearReferences() { if (this.#state) { this.#state.quote = null; this.#state.retry = null; this.#state.lease = null; } }
  apply(message) {
    if (!validPacket(message) || message.documentId !== this.#documentId || message.revision <= this.#revision) return false;
    this.#state = copy(message.state); this.#revision = message.revision; return true;
  }
}

const singleGesture = event => event == null || (event.detail === undefined || integer(event.detail, 0, 1)) && event.repeat !== true;

export class SparesInput {
  #context;
  #post;
  #id;
  #documentId = null;
  #lease = null;
  #revision = 0;
  #state = null;
  #inflight = null;
  #awaitingState = false;
  #faulted = false;
  constructor({ readContext, post, newTransportId }) { this.#context = readContext; this.#post = post; this.#id = newTransportId; }
  state() { return { documentId: this.#documentId, lease: this.#lease, revision: this.#revision, quote: copyQuote(this.#state?.quote),
    retry: copyRetry(this.#state?.retry), inflight: this.#inflight ? { ...this.#inflight } : null,
    awaitingState: this.#awaitingState, faulted: this.#faulted, ready: Boolean(this.#state?.available) }; }
  #eligible(context) { return Boolean(context && context.allowed === true && text(context.documentId) && text(context.lease) &&
    context.schemaVersion === 3 && context.menuOpen === true && context.sessionId === null && context.pendingResult === false &&
    context.faulted === false && !this.#faulted); }
  #forget() { this.#state = null; this.#inflight = null; this.#awaitingState = false; }
  reconcile() {
    const context = this.#context();
    if (context?.faulted === true) this.#faulted = true;
    if (this.#documentId !== context?.documentId) { this.#forget(); this.#documentId = context?.documentId ?? null; this.#revision = 0; this.#lease = null; }
    if (!this.#eligible(context) || this.#lease !== context.lease) { this.#forget(); this.#lease = this.#eligible(context) ? context.lease : null; }
  }
  hostState(message) {
    this.reconcile();
    if (!validPacket(message) || message.documentId !== this.#documentId || message.revision <= this.#revision) return false;
    this.#revision = message.revision;
    if (message.state.reason === 'save-outcome-unknown') { this.#faulted = true; this.#forget(); this.#lease = null; return true; }
    if (!this.#eligible(this.#context()) || !message.state.available || message.state.schemaVersion !== 3 || message.state.lease !== this.#lease) {
      this.#state = null; return true;
    }
    this.#state = copy(message.state);
    // A projection arriving before its reply cannot unlock another operation.
    // Native replies first, then sends a fresh authoritative projection.
    if (!this.#inflight) this.#awaitingState = false;
    return true;
  }
  #ready(event) {
    this.reconcile();
    return singleGesture(event) && this.#eligible(this.#context()) && this.#state?.available && this.#state.lease === this.#lease &&
      !this.#inflight && !this.#awaitingState;
  }
  #send(action, fields = {}) {
    const message = { kind: 'game-spares', documentId: this.#documentId, lease: this.#lease, transportId: this.#id(), action, ...fields };
    this.#inflight = { transportId: message.transportId, documentId: message.documentId, lease: message.lease, action };
    this.#state.quote = null; this.#state.retry = null; this.#post(message); return true;
  }
  quote(sku, event = null) {
    if (!this.#ready(event) || this.#state.retry || !key(sku) || !this.#state.items.some(item => item.sku === sku)) return false;
    return this.#send('quote', { sku });
  }
  buy(event = null) { if (!this.#ready(event) || !this.#state.quote) return false; return this.#send('buy', { offerId: this.#state.quote.offerId }); }
  retry(event = null) { if (!this.#ready(event) || !this.#state.retry) return false; return this.#send('retry', { operationId: this.#state.retry.operationId }); }
  cancel(event = null) { if (!this.#ready(event) || !this.#state.quote && !this.#state.retry) return false; return this.#send('cancel'); }
  reply(message) {
    this.reconcile();
    if (!this.#inflight || message?.kind !== 'game-reply' || message.documentId !== this.#documentId || message.transportId !== this.#inflight.transportId) return false;
    this.#inflight = null; this.#awaitingState = true;
    if (message.code === 'save-outcome-unknown') { this.#faulted = true; this.#forget(); this.#lease = null; }
    return true;
  }
}

const slots = new Set(['rod', 'reel', 'hook', 'bobber', 'sinker']);
const kinds = new Set([...slots, 'line']);
const locations = new Set(['inventory', 'rig-slot', 'reel-spool', 'lost']);
const actions = new Set(['replace-slot', 'move-line']);
const guid = value => typeof value === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(value) && value !== '00000000-0000-0000-0000-000000000000';
const integer = (value, minimum, maximum) => Number.isInteger(value) && value >= minimum && value <= maximum;
const text = value => typeof value === 'string' && value.length > 0 && value.length <= 128;
const key = value => typeof value === 'string' && /^[a-z0-9_.-]{1,96}$/.test(value);
const counter = (value, positive = false) => typeof value === 'string' && /^(0|[1-9]\d{0,18})$/.test(value) &&
  (value.length < 19 || value <= '9223372036854775807') && (!positive || value !== '0');
const itemFields = ['id', 'definitionKey', 'kind', 'revision', 'location', 'slot', 'reelId', 'initialLineMm', 'remainingLineMm'];
const copyItem = item => Object.fromEntries(itemFields.map(field => [field, item[field]]));
const copyIssue = issue => ({ code: issue.code, slot: issue.slot, itemId: issue.itemId, relatedId: issue.relatedId });
const copyRetry = retry => retry ? { operationId: retry.operationId, action: retry.action } : null;
const copy = s => ({ schemaVersion: s.schemaVersion, available: s.available, reason: s.reason, lease: s.lease,
  stateSequence: s.stateSequence, rigRevision: s.rigRevision, inventoryRevision: s.inventoryRevision, equipmentVersion: s.equipmentVersion,
  items: s.items.map(copyItem), issues: s.issues.map(copyIssue), retry: copyRetry(s.retry) });

function validState(s) {
  if (!s || !integer(s.schemaVersion, 0, 3) || typeof s.available !== 'boolean' ||
      (s.available ? s.reason !== null || !text(s.lease) : !text(s.reason)) || !(s.lease === null || text(s.lease)) ||
      !Array.isArray(s.items) || s.items.length > 128 || !Array.isArray(s.issues) || s.issues.length > 256) return false;
  if (s.schemaVersion === 3) {
    if (!counter(s.stateSequence) || !['rigRevision', 'inventoryRevision', 'equipmentVersion'].every(field => counter(s[field], true))) return false;
  } else if (s.available || s.stateSequence !== null || ['rigRevision', 'inventoryRevision', 'equipmentVersion'].some(field => s[field] !== null) ||
      s.items.length || s.issues.length || s.retry !== null) return false;
  const ids = new Map(), occupiedSlots = new Set(), occupiedSpools = new Set();
  for (const item of s.items) {
    if (!item || !guid(item.id) || ids.has(item.id) || !key(item.definitionKey) || !kinds.has(item.kind) || !counter(item.revision, true) ||
        !locations.has(item.location) || !integer(item.initialLineMm, 0, 100000000) || !integer(item.remainingLineMm, 0, item.initialLineMm)) return false;
    if (item.kind === 'line' ? item.initialLineMm === 0 || item.location === 'rig-slot' || (item.location === 'lost') !== (item.remainingLineMm === 0) :
      item.initialLineMm !== 0 || item.remainingLineMm !== 0 || item.location === 'reel-spool') return false;
    if (item.location === 'rig-slot') {
      if (!slots.has(item.slot) || item.kind !== item.slot || occupiedSlots.has(item.slot) || item.reelId !== null) return false;
      occupiedSlots.add(item.slot);
    } else if (item.slot !== null) return false;
    if (item.location === 'reel-spool') {
      if (!guid(item.reelId) || occupiedSpools.has(item.reelId)) return false;
      occupiedSpools.add(item.reelId);
    } else if (item.reelId !== null) return false;
    ids.set(item.id, item);
  }
  for (const item of s.items) if (item.location === 'reel-spool') {
    const reel = ids.get(item.reelId);
    if (!reel || reel.kind !== 'reel' || reel.location === 'lost') return false;
  }
  for (const issue of s.issues) if (!issue || !key(issue.code) || !(issue.slot === null || slots.has(issue.slot)) ||
      !(issue.itemId === null || guid(issue.itemId)) || !(issue.relatedId === null || guid(issue.relatedId))) return false;
  if (s.retry !== null && (!s.available || !s.retry || !guid(s.retry.operationId) || !actions.has(s.retry.action))) return false;
  return true;
}

const validPacket = message => message && message.kind === 'game-equipment-state' && text(message.documentId) &&
  Number.isSafeInteger(message.revision) && message.revision > 0 && validState(message.state);

// Only host-confirmed inventory and line lengths are projected. Definitions,
// compatibility and persistent identifiers are never invented in this module.
export class EquipmentView {
  #documentId;
  #revision = 0;
  #state = null;
  constructor(documentId) { this.#documentId = documentId; }
  get revision() { return this.#revision; }
  state() { return this.#state ? copy(this.#state) : null; }
  clearReferences() { if (this.#state) { this.#state.retry = null; this.#state.lease = null; } }
  apply(message) {
    if (!validPacket(message) || message.documentId !== this.#documentId || message.revision <= this.#revision) return false;
    this.#state = copy(message.state); this.#revision = message.revision; return true;
  }
}

const singleGesture = event => event == null || (event.detail === undefined || integer(event.detail, 0, 1)) && event.repeat !== true;

export class EquipmentInput {
  #context;
  #post;
  #id;
  #documentId = null;
  #lease = null;
  #revision = 0;
  #state = null;
  #inflight = null;
  #awaitingState = false;
  #next = 0;
  #faulted = false;
  constructor({ readContext, post, newTransportId }) { this.#context = readContext; this.#post = post; this.#id = newTransportId; }
  state() { return { documentId: this.#documentId, lease: this.#lease, revision: this.#revision, retry: copyRetry(this.#state?.retry),
    inflight: this.#inflight ? { ...this.#inflight } : null, awaitingState: this.#awaitingState, faulted: this.#faulted, ready: Boolean(this.#state?.available) }; }
  #eligible(c) { return Boolean(c && c.allowed === true && text(c.documentId) && text(c.lease) && c.schemaVersion === 3 &&
    c.menuOpen === true && c.sessionId === null && c.pendingResult === false && c.faulted === false && !this.#faulted); }
  #forget() { this.#state = null; this.#inflight = null; this.#awaitingState = false; }
  reconcile() {
    const c = this.#context();
    if (c?.faulted === true) this.#faulted = true;
    if (this.#documentId !== c?.documentId) { this.#forget(); this.#documentId = c?.documentId ?? null; this.#revision = 0; this.#lease = null; }
    if (!this.#eligible(c) || this.#lease !== c.lease) { this.#forget(); this.#lease = this.#eligible(c) ? c.lease : null; }
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
    // A state received before the reply cannot release a pending action. The
    // reply requires another fresh projection, matching the host send order.
    if (!this.#inflight) this.#awaitingState = false;
    return true;
  }
  #ready(event) {
    this.reconcile();
    return singleGesture(event) && this.#eligible(this.#context()) && this.#state?.available && this.#state.lease === this.#lease && !this.#inflight && !this.#awaitingState;
  }
  #send(action, fields = {}) {
    const message = { kind: 'game-equipment', documentId: this.#documentId, lease: this.#lease, transportId: this.#id(), action, ...fields };
    this.#inflight = { transportId: message.transportId, documentId: message.documentId, lease: message.lease, action };
    this.#state.retry = null; this.#post(message); return true;
  }
  replaceSlot(slot, itemId, event = null) {
    if (!this.#ready(event) || this.#state.retry || !slots.has(slot) || !(itemId === null || guid(itemId)) || this.#next === Number.MAX_SAFE_INTEGER) return false;
    const old = this.#state.items.find(item => item.location === 'rig-slot' && item.slot === slot);
    if ((old?.id ?? null) === itemId) return false;
    if (itemId !== null) {
      const item = this.#state.items.find(item => item.id === itemId);
      if (!item || item.kind !== slot || item.location !== 'inventory') return false;
    }
    return this.#send('replace-slot', { slot, itemId, expectedSequence: this.#state.stateSequence, inputSequence: String(++this.#next) });
  }
  moveLine(lineId, reelId, event = null) {
    if (!this.#ready(event) || this.#state.retry || !guid(lineId) || !(reelId === null || guid(reelId)) || this.#next === Number.MAX_SAFE_INTEGER) return false;
    const line = this.#state.items.find(item => item.id === lineId);
    if (!line || line.kind !== 'line' || line.location === 'lost' || line.reelId === reelId) return false;
    if (reelId !== null) {
      const reel = this.#state.items.find(item => item.id === reelId);
      if (!reel || reel.kind !== 'reel' || reel.location === 'lost' || this.#state.items.some(item => item.location === 'reel-spool' && item.reelId === reelId)) return false;
    }
    return this.#send('move-line', { lineId, reelId, expectedSequence: this.#state.stateSequence, inputSequence: String(++this.#next) });
  }
  retry(event = null) { if (!this.#ready(event) || !this.#state.retry) return false; return this.#send('retry', { operationId: this.#state.retry.operationId }); }
  cancel(event = null) { if (!this.#ready(event) || !this.#state.retry) return false; return this.#send('cancel'); }
  reply(message) {
    this.reconcile();
    if (!this.#inflight || message?.kind !== 'game-reply' || message.documentId !== this.#documentId || message.transportId !== this.#inflight.transportId) return false;
    this.#inflight = null; this.#awaitingState = true;
    if (message.code === 'save-outcome-unknown') { this.#faulted = true; this.#forget(); this.#lease = null; }
    return true;
  }
}

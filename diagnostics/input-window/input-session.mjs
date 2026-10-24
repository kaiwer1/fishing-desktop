// An isolated input contract; no simulation, save or economic state.
export class InputSession {
  enabled = false;
  revision = 0;
  cancellationEpoch = 0;
  held = new Map();
  trackedPointers = new Set();
  buttonTotals = [0,1,2].map(() => ({down:0,up:0}));
  presses = 0;
  releases = 0;
  keys = 0;
  wheels = 0;
  cancellations = 0;
  lastReason = 'startup';

  setMode(enabled, revision, reason = 'host', expectedEpoch = this.cancellationEpoch) {
    if (typeof enabled !== 'boolean' || typeof reason !== 'string' ||
        !Number.isSafeInteger(revision) || revision <= this.revision) return false;
    this.revision = revision;
    if (!enabled) {
      this.cancel(reason);
      return true;
    }
    // A host command queued before a local blur/Esc must not undo that cancellation.
    // Consume its revision so replaying the command cannot activate a later session.
    if (expectedEpoch !== this.cancellationEpoch) {
      this.clearInput('stale-enable');
      return false;
    }
    this.clearInput(reason);
    this.enabled = true;
    return true;
  }
  clearInput(reason) {
    this.held.clear();
    this.trackedPointers.clear();
    this.enabled = false;
    this.lastReason = reason;
  }
  cancel(reason) {
    this.clearInput(reason);
    this.cancellations++;
    this.cancellationEpoch++;
  }
  down(pointerId, button) {
    if (!this.enabled || ![0,1,2].includes(button)) return false;
    const key = `${pointerId}:${button}`;
    if (this.held.has(key)) return false;
    this.held.set(key, button);
    this.presses++;
    this.buttonTotals[button].down++;
    return true;
  }
  up(pointerId, button) {
    if (!this.enabled || !this.held.delete(`${pointerId}:${button}`)) return false;
    this.releases++;
    this.buttonTotals[button].up++;
    return true;
  }
  hasPointer(pointerId) {
    return [...this.held.keys()].some(key => key.startsWith(`${pointerId}:`));
  }
  pointerButtons(pointerId, buttons, begin = false) {
    if (!this.enabled || !Number.isSafeInteger(pointerId) || !Number.isSafeInteger(buttons) || buttons < 0 || buttons > 31) return [];
    const mask = buttons & 7;
    // A move/up after cancellation cannot begin a new gesture, even if a button is still held.
    if (!this.trackedPointers.has(pointerId)) {
      if (!begin || mask === 0) return [];
      this.trackedPointers.add(pointerId);
    }
    const changes = [];
    // PointerEvent.buttons: left=1, right=2, middle=4 (not the button index order).
    for (const [button,bit] of [[0,1],[2,2],[1,4]]) {
      const held = this.held.has(`${pointerId}:${button}`);
      if ((mask & bit) && !held && this.down(pointerId,button)) changes.push({kind:'down',button});
      if (!(mask & bit) && held && this.up(pointerId,button)) changes.push({kind:'up',button});
    }
    if (mask === 0) this.trackedPointers.delete(pointerId);
    return changes;
  }
  key() { if (this.enabled) this.keys++; }
  wheel() { if (this.enabled) this.wheels++; }
  snapshot() {
    return { enabled:this.enabled, revision:this.revision, cancellationEpoch:this.cancellationEpoch, held:this.held.size,
      presses:this.presses, releases:this.releases, keys:this.keys, wheels:this.wheels,
      cancellations:this.cancellations, lastReason:this.lastReason,
      buttonTotals:this.buttonTotals.map(value => ({...value})) };
  }
}

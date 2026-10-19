// An isolated input contract; no simulation, save or economic state.
export class InputSession {
  enabled = false;
  revision = 0;
  held = new Map();
  presses = 0;
  releases = 0;
  keys = 0;
  wheels = 0;
  cancellations = 0;
  lastReason = 'startup';

  setMode(enabled, revision, reason = 'host') {
    if (!Number.isSafeInteger(revision) || revision <= this.revision) return false;
    this.revision = revision;
    this.cancel(reason);
    this.enabled = Boolean(enabled);
    return true;
  }
  cancel(reason) {
    this.held.clear();
    this.enabled = false;
    this.cancellations++;
    this.lastReason = reason;
  }
  down(pointerId, button) {
    if (!this.enabled || ![0,1,2].includes(button)) return false;
    const key = `${pointerId}:${button}`;
    if (this.held.has(key)) return false;
    this.held.set(key, button);
    this.presses++;
    return true;
  }
  up(pointerId, button) {
    if (!this.enabled || !this.held.delete(`${pointerId}:${button}`)) return false;
    this.releases++;
    return true;
  }
  key() { if (this.enabled) this.keys++; }
  wheel() { if (this.enabled) this.wheels++; }
  snapshot() {
    return { enabled:this.enabled, revision:this.revision, held:this.held.size,
      presses:this.presses, releases:this.releases, keys:this.keys, wheels:this.wheels,
      cancellations:this.cancellations, lastReason:this.lastReason };
  }
}

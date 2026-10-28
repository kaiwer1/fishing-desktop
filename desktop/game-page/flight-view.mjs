const phases = new Set(['idle', 'flying', 'waiting', 'invalid', 'ended']);
const guid = /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/;
const integer = (n, min, max) => Number.isInteger(n) && n >= min && n <= max;
const point = p => p !== null && typeof p === 'object' && !Array.isArray(p) &&
  ['xMm', 'yMm', 'zMm'].every(k => integer(p[k], -1000000, 1000000));
const copyPoint = p => ({ xMm: p.xMm, yMm: p.yMm, zMm: p.zMm });
const copyState = s => ({
  sessionId: s.sessionId, phase: s.phase, reason: s.reason,
  elapsedMilliseconds: s.elapsedMilliseconds, xMm: s.xMm, yMm: s.yMm, zMm: s.zMm,
  lineUsedMm: s.lineUsedMm, origin: copyPoint(s.origin), waterYmm: s.waterYmm,
  trail: s.trail.map(copyPoint),
});

// Read-only projection of bounded host snapshots. It has no timers, trajectory
// calculation, random fish, time extrapolation, storage or action transport.
export class FlightView {
  #documentId;
  #revision = 0;
  #state = null;
  constructor(documentId) { this.#documentId = documentId; }
  get revision() { return this.#revision; }
  state() { return this.#state ? copyState(this.#state) : null; }
  apply(message) {
    if (!message || message.kind !== 'game-flight-state' || message.documentId !== this.#documentId ||
        !Number.isSafeInteger(message.revision) || message.revision <= this.#revision) return false;
    const s = message.state;
    if (!s || !phases.has(s.phase) || !point(s) || !point(s.origin) ||
        !integer(s.waterYmm, -1000000, 1000000) || !integer(s.elapsedMilliseconds, 0, 86400000) ||
        !integer(s.lineUsedMm, 0, 1000000) ||
        !(s.reason === null || (typeof s.reason === 'string' && s.reason.length <= 128)) ||
        !Array.isArray(s.trail) || s.trail.length > 32 || !s.trail.every(point)) return false;
    if (s.phase === 'idle' ? s.sessionId !== null :
      typeof s.sessionId !== 'string' || !guid.test(s.sessionId) || s.sessionId === '00000000-0000-0000-0000-000000000000') return false;
    this.#state = copyState(s); this.#revision = message.revision;
    return true;
  }
  diagram() {
    const s = this.#state;
    if (!s || s.phase === 'idle') return null;
    // Fixed side diagram: horizontal distance from the host origin and height
    // above its flat water plane. Projection never advances the host position.
    let clipped = false;
    const project = p => {
      const range = Math.hypot(p.xMm - s.origin.xMm, p.zMm - s.origin.zMm);
      const height = p.yMm - s.waterYmm;
      if (range > 50000 || height > 15000 || height < -2500) clipped = true;
      return { x: 48 + Math.min(50000, range) * 648 / 50000,
        y: 190 - Math.max(-2500, Math.min(15000, height)) * 170 / 15000 };
    };
    const origin = project(s.origin), current = project(s);
    const recent = s.trail.map(project);
    const last = s.trail.at(-1);
    if (!last || last.xMm !== s.xMm || last.yMm !== s.yMm || last.zMm !== s.zMm) recent.push(current);
    return { origin, current, recent, clipped, phase: s.phase,
      distanceMm: Math.hypot(s.xMm - s.origin.xMm, s.zMm - s.origin.zMm),
      heightMm: s.yMm - s.waterYmm, lineUsedMm: s.lineUsedMm,
      elapsedMilliseconds: s.elapsedMilliseconds, reason: s.reason };
  }
}

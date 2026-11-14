const integer = (value, min, max) => Number.isSafeInteger(value) && value >= min && value <= max;
const coordinate = value => integer(value, -1000000000, 1000000000);
const identifier = value => typeof value === 'string' && /^[a-z0-9][a-z0-9._-]{0,95}$/.test(value);
const reason = value => typeof value === 'string' && value.length > 0 && value.length <= 128;
const isSession = value => typeof value === 'string' && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value);
const validBounds = b => b && ['minXmm', 'maxXmm', 'minZmm', 'maxZmm'].every(key => coordinate(b[key])) &&
  b.minXmm < b.maxXmm && b.minZmm < b.maxZmm;
const copyBounds = b => ({ minXmm: b.minXmm, maxXmm: b.maxXmm, minZmm: b.minZmm, maxZmm: b.maxZmm });
const point = value => value && coordinate(value.xMm) && coordinate(value.zMm) && integer(value.depthMm, 0, 100000);
const clonePoint = (value, extra = []) => value === null ? null : Object.fromEntries(['xMm', 'zMm', 'depthMm', ...extra].map(key => [key, value[key]]));
const copy = state => !state.enabled ? { enabled: false } : {
  enabled: true, geometryVersion: state.geometryVersion, waterId: state.waterId, spotId: state.spotId,
  bounds: copyBounds(state.bounds),
  ...(state.waterBounds ? { waterBounds: copyBounds(state.waterBounds), anchor: { xMm: state.anchor.xMm, yMm: state.anchor.yMm, zMm: state.anchor.zMm } } : {}),
  surfaceYmm: state.surfaceYmm, floatDepthMm: state.floatDepthMm, clearanceMm: state.clearanceMm,
  bed: state.bed.map(row => ({ zMm: row.zMm, depthMm: row.depthMm })),
  target: clonePoint(state.target, ['u', 'v', 'requiredLineMm']),
  prediction: clonePoint(state.prediction, ['powerPermille']), landing: clonePoint(state.landing, state.landing?.sessionId ? ['accepted', 'reason', 'sessionId'] : ['accepted', 'reason'])
};
function validState(s) {
  if (!s || typeof s.enabled !== 'boolean') return false;
  if (!s.enabled) return Object.keys(s).length === 1;
  const b = s.bounds;
  if (!integer(s.geometryVersion, 1, Number.MAX_SAFE_INTEGER) || !identifier(s.waterId) || !identifier(s.spotId) ||
      !validBounds(b) ||
      !coordinate(s.surfaceYmm) || !integer(s.floatDepthMm, 1, 100000) || !integer(s.clearanceMm, 0, 100000) ||
      !Array.isArray(s.bed) || s.bed.length < 2 || s.bed.length > 128) return false;
  if (!s.bed.every((row, index) => row && coordinate(row.zMm) && integer(row.depthMm, 1, 100000) &&
      (index === 0 || row.zMm > s.bed[index - 1].zMm)) || s.bed[0].zMm > b.minZmm || s.bed.at(-1).zMm < b.maxZmm) return false;
  if ('waterBounds' in s || 'anchor' in s) {
    const w = s.waterBounds, a = s.anchor;
    if (!validBounds(w) || w.minXmm > b.minXmm || w.maxXmm < b.maxXmm || w.minZmm > b.minZmm || w.maxZmm < b.maxZmm ||
        s.bed[0].zMm !== w.minZmm || s.bed.at(-1).zMm !== w.maxZmm || !a || ![a.xMm, a.yMm, a.zMm].every(coordinate) ||
        a.yMm <= s.surfaceYmm || a.zMm >= w.minZmm || a.xMm < w.minXmm || a.xMm > w.maxXmm) return false;
  }
  if (s.target !== null && (!point(s.target) || !integer(s.target.u, 0, 10000) || !integer(s.target.v, 0, 10000) ||
      !integer(s.target.requiredLineMm, 1, 100000000) || s.target.xMm < b.minXmm || s.target.xMm > b.maxXmm ||
      s.target.zMm < b.minZmm || s.target.zMm > b.maxZmm || s.target.depthMm < s.floatDepthMm + s.clearanceMm)) return false;
  if (s.prediction !== null && (!point(s.prediction) || !integer(s.prediction.powerPermille, 0, 1000))) return false;
  if (s.landing !== null && (!point(s.landing) || typeof s.landing.accepted !== 'boolean' || !reason(s.landing.reason) ||
      ('sessionId' in s.landing && !isSession(s.landing.sessionId)) || s.landing.accepted && s.landing.depthMm < s.floatDepthMm + s.clearanceMm)) return false;
  return true;
}

// Host measurements only: no sampled depth, ballistics or validity decisions
// are calculated here. Off-chart points remain off-chart, never clamped hits.
export class WaterView {
  #documentId; #revision = 0; #state = null;
  constructor(documentId) { this.#documentId = documentId; }
  get revision() { return this.#revision; }
  state() { return this.#state ? copy(this.#state) : null; }
  apply(message) {
    if (message?.kind !== 'game-water-state' || message.documentId !== this.#documentId ||
        ('sceneEnabled' in message && typeof message.sceneEnabled !== 'boolean') ||
        !Number.isSafeInteger(message.revision) || message.revision <= this.#revision || !validState(message.state)) return false;
    this.#state = copy(message.state); this.#revision = message.revision; return true;
  }
  geometryKey() {
    const s = this.#state;
    return s?.enabled ? JSON.stringify([s.geometryVersion, s.waterId, s.spotId, s.bounds, s.waterBounds, s.anchor, s.surfaceYmm, s.floatDepthMm, s.clearanceMm, s.bed]) : null;
  }
  diagram() {
    const s = this.#state; if (!s?.enabled) return null;
    const b = s.bounds;
    const project = p => {
      if (p === null) return null;
      const x = (p.xMm - b.minXmm) * 480 / (b.maxXmm - b.minXmm);
      const y = (b.maxZmm - p.zMm) * 280 / (b.maxZmm - b.minZmm);
      const outside = [];
      if (p.xMm < b.minXmm) outside.push('left'); else if (p.xMm > b.maxXmm) outside.push('right');
      if (p.zMm < b.minZmm) outside.push('near'); else if (p.zMm > b.maxZmm) outside.push('far');
      return { x, y, outside, visible: outside.length === 0 };
    };
    return { target: project(s.target), prediction: project(s.prediction), landing: project(s.landing) };
  }
}

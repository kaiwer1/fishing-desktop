// Input coordinates only. The host owns world geometry, target validity,
// flight planning and resource changes. U points right; V points down.
const scale = 10000;
const finitePoint = point => point && Number.isFinite(point.x) && Number.isFinite(point.y);
const validVersion = version => Number.isSafeInteger(version) && version > 0;

// Reject before quantization: a point just outside an edge must not round into
// the legal range. A returned value is a detached, immutable pointer-down aim.
export function normalizeAim(point, viewBox, geometryVersion) {
  if (!finitePoint(point) || !viewBox || !validVersion(geometryVersion) ||
      !['x', 'y', 'width', 'height'].every(key => Number.isFinite(viewBox[key])) ||
      viewBox.width <= 0 || viewBox.height <= 0) return null;
  const right = viewBox.x + viewBox.width, bottom = viewBox.y + viewBox.height;
  if (!Number.isFinite(right) || !Number.isFinite(bottom) || point.x < viewBox.x || point.x > right ||
      point.y < viewBox.y || point.y > bottom) return null;
  const u = (point.x - viewBox.x) / viewBox.width, v = (point.y - viewBox.y) / viewBox.height;
  if (!Number.isFinite(u) || !Number.isFinite(v) || u < 0 || u > 1 || v < 0 || v > 1) return null;
  return Object.freeze({ u: Math.round(u * scale), v: Math.round(v * scale), geometryVersion });
}

function validMatrix(matrix) {
  if (!matrix || matrix.is2D !== undefined && matrix.is2D !== true ||
      !['a', 'b', 'c', 'd', 'e', 'f'].every(key => Number.isFinite(matrix[key]))) return false;
  const determinant = matrix.a * matrix.d - matrix.b * matrix.c;
  return Number.isFinite(determinant) && determinant !== 0;
}

// SVG's screen CTM includes the viewBox / preserveAspectRatio transform,
// unlike scaling directly by the element's bounding rectangle. Its inverse
// sends letterbox pixels outside the viewBox, where normalizeAim rejects them.
export function screenToLocal(svg, event) {
  if (!svg || typeof svg.getScreenCTM !== 'function' ||
      !Number.isFinite(event?.clientX) || !Number.isFinite(event?.clientY)) return null;
  try {
    const matrix = svg.getScreenCTM();
    if (!validMatrix(matrix) || typeof matrix.inverse !== 'function') return null;
    const inverse = matrix.inverse();
    if (!validMatrix(inverse)) return null;
    const point = {
      x: inverse.a * event.clientX + inverse.c * event.clientY + inverse.e,
      y: inverse.b * event.clientX + inverse.d * event.clientY + inverse.f
    };
    return finitePoint(point) ? Object.freeze(point) : null;
  } catch {
    // Detached SVGs and unsupported/singular transforms cannot select a target.
    return null;
  }
}

// Call once for the accepted pointer-down event. Later moves or geometry
// projections never mutate the returned aim; a new gesture needs a new pick.
export function pickAim(svg, event, geometryVersion) {
  if (!validVersion(geometryVersion)) return null;
  try {
    const box = svg?.viewBox?.baseVal;
    if (!box) return null;
    const viewBox = { x: box.x, y: box.y, width: box.width, height: box.height };
    return normalizeAim(screenToLocal(svg, event), viewBox, geometryVersion);
  } catch {
    return null;
  }
}

import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeAim, screenToLocal, pickAim } from './water-aim.mjs';

const box = () => ({ x: -40, y: 10, width: 80, height: 40 });
function matrix(a = 1, b = 0, c = 0, d = 1, e = 0, f = 0) {
  return { a, b, c, d, e, f, is2D: true, inverse() {
    const determinant = a * d - b * c;
    return { a: d / determinant, b: -b / determinant, c: -c / determinant, d: a / determinant,
      e: (c * f - d * e) / determinant, f: (b * e - a * f) / determinant, is2D: true };
  } };
}
const svg = (transform = matrix(), bounds = box()) => ({ viewBox: { baseVal: bounds }, getScreenCTM: () => transform });
const client = (m, point) => ({ clientX: m.a * point.x + m.c * point.y + m.e, clientY: m.b * point.x + m.d * point.y + m.f });

test('normalization retains nonzero viewBox origins, inclusive edges and exact integer intent fields', () => {
  assert.deepEqual(normalizeAim({ x: -40, y: 10 }, box(), 7), { u: 0, v: 0, geometryVersion: 7 });
  assert.deepEqual(normalizeAim({ x: 40, y: 50 }, box(), 7), { u: 10000, v: 10000, geometryVersion: 7 });
  assert.deepEqual(normalizeAim({ x: 0, y: 30 }, box(), 7), { u: 5000, v: 5000, geometryVersion: 7 });
  assert.deepEqual(normalizeAim({ x: 1 / 3, y: 2 / 3 }, { x: 0, y: 0, width: 1, height: 1 }, 1), { u: 3333, v: 6667, geometryVersion: 1 });
  assert.equal(Object.isFrozen(normalizeAim({ x: 0, y: 30 }, box(), 7)), true);
});

test('outside points cannot round or clamp into a target and malformed coordinates, bounds and versions refuse', () => {
  for (const point of [{ x: -40.000001, y: 30 }, { x: 40.000001, y: 30 }, { x: 0, y: 9.999999 }, { x: 0, y: 50.000001 },
    { x: NaN, y: 30 }, { x: 0, y: Infinity }, { x: '0', y: 30 }, {}, null]) {
    assert.equal(normalizeAim(point, box(), 1), null);
  }
  for (const bounds of [null, {}, { ...box(), width: 0 }, { ...box(), height: -1 }, { ...box(), x: NaN },
    { ...box(), width: Infinity }, { ...box(), width: '80' }, { x: Number.MAX_VALUE, y: 0, width: Number.MAX_VALUE, height: 1 }]) {
    assert.equal(normalizeAim({ x: 0, y: 30 }, bounds, 1), null);
  }
  for (const version of [0, -1, 1.5, Infinity, NaN, Number.MAX_SAFE_INTEGER + 1, '1', null, undefined]) {
    assert.equal(normalizeAim({ x: 0, y: 30 }, box(), version), null);
    assert.equal(pickAim(svg(), { clientX: 0, clientY: 30 }, version), null);
  }
  assert.equal(normalizeAim({ x: 0, y: 30 }, box(), Number.MAX_SAFE_INTEGER).geometryVersion, Number.MAX_SAFE_INTEGER);
});

test('inverse screen CTM preserves one target across scaling, translation, rotation and skew', () => {
  const local = { x: -20, y: 40 };
  for (const m of [matrix(), matrix(2, 0, 0, 2, 120, 80), matrix(1.5, 0, 0, .75, 800, 20),
    matrix(0, 2, -2, 0, 400, 200), matrix(2, .5, .25, 1, 80, 100), matrix(-2, 0, 0, 2, 500, 20)]) {
    const element = svg(m), pointer = client(m, local);
    const resolved = screenToLocal(element, pointer);
    assert.ok(Math.abs(resolved.x - local.x) < 1e-10); assert.ok(Math.abs(resolved.y - local.y) < 1e-10);
    assert.equal(Object.isFrozen(resolved), true);
    assert.deepEqual(pickAim(element, pointer, 3), { u: 2500, v: 7500, geometryVersion: 3 });
  }
});

test('preserveAspectRatio letterbox space and points beyond the SVG content do not select shore-edge targets', () => {
  // A 480x240 viewBox fitted inside a 600x400 viewport has 50px top/bottom bars.
  const element = svg(matrix(1.25, 0, 0, 1.25, 0, 50), { x: 0, y: 0, width: 480, height: 240 });
  for (const pointer of [{ clientX: 300, clientY: 25 }, { clientX: 300, clientY: 375 },
    { clientX: -1, clientY: 200 }, { clientX: 601, clientY: 200 }, { clientX: 300, clientY: 49.999999 }]) {
    assert.equal(pickAim(element, pointer, 2), null);
  }
  assert.deepEqual(pickAim(element, { clientX: 300, clientY: 50 }, 2), { u: 5000, v: 0, geometryVersion: 2 });
  assert.deepEqual(pickAim(element, { clientX: 300, clientY: 200 }, 2), { u: 5000, v: 5000, geometryVersion: 2 });
  assert.deepEqual(pickAim(element, { clientX: 600, clientY: 350 }, 2), { u: 10000, v: 10000, geometryVersion: 2 });
});

test('missing, nonfinite, singular, three-dimensional or throwing DOM transforms fail closed', () => {
  const pointer = { clientX: 0, clientY: 30 };
  for (const transform of [null, {}, matrix(1, 2, 2, 4), matrix(Infinity), { ...matrix(), is2D: false },
    { ...matrix(), inverse: null }, { ...matrix(), inverse() { throw new Error('not invertible'); } },
    { ...matrix(), inverse: () => ({ ...matrix(), e: NaN }) }, { ...matrix(), inverse: () => matrix(0, 0, 0, 0) }]) {
    assert.equal(screenToLocal(svg(transform), pointer), null); assert.equal(pickAim(svg(transform), pointer, 1), null);
  }
  for (const element of [null, {}, { viewBox: { baseVal: box() }, getScreenCTM() { throw new Error('detached'); } }]) {
    assert.equal(screenToLocal(element, pointer), null); assert.equal(pickAim(element, pointer, 1), null);
  }
  assert.equal(pickAim({ getScreenCTM: () => matrix() }, pointer, 1), null);
  for (const bad of [{ clientX: NaN, clientY: 30 }, { clientX: 0, clientY: Infinity }, { clientX: '0', clientY: 30 }, null]) {
    assert.equal(screenToLocal(svg(), bad), null);
  }
  assert.equal(screenToLocal(svg(matrix(1e-150, 0, 0, 1e-150)), { clientX: Number.MAX_VALUE, clientY: 0 }), null);
});

test('pointer-down picks detach from subsequent pointer, SVG and geometry-version changes', () => {
  const element = svg(), pointer = { clientX: -20, clientY: 40 }; let version = 5;
  const aim = pickAim(element, pointer, version);
  pointer.clientX = 40; element.viewBox.baseVal.width = 160; version = 6;
  assert.deepEqual(aim, { u: 2500, v: 7500, geometryVersion: 5 });
  assert.throws(() => { aim.u = 0; }, TypeError);
  assert.deepEqual(pickAim(element, pointer, version), { u: 5000, v: 7500, geometryVersion: 6 });
  assert.deepEqual(Object.keys(aim), ['u', 'v', 'geometryVersion']);
});

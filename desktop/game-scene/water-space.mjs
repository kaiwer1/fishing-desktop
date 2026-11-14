import * as THREE from 'three';

const finiteInteger = Number.isSafeInteger;

function rectangle(b) {
  return b && [b.minXmm, b.maxXmm, b.minZmm, b.maxZmm].every(finiteInteger)
    && b.maxXmm > b.minXmm && b.maxZmm > b.minZmm;
}

export function validWater(water) {
  if (!water?.enabled || !finiteInteger(water.geometryVersion) || water.geometryVersion < 1
      || !rectangle(water.bounds) || !rectangle(water.waterBounds)
      || ![water.anchor?.xMm, water.anchor?.yMm, water.anchor?.zMm, water.surfaceYmm].every(finiteInteger)
      || water.anchor.yMm <= water.surfaceYmm || !Array.isArray(water.bed) || water.bed.length < 2) return false;
  const b = water.bounds, w = water.waterBounds;
  if (b.minXmm < w.minXmm || b.maxXmm > w.maxXmm || b.minZmm < w.minZmm || b.maxZmm > w.maxZmm) return false;
  return water.bed[0]?.zMm === w.minZmm && water.bed.at(-1)?.zMm === w.maxZmm
    && water.bed.every((k, i) => k && finiteInteger(k.zMm) && finiteInteger(k.depthMm) && k.depthMm >= 0
      && (i === 0 || k.zMm > water.bed[i - 1].zMm));
}

export function toWorld(point, out = new THREE.Vector3()) {
  return out.set(point.xMm / 1000, point.yMm / 1000, -point.zMm / 1000);
}

export function setBankCamera(camera, water, aspect) {
  const rect = water.bounds;
  const aimCentre = new THREE.Vector3((rect.minXmm + rect.maxXmm) / 2000,
    water.surfaceYmm / 1000, -(rect.minZmm + rect.maxZmm) / 2000);
  toWorld(water.anchor, camera.position);
  // The angler stands behind the rod's launch anchor. Keep the horizon in the
  // upper half while preserving exactly the host's coordinate system.
  camera.position.y += .23;
  camera.position.z += .5;
  camera.aspect = aspect;
  const nearest = Math.max(.1, camera.position.z + rect.minZmm / 1000);
  const halfWidth = Math.max(Math.abs(camera.position.x - rect.minXmm / 1000), Math.abs(camera.position.x - rect.maxXmm / 1000));
  camera.fov = Math.min(104, Math.max(47, THREE.MathUtils.radToDeg(2 * Math.atan(halfWidth * 1.05 / (nearest * aspect)))));
  camera.lookAt(aimCentre.x, aimCentre.y - 1.8, aimCentre.z);
  camera.updateProjectionMatrix();
  camera.updateMatrixWorld(true);
}

function inside(value) { return Number.isSafeInteger(value) && value >= 0 && value <= 10000; }

export function aimPoint(water,u,v,out=new THREE.Vector3()) {
  if(!inside(u)||!inside(v))return null;
  const b = water.bounds;
  const x = b.minXmm + (b.maxXmm - b.minXmm) * u / 10000;
  const z = b.maxZmm - (b.maxZmm - b.minZmm) * v / 10000;
  return toWorld({xMm:x,yMm:water.surfaceYmm,zMm:z},out);
}

export function projectAim(camera, water, rect, u, v) {
  const point=aimPoint(water,u,v);
  if (!point || !rect.width || !rect.height) return null;
  const ndc = point.project(camera);
  if (Math.abs(ndc.x) > 1 || Math.abs(ndc.y) > 1 || Math.abs(ndc.z) > 1) return null;
  return { clientX: rect.left + rect.width * (ndc.x + 1) / 2, clientY: rect.top + rect.height * (1 - ndc.y) / 2 };
}

export function pickAim(camera, water, rect, clientX, clientY) {
  if (![clientX, clientY].every(Number.isFinite) || !rect.width || !rect.height) return null;
  const ndc = new THREE.Vector2(2 * (clientX - rect.left) / rect.width - 1, 1 - 2 * (clientY - rect.top) / rect.height);
  if (Math.abs(ndc.x) > 1 || Math.abs(ndc.y) > 1) return null;
  const ray = new THREE.Raycaster();
  ray.setFromCamera(ndc, camera);
  const point = ray.ray.intersectPlane(new THREE.Plane(new THREE.Vector3(0, 1, 0), -water.surfaceYmm / 1000), new THREE.Vector3());
  if (!point) return null;
  const b = water.bounds, x = point.x * 1000, z = -point.z * 1000;
  // A point projected from the exact boundary can round-trip a few nanometres
  // outside it in double precision. The host's coordinate resolution is 1 mm.
  const epsilon = .00001;
  if (x < b.minXmm - epsilon || x > b.maxXmm + epsilon || z < b.minZmm - epsilon || z > b.maxZmm + epsilon) return null;
  return { u: Math.round(10000 * (x - b.minXmm) / (b.maxXmm - b.minXmm)) || 0,
    v: Math.round(10000 * (b.maxZmm - z) / (b.maxZmm - b.minZmm)) || 0, geometryVersion: water.geometryVersion };
}

export function disposeGroup(group) {
  const geometries = new Set(), materials = new Set(), textures = new Set();
  group.traverse(object => {
    if (object.geometry) geometries.add(object.geometry);
    if (object.isInstancedMesh) object.dispose();
    if (object.material) for (const material of [].concat(object.material)) materials.add(material);
  });
  for (const material of materials) {
    if (material.map) textures.add(material.map);
    material.dispose();
  }
  for (const geometry of geometries) geometry.dispose();
  for (const texture of textures) texture.dispose();
  group.removeFromParent();
  group.clear();
}

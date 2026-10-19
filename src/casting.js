import { isWater } from './ecosystem.js';

export function castPower(seconds) {
  return Math.max(0, Math.min(1, Number.isFinite(seconds) ? seconds / 1.4 : 0));
}

/** Mouse direction supplies the bearing; charge scales distance toward that point. */
export function castLanding(map, origin, aim, seconds) {
  if (!isWater(map, aim) || !origin || !Number.isFinite(origin.x) || !Number.isFinite(origin.z)) return null;
  const dx = aim.x - origin.x, dz = aim.z - origin.z, distance = Math.hypot(dx, dz);
  if (!distance) return null;
  const reach = Math.max(6, Math.min(50, distance) * (.25 + .75 * castPower(seconds)));
  const point = { x: origin.x + dx / distance * reach, z: origin.z + dz / distance * reach };
  return isWater(map, point) ? point : null;
}

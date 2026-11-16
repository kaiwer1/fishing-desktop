import * as THREE from 'three';

// A low, uneven gravel tongue extends into the surveyed river at the bank.
export function gravelHeight(x, z, near) {
  const edge = near + .42 * Math.sin(x * .8 + .5) + .26 * Math.cos(x * 1.75);
  return (z - edge) * .075 - .015;
}

export function riverSedges(random, centre, near, surfaceY, minX, maxX) {
  const points = [], colours = [], indices = [];
  const shade = new THREE.Color(), green = new THREE.Color(0x677244), straw = new THREE.Color(0xb39e6d);
  function vertex(x, y, z, light) {
    points.push(x, y, z);
    colours.push(shade.r * light, shade.g * light, shade.b * light);
    return points.length / 3 - 1;
  }
  for (let i = 0; i < 184; i++) {
    const cx = centre + (random() - .5) * 38;
    if (cx < minX || cx > maxX || (Math.abs(cx - centre) < 1.6 && random() < .8)) continue;
    const cover = .55 + .35 * Math.sin(cx * 1.41) + .16 * Math.sin(cx * 3.1);
    if (random() > cover) continue;
    const cz = near + (random() - .46) * 1.65;
    const height = (.24 + random() * .42) * (Math.abs(cx - centre) < 1.6 ? .6 : 1);
    const dry = random() ** 1.7;
    const count = 7 + Math.floor(random() * 9);
    for (let j = 0; j < count; j++) {
      const a = j * 2.39996 + random() * .6;
      const dx = Math.cos(a), dz = Math.sin(a), radius = .10 * Math.sqrt(random());
      const x = cx + dx * radius, z = cz + dz * radius, y = surfaceY + gravelHeight(x, z, near) - .012;
      const h = height * (.52 + random() * .65), arch = h * (.23 + random() * .81);
      const width = .008 + random() * .009, droop = .15 + random() * .36;
      const twist = (random() - .5) * .8;
      shade.copy(green).lerp(straw, Math.min(.95, dry * .68 + random() * .29));
      let last;
      for (const t of [0, .29, .58, .82]) {
        const bow = arch * t * t, xc = x + dx * bow + .026 * t, zc = z + dz * bow;
        const yc = y + h * (t - droop * t * t * t);
        const side = a + Math.PI / 2 + twist * t, sx = Math.cos(side), sz = Math.sin(side);
        const w = width * (1 - t * t), light = .37 + .55 * t;
        const row = vertex(xc - sx * w, yc, zc - sz * w, light * .81);
        vertex(xc - dx * w * .38, yc + w * .23, zc - dz * w * .38, light * 1.22);
        vertex(xc + sx * w, yc, zc + sz * w, light);
        if (last !== undefined) {
          indices.push(last, last+1, row, row, last+1, row+1,
            last+1, last+2, row+1, row+1, last+2, row+2);
        }
        last = row;
      }
      const tip = vertex(x + dx * arch + .026, y + h * (1 - droop), z + dz * arch, 1.04);
      indices.push(last, last+1, tip, last+1, last+2, tip);
    }
  }
  const mesh = new THREE.BufferGeometry();
  mesh.setAttribute('position', new THREE.Float32BufferAttribute(points, 3));
  mesh.setAttribute('color', new THREE.Float32BufferAttribute(colours, 3));
  mesh.setIndex(indices);
  const sedges = new THREE.Mesh(mesh, new THREE.MeshBasicMaterial({vertexColors:true, side:THREE.DoubleSide}));
  sedges.name = 'bank-vegetation';
  return sedges;
}

// Only the photographed upland is used. The foreground, water, sky, weather
// and the physical fishing positions remain independent three-dimensional layers.
export function riverRidge(cx, near, sy) {
  const radius = 226, angle = Math.PI * 161 / 180, segments = 40;
  const positions = [], uvs = [], indices = [];
  for (let i = 0; i <= segments; i++) {
    const u = i / segments, a = (u - .5) * angle;
    const x = cx + radius * Math.sin(a), z = near - radius * Math.cos(a);
    positions.push(x, sy - 1.8, z, x, sy + 58, z);
    uvs.push(u, 0, u, 1);
    if (i > 0) { const k = i * 2; indices.push(k-2,k,k-1,k-1,k,k+1); }
  }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  geometry.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2));
  geometry.setIndex(indices);
  const material = new THREE.MeshBasicMaterial({side:THREE.DoubleSide, alphaTest:.47, fog:false});
  const air = {value:new THREE.Color(.48,.58,.62)}, haze = {value:.13};
  material.onBeforeCompile = shader => {
    shader.uniforms.ridgeAir = air;shader.uniforms.ridgeHaze = haze;
    shader.fragmentShader = 'uniform vec3 ridgeAir; uniform float ridgeHaze;\n' + shader.fragmentShader
      .replace('#include <opaque_fragment>', 'outgoingLight=mix(outgoingLight,ridgeAir,ridgeHaze);\n#include <opaque_fragment>');
  };
  const mesh = new THREE.Mesh(geometry, material);mesh.name = 'shudu-river-ridge';mesh.visible = false;
  return {mesh,
    setTexture(texture) { material.map = texture; material.needsUpdate = true; mesh.visible = true; },
    setConditions(sample) {
      if (sample) air.value.fromArray(sample.fogColor); else air.value.setRGB(.48,.58,.62);
      haze.value = sample ? Math.min(.66, .035 + 15 * sample.fogDensity) : .13;
      material.color.setScalar(sample ? .08 + 1.05 * sample.waterLight ** 2.4 : 1.12);
    }
  };
}

import * as THREE from 'three';
import { Reflector } from 'three/addons/objects/Reflector.js';
import { disposeGroup } from './water-space.mjs';
import { weatherUniforms, setWeatherUniforms, skyWeatherGLSL, waterWeatherGLSL } from './weather-lighting.mjs';
import { gravelHeight, riverRidge, riverSedges } from './river-bank.mjs';

function randomSource(seed) {
  return () => { seed = (Math.imul(1664525, seed) + 1013904223) >>> 0; return seed / 4294967296; };
}

// Original foliage plates. Fork the actual limbs before drawing leaf clusters
// so gaps between branches survive downsampling at the opposite shore.
function treeTexture(kind) {
  const random = randomSource(4241 + kind * 1789);
  const image = document.createElement('canvas');
  image.width = image.height = 384;
  const ctx = image.getContext('2d');
  const leaves = [];
  ctx.lineCap = 'round';
  function limb(x, y, length, angle, level) {
    const nextX = x + Math.sin(angle) * length, nextY = y - Math.cos(angle) * length;
    ctx.lineWidth = Math.max(1, Math.pow(level + .5, 1.65) * .56);
    ctx.strokeStyle = kind === 1 ? '#989785' : '#564d39';
    ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(nextX, nextY); ctx.stroke();
    if (level <= 2) leaves.push([nextX, nextY, 7 + random() * 10]);
    if (!level) return;
    const lean = angle * .62;
    limb(nextX, nextY, length * (.66 + random() * .13), lean - .26 - random() * .68, level - 1);
    limb(nextX, nextY, length * (.67 + random() * .13), lean + .26 + random() * .68, level - 1);
    if (level >= 3) limb(x + (nextX - x) * .7, y + (nextY - y) * .7, length * .68, lean + (random() - .5) * 1.6, level - 2);
  }
  function foliage(x, y, radius, count, elongated) {
    for (let j = 0; j < count; j++) {
      const a = random() * Math.PI * 2, r = Math.sqrt(random()) * radius;
      const px = x + r * Math.cos(a), py = y + r * Math.sin(a) * elongated;
      const light = 18 + (1 - px / 380) * 11 + (1 - py / 380) * 5 + random() * 13;
      ctx.fillStyle = 'hsl(' + ((kind === 2 ? 90 : kind === 1 ? 73 : 83) + random() * 13) + ' 24% ' + light + '%)';
      ctx.beginPath(); ctx.ellipse(px, py, 1.4 + random() * 3.9, 1 + random() * 2.7, random() * Math.PI, 0, Math.PI * 2); ctx.fill();
    }
  }
  if (kind === 2) {
    ctx.fillStyle = '#534c3e'; ctx.fillRect(190, 31, 5, 343);
    for (let layer = 0; layer < 38; layer++) {
      const y = 35 + layer * 7.7, width = (6 + layer * 2.05) * (.68 + random() * .32);
      for (const side of [-1, 1]) {
        for (let j = 0; j < 8; j++) {
          const t = j / 7;
          foliage(192 + side * width * t, y + t * 9 - t * t * 11, (5 + layer * .29) * (1 - .48 * t), 21, .65);
        }
      }
    }
  } else {
    limb(190, 377, kind === 1 ? 106 : 83, (random() - .5) * .1, 5);
    for (const [x, y, radius] of leaves) foliage(x, y, radius, 57, kind === 1 ? 1.18 : .83);
  }
  const texture = new THREE.CanvasTexture(image); texture.colorSpace = THREE.SRGBColorSpace;
  return texture;
}

const skyShader = {
  uniforms: { ...weatherUniforms(), horizon: { value:new THREE.Vector3(.92,.81,.62) }, zenith: { value:new THREE.Vector3(.18,.42,.65) } },
  vertexShader: `varying vec3 direction;
    void main(){direction=position;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}`,
  fragmentShader: `varying vec3 direction;uniform vec3 horizon;uniform vec3 zenith;
    ${skyWeatherGLSL}
    float hash(vec2 p){return fract(sin(dot(p,vec2(12.9898,78.233)))*43758.5453);}
    float noise(vec2 p){vec2 i=floor(p),f=fract(p); f=f*f*(3.-2.*f);
      return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+1.),f.x),f.y);}
    void main(){vec3 d=normalize(direction);float h=max(d.y,0.);
      vec3 sky=mix(horizon,zenith,pow(h,.45));
      vec2 p=d.xz/max(.09,h+.12);p+=vec2(noise(p*1.4),noise(p*1.6+3.5))*.45;
      float clouds=.50*noise(p*2.)+.27*noise(p*4.3)+.14*noise(p*9.1)+.06*noise(p*19.)+.03*noise(p*38.);
      if(weatherOn>.5)sky=conditionsSky(d,h,clouds);
      else{
        sky=mix(sky,mix(vec3(.67,.72,.74),vec3(.98,.95,.87),smoothstep(.46,.70,clouds)),smoothstep(.46,.68,clouds)*smoothstep(.03,.22,h)*.84);
        float sun=max(dot(d,normalize(vec3(-.47,.23,-1.))),0.);
        sky+=vec3(.64,.38,.13)*pow(sun,22.)+vec3(3.,2.3,1.4)*smoothstep(.9996,.99998,sun);
      }
      gl_FragColor=vec4(sky,1.);
      #include <tonemapping_fragment>
      #include <colorspace_fragment>
    }`
};

const waterShader = {
  name: 'StillwaterSurface',
  uniforms: { ...weatherUniforms(), tDiffuse: { value: null }, color: { value: null }, textureMatrix: { value: null },
    time: { value: 0 }, impact: { value: new THREE.Vector3(0, 0, -99) }, rough: { value: .65 }, body: {value: new THREE.Color(0x36594e)},
    fishPos:{value:new THREE.Vector3()},fishShape:{value:new THREE.Vector4()},spoon:{value:new THREE.Vector4()} },
  vertexShader: `uniform mat4 textureMatrix;attribute float sounding;varying float bedDepth;varying vec4 uvMirror;varying vec3 world;
    void main(){bedDepth=sounding;uvMirror=textureMatrix*vec4(position,1.);world=(modelMatrix*vec4(position,1.)).xyz;
      gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}`,
  fragmentShader: `uniform sampler2D tDiffuse;uniform float time;uniform float rough;uniform vec3 body;uniform vec3 impact;
    ${waterWeatherGLSL}
    uniform vec3 fishPos;uniform vec4 fishShape;uniform vec4 spoon;varying float bedDepth;varying vec4 uvMirror;varying vec3 world;
    float oval(vec2 p,vec2 radius){float d=length(p/radius);return 1.-smoothstep(.91,1.06,d);}
    // Suppress ripples narrower than a pixel towards the horizon. In the
    // foreground overlapping trains and moving wind patches break up the crests.
    float slope(float phase){return cos(phase)*(1.-smoothstep(.30,2.9,fwidth(phase)));}
    float waveHash(vec2 cell){
      vec3 q=fract(cell.xyx*vec3(.1031,.11369,.13787));q+=dot(q,q.yzx+19.19);
      return fract((q.x+q.y)*q.z);
    }
    // Derivative of smooth value noise. Crosswinds shear these patches into
    // short crests that lose contrast once their spacing falls below a pixel.
    vec2 windSlope(vec2 p){
      vec2 cell=floor(p),f=fract(p),u=f*f*(3.-2.*f);
      float a=waveHash(cell),b=waveHash(cell+vec2(1.,0.)),c=waveHash(cell+vec2(0.,1.)),d=waveHash(cell+1.);
      vec2 gradient=6.*f*(1.-f)*mix(vec2(b-a,c-a),vec2(d-c,d-b),u.yx);
      return gradient*(1.-smoothstep(.22,1.3,length(fwidth(p))));
    }
    void main(){vec2 p=world.xz;vec3 eye=normalize(cameraPosition-world);
      float amp=clamp(eye.y*4.,.32,1.);
      vec2 patchPhase=p*vec2(.34,.21)+vec2(-.14,.10)*time;
      float wind=.68+.21*sin(patchPhase.x+sin(patchPhase.y))+.11*cos(patchPhase.y*1.8+.65*patchPhase.x);
      float broad=dot(p,vec2(.94,1.53))-.95*time+.71*sin(p.x*.35-p.y*.08);
      float crossing=dot(p,vec2(-2.31,1.14))-.80*time+sin(p.y*.44+.16*time);
      float ripple=dot(p,vec2(3.37,6.09))-2.0*time+1.4*sin(p.x*.7+p.y*.35+.2*time);
      float capillary=dot(p,vec2(-8.16,10.71))-3.2*time+2.5*sin(p.y*.63+p.x*.81);
      vec2 tilt=vec2(.007,.011)*slope(broad)+vec2(-.007,.004)*slope(crossing);
      tilt+=wind*(vec2(.013,.024)*slope(ripple)+vec2(-.010,.016)*slope(capillary));
      vec2 rippled=vec2(p.x*1.18+p.y*.37,p.y*3.43-p.x*.64)+vec2(.20,-.82)*time;
      tilt+=vec2(.028,.042)*windSlope(rippled)+wind*vec2(.024,.040)*windSlope(rippled*2.17-vec2(.4,.61)*time);
      float ax=tilt.x,az=tilt.y;
      vec2 radial=p-impact.xy;float age=time-impact.z;float r=length(radial);
      if(age>=0.&&age<4.){float ring=sin((r-age*1.12)*24.)*exp(-pow((r-age*1.12)*3.5,2.))*exp(-age*.8);
        vec2 impactSlope=radial/max(r,.001)*ring*.07;ax+=impactSlope.x;az+=impactSlope.y;}
      vec3 normal=normalize(vec3(ax*rough,1.,az*rough));vec2 uv=uvMirror.xy/uvMirror.w;
      vec3 shore=texture2D(tDiffuse,clamp(uv+vec2(ax,az)*rough*(.09+.13*amp),.001,.999)).rgb;
      float fresnel=.09+.89*pow(1.-max(dot(eye,normal),0.),3.7);
      vec3 water=mix(body,shore,min(.9,fresnel));
      if(bedDepth>0.){
        // Diffuse light returning from the bed fades with the optical path
        // through water. Grazing angles retain the reflected skyline.
        float bottomLight=exp(-bedDepth*.79)*(1.-fresnel);
        water+=vec3(.071,.052,.025)*bottomLight;
      }
      if(fishShape.w>0. && eye.y>.012){
        // Project the fragment's viewing ray to the confirmed swimming depth.
        // The bed and shoreline mask the effect naturally at the water edge.
        float depth=max(0.,world.y-fishPos.y);
        vec2 submerged=world.xz-eye.xz*depth/eye.y;
        vec2 delta=submerged-fishPos.xz;vec2 heading=vec2(cos(fishShape.x),sin(fishShape.x));
        vec2 g=vec2(dot(delta,heading)/fishShape.y,dot(delta,vec2(-heading.y,heading.x))/fishShape.z);
        if(abs(g.x)<.9&&abs(g.y)<1.3){
          float beat=sin(time*9.1), tailWeight=smoothstep(.02,.58,-g.x);
          g.y-=beat*tailWeight*tailWeight*.46;
          float bodyMask=oval(g-vec2(.08,0.),vec2(.395,.49));
          float stalk=oval(g-vec2(-.28,0.),vec2(.16,.14));
          float lobeA=oval(vec2(g.x+.49+(g.y-.22)*.23,g.y-.22),vec2(.093,.26));
          float lobeB=oval(vec2(g.x+.49-(g.y+.22)*.23,g.y+.22),vec2(.093,.26));
          float fins=oval(vec2(g.x-.03+(abs(g.y)-.43)*.37,abs(g.y)-.43),vec2(.068,.28));
          float mask=max(max(bodyMask,stalk),max(max(lobeA,lobeB),fins*.64));
          vec3 back=mix(vec3(.029,.049,.036),vec3(.12,.15,.097),(1.-smoothstep(-.4,.43,g.y))*.55);
          water=mix(water,back,mask*fishShape.w);
        }
        vec2 surfaceDelta=world.xz-fishPos.xz;
        float wake=length(vec2(dot(surfaceDelta,heading)*.8,dot(surfaceDelta,vec2(-heading.y,heading.x))));
        float crest=sin(wake*35.-time*11.)*exp(-wake*5.)*smoothstep(0.,.11,wake);
        water+=vec3(.10,.13,.12)*crest*fishShape.w*(1.-smoothstep(0.,.42,depth));
      }
      if(spoon.w>0. && eye.y>.02){
        float depth=world.y-spoon.y;
        vec2 delta=world.xz-eye.xz*depth/eye.y-spoon.xz;
        if(dot(delta,delta)<.003){
          float flash=oval(delta,vec2(.012,.034))*spoon.w;
          water=mix(water,vec3(.51,.57,.53),flash*(1.-smoothstep(.0,1.9,depth)));
        }
      }
      vec3 lightDir=weatherOn>.5?weatherSun:normalize(vec3(-.47,.23,-1.));
      float spark=pow(max(dot(normal,normalize(eye+lightDir)),0.),850.);
      water+=vec3(.98,.77,.41)*spark*.83*(weatherOn>.5?weatherGlow:1.);
      if(weatherOn>.5)water=conditionsWater(water,world,fresnel);
      gl_FragColor=vec4(water,1.);
      #include <tonemapping_fragment>
      #include <colorspace_fragment>
    }`
};

function earthTexture(random, kind) {
  const size=128, data=new Uint8Array(size*size*4);
  for(let z=0;z<size;z++)for(let x=0;x<size;x++){
    const n=random(),w=2*Math.PI/size;
    const stratum=.5+.5*Math.sin(16*z*w+Math.sin(x*w*3)*1.9);
    const grain=.69+n*.27+(kind==='sea'?stratum:.5+.5*Math.sin(x*w*3+Math.cos(z*w*5)))*.12;
    const k=(z*size+x)*4;data[k]=Math.min(255,grain*242);data[k+1]=Math.min(255,grain*240);data[k+2]=Math.min(255,grain*229);data[k+3]=255;
  }
  const t=new THREE.DataTexture(data,size,size,THREE.RGBAFormat);t.colorSpace=THREE.SRGBColorSpace;
  t.wrapS=t.wrapT=THREE.RepeatWrapping;t.generateMipmaps=true;t.minFilter=THREE.LinearMipmapLinearFilter;t.magFilter=THREE.LinearFilter;t.needsUpdate=true;
  return t;
}

function terrain(group, width, depth, xCentre, zCentre, surfaceY, height, far, material, tileMetres=3) {
  const g = new THREE.PlaneGeometry(width, depth, far ? 76 : 48, far ? 23 : 12);
  g.rotateX(-Math.PI / 2);
  const p = g.attributes.position, colors = [], c = new THREE.Color();
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i) + xCentre, z = p.getZ(i) + zCentre, y = height(x,z);
    p.setXYZ(i, x, y + surfaceY, z);
    c.setScalar(.83 + .09 * Math.sin(x * .47 + z * .85) + .08 * Math.cos(x * 1.71 - z * .29));
    colors.push(c.r, c.g, c.b);
    g.attributes.uv.setXY(i,x/tileMetres,z/tileMetres);
  }
  g.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3)); g.computeVertexNormals();
  group.add(new THREE.Mesh(g,material));
}

function bedColumns(water) {
  return water.crossBed?.length?water.crossBed:[{xMm:water.waterBounds.minXmm,extraDepthMm:0},{xMm:water.waterBounds.maxXmm,extraDepthMm:0}];
}

function waterSurfaceGeometry(water,width,depth){
  const columns=bedColumns(water),bounds=water.waterBounds;
  let rows=water.bed;
  if(water.waterId==='river-bend'){
    // The visual waterline passes under the irregular gravel banks. Continue
    // the edge's exact cross-bank depth so the water colour stays continuous.
    const first=rows[0],last=rows.at(-1);
    rows=[{...first,zMm:first.zMm-4000},...rows,{...last,zMm:last.zMm+4000}];
  }else if(water.waterId==='coastal-bay'){
    rows=[{...rows[0],zMm:rows[0].zMm-4000},...rows];
  }
  const geometry=new THREE.PlaneGeometry(width,depth,columns.length-1,rows.length-1);
  const pos=geometry.attributes.position,soundings=[];
  const middleX=(bounds.minXmm+bounds.maxXmm)/2,middleZ=(bounds.minZmm+bounds.maxZmm)/2;
  for(let i=rows.length-1;i>=0;i--)for(const col of columns){
    pos.setXY(soundings.length,(col.xMm-middleX)/1000,(rows[i].zMm-middleZ)/1000);
    soundings.push((rows[i].depthMm+col.extraDepthMm)/1000);
  }
  geometry.setAttribute('sounding',new THREE.Float32BufferAttribute(soundings,1));
  return geometry;
}

function bedMesh(water) {
  const pos = [], index = [],columns=bedColumns(water);
  for (const [i, k] of water.bed.entries()) {
    for(const [j,col] of columns.entries()){
      pos.push(col.xMm/1000,(water.surfaceYmm-k.depthMm-col.extraDepthMm)/1000,-k.zMm/1000);
      if(i&&j){const far=i*columns.length+j,near=far-columns.length;index.push(near-1,near,far-1,far-1,near,far);}
    }
  }
  const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3)); g.setIndex(index); g.computeVertexNormals();
  const bed = new THREE.Mesh(g, new THREE.MeshLambertMaterial({ color: 0x474938 })); bed.name = 'surveyed-bed'; return bed;
}

function createStoneMaterial(surfaceY) {
  const material=new THREE.MeshStandardMaterial({color:0x9ca39f,roughness:.83,metalness:0});
  material.onBeforeCompile=shader=>{
    shader.uniforms.tideHeight={value:surfaceY};
    shader.vertexShader=shader.vertexShader
      .replace('#include <common>','#include <common>\nvarying vec3 stoneWorld; varying vec3 stoneAxis;')
      .replace('#include <defaultnormal_vertex>','#include <defaultnormal_vertex>\nstoneAxis=inverseTransformDirection(transformedNormal,viewMatrix);')
      .replace('#include <worldpos_vertex>',`#include <worldpos_vertex>
        vec4 stoneLocal=vec4(transformed,1.);
        #ifdef USE_INSTANCING
          stoneLocal=instanceMatrix*stoneLocal;
        #endif
        stoneWorld=(modelMatrix*stoneLocal).xyz;`);
    shader.fragmentShader=shader.fragmentShader
      .replace('#include <common>','#include <common>\nvarying vec3 stoneWorld; varying vec3 stoneAxis; uniform float tideHeight;')
      .replace('#include <map_fragment>',`// Sample in metres so a large stone gets more detail than a pebble.
        #ifdef USE_MAP
          vec3 normalWeights=abs(normalize(stoneAxis));
          normalWeights=pow(max(normalWeights,vec3(.001)),vec3(4.));
          normalWeights/=dot(normalWeights,vec3(1.));
          vec3 tile=stoneWorld/1.05;
          vec3 scan=texture2D(map,tile.zy).rgb*normalWeights.x
            +texture2D(map,tile.xz).rgb*normalWeights.y+texture2D(map,tile.xy).rgb*normalWeights.z;
          diffuseColor.rgb*=mix(scan,sqrt(scan)*.76,.31);
        #endif
        float splashMark=tideHeight+.07+.023*sin(stoneWorld.x*3.1+stoneWorld.z*1.7);
        float wetStone=1.-smoothstep(splashMark,splashMark+.09,stoneWorld.y);
        diffuseColor.rgb*=mix(vec3(1.),vec3(.65,.72,.71),wetStone);`)
      .replace('#include <roughnessmap_fragment>','#include <roughnessmap_fragment>\nroughnessFactor=mix(roughnessFactor,.37,wetStone);');
  };
  return material;
}

// The overcast sky dome illuminates the weathered rock faces from all sides.
// Share its irradiance between the beach and boulders, including the fallback
// materials, and preserve the albedo and orientation of each surface.
function addCoastalSkylight(material,irradiance) {
  const decorate=material.onBeforeCompile.bind(material);
  material.onBeforeCompile=shader=>{
    decorate(shader);
    shader.uniforms.coastalSky=irradiance;
    shader.fragmentShader=shader.fragmentShader
      .replace('#include <common>','#include <common>\nuniform vec3 coastalSky;')
      .replace('#include <lights_fragment_begin>',`#include <lights_fragment_begin>
        #ifdef RE_IndirectDiffuse
          float upward=inverseTransformDirection(geometryNormal,viewMatrix).y;
          irradiance+=coastalSky*(.56+.44*upward);
        #endif`);
  };
}

// Connected weathered shelves project a continuous headland against the sky.
// Both ends taper below water; the model sits beyond the surveyed casting area.
function coastalHeadland(random,group,cx,width,far,sy) {
  const positions=[],colors=[],indices=[];
  const columns=72,rows=6;
  const heights=[.32,.65,1.6,1.2,2.3,3.05,1.9,2.4,1.1,.7,.23];
  for(let i=0;i<=columns;i++){
    const t=i/columns,along=t*(heights.length-1),segment=Math.min(heights.length-2,Math.floor(along));
    const u=along-segment,ease=u*u*(3-2*u);
    const ridge=THREE.MathUtils.lerp(heights[segment],heights[segment+1],ease)+random()*.17;
    for(let j=0;j<rows;j++){
      const land=j/(rows-1);
      const elevation=sy+ridge*Math.sin(Math.PI*land)**.61-.20+(.12*random()-.06)*Math.sin(Math.PI*land);
      const x=cx+width*(.31+.83*t)+.11*Math.sin(land*5+i*.40);
      const z=far-37-18*land-4.8*Math.sin(t*8)-1.3*Math.sin(t*25);
      positions.push(x,elevation,z);
      const colour=new THREE.Color(0x89958b).lerp(new THREE.Color(0x64716b),.42*land+.17*random());
      colors.push(colour.r,colour.g,colour.b);
      if(i<columns&&j<rows-1){const a=i*rows+j,b=a+rows;indices.push(a,b,a+1,b,b+1,a+1);}
    }
  }
  const geometry=new THREE.BufferGeometry();
  geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));
  geometry.setAttribute('color',new THREE.Float32BufferAttribute(colors,3));
  geometry.setIndex(indices);geometry.computeVertexNormals();
  const rocks=new THREE.Mesh(geometry,new THREE.MeshLambertMaterial({vertexColors:true}));
  rocks.name='distant-headland';group.add(rocks);
}

function distantTrees(group, random, farZ, cx, width, sy, height) {
  const transform = new THREE.Object3D(), color = new THREE.Color();
  for (let kind = 0; kind < 3; kind++) {
    const geom = new THREE.PlaneGeometry(1,1).translate(0,.5,0);
    const mat = new THREE.MeshLambertMaterial({ map: treeTexture(kind), alphaTest: .43, side: THREE.DoubleSide });
    const count = kind===2?132:114;
    const mesh = new THREE.InstancedMesh(geom, mat, count);
    for (let i = 0; i < count; i++) {
      const x = cx + (random() - .5) * Math.max(158, width * 2.35), z = farZ - .6 - random() * 30;
      const h = (6 + random() * 7.9) * (kind === 1 ? .87 : 1);
      transform.position.set(x, sy + height(x, z) - .12, z);
      transform.scale.set(h * (kind === 2 ? .8 : 1.13), h, 1);
      transform.rotation.set(0, (random() - .5) * .51, (random() - .5) * .07);
      transform.updateMatrix(); mesh.setMatrixAt(i, transform.matrix);
      color.setHSL(.16 + random() * .05, .09 + random() * .14, .52 + random() * .4);
      mesh.setColorAt(i, color);
    }
    group.add(mesh);
  }
}

function lakeGrass(group, random, anchorX, nearZ, sy, waterMinX, waterMaxX) {
  const p = [], c = [], t = new THREE.Color(), dryGrass=new THREE.Color(0x9c9354);
  for (let i = 0; i < 2200; i++) {
    const side = i % 2 ? -1 : 1;
    const offset = side * (1.8 + Math.pow(random(), 1.4) * 14);
    const x = anchorX + offset;
    if (x < waterMinX || x > waterMaxX) continue;
    const z = nearZ + (random() - .38) * 2 + .28 * Math.sin(offset * 1.5);
    const h = (.23 + random() * .72) * Math.min(1, Math.abs(offset) / 3);
    const r = .007 + random() * .024, lean = (random() - .36) * h * .49, y = sy - .047;
    p.push(x-r,y,z,x+r,y,z,x+lean*.35+r*.4,y+h*.65,z-.02,
      x-r,y,z,x+lean*.35+r*.4,y+h*.65,z-.02,x+lean*.35-r*.45,y+h*.65,z-.02,
      x+lean*.35-r*.45,y+h*.65,z-.02,x+lean*.35+r*.4,y+h*.65,z-.02,x+lean,y+h,z+.04);
    t.set(0x52623c).lerp(dryGrass,random()*.8);
    for (const shade of [.37,.49,.85,.37,.85,.76,.76,.85,1.09]) c.push(t.r*shade,t.g*shade,t.b*shade);
  }
  const g = new THREE.BufferGeometry(); g.setAttribute('position',new THREE.Float32BufferAttribute(p,3)); g.setAttribute('color',new THREE.Float32BufferAttribute(c,3));
  const grass = new THREE.Mesh(g,new THREE.MeshBasicMaterial({vertexColors:true,side:THREE.DoubleSide}));grass.name='bank-vegetation';group.add(grass);
}
function nearShore(group, random, anchorX, nearZ, sy, waterMinX, waterMaxX, kind, rockMaterial) {
  if(kind==='river')group.add(riverSedges(random,anchorX,nearZ,sy,waterMinX,waterMaxX));
  if(kind==='lake')lakeGrass(group,random,anchorX,nearZ,sy,waterMinX,waterMaxX);
  const t = new THREE.Color();
  const tform = new THREE.Object3D();
  const rock=new THREE.IcosahedronGeometry(1,kind==='river'?1:2), points=rock.attributes.position;
  const roundNormal=new THREE.Vector3(),shelfNormal=new THREE.Vector3();
  for(let i=0;i<points.count;i++){
    const x=points.getX(i),y=points.getY(i),z=points.getZ(i),scale=1+.09*Math.sin(4*x+3*z)+.043*Math.cos(5*y-3*x);
    const shelf=.66+.16*x-.08*z+.045*Math.sin(8*x),top=kind==='sea'?Math.min(y,shelf):y;
    points.setXYZ(i,x*scale,top*scale,z*scale);
    roundNormal.set(x,y,z);
    if(kind==='sea'){
      shelfNormal.set(-.16-.36*Math.cos(8*x),1,.08).normalize();
      roundNormal.lerp(shelfNormal,THREE.MathUtils.smoothstep(y-shelf,-.16,.12)).normalize();
    }
    rock.attributes.normal.setXYZ(i,roundNormal.x,roundNormal.y,roundNormal.z);
  }
  const stones = new THREE.InstancedMesh(rock,rockMaterial,kind==='sea'?107:kind==='river'?112:64);
  const shades=[0xc1c4b4,0xc9b8a6,0x969f99,0xb7bbb8,0xc4c4b7];
  stones.name=kind==='sea'?'tidal-rocks':'shore-pebbles';
  for (let i = 0; i < stones.count; i++) {
    const x = anchorX + (random() - .5) * 30, z = nearZ + (random() - .2) * 2.3;
    const s = .02 + random() ** 2 * (kind==='sea'?.88:kind==='river'?.12:.19);
    const ground=kind==='river'?gravelHeight(x,z,nearZ)+s*.06:
      kind==='sea'?coastalBankHeight(x-anchorX,z-nearZ)+s*.12:.025+Math.max(0,z-nearZ)*.05;
    tform.position.set(x,sy+ground,z);tform.rotation.set(random()*.3,random()*6,random()*.15);
    tform.scale.set(s*(1.27+random()*.5),s*(kind==='sea'?.55:.47),s);tform.updateMatrix();stones.setMatrixAt(i,tform.matrix);
    t.set(kind==='sea'?shades[i%shades.length]:0xbcb5a7).multiplyScalar(.72+random()*.35);stones.setColorAt(i,t);
  }
  group.add(stones);
}

// Rock shelves remain outside the shallow landing channel along the walkable bank.
function coastalBankHeight(x,z) {
  const inlet=1-THREE.MathUtils.smoothstep(Math.abs(x),8.5,13);
  const cove=.55*Math.sin(x*1.27)+.35*Math.sin(x*.49+.6);
  return (z+cove-.03)*.09-inlet*.24;
}

export function createLake(water,assetUrl=null) {
  const kind=water.waterId==='coastal-bay'?'sea':water.waterId==='river-bend'?'river':'lake';
  const ocean=kind==='sea',river=kind==='river';
  const group = new THREE.Group(), random = randomSource(ocean?83564:river?37189:948351);
  const b=water.waterBounds, width=(b.maxXmm-b.minXmm)/1000, depth=(b.maxZmm-b.minZmm)/1000;
  const x=(b.minXmm+b.maxXmm)/2000, near=-b.minZmm/1000, far=-b.maxZmm/1000, sy=water.surfaceYmm/1000;
  const uniforms=THREE.UniformsUtils.clone(skyShader.uniforms);
  if(ocean){uniforms.horizon.value.set(.63,.82,.9);uniforms.zenith.value.set(.085,.32,.64);}
  if(river){uniforms.horizon.value.set(.77,.87,.84);uniforms.zenith.value.set(.21,.41,.54);}
  const sky = new THREE.Mesh(new THREE.SphereGeometry(430,24,14), new THREE.ShaderMaterial({...skyShader,uniforms,side:THREE.BackSide,depthWrite:false}));
  sky.position.set(x,sy,far+.5*depth); sky.renderOrder=-10; group.add(sky);
  const farHeight = (px,pz) => {
    const inland = Math.max(0,far-pz);
    if(river){
      const shore=far+.96*Math.sin(px*.13)+.67*Math.sin(px*.26)-pz;
      return -.065 + THREE.MathUtils.clamp(shore,-5,5)*(.11+.045*Math.sin(px*.077))
        + Math.min(inland,16)*(.022+.016*Math.sin(px*.074+inland*.036));
    }
    return -.09 + Math.min(inland/5,1)*(.42+.16*Math.sin(px*.22)) + inland*(.12+.08*Math.sin(px*.086+inland*.021)+.045*Math.cos(px*.15-inland*.057));
  };
  const ridge=river?riverRidge(x,near,sy):null;
  if(ridge)group.add(ridge.mesh);
  const groundTexture=earthTexture(random,kind);
  const bankColor=ocean?0x666b6b:river?0xa29475:0x77694e;
  const bankMaterial=new THREE.MeshLambertMaterial({color:bankColor,map:groundTexture,vertexColors:true});
  const rockMaterial=createStoneMaterial(sy);
  const coastalSky={value:new THREE.Color(0,0,0)};
  if(ocean){
    addCoastalSkylight(bankMaterial,coastalSky);addCoastalSkylight(rockMaterial,coastalSky);
  }
  const tileMetres=ocean?1.5:river?3:3.2;
  if(ocean){
    terrain(group,width+3,18,x,near+7,sy,(px,pz)=>coastalBankHeight(px-x,pz-near),false,bankMaterial,tileMetres);
  }else if(river){
    terrain(group,width+3,20,x,near+7,sy,(px,pz)=>gravelHeight(px,pz,near),false,bankMaterial,tileMetres);
  }else{
    terrain(group,width+3,16,x,near+8,sy,(px,pz)=>-.025+(pz-near)*.053+.05*Math.sin(px*.89)*Math.min(1,(pz-near)),false,bankMaterial,tileMetres);
  }
  if(!ocean){
    const opposite=new THREE.MeshLambertMaterial({color:river?0x636249:0x637054,vertexColors:true});
    if(river)opposite.onBeforeCompile=shader=>{
      // The captured ridge already carries atmospheric haze. Keep the closer
      // alluvial edge from fogging out to white in front of that backdrop.
      shader.vertexShader=shader.vertexShader.replace('#include <fog_vertex>',
        '#include <fog_vertex>\n#ifdef USE_FOG\nvFogDepth *= .36;\n#endif');
    };
    terrain(group,Math.max(300,width*3.6),river?104:100,x,far-50,sy,farHeight,true,opposite);
    for (const side of [-1,1]) {
      const edge = side===1?b.maxXmm/1000:b.minXmm/1000;
      terrain(group,18,depth,x+(width/2+9)*side,(near+far)/2,sy,(px,pz)=>Math.abs(px-edge)*(river?.045+.019*Math.sin(pz*.097):.18+.055*Math.cos(pz*.28))-.03,false,bankMaterial,tileMetres);
    }
    if(!river)distantTrees(group,random,far,x,width,sy,farHeight);
  }else{
    // These distant rocks stand beyond the surveyed rectangle. They and the
    // horizon are scenic only. Input still intersects the surveyed surface.
    coastalHeadland(randomSource(83576),group,x,width,far,sy);
  }
  nearShore(group,random,x,near,sy,b.minXmm/1000,b.maxXmm/1000,kind,rockMaterial);
  group.add(bedMesh(water));
  // Extend only the visual ocean past its playable rectangle. The two meshes
  // share a reflection transform computed for the physical water plane.
  const surface = new Reflector(waterSurfaceGeometry(water,width,depth),{shader:waterShader,textureWidth:768,textureHeight:384,multisample:0,clipBias:.003});
  surface.material.defaultAttributeValues.sounding=[0]; // Scenic sea outside the survey has no visible bed.
  surface.name='surveyed-surface'; surface.rotation.x=-Math.PI/2; surface.position.set(x,sy,(near+far)/2); group.add(surface);
  surface.material.uniforms.body.value.set(ocean?0x235d72:river?0x555b37:0x36594e);
  surface.material.uniforms.rough.value=ocean?1.35:river?.91:.65;
  if(ocean){
    const ext=310;
    const segments=[[0,depth/2+ext/2,width+2*ext,ext],[-(width+ext)/2,0,ext,depth],[(width+ext)/2,0,ext,depth]];
    for(const [cx,cy,w,d] of segments){
      const plane=new THREE.Mesh(new THREE.PlaneGeometry(w,d).translate(cx,cy,0),surface.material);
      plane.position.copy(surface.position);plane.quaternion.copy(surface.quaternion);plane.layers.set(1);plane.renderOrder=1;plane.name='open-sea-horizon';group.add(plane);
    }
  }
  // A stationary camera reuses its last shore reflection. During a walk the
  // reflection is projected from its last camera and refreshed at most 10 Hz.
  let mirrorDirty=true,forceMirror=true,lastMirror=-Infinity,mirrorUpdates=0;
  let disposed=false,loadedMaterials=0;
  const renderMirror=surface.onBeforeRender;
  surface.onBeforeRender=function(renderer,scene,camera){
    const now=surface.material.uniforms.time.value;
    if(!mirrorDirty||(!forceMirror&&now-lastMirror<.10))return;
    this.getReflectionCamera(camera).layers.disable(1);
    renderMirror.call(this,renderer,scene,camera);mirrorDirty=false;forceMirror=false;lastMirror=now;mirrorUpdates++;
  };
  // Each location needs just its soil and stone scans. A missing/invalid
  // resource leaves the procedural material visible. Navigation invalidates
  // pending image loads before they can mutate a replacement location.
  if(assetUrl){
    if(ridge){
      let pending;
      pending=new THREE.TextureLoader().load(assetUrl('shudu-ridge.webp'),texture=>{
        if(disposed){texture.dispose();return;}
        texture.colorSpace=THREE.SRGBColorSpace;
        ridge.setTexture(texture);loadedMaterials++;mirrorDirty=true;forceMirror=true;
      },undefined,()=>pending?.dispose());
    }
    const soil=ocean?'coastal-rock.jpg':river?'river-pebbles.jpg':'forest-ground.jpg';
    for(const name of new Set([soil,'coastal-rock.jpg'])){
      let texture;
      texture=new THREE.TextureLoader().load(assetUrl(name),scan=>{
        if(disposed){scan.dispose();return;}
        scan.colorSpace=THREE.SRGBColorSpace;scan.wrapS=scan.wrapT=THREE.RepeatWrapping;
        scan.anisotropy=4;
        if(name===soil){
          bankMaterial.map=scan;bankMaterial.color.set(0xe5e6dd);bankMaterial.needsUpdate=true;
          groundTexture.dispose();
        }
        if(name==='coastal-rock.jpg'){
          rockMaterial.map=scan;rockMaterial.color.set(0xffffff);rockMaterial.needsUpdate=true;
        }
        loadedMaterials++;mirrorDirty=true;forceMirror=true;
      },undefined,()=>texture?.dispose());
    }
  }
  const grass=group.getObjectByName('bank-vegetation');
  return {group,kind,waterId:water.waterId,
    setConditions(value){
      setWeatherUniforms(uniforms,value);setWeatherUniforms(surface.material.uniforms,value);
      ridge?.setConditions(value);
      if(ocean){
        const day=value?THREE.MathUtils.smoothstep(value.waterLight,.28,.53):0;
        const skyFill=day*(.12+2.95*(value?.cloudCover??0)**1.2);
        coastalSky.value.setRGB(.90*skyFill,.96*skyFill,skyFill);
      }
      if(grass)grass.material.color.setScalar(value?.waterLight??1);
      mirrorDirty=true;
    },
    refreshMirror(immediate=false){mirrorDirty=true;forceMirror=forceMirror||immediate;},
    mirrorUpdates(){return mirrorUpdates;},
    loadedMaterials(){return loadedMaterials;},
    lure(mesh){
      const v=surface.material.uniforms.spoon.value;
      if(mesh&&mesh.position.y<=sy)v.set(mesh.position.x,mesh.position.y,mesh.position.z,.42+.22*Math.sin(mesh.rotation.z));
      else v.w=0;
    },
    fish(sample){
      const u=surface.material.uniforms;
      if(sample){
        u.fishPos.value.set(sample.xMm/1000,sample.yMm/1000,-sample.zMm/1000);
        u.fishShape.value.set(sample.yaw,sample.length,sample.width,sample.opacity);
      }else u.fishShape.value.w=0;
    },
    animate(time){surface.material.uniforms.time.value=time;},
    impact(point,time){surface.material.uniforms.impact.value.set(point.x,point.z,time);},
    dispose(){disposed=true;surface.getRenderTarget().dispose();disposeGroup(group);}
  };
}

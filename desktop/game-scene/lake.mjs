import * as THREE from 'three';
import { Reflector } from 'three/addons/objects/Reflector.js';
import { disposeGroup } from './water-space.mjs';

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
    if (level <= 2) leaves.push([nextX, nextY, 9 + random() * 12]);
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
      const light = 15 + (1 - px / 380) * 12 + random() * 11;
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
    for (const [x, y, radius] of leaves) foliage(x, y, radius, 78, kind === 1 ? 1.18 : .83);
  }
  const texture = new THREE.CanvasTexture(image); texture.colorSpace = THREE.SRGBColorSpace;
  return texture;
}

const skyShader = {
  uniforms: { horizon: { value:new THREE.Vector3(.92,.81,.62) }, zenith: { value:new THREE.Vector3(.18,.42,.65) } },
  vertexShader: `varying vec3 direction;
    void main(){direction=position;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}`,
  fragmentShader: `varying vec3 direction;uniform vec3 horizon;uniform vec3 zenith;
    float hash(vec2 p){return fract(sin(dot(p,vec2(12.9898,78.233)))*43758.5453);}
    float noise(vec2 p){vec2 i=floor(p),f=fract(p); f=f*f*(3.-2.*f);
      return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+1.),f.x),f.y);}
    void main(){vec3 d=normalize(direction);float h=max(d.y,0.);
      vec3 sky=mix(horizon,zenith,pow(h,.45));
      vec2 p=d.xz/max(.09,h+.12);float clouds=.54*noise(p*2.)+.28*noise(p*4.3)+.13*noise(p*9.1)+.05*noise(p*19.);
      sky=mix(sky,vec3(.92,.91,.85),smoothstep(.47,.74,clouds)*smoothstep(.03,.22,h)*.82);
      float sun=max(dot(d,normalize(vec3(-.47,.23,-1.))),0.);
      sky+=vec3(.64,.38,.13)*pow(sun,22.)+vec3(3.,2.3,1.4)*smoothstep(.9996,.99998,sun);
      gl_FragColor=vec4(sky,1.);
      #include <tonemapping_fragment>
      #include <colorspace_fragment>
    }`
};

const waterShader = {
  name: 'StillwaterSurface',
  uniforms: { tDiffuse: { value: null }, color: { value: null }, textureMatrix: { value: null },
    time: { value: 0 }, impact: { value: new THREE.Vector3(0, 0, -99) }, rough: { value: .65 }, body: {value: new THREE.Color(0x36594e)},
    fishPos:{value:new THREE.Vector3()},fishShape:{value:new THREE.Vector4()} },
  vertexShader: `uniform mat4 textureMatrix;varying vec4 uvMirror;varying vec3 world;
    void main(){uvMirror=textureMatrix*vec4(position,1.);world=(modelMatrix*vec4(position,1.)).xyz;
      gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}`,
  fragmentShader: `uniform sampler2D tDiffuse;uniform float time;uniform float rough;uniform vec3 body;uniform vec3 impact;
    uniform vec3 fishPos;uniform vec4 fishShape;varying vec4 uvMirror;varying vec3 world;
    float oval(vec2 p,vec2 radius){float d=length(p/radius);return 1.-smoothstep(.91,1.06,d);}
    void main(){vec2 p=world.xz;vec3 eye=normalize(cameraPosition-world);
      float amp=clamp(eye.y*4.,.32,1.);
      float ax=cos(p.x*2.2+p.y*.5+time*1.4+sin(p.y*.67))*.022+cos(p.x*7.7-p.y*3.3-time*2.1)*.013*amp;
      float az=cos(p.y*4.7-p.x*.6-time*1.7+sin(p.x*.6+time*.22))*.036+cos(p.y*11.2+p.x*6.+time*2.5)*.018*amp;
      vec2 radial=p-impact.xy;float age=time-impact.z;float r=length(radial);
      if(age>=0.&&age<4.){float ring=sin((r-age*1.12)*24.)*exp(-pow((r-age*1.12)*3.5,2.))*exp(-age*.8);
        vec2 tilt=radial/max(r,.001)*ring*.07;ax+=tilt.x;az+=tilt.y;}
      vec3 normal=normalize(vec3(ax*rough,1.,az*rough));vec2 uv=uvMirror.xy/uvMirror.w;
      vec3 shore=texture2D(tDiffuse,clamp(uv+vec2(ax,az)*rough*(.09+.13*amp),.001,.999)).rgb;
      float fresnel=.09+.89*pow(1.-max(dot(eye,normal),0.),3.7);
      vec3 water=mix(body,shore,fresnel);
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
      float spark=pow(max(dot(normal,normalize(eye+normalize(vec3(-.47,.23,-1.)))),0.),850.);
      water+=vec3(.98,.77,.41)*spark*.83;
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

function terrain(group, width, depth, xCentre, zCentre, surfaceY, height, far, texture=null, tint=0x837d5d) {
  const g = new THREE.PlaneGeometry(width, depth, far ? 76 : 48, far ? 23 : 12);
  g.rotateX(-Math.PI / 2);
  const p = g.attributes.position, colors = [], c = new THREE.Color();
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i) + xCentre, z = p.getZ(i) + zCentre, y = height(x,z);
    p.setXYZ(i, x, y + surfaceY, z);
    c.set(far ? 0x637054 : tint).multiplyScalar(.81 + .13 * Math.sin(x * .47 + z * .85) + .08 * Math.cos(x * 1.71 - z * .29));
    colors.push(c.r, c.g, c.b);
    if(texture)g.attributes.uv.setXY(i,x*.47,z*.47);
  }
  g.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3)); g.computeVertexNormals();
  group.add(new THREE.Mesh(g, new THREE.MeshLambertMaterial({ vertexColors: true, map:texture })));
}

function bedMesh(water) {
  const pos = [], index = [], min = water.waterBounds.minXmm / 1000, max = water.waterBounds.maxXmm / 1000;
  for (const [i, k] of water.bed.entries()) {
    const y = (water.surfaceYmm - k.depthMm) / 1000, z = -k.zMm / 1000;
    pos.push(min, y, z, max, y, z);
    if (i) { const n = i * 2; index.push(n - 2, n - 1, n, n, n - 1, n + 1); }
  }
  const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3)); g.setIndex(index); g.computeVertexNormals();
  const bed = new THREE.Mesh(g, new THREE.MeshLambertMaterial({ color: 0x474938 })); bed.name = 'surveyed-bed'; return bed;
}

function distantTrees(group, random, farZ, cx, width, sy, height, river) {
  const transform = new THREE.Object3D(), color = new THREE.Color();
  for (let kind = 0; kind < 3; kind++) {
    const geom = new THREE.PlaneGeometry(1,1).translate(0,.5,0);
    const mat = new THREE.MeshLambertMaterial({ map: treeTexture(kind), alphaTest: .43, side: THREE.DoubleSide });
    const count = river ? (kind===2?22:73) : (kind===2?132:114);
    const mesh = new THREE.InstancedMesh(geom, mat, count);
    for (let i = 0; i < count; i++) {
      const x = cx + (random() - .5) * Math.max(158, width * 2.35), z = farZ - .6 - random() * 30;
      const h = (6 + random() * 7.9) * (kind === 1 ? .87 : 1) * (river?.72:1);
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

function nearShore(group, random, anchorX, nearZ, sy, waterMinX, waterMaxX, kind) {
  const p = [], c = [], t = new THREE.Color(), dryGrass=new THREE.Color(0x9c9354);
  const grassCount=kind==='sea'?0:kind==='river'?1300:2200;
  for (let i = 0; i < grassCount; i++) {
    const side = i % 2 ? -1 : 1;
    const offset = side * (1.8 + Math.pow(random(), 1.4) * 14);
    const x = anchorX + offset;
    if (x < waterMinX || x > waterMaxX) continue;
    const z = nearZ + (random() - .38) * 2 + .28 * Math.sin(offset * 1.5);
    const h = (.23 + random() * .72) * Math.min(1, Math.abs(offset) / 3) * (kind==='river'?.59:1);
    const r = .007 + random() * .024, lean = (random() - .36) * h * .49, y = sy - .047;
    p.push(x-r,y,z,x+r,y,z,x+lean*.35+r*.4,y+h*.65,z-.02,
      x-r,y,z,x+lean*.35+r*.4,y+h*.65,z-.02,x+lean*.35-r*.45,y+h*.65,z-.02,
      x+lean*.35-r*.45,y+h*.65,z-.02,x+lean*.35+r*.4,y+h*.65,z-.02,x+lean,y+h,z+.04);
    t.set(0x52623c).lerp(dryGrass,random()*.8);
    for (const shade of [.37,.49,.85,.37,.85,.76,.76,.85,1.09]) c.push(t.r*shade,t.g*shade,t.b*shade);
  }
  const g = new THREE.BufferGeometry(); g.setAttribute('position',new THREE.Float32BufferAttribute(p,3)); g.setAttribute('color',new THREE.Float32BufferAttribute(c,3));
  const grass = new THREE.Mesh(g,new THREE.MeshBasicMaterial({vertexColors:true,side:THREE.DoubleSide}));grass.name='bank-vegetation';group.add(grass);
  const tform = new THREE.Object3D();
  const rock=new THREE.IcosahedronGeometry(1,2), points=rock.attributes.position;
  for(let i=0;i<points.count;i++){
    const x=points.getX(i),y=points.getY(i),z=points.getZ(i),scale=1+.09*Math.sin(4*x+3*z)+.043*Math.cos(5*y-3*x);
    points.setXYZ(i,x*scale,y*scale,z*scale);
    rock.attributes.normal.setXYZ(i,x,y,z);
  }
  const stones = new THREE.InstancedMesh(rock,new THREE.MeshLambertMaterial({color:0xffffff}),kind==='sea'?107:64);
  stones.name=kind==='sea'?'tidal-rocks':'shore-pebbles';
  for (let i = 0; i < stones.count; i++) {
    const x = anchorX + (random() - .5) * 30, z = nearZ + (random() - .2) * 2.3;
    const s = .02 + random() ** 2 * (kind==='sea'?.88:kind==='river'?.12:.19);
    tform.position.set(x,sy+.025+Math.max(0,z-nearZ)*.05,z);tform.rotation.set(random()*.3,random()*6,random()*.15);
    tform.scale.set(s*(1.27+random()*.5),s*(kind==='sea'?.55:.47),s);tform.updateMatrix();stones.setMatrixAt(i,tform.matrix);
    t.set(kind==='sea'?0x536064:0x77786c).multiplyScalar(.53+random()*.63);stones.setColorAt(i,t);
  }
  group.add(stones);
}

export function createLake(water) {
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
    return -.09 + Math.min(inland/5,1)*(.42+.16*Math.sin(px*.22)) + inland*(.12+.08*Math.sin(px*.086+inland*.021)+.045*Math.cos(px*.15-inland*.057))*(river?1.9:1);
  };
  const groundTexture=earthTexture(random,kind);
  const bankColor=ocean?0x666b6b:river?0xa29475:0x77694e;
  terrain(group,width+3,16,x,near+8,sy,(px,pz)=>-.025+(pz-near)*.053+.05*Math.sin(px*.89)*Math.min(1,(pz-near)),false,groundTexture,bankColor);
  if(!ocean){
    terrain(group,Math.max(300,width*3.6),100,x,far-50,sy,farHeight,true);
    for (const side of [-1,1]) {
      const edge = side===1?b.maxXmm/1000:b.minXmm/1000;
      terrain(group,18,depth,x+(width/2+9)*side,(near+far)/2,sy,(px,pz)=>Math.abs(px-edge)*(.18+.055*Math.cos(pz*.28))-.03,false,groundTexture,bankColor);
    }
    distantTrees(group,random,far,x,width,sy,farHeight,river);
  }else{
    // These distant rocks stand beyond the surveyed rectangle. They and the
    // horizon are scenic only. Input still intersects the surveyed surface.
    const outcropMaterial=new THREE.MeshLambertMaterial({color:0x728184});
    for(let i=0;i<7;i++){
      const rock=new THREE.Mesh(new THREE.IcosahedronGeometry(1,2),outcropMaterial);
      rock.position.set(x+width*.7+i*4,sy-1.15,far-38-random()*7);
      rock.scale.set(5+random()*5,1.5+random()*4.2,5+random()*3);
      rock.rotation.y=random()*4;group.add(rock);
    }
  }
  nearShore(group,random,water.anchor.xMm/1000,near,sy,b.minXmm/1000,b.maxXmm/1000,kind);
  group.add(bedMesh(water));
  // Extend only the visual ocean past its playable rectangle. The two meshes
  // share a reflection transform computed for the physical water plane.
  const surface = new Reflector(new THREE.PlaneGeometry(width,depth),{shader:waterShader,textureWidth:768,textureHeight:384,multisample:0,clipBias:.003});
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
  // The camera and shore are fixed at this spot. Reuse the reflection until
  // the projection changes. Waves still sample moving normals on every frame.
  let mirrorDirty=true;
  const renderMirror=surface.onBeforeRender;
  surface.onBeforeRender=function(renderer,scene,camera){
    if(!mirrorDirty)return;
    this.getReflectionCamera(camera).layers.disable(1);
    renderMirror.call(this,renderer,scene,camera); mirrorDirty=false;
  };
  return {group,kind, refreshMirror(){mirrorDirty=true;},
    fish(sample){
      const u=surface.material.uniforms;
      if(sample){
        u.fishPos.value.set(sample.xMm/1000,sample.yMm/1000,-sample.zMm/1000);
        u.fishShape.value.set(sample.yaw,sample.length,sample.width,sample.opacity);
      }else u.fishShape.value.w=0;
    },
    animate(time){surface.material.uniforms.time.value=time;},
    impact(point,time){surface.material.uniforms.impact.value.set(point.x,point.z,time);},
    dispose(){surface.getRenderTarget().dispose();disposeGroup(group);}
  };
}

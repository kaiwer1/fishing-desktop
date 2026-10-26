import * as THREE from 'three';
// Environment geometry/shaders copied from this project's existing src/main.js.
// No fishing session, UI, input, inventory or save code is included.
export function createLakeEnvironment() {
  const scene = new THREE.Scene();
  scene.fog = new THREE.FogExp2(0xbacbc7, 0.0036);
  const camera = new THREE.PerspectiveCamera(49, 1, 0.1, 550);
  camera.position.set(0, 3.35, 12);
  camera.lookAt(0, 1.2, -42);
  const ambient = new THREE.HemisphereLight(0xd3e4e7, 0x3b4839, 2.15);
  scene.add(ambient);
  const sun = new THREE.DirectionalLight(0xffe2ae, 2.5);
  sun.position.set(-70, 50, -100);
  scene.add(sun);
  const sunDirection = sun.position.clone().normalize();

  const sky = new THREE.Mesh(new THREE.SphereGeometry(450, 32, 20), new THREE.ShaderMaterial({
    side: THREE.BackSide, depthWrite: false,
    uniforms: { sunDirection: { value: sunDirection }, daylight: { value: 1 } },
    vertexShader: 'varying vec3 vDirection; void main(){ vDirection=position; gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0); }',
    fragmentShader: `varying vec3 vDirection; uniform vec3 sunDirection; uniform float daylight;
      void main(){ vec3 d=normalize(vDirection); float h=max(d.y,0.0);
        vec3 color=mix(vec3(.75,.81,.79),vec3(.29,.48,.61),pow(h,.38));
        float glow=pow(max(dot(d,sunDirection),0.0),9.0);
        color+=vec3(.3,.22,.12)*glow;
        color+=vec3(1.5,1.2,.7)*smoothstep(.9994,.99975,dot(d,sunDirection));
        gl_FragColor=vec4(color*(.2+.8*daylight),1.0);
        #include <tonemapping_fragment>
        #include <colorspace_fragment>
      }`,
  }));
  scene.add(sky);

  const waterMaterial = new THREE.ShaderMaterial({
    uniforms: { time: { value: 0 }, detail: { value: 0.75 }, daylight: { value: 1 }, waterTint: { value: new THREE.Color(0x244f43) }, sunDirection: { value: sunDirection }, ripple: { value: new THREE.Vector3(1.4, 0, -6) }, rippleStart: { value: -100 } },
    vertexShader: `uniform float time; varying vec3 vWorld;
      void main(){ vec3 p=position; p.z+=sin(p.x*.42+time*.6)*.025+sin(p.y*.33+time*.43)*.022;
        vWorld=(modelMatrix*vec4(p,1.0)).xyz;
        gl_Position=projectionMatrix*viewMatrix*vec4(vWorld,1.0); }`,
    fragmentShader: `uniform float time; uniform float detail; uniform float daylight; uniform vec3 sunDirection; uniform vec3 waterTint;
      uniform vec3 ripple; uniform float rippleStart; varying vec3 vWorld;
      float waves(vec2 p){ return sin(p.x*1.3+sin(p.y*.7)+time*.9)*.22
        +sin(p.y*2.7-p.x*.8+time*1.1)*.12+sin(p.x*6.2+p.y*3.7-time*1.4)*.04*detail; }
      void main(){ vec2 p=vWorld.xz; float e=.035;
        vec3 N=normalize(vec3((waves(p-vec2(e,0.))-waves(p+vec2(e,0.)))*.9,1.,(waves(p-vec2(0.,e))-waves(p+vec2(0.,e)))*.9));
        float age=time-rippleStart; float r=length(p-ripple.xz);
        float ring=sin((r-age*1.4)*20.)*exp(-pow(r-age*1.4,2.)*5.)*exp(-max(age,0.)*.55);
        if(age>0. && age<12.) N=normalize(N+vec3(ring*.03,0.,ring*.02));
        vec3 V=normalize(cameraPosition-vWorld); vec3 R=reflect(-V,N);
        float fresnel=.04+.96*pow(1.-max(dot(V,N),0.),4.);
        vec3 reflected=mix(vec3(.73,.79,.74),vec3(.3,.49,.59),pow(max(R.y,0.),.45));
        vec3 body=mix(waterTint*.32,waterTint,clamp(V.y,0.,1.));
        vec3 color=mix(body,reflected,fresnel*.85+.16);
        float sparkle=pow(max(dot(reflect(-sunDirection,N),V),0.),180.);
        color+=vec3(1.,.77,.4)*sparkle*.8;
        float mist=1.-exp(-length(cameraPosition-vWorld)*.004);
        color=mix(color,vec3(.67,.76,.71),mist*.63);
        gl_FragColor=vec4(color*(.22+.78*daylight),1.);
        #include <tonemapping_fragment>
        #include <colorspace_fragment>
      }`,
  });
  const water = new THREE.Mesh(new THREE.PlaneGeometry(450, 400, 100, 80), waterMaterial);
  water.rotation.x = -Math.PI / 2;
  water.position.z = -100;
  scene.add(water);

  const terrain = new THREE.PlaneGeometry(340, 120, 110, 48);
  terrain.rotateX(-Math.PI / 2);
  const points = terrain.attributes.position;
  for (let i=0; i<points.count; i++) {
    const x=points.getX(i), z=points.getZ(i);
    const inland=Math.max(0,(-z+50)/100);
    const hills=7+9*Math.sin(x*.028+1.7)+5*Math.cos(x*.059);
    points.setY(i,Math.max(-1, hills*inland+Math.sin(x*.14+z*.07)*1.5));
  }
  terrain.computeVertexNormals();
  const bank = new THREE.Mesh(terrain,new THREE.MeshStandardMaterial({color:0x536854,roughness:1}));
  bank.position.z=-125;
  scene.add(bank);
  const treeMaterial=new THREE.MeshStandardMaterial({color:0x2c4b3d,roughness:1});
  const trees=new THREE.InstancedMesh(new THREE.ConeGeometry(2.4,8,10),treeMaterial,300);
  const treeTransform=new THREE.Object3D();
  for(let i=0;i<150;i++) {
    const r=random(i+12), x=(random(i+2)-.5)*310, z=-93-random(i+180)*65;
    const scale=.65+random(i+60)*1.25;
    const ground=2+Math.max(0,(-z-70)/100)*(7+9*Math.sin(x*.028+1.7)+5*Math.cos(x*.059));
    for(let layer=0;layer<2;layer++) {
      treeTransform.position.set(x,ground+scale*(3+layer*2),z);
      treeTransform.scale.setScalar(scale*(layer? .73:1));
      treeTransform.rotation.y=r*Math.PI;
      treeTransform.updateMatrix(); trees.setMatrixAt(i*2+layer,treeTransform.matrix);
    }
  }
  trees.instanceMatrix.needsUpdate=true;
  scene.add(trees);

  const riverBanks = new THREE.Group();
  for (const x of [-48, 48]) {
    const shore = new THREE.Mesh(new THREE.SphereGeometry(1, 28, 16), new THREE.MeshStandardMaterial({ color: 0x64755b, roughness: 1 }));
    shore.position.set(x, -1.2, -48); shore.scale.set(24, 4.3, 75); riverBanks.add(shore);
  }
  scene.add(riverBanks);
  const reeds = new THREE.InstancedMesh(new THREE.ConeGeometry(.065, 2.7, 5), new THREE.MeshStandardMaterial({ color: 0x7f8450, roughness: 1 }), 220);
  for(let i=0;i<220;i++) {
    treeTransform.position.set((i%2?1:-1)*(10+random(i+11)*13),1,-5-random(i+31)*34);
    treeTransform.scale.set(1,.6+random(i+92)*.7,1); treeTransform.rotation.set(0,0,(random(i+34)-.5)*.2);
    treeTransform.updateMatrix(); reeds.setMatrixAt(i,treeTransform.matrix);
  }
  reeds.instanceMatrix.needsUpdate=true; scene.add(reeds);


  riverBanks.visible=false;reeds.visible=false;bank.position.z=-125;
  return {scene,camera,waterMaterial,dispose(){
    const geometries=new Set(),materials=new Set();scene.traverse(item=>{if(item.geometry)geometries.add(item.geometry);if(item.material)for(const material of [item.material].flat())materials.add(material);});
    for(const geometry of geometries)geometry.dispose();for(const material of materials)material.dispose();scene.clear();
  }};
}
function random(seed){const v=Math.sin(seed*127.1+311.7)*43758.5453123;return v-Math.floor(v);}

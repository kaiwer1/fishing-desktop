import {specimens} from './specimen-model-data.mjs';

const tau=2*Math.PI, lerp=(a,b,t)=>a+(b-a)*t;
const clamp=(a,lo=0,hi=1)=>Math.min(hi,Math.max(lo,a));
const smooth=(lo,hi,x)=>{const t=clamp((x-lo)/(hi-lo));return t*t*(3-2*t);};
const fract=n=>n-Math.floor(n);
const rgb=n=>[(n>>16)&255,(n>>8)&255,n&255];
const knots=[0,.07,.16,.27,.40,.53,.64,.735,.825,.90,.965,1];
const tailX=-.308, length=.808;
function contour(p,t){
  t=clamp(t);let i=0;while(i+2<knots.length&&t>knots[i+1])i++;
  const j=i+1,a=p.profile[i],b=p.profile[j],dx=knots[j]-knots[i],q=(t-knots[i])/dx;
  // Local Hermite tangents flatten at extrema and preserve a smooth peduncle.
  const slope=k=>{
    if(k===0)return (p.profile[1]-p.profile[0])/(knots[1]-knots[0]);
    if(k===knots.length-1)return (p.profile[k]-p.profile[k-1])/(knots[k]-knots[k-1]);
    const l=(p.profile[k]-p.profile[k-1])/(knots[k]-knots[k-1]);
    const r=(p.profile[k+1]-p.profile[k])/(knots[k+1]-knots[k]);return l*r<=0?0:2*l*r/(l+r);
  };
  return lerp(a,b,q*q*(3-2*q))+dx*q*(1-q)*((1-q)*slope(i)-q*slope(j));
}
function section(p,t){
  const h=contour(p,t),cheek=smooth(.56,.80,t)*(1-smooth(.845,1,t));
  return {
    x:tailX+t*length,
    y:p.hump*Math.sin(Math.PI*t)**2+p.noseY*smooth(.72,1,t),
    z:-.025*(1-t)**3+.006*Math.sin(t*tau),
    up:p.depth*h*.53, down:p.depth*h*(p.spots ? .46 : .47),
    width:p.width*.5*(h**.84)*(.91+.08*Math.sin(t*Math.PI)+.12*cheek)*(.22+.78*smooth(0,.065,t))
  };
}
function pointAt(p,t,yFraction,side){
  const s=section(p,t);return [s.x+jawOffset(p,t,yFraction),s.y+(yFraction>=0?s.up:s.down)*yFraction,
    s.z+side*s.width*Math.sqrt(Math.max(0,1-yFraction*yFraction))];
}
function jawOffset(p,t,vertical){
  return (p.spots ? .029 : p.whiskers ? .009 : 0)*smooth(.87,1,t)*Math.max(0,-vertical);
}
function hash(i,j){
  let h=Math.imul(i+17,374761393)+Math.imul(j+29,668265263);
  h=Math.imul(h^(h>>>13),1274126177);return ((h^(h>>>16))>>>0)/4294967296;
}
function skin(THREE,p){
  const w=512,h=256,colour=new Uint8Array(w*h*4),relief=new Uint8Array(w*h*4);
  const [back,flank,belly]=p.colors.map(rgb);
  const columns=Array.from({length:w},(_,x)=>{
    const u=(x+.5)/w;return {u,shape:contour(p,u),scale:smooth(.012,.09,u)*(1-smooth(p.head-.018,p.head+.025,u))};
  });
  for(let y=0;y<h;y++){
    const theta=tau*(y+.5)/h-Math.PI/2,vert=Math.sin(theta),side=Math.cos(theta);
    const upper=smooth(.12,.95,vert),lower=smooth(.12,.91,-vert);
    for(let x=0;x<w;x++){
      const {u,shape,scale}=columns[x],i=(y*w+x)*4;
      const rows=vert*shape*(p.scales*.27),row=Math.floor(rows),v=fract(rows)-.5;
      const scaleX=u*p.scales+(row&1)*.5,cell=Math.floor(scaleX);
      const arc=.31-.49*Math.sqrt(Math.max(0,1-(v/.59)**2)),dist=fract(scaleX-arc+.5)-.5;
      const edge=Math.exp(-dist*dist*160),glint=Math.exp(-125*(dist-.095)**2);
      const poreY=.007+.108*u-.065*Math.sin(Math.PI*u)+(p.spots ? .105*smooth(.35,.78,u) : 0);
      const pore=Math.exp(-1920*((vert-poreY)*shape)**2)*scale;
      const scaleShade=(glint*9-edge*16+(hash(cell,row)-.5)*12)*scale;
      let pigment=0;
      if(p.spots&&u>.06&&u<.84&&vert>-.12){
        const a=u*26,b=(vert+.12)*4.1+.19*Math.sin(u*18),ix=Math.floor(a),iy=Math.floor(b),s=side>0?3:29;
        const dx=fract(a)-(.2+.60*hash(ix+s,iy+31)),dy=fract(b)-(.18+.64*hash(ix+17,iy+s));
        const radius=.072+.14*hash(ix+s+8,iy);
        if(hash(ix+s,iy+12)>.18)pigment-=74*(1-smooth(radius*.50,radius,Math.hypot(dx,dy*1.15)))*smooth(-.12,.06,vert);
      }
      if(p.bars){
        const bar=Math.pow(.5+.5*Math.cos(u*tau*7.15+vert*.83+.16*Math.sin(vert*7)),3);
        const darkScaleRow=Math.pow(.5+.5*Math.cos(tau*(rows+.12*Math.sin(u*9))),5);
        pigment-=32*bar*smooth(-.68,.52,vert)*scale;
        pigment-=11*darkScaleRow*scale*(1-smooth(.78,1,Math.abs(vert)));
      }
      if(p.bars||p.yellow){
        const spotX=(u-.69)/.017,spotY=(vert-.18)/.10;pigment-=33*Math.exp(-spotX*spotX-spotY*spotY);
      }
      const tiny=(hash(x,y)-.5)*4, line=pore*(6+12*edge);
      const cheekMask=smooth(p.head-.03,p.head+.06,u)*(1-smooth(.94,1,u));
      const cheekArc=vert+.23-2.1*(u-p.head);
      const cheekSheen=cheekMask*(14*Math.exp(-16*cheekArc*cheekArc)-9*Math.exp(-48*(cheekArc-.32)**2));
      const lineOfPores=(1-smooth(.006,.044,Math.abs(cheekArc+.16)))*cheekMask;
      const tinyPore=(1-smooth(.03,.16,Math.abs(fract(u*76)-.5)))*lineOfPores;
      pigment-=17*tinyPore;
      const faceShade=(1-scale)*(2.8*Math.cos(vert*11+u*5)-1.8)+cheekSheen;
      for(let k=0;k<3;k++){
        const base=lerp(lerp(flank[k],back[k],upper),belly[k],lower);
        colour[i+k]=clamp(Math.round(base+scaleShade+pigment-line+tiny+faceShade),0,255);
        relief[i+k]=clamp(Math.round(136+scale*(28*glint-46*edge)-pore*9+tiny),0,255);
      }
      colour[i+3]=relief[i+3]=255;
    }
  }
  function texture(data,albedo){
    const tex=new THREE.DataTexture(data,w,h,THREE.RGBAFormat);
    tex.colorSpace=albedo?THREE.SRGBColorSpace:THREE.NoColorSpace;
    tex.magFilter=THREE.LinearFilter;tex.minFilter=THREE.LinearMipmapLinearFilter;
    tex.generateMipmaps=true;tex.needsUpdate=true;return tex;
  }
  return new THREE.MeshStandardMaterial({map:texture(colour,true),bumpMap:texture(relief,false),bumpScale:.00155,
    roughness:.43,metalness:.18});
}
function geometry(THREE,positions,indices,uvs,colours){
  const g=new THREE.BufferGeometry();
  g.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));
  if(uvs)g.setAttribute('uv',new THREE.Float32BufferAttribute(uvs,2));
  if(colours)g.setAttribute('color',new THREE.Float32BufferAttribute(colours,3));
  g.setIndex(indices);g.computeVertexNormals();return g;
}
function body(THREE,p){
  const positions=[],uvs=[],indices=[],columns=72,rings=56;
  for(let i=0;i<=columns;i++){
    const t=i/columns,s=section(p,t);
    for(let j=0;j<=rings;j++){
      const q=j/rings,a=tau*q-Math.PI/2,sy=Math.sin(a);
      positions.push(s.x+jawOffset(p,t,sy),s.y+sy*(sy<0?s.down:s.up),s.z+s.width*Math.cos(a));uvs.push(t,q);
      if(i<columns&&j<rings){
        const k=i*(rings+1)+j,n=k+rings+1;indices.push(k,n,k+1,n,n+1,k+1);
      }
    }
  }
  for(const i of [0,columns]){
    const s=section(p,i/columns),mid=positions.length/3;
    positions.push(s.x,s.y,s.z);uvs.push(i/columns,.25);
    for(let j=0;j<rings;j++){const a=i*(rings+1)+j;indices.push(mid,i===0?a:a+1,i===0?a+1:a);}
  }
  const g=geometry(THREE,positions,indices,uvs);
  // Average the duplicated texture seam normals on the underside.
  const normals=g.attributes.normal,normal=new THREE.Vector3(),second=new THREE.Vector3();
  for(let i=0;i<=columns;i++){
    const k=i*(rings+1);normal.fromBufferAttribute(normals,k).add(second.fromBufferAttribute(normals,k+rings)).normalize();
    normals.setXYZ(k,normal.x,normal.y,normal.z);normals.setXYZ(k+rings,normal.x,normal.y,normal.z);
  }
  return new THREE.Mesh(g,skin(THREE,p));
}
function fins(THREE,p){
  const positions=[],colours=[],triangles=[];
  function fan(count,baseColour,curves){
    const c=new THREE.Color(baseColour),start=positions.length/3,segments=count*4,rows=4;
    for(let i=0;i<=segments;i++){
      const u=i/segments,rib=i%4===0,scallop=.5-.5*Math.cos(u*count*tau);
      const [a,b]=curves(u,scallop);
      for(let j=0;j<=rows;j++){
        const t=j/rows;positions.push(lerp(a[0],b[0],t),lerp(a[1],b[1],t),
          lerp(a[2],b[2],t)+.0017*Math.sin(Math.PI*t)*Math.sin(Math.PI*u));
        const tone=.67+.28*t+(rib ? .19 : -.07*scallop)+.035*Math.sin(11*u);
        colours.push(c.r*tone,c.g*tone,c.b*tone);
        if(i<segments&&j<rows){const k=start+i*(rows+1)+j,n=k+rows+1;triangles.push(k,n,k+1,n,n+1,k+1);}
      }
    }
  }
  function median(spec,up){
    const [front,back,height,rays,spiny]=spec;
    fan(rays,up?p.fin:p.ventral,(u,scallop)=>{
      const t=lerp(front,back,u),s=section(p,t),sgn=up?1:-1;
      const base=s.y+sgn*(up?s.up:s.down)*.973;
      let profile=(smooth(0,spiny ? .20 : .11,u)*(1-.60*u)+(spiny ? .18 : .12)*(1-u))*(1-.63*smooth(.78,1,u));
      const joined=up&&p.dorsal.length===2&&!p.spots;
      if(joined)profile=spiny?lerp(profile,.36,smooth(.73,1,u)):lerp(.58,profile,smooth(0,.16,u));
      if(p.bars&&spiny)profile*=1-.38*smooth(.35,.94,u);
      if(p.spots&&up&&!spiny)profile=.06+.88*Math.sin(Math.PI*u)**.76;
      const notch=spiny ? (p.bars ? .43 : .30) : .065;
      const rise=height*(profile*(1-notch*scallop)+.06);
      const sweep=joined ? .026 : .018+.029*u;
      return [[s.x,base,s.z],[s.x-sweep,base+sgn*rise,s.z-.003*Math.sin(u*Math.PI)]];
    });
  }
  for(const d of p.dorsal)median(d,true);median(p.anal,false);
  fan(25,p.yellow?0xa99a52:p.fin,(u,scallop)=>{
    const v=u*2-1,s=section(p,.023),lobe=smooth(0,.87,Math.abs(v));
    const reach=.209*(1-.77*p.fork*(1-lobe))*(1-.055*smooth(.80,1,Math.abs(v))-.017*scallop);
    return [[s.x,s.y+v*(v>0?s.up:s.down)*.90,s.z],
      [s.x-reach,s.y+Math.sign(v)*Math.abs(v)**.93*p.tail,s.z-.031*lobe-.009*v]];
  });
  function paired(rootT,yFrac,reach,colour,pelvic){
    for(const side of [-1,1])fan(pelvic?9:15,colour,(u,scallop)=>{
      const a=pointAt(p,rootT-.009*u,yFrac-.12*u,side);
      const angle=(pelvic ? .29 : p.bars ? -.03 : .13)+u*(p.bars&&!pelvic ? .49 : .70);
      const fanLength=reach*(.82+.18*Math.sin(Math.PI*u))*(1-.49*u*u)*(1-.025*scallop);
      return [a,[a[0]-fanLength*Math.cos(angle),a[1]-fanLength*Math.sin(angle),
        a[2]+side*(pelvic ? .035 : .045)*(.50+.50*Math.sin(Math.PI*(.28+.59*u)))]];
    });
  }
  paired(p.chest,-.04,p.pectoral,p.fin,false);paired(p.pelvis,-.77,.106,p.ventral,true);
  return new THREE.Mesh(geometry(THREE,positions,triangles,null,colours),new THREE.MeshStandardMaterial({
    color:0xffffff,vertexColors:true,side:THREE.DoubleSide,roughness:.58,metalness:.06}));
}
function stroke(THREE,points,radius,material,taper=false){
  const curve=new THREE.CatmullRomCurve3(points.map(v=>new THREE.Vector3(...v)));
  const g=new THREE.TubeGeometry(curve,15,radius,6,false);
  if(taper){
    const pos=g.attributes.position,vert=new THREE.Vector3();
    for(let i=0;i<=15;i++){
      const c=curve.getPointAt(i/15),scale=1-.86*(i/15)**.8;
      for(let j=0;j<=6;j++){const k=i*7+j;vert.fromBufferAttribute(pos,k).sub(c).multiplyScalar(scale).add(c);pos.setXYZ(k,vert.x,vert.y,vert.z);}
    }
    g.computeVertexNormals();
  }
  return new THREE.Mesh(g,material);
}
function face(THREE,p,group){
  const standard=(color,roughness=.48,metalness=.10)=>new THREE.MeshStandardMaterial({color,roughness,metalness});
  const bone=standard(p.colors[2]),crease=standard(p.colors[0],.72),mouth=standard(0x5d5043,.70),
    rim=standard(p.colors[1]),iris=standard(p.iris,.30,.26),pupil=standard(0x121916,.16,.08);
  const globe=new THREE.SphereGeometry(1,20,14);
  function lens(name,a,r,d,mat,side){
    const mesh=new THREE.Mesh(globe,mat);mesh.position.set(...a);mesh.scale.set(r,r*.96,d);mesh.name=name+(side>0?'-left':'-right');group.add(mesh);return mesh;
  }
  for(const side of [-1,1]){
    const sidePoint=(q,v,offset=0)=>{const v3=pointAt(p,q,v,side);v3[2]+=side*offset;return v3;};
    const edge=(name,coords,r,mat,offset=.0009)=>{
      const mesh=stroke(THREE,coords.map(([q,v])=>sidePoint(q,v,offset)),r,mat);mesh.name=name;group.add(mesh);
    };
    const h=p.head;
    edge('opercular-rim',[[h+.055,.68],[h+.015,.45],[h-.012,.17],[h-.020,-.13],[h+.002,-.37],[h+.044,-.58]],.00135,crease);
    edge('opercular-reflection',[[h+.026,.43],[h-.001,.16],[h-.009,-.12],[h+.012,-.32]],.00115,bone,.0011);
    const gape=p.spots ? .785 : .906;
    if(p.spots){
      edge('jaw-opening',[[gape,-.18],[.818,-.34],[.869,-.42],[.935,-.24],[.973,-.02],[.999,.16]],.0029,mouth);
      edge('lower-lip',[[.80,-.29],[.868,-.50],[.935,-.35],[.976,-.14],[.998,.035]],.0024,bone);
    }else{
      edge('jaw-opening',[[gape,-.22],[gape+.035,-.26],[.969,-.19],[.989,-.09],[.999,.01]],.00185,mouth);
      edge('lower-lip',[[gape+.027,-.31],[.965,-.31],[.989,-.20],[.999,-.03]],.00165,rim);
    }
    if(p.spots||p.bars){
      edge('preopercular-fold',[[h+.085,.29],[h+.057,.03],[h+.045,-.22],[h+.075,-.45],[h+.108,-.48]],.00125,rim);
      edge('cheek-plate',[[h+.05,-.42],[h+.08,-.56],[h+.125,-.62],[h+.155,-.52]],.0015,bone);
    }
    const a=sidePoint(p.eye,p.spots ? .40 : .34),r=p.eyeSize;
    lens('orbital-rim',a,r*1.20,r*.27,rim,side);
    const b=[a[0],a[1],a[2]+side*r*.24];
    lens('iris',b,r,r*.32,iris,side);
    lens('pupil',[b[0]+r*.025,b[1],b[2]+side*r*.265],r*.62,r*.16,pupil,side);
    lens('nostril',sidePoint(.954,.34,.0005),.0034,.0015,crease,side);
    if(p.whiskers){
      for(const [t,size] of [[.958,.060],[.987,.032]]){
        const a=sidePoint(t,-.28),points=[a,[a[0]-.007,a[1]-size*.28,a[2]+side*.007],
          [a[0]-.018,a[1]-size*.76,a[2]+side*.017],[a[0]-.032,a[1]-size,a[2]+side*.024]];
        const whisker=stroke(THREE,points,.0028,mouth,true);whisker.name='barbel';group.add(whisker);
      }
    }
  }
}

export function createSpecimen(THREE,speciesId){
  if(!Object.hasOwn(specimens,speciesId))return null;
  const p=specimens[speciesId],group=new THREE.Group();
  group.name='specimen-'+speciesId;group.userData.speciesId=speciesId;
  const trunk=body(THREE,p),appendages=fins(THREE,p);
  trunk.name='skin';appendages.name='fins';group.add(trunk,appendages);face(THREE,p,group);
  const centre=new THREE.Box3().setFromObject(group).getCenter(new THREE.Vector3());
  for(const part of group.children)part.position.sub(centre);
  return group;
}

export function disposeSpecimen(group){
  if(!group||group.userData.specimenDisposed)return;
  group.userData.specimenDisposed=true;
  const geometries=new Set(),materials=new Set(),textures=new Set();
  group.traverse(o=>{if(o.isMesh){geometries.add(o.geometry);materials.add(o.material);}});
  for(const m of materials){for(const t of [m.map,m.bumpMap])if(t)textures.add(t);}
  for(const t of textures)t.dispose();
  for(const m of materials)m.dispose();
  for(const g of geometries)g.dispose();
}

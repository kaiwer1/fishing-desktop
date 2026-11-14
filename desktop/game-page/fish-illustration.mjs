// Original lateral studies. Paths are authored in a common 240 × 112 plate,
// with a separate contour, fin arrangement and pigment pattern per species.
const fish = {
  carp: { name:'鲤鱼', colors:['#4d5240','#b09a60','#d9bc89','#91714d'], eye:[40,52], gill:[58,78],
    body:'M17 60Q22 52 29 49C54 26 77 21 111 24S154 40 178 46Q186 49 197 48L199 63Q187 61 179 64C156 71 139 84 103 85Q58 86 32 71L20 68Q14 67 17 60Z',
    tail:'M185 49Q205 46 227 27Q224 45 213 55Q221 65 231 82Q207 75 185 63Z',
    dorsal:'M75 30L86 11L94 18Q119 24 148 31L167 43L138 44Z',
    lower:'M126 77L146 96L153 87L148 74ZM83 77L91 100L105 88L104 79Z', pectoral:'M62 62Q77 61 100 79Q84 80 69 73Z',
    marks:'M17 65Q21 64 27 66M22 69Q19 78 15 78M28 71Q27 75 28 78', texture:'scales' },
  crucian: { name:'鲫鱼', colors:['#555746','#919782','#dbd8b3','#777761'], eye:[38,51], gill:[55,76],
    body:'M18 58C27 51 37 42 46 33C65 16 90 16 111 19C138 23 156 38 174 45Q184 50 197 49L199 62Q185 61 170 68C149 80 133 92 101 92C65 91 43 77 25 67L18 64Z',
    tail:'M186 49Q207 44 227 30Q225 46 212 55Q224 66 228 80Q208 72 185 63Z',
    dorsal:'M72 28Q76 11 87 7L99 13Q123 17 148 31L158 42Z',
    lower:'M123 83L141 99L149 90L145 79ZM80 81L92 101L100 94L102 84Z', pectoral:'M58 62Q71 63 88 81Q70 78 61 70Z',
    marks:'M17 62Q23 63 28 61M78 40Q120 32 162 48', texture:'scales' },
  'grass-carp': { name:'草鱼', colors:['#444f3c','#839273','#d1d0a9','#666e4f'], eye:[40,48], gill:[59,66],
    body:'M13 54Q19 46 31 42Q49 31 79 30C124 29 150 35 177 45Q188 49 199 49L200 62Q185 61 170 66C131 76 98 77 64 73Q31 69 19 63L13 60Z',
    tail:'M188 49Q207 44 229 28Q225 46 214 55L230 80Q207 73 187 62Z',
    dorsal:'M104 33L115 13Q123 21 134 31L145 38Z', lower:'M151 67L169 84L174 77L169 65ZM96 71L111 91L117 82L115 72Z',
    pectoral:'M61 55L92 70Q77 74 62 64Z', marks:'M13 58L27 59M71 37Q122 32 160 42', texture:'large-scales' },
  'blunt-snout-bream': { name:'鳊鱼', colors:['#4f6465','#97a5a2','#dddccc','#7a8781'], eye:[37,51], gill:[56,75],
    body:'M20 60L23 48Q35 47 43 35C62 17 83 12 106 18C132 24 144 40 168 45Q185 50 196 49L198 63Q178 63 158 74Q124 95 87 93Q62 90 45 80L26 68Z',
    tail:'M184 49L228 26Q225 44 212 56L228 83Q207 72 182 64Z',
    dorsal:'M81 23L95 3Q99 13 102 21L117 28Z',
    lower:'M103 88Q130 82 159 71L165 75Q150 86 121 99L109 101ZM68 83L77 101L87 92L84 85Z',
    pectoral:'M58 59Q76 62 100 83Q77 78 63 69Z', marks:'M20 61L29 63M72 42Q117 26 162 48', texture:'fine-scales' },
  'black-seabream': { name:'黑鲷', colors:['#394c50','#778785','#d2d4c4','#4e5c5c'], eye:[39,47], gill:[59,73],
    body:'M16 61L28 47Q39 26 68 22C97 15 126 23 151 35Q175 48 194 47L197 62Q178 63 159 74C136 87 111 91 86 86Q54 82 34 69L20 68Z',
    tail:'M184 48Q208 44 226 31Q224 45 211 56Q223 66 228 80L182 63Z',
    dorsal:'M55 30L57 17L63 24L67 11L72 20L77 7L83 18L89 6L94 17L101 8L105 19L113 10L117 22L125 13L128 25Q145 21 156 31L172 43Z',
    lower:'M119 80L128 97L133 88L137 94Q154 88 159 76L155 73ZM76 78L89 99L98 87L93 81Z',
    pectoral:'M60 59Q78 57 108 79Q86 79 65 68Z', marks:'M16 63L30 63L35 60M45 39L54 34', texture:'bars' },
  'yellowfin-seabream': { name:'黄鳍鲷', colors:['#53635f','#a7aca0','#e0d7b3','#bc9844'], eye:[39,48], gill:[59,71],
    body:'M15 59L27 48C40 30 60 22 82 22Q117 17 151 36Q175 49 195 49L198 63Q177 64 157 74Q123 88 94 85Q55 82 33 69L18 66Z',
    tail:'M184 49Q209 43 229 29Q224 47 212 56Q225 68 230 81Q209 74 184 63Z',
    dorsal:'M54 31L57 17L63 24L67 12L73 21L77 9L83 20L88 8L94 20L100 10L105 22L112 12L117 24L124 16L129 26Q147 23 157 33L170 44Z',
    lower:'M117 80L125 96L131 87L137 93Q154 88 161 74L155 71ZM74 77L85 99L95 86L90 80Z',
    pectoral:'M61 59Q79 58 111 78Q88 79 65 68Z', marks:'M15 62L28 63M47 40L56 34', texture:'gold-lines' },
  'spotted-seabass': { name:'花鲈', colors:['#43575b','#8a9c9c','#dddcca','#738281'], eye:[42,46], gill:[64,68],
    body:'M12 59L24 50Q40 41 51 35Q69 29 91 29C128 29 149 36 177 45L198 49L200 61Q181 61 159 67C126 78 99 79 70 74Q50 71 34 66L13 67L9 64Z',
    tail:'M189 48Q208 46 228 35Q227 48 218 56L230 75Q209 69 188 63Z',
    dorsal:'M64 35L68 20L73 30L79 13L84 27L92 11L97 27L104 14L109 29L115 21L120 32ZM126 34L134 19Q151 20 162 34L175 45Z',
    lower:'M135 71L147 87Q158 85 167 76L162 68ZM74 70L83 88L96 79L91 73Z',
    pectoral:'M64 58L94 74Q76 76 65 65Z', marks:'M10 64Q22 66 44 56L48 52M18 59Q33 58 45 53M53 49L60 41', texture:'spots' },
};

const svgNS = 'http://www.w3.org/2000/svg';
const idFor = s => 'fd-fish-' + s;
const svgNode = (doc, tag, attrs) => {
  const node = doc.createElementNS(svgNS, tag);
  for (const [k,v] of Object.entries(attrs)) node.setAttribute(k,String(v));
  return node;
};
const pointPath = (doc, d, fill, stroke, width = 1) => svgNode(doc, 'path', { d, fill, stroke, 'stroke-width':width,
  'stroke-linecap':'round','stroke-linejoin':'round' });
function pigmentation(doc, s, id) {
  const group = svgNode(doc, 'g', { 'clip-path':`url(#${id}-clip)` });
  if (s.texture === 'bars') {
    for (let n=0;n<6;n++) {
      const x=73+n*18; const stripe = pointPath(doc,`M${x} 19q-8 12 -6 30t5 28q-1 -21 6 -37l6 -19Z`,'#344147','none');
      stripe.setAttribute('opacity','.29'); group.append(stripe);
    }
  }
  if (s.texture === 'spots') {
    for (let row=0;row<3;row++) for (let n=0;n<12-row*2;n++) {
      const x=63+n*10+row*7, y=33+row*7+Math.sin(n*2.1+row)*2.5;
      group.append(svgNode(doc,'ellipse',{cx:x,cy:y,rx:1.3+(n%3)*.45,ry:1.1+(n%4)*.24,fill:'#3f4d50',opacity:'.86'}));
    }
  }
  if (s.texture === 'gold-lines') {
    for (let n=0;n<5;n++) {
      const y=34+n*7; const p=pointPath(doc,`M57 ${y}Q109 ${y-9} 179 ${y+4}`,'none','#9e955d',1.4);
      p.setAttribute('opacity','.56');group.append(p);
    }
    group.append(pointPath(doc,'M182 52Q191 53 198 52L199 57Q189 57 182 58Z','#5d6356','none'));
  }
  const spacing=s.texture==='large-scales'?9:s.texture==='fine-scales'?5:7;
  const scales = []; for (let row=0;row<7;row++) for (let n=0;n<Math.ceil(125/spacing);n++) {
    const x=63+n*spacing+(row%2)*spacing/2, y=31+row*7+Math.pow((x-110)/30,2);
    scales.push(`M${x.toFixed(1)} ${y.toFixed(1)}q${spacing*.52} 2.4 0 5`);
  }
  const scaleInk=pointPath(doc,scales.join(' '),'none',s.colors[0],s.texture==='large-scales'?1.2:.7);
  scaleInk.setAttribute('opacity',s.texture==='spots'?'.13':'.34'); group.append(scaleInk);
  const lateral=pointPath(doc,'M58 53Q117 47 187 55','none',s.colors[2],1.5); lateral.setAttribute('opacity','.64'); group.append(lateral);
  return group;
}

// Repeated spot, creel and result cards reference the seven shared plates.
// All fragment IDs and path data are local constants. Packet text is never SVG.
export function installFishIllustrations(doc, bank) {
  if (!doc.createElementNS) return;
  const defs=svgNode(doc,'defs',{});
  for (const [key,s] of Object.entries(fish)) {
    const id=idFor(key), gradient=svgNode(doc,'linearGradient',{id:id+'-skin',x1:'0%',x2:'0%',y1:'0%',y2:'100%'});
    for (const [offset,color] of [['0%',s.colors[0]],['45%',s.colors[1]],['88%',s.colors[2]],['100%',s.colors[1]]])
      gradient.append(svgNode(doc,'stop',{offset,'stop-color':color}));
    const clip=svgNode(doc,'clipPath',{id:id+'-clip'}); clip.append(svgNode(doc,'path',{d:s.body}));
    const symbol=svgNode(doc,'symbol',{id,viewBox:'0 0 240 112'}), outline=s.colors[0];
    symbol.append(pointPath(doc,s.tail,s.colors[3],outline),pointPath(doc,s.dorsal,s.colors[3],outline),pointPath(doc,s.lower,s.colors[3],outline));
    const rays=pointPath(doc,'M191 54L220 36M193 55L219 42M192 58L219 69M189 59L224 75','none',s.colors[2],.8);
    rays.setAttribute('opacity','.55'); symbol.append(rays);
    symbol.append(pointPath(doc,s.body,`url(#${id}-skin)`,outline,1.3),pigmentation(doc,s,id));
    const [g,y]=s.gill;
    symbol.append(pointPath(doc,`M${g-5} 43Q${g+10} 61 ${g-2} ${y}M${g-2} 46Q${g+5} 57 ${g+1} 63`,'none',outline,1.2));
    symbol.append(pointPath(doc,s.pectoral,s.colors[3],outline,.9),pointPath(doc,s.marks,'none',outline,1.15));
    const [cx,cy]=s.eye; symbol.append(svgNode(doc,'circle',{cx,cy,r:4.9,fill:s.colors[2],stroke:outline,'stroke-width':1.2}),
      svgNode(doc,'circle',{cx:cx-.6,cy,r:2.9,fill:'#222a28'}),svgNode(doc,'circle',{cx:cx-1.5,cy:cy-1.2,r:.9,fill:'#f4f0d3'}));
    defs.append(gradient,clip,symbol);
  }
  bank.replaceChildren(defs);
}
export const fishName = key => Object.hasOwn(fish,key) ? fish[key].name : null;
const byName = new Map(Object.entries(fish).map(([key,s])=>[s.name,key]));
export function fishFigure(doc, value) {
  const key=fishName(value) ? value : byName.get(value);
  if (!key || !doc.createElementNS) return null;
  const svg=svgNode(doc,'svg',{viewBox:'0 0 240 112',class:'fish-plate','aria-hidden':'true',focusable:'false','data-fish':key});
  svg.append(svgNode(doc,'use',{href:'#'+idFor(key),width:240,height:112})); return svg;
}

function playable(s) { return s?.HasConfirmedState && !s.Faulted && !s.ShutdownConfirmed; }
export function newCreelCatch(previous, current) {
  if (!playable(previous) || !playable(current) || !previous.SessionId || current.SessionId || current.PendingResult ||
      !Number.isSafeInteger(previous.CreelCount) || current.CreelCount !== previous.CreelCount+1 || current.Sequence === previous.Sequence) return null;
  const index = current.CreelCount-1-current.CreelOffset;
  const row = current.Creel?.[index];
  return row && typeof row.Id==='string' && fishName(row.SpeciesKey) && typeof row.MassMg==='string' && /^[1-9]\d{0,18}$/.test(row.MassMg) &&
    !previous.Creel?.some(f=>f.Id===row.Id) ? {id:row.Id, speciesKey:row.SpeciesKey, massMg:row.MassMg} : null;
}

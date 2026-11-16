// Original drawings in a common 160 × 100 catalogue frame. Each instance
// owns its paint and clipping IDs so a card can move between backpack tabs.
const ns='http://www.w3.org/2000/svg';
const editions=new WeakMap();
const paints={
  graphite:['#323936','#66736d','#343e39','#1e2523'],
  cork:['#b48b5a','#e2c79a','#d2b385','#a17749'],
  steel:['#5a6361','#e4e6da','#8f9997','#454f4c'],
  brass:['#806039','#e8c983','#b29150','#765a36'],
  nylon:['#7b857b','#d1dbca','#aeb9a9','#7e8980'],
  lead:['#657371','#aab4ac','#6f7c79','#45514e'],
  pearl:['#bcbbb0','#edeade','#c6c5ba','#96998f'],
  enamel:['#8f392b','#e18351','#bc5834','#873928']
};

function createBrush(doc,defs,prefix){
  const node=(parent,tag,attrs)=>{
    const child=doc.createElementNS(ns,tag);
    for(const [key,value] of Object.entries(attrs))child.setAttribute(key,String(value));
    if(parent)parent.append(child);return child;
  };
  const loaded=new Set();
  function paint(name){
    if(!Object.hasOwn(paints,name))return name;
    const id=prefix+'-'+name;
    if(!loaded.has(name)){
      const grad=node(defs,'linearGradient',{id,x1:'0%',x2:'17%',y1:'0%',y2:'100%'});
      paints[name].forEach((color,i)=>node(grad,'stop',{offset:['0%','29%','63%','100%'][i],'stop-color':color}));
      loaded.add(name);
    }
    return 'url(#'+id+')';
  }
  const path=(g,d,fill,stroke='none',width=.8)=>node(g,'path',{d,fill:paint(fill),stroke:paint(stroke),'stroke-width':width});
  const ink=(g,d,stroke,width=1)=>path(g,d,'none',stroke,width);
  const oval=(g,cx,cy,rx,ry,fill,stroke='none',width=.8)=>node(g,'ellipse',{cx,cy,rx,ry,fill:paint(fill),stroke:paint(stroke),'stroke-width':width});
  const group=(g,transform)=>node(g,'g',transform?{transform}:{});
  function clip(g,d){
    const id=prefix+'-clip',clip=node(defs,'clipPath',{id});
    node(clip,'path',{d});return node(g,'g',{'clip-path':'url(#'+id+')'});
  }
  function shadow(g,x,y,rx,ry){
    const id=prefix+'-shadow',grad=node(defs,'radialGradient',{id});
    node(grad,'stop',{offset:'0%','stop-color':'#584d36','stop-opacity':.21});
    node(grad,'stop',{offset:'100%','stop-color':'#584d36','stop-opacity':0});
    oval(g,x,y,rx,ry,'url(#'+id+')');
  }
  return {path,ink,oval,group,clip,shadow};
}

function rod(b,parent,kind){
  const {path,ink,oval,group}=b;
  const feeder=kind==='bottom-rod',spinning=kind==='lure-rod';
  const blank=group(parent,spinning?'translate(22 80) rotate(-30) scale(.92 1)':'translate(16 77) rotate(-23)');
  const lacquer=feeder?'#66513a':spinning?'#3b4746':'#526457';
  // The ferrules, guides and wraps are shown with the sections laid apart.
  const shaft=spinning?'M0 -1.1L25 -1.35 53 -2.5 127 -.9 139 -.4 139 .7 53 1.3 25 1.25 0 1.3Z':
    'M0 -2.8Q0 -4 3 -4L30 -3.1 53 -2.5 127 -.9 139 -.4 139 .7 53 1.3 30 2.1 3 3.1Q0 3.6 0 2Z';
  path(blank,shaft,lacquer,'#303630',.6);
  ink(blank,'M46 -1.6L131 -.45','#acb6a7',.65);
  const seat=spinning?25:29;
  if(spinning){
    path(blank,'M0 -4.5Q3 -5 7 -4L8 3.8Q3 4.7 0 4Z','cork','#665941',.55);
    path(blank,'M17 -3.8Q21 -5 25 -3.8L25 2.8Q19 3.8 17 2.4Z','cork','#665941',.55);
  }else path(blank,'M2 -4.3Q6 -5 9 -4L'+(seat-3)+' -3.4Q'+seat+' -2.5 '+(seat-1)+' 1.8L4 3.5Q0 3.6 1 1Z',feeder?'graphite':'cork','#5f5746',.55);
  path(blank,'M0 -4L3 -4.5 4 3.7 1 4Z','graphite');
  if(!feeder){
    ink(blank,'M6 -.8L9 -1.2M11 1L13 .5M17 -2L19 -2.3M20 1L22 .6','#997849',.65);
  }
  path(blank,'M'+seat+' -3L'+(seat+16)+' -2.9 '+(seat+16)+' 1.9 '+seat+' 2.3Z','graphite','#39433c',.6);
  for(const x of [seat,seat+4,seat+14])ink(blank,'M'+x+' -2.5v4.4','steel',1.3);
  if(feeder)ink(blank,'M5 -2L23 -2M5 -1L22 -1','#858883',.55);
  const wrapped=feeder?'#ac6b41':spinning?'#b57d55':'#9e955f';
  for(const x of [51,77,100,119])ink(blank,'M'+x+' -1.4v2.6',wrapped,1.4);
  for(const [x,r] of [[73,spinning?4.8:feeder?4.1:2.6],[98,2.5],[119,1.7],[138,1.15]]){
    ink(blank,'M'+(x-5)+' 1L'+(x-1)+' '+(r+1)+'L'+(x+2)+' 1','#515d59',.85);
    oval(blank,x,r+1,r,r*.68,'none','#39433f',1);
  }
  const tip=group(blank,'translate(17 -11) rotate(2)');
  path(tip,'M0 -1.35L111 -.45 124 0 111 .5 0 1.4Z',lacquer,'#4a5148',.4);
  ink(tip,'M0 0h6','steel',2.1);
  for(const x of [32,60,82,103,121]){
    ink(tip,'M'+(x-2)+' .4l2 2.2 2-2.2','#58635c',.7);
    oval(tip,x,2.4,x<70?1.8:1.1,1,'none','#4a564f',.75);
  }
  if(feeder){
    ink(tip,'M88 0l35 .1','#db6a3d',1.5);
    for(const x of [93,104,114])ink(tip,'M'+x+' -.6v1.3','#f2e8c3',1.8);
    ink(blank,'M41 -16L124 -15.2','#738477',1.45);ink(blank,'M94 -15.6L124 -15.2','#97a557',1.45);
  }else if(!spinning){
    const fine=group(blank,'translate(40 -15) rotate(-.6)');
    path(fine,'M0 -1L87 -.35 101 0 87 .40 0 .9Z','#7e8b77');
    for(const x of [4,27,50,70])ink(fine,'M'+x+' -.75v1.8','#bdab77',1.5);
  }
}

function reel(b,g){
  const {path,ink,oval,group}=b;
  b.shadow(g,75,77,37,5);
  const r=group(g,'translate(3 0) rotate(-12 80 50)');
  path(r,'M75 33Q69 26 61 20L49 19Q45 18 45 15L62 14Q70 14 76 17L87 17Q88 21 81 22L76 23 87 37Z','graphite','#35413b');
  path(r,'M54 41Q60 35 73 35L86 39 96 58 91 70Q87 77 76 77Q65 76 58 67L55 55 48 54Z','graphite','#333e38',1.1);
  path(r,'M61 54Q70 43 84 43L88 48 84 61 71 67 61 66Z','steel','#4e5b56');
  oval(r,72,57,9,8,'graphite','#647068');oval(r,72,57,4.3,3.5,'steel','#414e48');
  path(r,'M82 40Q92 36 99 30L107 34 113 57 108 65Q101 68 88 63L83 58Z','graphite','#424c46');
  const spool=group(r,'rotate(-14 105 43)');
  path(spool,'M94 29Q102 25 118 29L119 50Q110 56 97 52Z','nylon','#646f65');
  for(const y of [32,35,38,41,44,47])ink(spool,'M96 '+y+'Q106 '+(y+4)+' 118 '+y,'#778276',.7);
  oval(spool,106,29,13,4.5,'brass','#695c43');oval(spool,106,28.3,8.2,2.8,'graphite');oval(spool,106,28.1,3,1.2,'steel');
  path(spool,'M95 49Q106 55 120 49L120 53Q109 60 95 54Z','brass','#756442');
  ink(r,'M91 56C75 47 78 33 93 28Q109 23 118 34Q123 42 120 52','#40504a',2.5);
  ink(r,'M91 55C76 46 80 34 93 29Q109 24 117 34Q121 40 120 48','#d3d7c9',1.25);
  ink(r,'M71 58L59 72 40 69','#39463f',4);
  ink(r,'M69 58L58 70 40 67','#b5b9aa',1.5);
  path(r,'M33 63Q28 64 29 69L33 76Q38 80 43 74L43 69Q40 64 33 63Z','graphite','#36423b');
}

function lineSpool(b,g){
  const {path,ink,oval,group}=b;
  b.shadow(g,76,66,32,7);
  const s=group(g,'rotate(-13 76 51)');
  // Transparent monofilament runs from the lower right edge of the wound spool.
  ink(s,'M94 64C114 60 139 70 131 81S116 85 111 82','#9ba99a',1.1);
  oval(s,73,63,30,10,'graphite','#4b554e');
  path(s,'M45 43Q71 32 100 42L100 61Q75 77 45 62Z','nylon','#7b8577');
  for(const y of [45,48,51,54,57,60])ink(s,'M46 '+y+'Q72 '+(y+13)+' 100 '+y,'#7c8c7b',.8);
  oval(s,73,41,31,10.5,'steel','#526059');oval(s,73,40,24,7,'#657465','#506051');
  oval(s,73,40,15,4.2,'#adb398');oval(s,73,40,7,2.5,'#3b4741','#d0d0b1');
  ink(s,'M50 47Q60 51 71 51','#e7ead8',1.2);
}

function hook(b,g){
  const {path,ink,oval}=b;
  const bend='M83 27L94 52C104 74 80 86 65 68Q60 62 59 42';
  ink(g,bend,'#303d38',4.3);ink(g,bend,'steel',2.45);
  path(g,'M57.4 44L58.1 31.5 63.2 46.8 59.4 43.1Z','steel','#36463d',.6);
  oval(g,81.4,23.3,4.8,5.4,'none','#46564e',2.8);
  ink(g,'M78 23Q77 19 81 19','#d9d9c6',1);
}

function bobber(b,g){
  const {path,ink,oval,group}=b,r=group(g,'rotate(29 78 50)');
  ink(r,'M78 19V48','#414b40',2.5);ink(r,'M78 9V28','#d75c39',3);
  ink(r,'M78 17V20','#ede6b4',3.2);ink(r,'M78 26V29','#dbb857',2.8);
  path(r,'M78 35Q82 36 85 47C88 57 83 65 78 69Q71 63 70.5 56Q69 46 78 35Z','enamel','#76583b');
  path(r,'M71 48Q77 52 85 49Q87 58 78 68Q70 60 71 48Z','pearl','#756e51',.5);
  ink(r,'M71 48Q78 52 85 48','#4b554a',1.65);ink(r,'M73 46Q75 40 77 39','#f6d8af',1.4);
  ink(r,'M78 68V84','#515d55',1.7);oval(r,78,85.5,1.5,2,'none','#525e54',.9);
}

function sinker(b,g){
  const {path,ink,oval,group}=b,r=group(g,'rotate(18 78 51)');
  oval(r,78,24,3.8,5,'none','#66726a',2);ink(r,'M78 28v6','#515f56',2.1);
  path(r,'M78 31Q84 35 90 50C98 68 91 79 79 80C66 82 59 70 65 54Q71 40 78 31Z','lead','#526057',1.15);
  ink(r,'M76 38Q67 57 67 65','#c2c8bb',1.5);ink(r,'M83 75Q91 71 90 61','#4f6058',1);
}

function leader(b,g){
  const {ink,oval,path}=b;
  ink(g,'M37 62C32 57 36 49 47 48C68 47 79 76 96 71C109 66 95 52 78 44C59 35 61 26 79 27C94 27 106 37 115 42','#657969',1.65);
  ink(g,'M37 60C33 55 38 50 47 50M78 29Q96 30 113 43','#c6cbb4',.8);
  oval(g,32,63,4.3,2.3,'none','#606b5e',1.3);
  ink(g,'M36 63l7 -3m-5 -1l2 3m0 -4l2 3','#726547',1.5);
  path(g,'M112 39L118 42 117 46 111 43Z','brass','#6b614a');
  oval(g,120,44,3.4,2.6,'none','#6a7467',1.2);
  ink(g,'M123 44L129 46Q133 48 130 51L125 49 125 46','#667467',1.1);
}

function spoon(b,g){
  const {path,ink,oval,group}=b,r=group(g,'rotate(-28 80 50)');
  oval(r,112,49,4.7,4,'none','#657267',1.7);
  path(r,'M109 44Q120 49 104 56Q88 65 66 60C48 56 50 47 65 42Q89 36 109 44Z','brass','#685f47',1.1);
  path(r,'M62 48Q82 39 101 45Q96 51 76 55Q65 57 60 53Z','steel','#a99b76',.55);
  ink(r,'M66 49Q83 42 99 45','#eee3b9',1.3);
  oval(r,58,52,1.5,1.5,'#76683f');oval(r,108,48,1.6,1.6,'#665e44');oval(r,52,53,4.5,3.8,'none','#58665a',1.45);
  ink(r,'M49 54L37 52Q27 50 30 44L33 42M38 52Q29 60 27 55L26 50M37 52Q28 55 22 51L21 47','#445649',1.9);
  ink(r,'M36 52l-6 -2M43 53l4 .6','#c1c6b5',.8);
}

function net(b,g){
  const {path,ink,clip}=b;
  const bag='M69 40C76 20 112 14 131 26C138 35 125 57 112 68C101 77 87 69 80 61Q72 52 69 40Z';
  path(g,bag,'#9aab95','#6e8172',1);
  const mesh=clip(g,bag);
  for(let n=-2;n<13;n++){
    const x=54+n*8;
    ink(mesh,'M'+x+' 12Q'+(x+14)+' 39 '+(x+33)+' 80','#5e7262',.85);
    ink(mesh,'M'+(x+37)+' 14Q'+(x+13)+' 45 '+x+' 80','#6b816e',.8);
  }
  const rim='M68 42C70 26 92 14 117 16C135 18 142 29 132 40C117 54 90 58 73 48Z';
  ink(g,rim,'#40544a',3.5);ink(g,rim,'steel',1.35);
  path(g,'M73 40L82 47 67 55 63 51Z','graphite','#42544a');
  ink(g,'M67 52L28 77','#43584c',4.4);ink(g,'M67 51L28 76','steel',2.1);
  ink(g,'M38 71L22 81','graphite',5.3);
}

function stand(b,g){
  const {path,ink,oval}=b;
  for(const [x,y,footX,footY] of [[65,56,47,79],[98,46,109,73],[60,57,70,84],[102,44,126,58]]){
    ink(g,'M'+x+' '+y+'L'+footX+' '+footY,'#44554b',3);
    oval(g,footX,footY,2,1.2,'#48564c');
  }
  path(g,'M47 59L111 36 114 40 50 63Z','graphite','#405046');
  ink(g,'M51 58L110 37','steel',1.1);
  for(const [x,y] of [[55,57],[86,45]]){
    ink(g,'M'+x+' '+y+'v-20','#4c5f53',3.5);
    ink(g,'M'+(x-11)+' '+(y-20)+'l31 -8','#46594c',3.1);
    for(const i of [0,13,26]){
      const u=x-11+i,v=y-20-i*.26;
      ink(g,'M'+u+' '+v+'v-4m-3 -4q0 7 3 5t3-7','#687750',2.25);
    }
  }
}

function worm(b,g){
  const {ink}=b;
  const coil='M43 67C32 62 43 51 56 56S70 67 81 61C98 51 79 37 67 44S63 59 77 57C99 53 96 32 115 32Q123 32 123 39';
  ink(g,coil,'#76523f',6);ink(g,coil,'#be8c69',4.2);
  ink(g,'M43 58l1 2m4-4l2 3m15 5l1-3m24-13l-3 1m14-8l3 2m10-11l.7 2','#905e48',1.15);
}

function breadBait(b,g){
  const {path,ink,oval}=b;
  b.shadow(g,81,71,38,6);
  path(g,'M43 57Q41 44 52 40Q64 36 73 44Q82 51 77 62Q72 74 56 71Q44 70 43 57Z','cork','#ab8655');
  path(g,'M71 64Q66 52 76 44Q89 39 99 47Q109 54 103 66Q98 75 83 73Q74 72 71 64Z','cork','#a78151');
  path(g,'M91 47Q87 36 97 31Q108 25 117 33Q126 40 120 50Q114 57 103 55Q95 54 91 47Z','cork','#ae895c');
  ink(g,'M46 53l3-3m5 13l3 1m17-8l4-3m13 11l3-2m7-25l3-2m7 12l3 .4','#94754d',1.1);
  path(g,'M50 44l3-2 2 1-1 2-3 1ZM79 48l2-3 5 .4-2.5 2ZM102 32l4-1 3 1-5 2Z','#ead6a9');
  for(const [x,y] of [[35,67],[109,72],[117,65],[48,75]])oval(g,x,y,1.6,1.2,'#ba9a68');
}

const figures=new Map([
  ['float-rod',rod],['bottom-rod',rod],['lure-rod',rod],['reel',reel],['line',lineSpool],['hook',hook],
  ['float',bobber],['sinker',sinker],['leader',leader],['spoon',spoon],['net',net],['stand',stand],
  ['worm',worm],['bread-bait',breadBait]
]);

export function tackleFigure(doc,kind){
  if(!figures.has(kind)||!doc?.createElementNS)return null;
  const index=(editions.get(doc)??0)+1;editions.set(doc,index);
  const svg=doc.createElementNS(ns,'svg');
  for(const [key,val] of Object.entries({viewBox:'0 0 160 100',class:'tackle-figure',width:'100%',
    'aria-hidden':'true',focusable:'false','data-tackle':kind,'stroke-linecap':'round','stroke-linejoin':'round'}))svg.setAttribute(key,val);
  const defs=doc.createElementNS(ns,'defs');svg.append(defs);
  const b=createBrush(doc,defs,'fd-tackle-'+index);
  b.path(svg,'M0 0h160v100H0Z','#f0ebdf');
  figures.get(kind)(b,svg,kind);
  return svg;
}

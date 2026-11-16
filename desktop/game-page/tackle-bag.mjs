const kinds = {rod:'鱼竿',reel:'渔轮',line:'主线',leader:'子线',hook:'鱼钩',bobber:'浮漂',sinker:'沉子',spoon:'拟饵',net:'抄网',stand:'竿架'};
export const bagFilters = {all:'全部',rod:'鱼竿',reel:'渔轮',line:'鱼线',hook:'鱼钩',bobber:'浮漂',sinker:'沉子',spoon:'拟饵',tools:'工具'};
const names = {
  'starter-rod':'基础浮漂竿','starter-reel':'基础纺车轮','nylon-018':'0.18 毫米尼龙线','single-hook':'单钩','float-3g':'3 克浮漂','sinker-2_5g':'2.5 克沉子',
  'bottom-bank-rod':'岸钓底竿','bottom-bank-reel':'底钓纺车轮','bottom-022-line':'0.22 毫米底钓主线','bottom-016-leader':'底钓子线',
  'bottom-single-hook':'底钓单钩','bottom-18g-sinker':'18 克底钓铅坠','spinning-rod':'亮片路亚竿','spinning-reel':'路亚纺车轮',
  'spinning-line':'路亚主线','coast-spoon-12g':'12 克勺形亮片','spoon-hook':'亮片自带钩','landing-net':'抄网','three-rod-stand':'三竿支架',
  'fixed-rod':'固定浮漂竿','fixed-reel':'固定渔轮','fixed-line':'固定主线','fixed-hook':'固定鱼钩','fixed-bobber':'固定浮漂','fixed-sinker':'固定沉子'
};
const groups={float:'浮漂钓组',lure:'路亚钓组',tools:'辅助装备','bottom-1':'底钓套装 1','bottom-2':'底钓套装 2','bottom-3':'底钓套装 3'};
const places={inventory:'背包',owned:'已持有',equipped:'已装备','rig-slot':'钓组槽位','reel-spool':'渔轮线杯',integral:'亮片钩位'};
const guid=value=>typeof value==='string' && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(value) && value!=='00000000-0000-0000-0000-000000000000';
const quantity=value=>typeof value==='string' && /^(0|[1-9]\d{0,18})$/.test(value) && (value.length<19||value<='9223372036854775807');
const units={massMg:['重量',3,'克'],minLoadMg:['最小负载',3,'克'],maxLoadMg:['最大负载',3,'克'],diameterUm:['线径',3,'毫米'],
  remainingMm:['剩余线长',3,'米'],initialMm:['原始线长',3,'米'],capacityMm:['当前线径容量',3,'米'],lengthMm:['长度',3,'米'],slots:['竿位',0,'个']};
export function bagName(key,kind) { return names[key] ?? (kind==='net'?'抄网':`${kinds[kind]}（${key}）`); }
export function equipmentBag(state) {
  // Earlier hosts only projected the editable float ledger.
  const raw=state.bag===undefined?state.items.filter(i=>i.location!=='lost').map(i=>({
    id:i.id,key:i.definitionKey,kind:i.kind,group:'float',place:i.location,owner:i.reelId,
    facts:i.kind==='line'?[{key:'remainingMm',value:String(i.remainingLineMm)},{key:'initialMm',value:String(i.initialLineMm)}]:[]
  })):state.bag;
  if(!Array.isArray(raw)||raw.length>154)return null;
  const rows=[],ids=new Set();
  for(const item of raw){
    if(!item||!guid(item.id)||ids.has(item.id)||typeof item.key!=='string'||!/^[a-z0-9_.-]{1,96}$/.test(item.key)||
      !Object.hasOwn(kinds,item.kind)||!Object.hasOwn(groups,item.group)||!Object.hasOwn(places,item.place)||
      (item.owner!==null&&!guid(item.owner))||!Array.isArray(item.facts)||item.facts.length>8)return null;
    const facts={}, {id,key,kind,group,place,owner}=item;
    for(const f of item.facts){
      if(!f||!Object.hasOwn(units,f.key)||Object.hasOwn(facts,f.key)||!quantity(f.value))return null;
      facts[f.key]=f.value;
    }
    ids.add(id);rows.push(Object.freeze({id,key,kind,group,place,owner,facts:Object.freeze(facts)}));
  }
  const byId=new Map(rows.map(r=>[r.id,r]));
  for(const row of rows){
    const owner=byId.get(row.owner);
    if(row.place==='reel-spool'?(row.kind!=='line'||owner?.kind!=='reel'||owner.group!==row.group):
      row.place==='integral'?(row.kind!=='hook'||owner?.kind!=='spoon'||owner.group!==row.group):row.owner!==null)return null;
  }
  return Object.freeze(rows);
}
function format(value,power,suffix){
  const n=BigInt(value),scale=10n**BigInt(power);
  const frac=power?String(n%scale).padStart(power,'0').replace(/0+$/,''):'';
  return (n/scale).toLocaleString('zh-CN')+(frac?'.'+frac:'')+' '+suffix;
}
const groupOrder=['float','bottom-1','bottom-2','bottom-3','lure','tools'];
const kindOrder=Object.keys(kinds);
export function compareBagRows(a,b){
  return groupOrder.indexOf(a.group)-groupOrder.indexOf(b.group)||kindOrder.indexOf(a.kind)-kindOrder.indexOf(b.kind)||
    a.key.localeCompare(b.key)||Number(a.place==='inventory')-Number(b.place==='inventory')||a.id.localeCompare(b.id);
}
export function bagDetails(item,rows){
  const owner=rows.find(row=>row.id===item.owner);
  const group=groups[item.group],location=owner?`${bagName(owner.key,owner.kind)} · ${places[item.place]}`:
    item.place==='rig-slot'?`${group} · ${kinds[item.kind]}槽`:places[item.place];
  const facts=[];
  for(const [key,[label,power,suffix]] of Object.entries(units)){
    if(!Object.hasOwn(item.facts,key))continue;
    if(key==='maxLoadMg'&&Object.hasOwn(item.facts,'minLoadMg'))continue;
    if(key==='minLoadMg'&&Object.hasOwn(item.facts,'maxLoadMg')){
      facts.push([item.kind==='bobber'?'适配坠重':'适配抛重',format(item.facts[key],power,'').trim()+'–'+format(item.facts.maxLoadMg,power,suffix)]);
    }else facts.push([label,format(item.facts[key],power,suffix)]);
  }
  const type=item.group==='tools'?'tools':item.kind==='leader'?'line':item.kind;
  const art=item.kind==='bobber'?'float':item.kind!=='rod'?item.kind:item.group==='float'?'float-rod':item.group==='lure'?'lure-rod':'bottom-rod';
  return {id:item.id,title:bagName(item.key,item.kind),group,location,compactPlace:item.group.startsWith('bottom-')?group:item.place==='rig-slot'?group:places[item.place],kind:kinds[item.kind],type,art,facts};
}
const words={reel:'鱼轮 线轮',line:'钓线 鱼线 线轴',leader:'前导线 子线 鱼线',sinker:'铅坠 坠子 配重',bobber:'浮标 鱼漂',hook:'钓钩 钩子',spoon:'假饵 亮片',rod:'钓竿 鱼杆',net:'网具',stand:'支架 竿座'};
function normalize(value){return value.normalize('NFKC').toLowerCase().replaceAll('鱼轮','渔轮').replaceAll('鱼线','主线').trim();}
export function matchBag(item,filter,query){
  if(filter!=='all'&&item.type!==filter)return false;
  const text=normalize([item.title,item.kind,words[item.art]??(item.type==='rod'?words.rod:''),bagFilters[item.type],item.group,item.location,...item.facts.flat()].join(' '));
  return normalize(query).split(/\s+/).every(word=>text.includes(word));
}

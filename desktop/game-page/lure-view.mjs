const prompts = {
  'in-water':['亮片入水','按住左键匀收，松手让亮片下沉。按住 R 滚动滚轮可调速度档。'],
  strikeable:['鱼已攻击亮片','现在按一次右键提竿。'],
  'bottom-recovery':['亮片已触底','按住左键把亮片收回岸边。'],
  'retrieve-pending':['亮片已到岸边','等待收回确认。'],
  hooked:['亮片已中鱼','重新按住左键遛鱼。'],
  ended:['本竿已收回','可以重新选择落点。'],
};
// A submerged follower produces no visible cue before it attacks the spoon.
prompts.following=prompts['in-water'];
const layers={surface:'水面',upper:'浅层',middle:'中层',deep:'深层'};
const sessionId = id => typeof id==='string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(id) &&
  id!=='00000000-0000-0000-0000-000000000000';
const distance = n => Number.isInteger(n) && n>=0 && n<=100000;
const point = p => p && Object.keys(p).length===3 && ['xMm','yMm','zMm'].every(key=>Number.isInteger(p[key]) && Math.abs(p[key])<=1000000);
const copy = s => s && (s.phase==='idle' ? {...s} : {...s,position:{...s.position}});

export class LureView {
  #doc; #revision=0; #state=null; #canRetry=false;
  constructor(documentId){this.#doc=documentId;}
  get revision(){return this.#revision;}
  get canRetryRetrieve(){return this.#canRetry;}
  state(){return copy(this.#state);}
  forSession(id){return id && this.#state?.sessionId===id ? this.state() : null;}
  apply(m){
    if (m?.kind!=='game-lure-state' || m.documentId!==this.#doc || !Number.isSafeInteger(m.revision) || m.revision<=this.#revision) return false;
    const s=m.state;
    if (!s || typeof s!=='object') return false;
    if (s.phase==='idle') {
      if (Object.keys(s).length!==2 || s.sessionId!==null) return false;
    } else {
      if (!Object.hasOwn(prompts,s.phase) || !sessionId(s.sessionId) || !Object.hasOwn(layers,s.layer) || !point(s.position) ||
          ![s.depthMm,s.bedClearanceMm,s.outLineMm,s.remainingLineMm].every(distance) || s.outLineMm+s.remainingLineMm>100000 ||
          s.outLineMm+s.remainingLineMm<1 || !Number.isInteger(s.speedLevel) || s.speedLevel<0 || s.speedLevel>4 ||
          typeof s.reeling!=='boolean' || typeof s.bailOpen!=='boolean' || (s.reeling && s.bailOpen)) return false;
      const encounterVisible=['following','strikeable','hooked'].includes(s.phase);
      if (encounterVisible ? !sessionId(s.encounterId) || s.encounterId===s.sessionId : s.encounterId!==null) return false;
      if (Object.keys(s).length!==12) return false;
    }
    if(m.canRetryRetrieve!==undefined && (typeof m.canRetryRetrieve!=='boolean' || (m.canRetryRetrieve && s.phase!=='retrieve-pending')))return false;
    this.#state=copy(s); this.#canRetry=m.canRetryRetrieve===true; this.#revision=m.revision; return true;
  }
}

export function lurePrompt(phase){return Object.hasOwn(prompts,phase) ? [...prompts[phase]] : null;}
export function lureMetrics(s){
  const metres=mm=>(mm/1000).toFixed(2);
  return `${layers[s.layer]} · 水下 ${metres(s.depthMm)} 米 · ${s.reeling?'收饵':'停顿'} · 速度 ${s.speedLevel+1}/5 档 · 出线 ${metres(s.outLineMm)} 米 · 余线 ${metres(s.remainingLineMm)} 米`;
}

export function createLurePanel(doc){
  const element=doc.createElement('section'), title=doc.createElement('strong'), hint=doc.createElement('p'), metrics=doc.createElement('p');
  title.setAttribute('role','status');element.append(title,hint,metrics);element.hidden=true;
  return {element,render(view,currentSessionId){
    const s=view.forSession(currentSessionId);
    element.hidden=!s;
    if(!s)return;
    [title.textContent,hint.textContent]=prompts[s.phase];
    metrics.textContent=lureMetrics(s);
  }};
}

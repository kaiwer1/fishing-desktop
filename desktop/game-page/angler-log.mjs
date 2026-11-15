import { fishName } from './fish-illustration.mjs';
import { formatMinor } from './shop-view.mjs';
import { specimenMass } from './specimen-view.mjs';

const labels = { float: '浮漂', bottom: '底钓', lure: '路亚' };
const count = n => Number.isInteger(n) && n >= 0 && n <= 10000;
const integerText = n => typeof n === 'string' && /^(0|[1-9]\d{0,18})$/.test(n) &&
  (n.length < 19 || n <= '9223372036854775807');
const speciesId = id => typeof id === 'string' && /^[a-z0-9_-]{1,64}$/.test(id);

function validLog(s) {
  if (!s || s.rulesVersion !== 1 || ![s.sequence,s.totalCasts,s.totalCaught,s.totalSold].every(count) ||
      !integerText(s.totalXp) || !integerText(s.saleMinor) || !Array.isArray(s.methods) || s.methods.length !== 3 ||
      !Array.isArray(s.species) || s.species.length > s.totalCaught) return false;
  let casts = 0, caught = 0, xp = 0n;
  const methods = new Set();
  for (const row of s.methods) {
    if (!row || typeof row.id !== 'string' || !Object.hasOwn(labels,row.id) || methods.has(row.id) || !count(row.casts) || !count(row.caught) || !integerText(row.xp)) return false;
    methods.add(row.id); casts += row.casts; caught += row.caught; xp += BigInt(row.xp);
  }
  if (casts !== s.totalCasts || caught !== s.totalCaught || xp !== BigInt(s.totalXp)) return false;
  const skill=s.steadyStrike;
  if (skill !== undefined && (!skill || typeof skill.learned !== 'boolean' ||
      ![skill.requiredXp,skill.baseMs,skill.bonusMs].every(n=>Number.isSafeInteger(n)&&n>0&&n<=100000) ||
      skill.learned && BigInt(s.methods.find(m=>m.id==='float').xp)<BigInt(skill.requiredXp))) return false;
  let speciesCaught = 0;
  const species = new Set();
  for (const row of s.species) {
    if (!row || !speciesId(row.id) || species.has(row.id) || !count(row.caught) || row.caught === 0 ||
        !integerText(row.maxMassMg) || row.maxMassMg === '0') return false;
    species.add(row.id); speciesCaught += row.caught;
  }
  return speciesCaught === s.totalCaught;
}

function copyLog(s) {
  const skill=s.steadyStrike;
  return { rulesVersion:1, sequence:s.sequence, totalCasts:s.totalCasts, totalCaught:s.totalCaught,
    totalSold:s.totalSold, saleMinor:s.saleMinor, totalXp:s.totalXp,
    ...(skill ? {steadyStrike:{learned:skill.learned,requiredXp:skill.requiredXp,baseMs:skill.baseMs,bonusMs:skill.bonusMs}} : {}),
    methods:s.methods.map(({id,casts,caught,xp}) => ({id,casts,caught,xp})),
    species:s.species.map(({id,caught,maxMassMg}) => ({id,caught,maxMassMg})) };
}

export class AnglerLog {
  #documentId; #revision = 0; #state = null;
  constructor(documentId) { this.#documentId = documentId; }
  get revision() { return this.#revision; }
  get sequence() { return this.#state?.sequence ?? null; }
  state() { return this.#state && copyLog(this.#state); }
  apply(m) {
    if (m?.kind !== 'game-angler-log' || m.documentId !== this.#documentId || !Number.isSafeInteger(m.revision) ||
        m.revision <= this.#revision || !validLog(m.state) || (this.#state && m.state.sequence < this.#state.sequence)) return false;
    this.#state = copyLog(m.state); this.#revision = m.revision;
    return true;
  }
}

export function createAnglerLogPanel(doc,log,canOpen,studyState,onLearn) {
  const get = id => doc.getElementById(id);
  const dialog = get('angler-log-dialog'), dismiss = get('angler-log-close');
  let drawn = 0, lastState;
  function element(tag,text) {
    const node = doc.createElement(tag); node.textContent = text; return node;
  }
  function metric(label,field,value) {
    const group = doc.createElement('div'), number = element('dd',value);
    number.dataset.logField = field;
    group.append(element('dt',label),number); return group;
  }
  function tableRow(title,values) {
    const row = doc.createElement('tr'), name = element('th',title);
    name.scope = 'row'; row.append(name,...values.map(value => element('td',value))); return row;
  }
  function render() {
    const s = drawn===log.revision ? lastState : (lastState=log.state());
    const skill=s.steadyStrike, learn=get('learn-steady-strike');
    get('steady-strike').hidden=!skill;
    if (skill) {
      const xp=s.methods.find(row=>row.id==='float').xp, enough=BigInt(xp)>=BigInt(skill.requiredXp);
      const phase=studyState(), available=enough && (phase==='ready'||phase==='retry');
      const status=skill.learned?'已学会':!enough?'未解锁':phase==='pending'?'正在保存':phase==='retry'?'上次未保存，可重试':available?'可学习':'暂不能学习';
      if(drawn!==log.revision)get('steady-strike-effect').textContent=`鱼漂沉水后的提竿时间从 ${skill.baseMs/1000} 秒延长至 ${(skill.baseMs+skill.bonusMs)/1000} 秒。`;
      const detail=`${status} · 浮漂经验 ${xp} / ${skill.requiredXp} · 学习不消耗经验`;
      if(get('steady-strike-status').textContent!==detail)get('steady-strike-status').textContent=detail;
      learn.hidden=skill.learned;learn.disabled=skill.learned||!available;
    }
    if (drawn === log.revision) return;
    get('angler-log-summary').replaceChildren(metric('累计抛竿','totalCasts',s.totalCasts),metric('渔获入护','totalCaught',s.totalCaught),
      metric('已售渔获','totalSold',s.totalSold),metric('售鱼收入 · 银币','saleMinor',formatMinor(s.saleMinor)),metric('累计经验','totalXp',s.totalXp));
    get('angler-log-methods').replaceChildren(...s.methods.map(m => {
      const row = tableRow(labels[m.id],[m.casts,m.caught,m.xp]); row.dataset.logMethod = m.id; return row;
    }));
    get('angler-log-species').replaceChildren(...s.species.map(f => {
      const row = tableRow(fishName(f.id) ?? f.id,[f.caught,specimenMass(f.maxMassMg)]);
      row.dataset.logSpecies = f.id; return row;
    }));
    get('angler-log-empty').hidden = s.species.length > 0;
    get('angler-log-records').hidden = s.species.length === 0;
    drawn = log.revision;
  }
  function close() {
    if (!dialog.open) return;
    dialog.close();
    if (canOpen() && !doc.hidden && doc.hasFocus()) get('camp-go-log').focus({preventScroll:true});
  }
  function open() {
    if (!log.revision || !canOpen() || doc.hidden || !doc.hasFocus()) return false;
    render();
    if (!dialog.open) { dialog.showModal(); dismiss.focus({preventScroll:true}); }
    return true;
  }
  function key(e) {
    if (!dialog.open) return;
    e.stopPropagation();
    if (e.type === 'keydown' && !e.repeat && (e.key === 'Escape' || (e.code === 'KeyO' && !e.altKey && !e.ctrlKey && !e.metaKey))) {
      e.preventDefault(); close();
    }
  }
  dismiss.addEventListener('click',close);
  get('learn-steady-strike').addEventListener('click',()=>{
    if(dialog.open && canOpen() && !doc.hidden && doc.hasFocus() && !get('learn-steady-strike').disabled)onLearn();
  });
  dialog.addEventListener('cancel',e => { e.preventDefault(); close(); });
  dialog.addEventListener('keydown',key); dialog.addEventListener('keyup',key);
  return { open, close, get shown() { return Boolean(dialog.open); }, reconcile() {
    if (!dialog.open) return;
    if (!canOpen() || doc.hidden) close(); else render();
  } };
}

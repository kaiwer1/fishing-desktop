const keyAction = new Map([['KeyX','place'],['KeyE','take'],['Digit1','one'],['Digit2','two'],['Digit3','three']]);
const numbers = ['one','two','three'];
const guid = id => typeof id === 'string' && id.length === 36 && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(id) &&
  id !== '00000000-0000-0000-0000-000000000000';
const counter = n => typeof n === 'string' && /^(0|[1-9]\d{0,18})$/.test(n) && (n.length < 19 || n <= '9223372036854775807');
const locationKey = s => typeof s === 'string' && /^[a-z0-9][a-z0-9._-]{0,95}$/.test(s);
const phases = new Map([['idle','未抛竿'],['charging','蓄力中'],['flying','飞行中'],['settling','沉底中'],
  ['waiting','等鱼'],['biting','有鱼口'],['fighting','遛鱼中'],['pending-result','等待处理渔获']]);
const movable = rod => ['idle','waiting','biting'].includes(rod.phase);
const sameLocation = (s,c) => s && s.waterId === c.waterId && s.spotId === c.spotId &&
  (!s.standId || c.geometryVersion === s.geometryVersion);
const point = p => p && [p.xMm,p.yMm,p.zMm].every(n=>Number.isInteger(n) && Math.abs(n)<=1000000);
const copyPoint = p => p && ({xMm:p.xMm,yMm:p.yMm,zMm:p.zMm});
const copyRig = r => r && ({ rigId:r.rigId, sessionId:r.sessionId, phase:r.phase, label:r.label,
  tipPermille:r.tipPermille, lineEnd:copyPoint(r.lineEnd) });

function validStand(s) {
  if (!s || !locationKey(s.waterId) || !locationKey(s.spotId)) return false;
  if (s.standId === null) return Object.keys(s).length === 3;
  if (!guid(s.standId) || !counter(s.standRevision) || s.standRevision === '0' || !counter(s.controlEpoch) ||
      !counter(s.handGeneration) || s.handGeneration==='0' || !point(s.origin) || !Number.isSafeInteger(s.geometryVersion) || s.geometryVersion<1 ||
      !Number.isInteger(s.aimedSlot) || s.aimedSlot < 0 || s.aimedSlot > 3 ||
      typeof s.pending !== 'boolean' || !Array.isArray(s.slots) || s.slots.length !== 3) return false;
  const ids = new Set([s.standId]), positions = new Set();
  function validRig(r) {
    if (r === null) return true;
    if (!r || !guid(r.rigId) || ids.has(r.rigId) || !phases.has(r.phase) || typeof r.label !== 'string' ||
        r.label.length > 72 || !r.label.trim() || /[\x00-\x1f\x7f]/.test(r.label) ||
        !Number.isInteger(r.tipPermille) || r.tipPermille<0 || r.tipPermille>1000 || !(r.lineEnd===null || point(r.lineEnd))) return false;
    ids.add(r.rigId);
    if (['idle','charging'].includes(r.phase)) return r.sessionId === null;
    if (!guid(r.sessionId) || ids.has(r.sessionId)) return false;
    ids.add(r.sessionId); return true;
  }
  if (!validRig(s.heldRig)) return false;
  for (const place of s.slots) {
    if (!place || !Number.isInteger(place.slot) || place.slot < 1 || place.slot > 3 || positions.has(place.slot) ||
        typeof place.reachable !== 'boolean' || !['none','touch','bite'].includes(place.alarm) || !validRig(place.rig) ||
        (place.rig && !movable(place.rig)) || (place.alarm !== 'none' && !place.rig?.sessionId)) return false;
    positions.add(place.slot);
  }
  return true;
}
function copyStand(s) {
  if (!s?.standId) return null;
  return { standId:s.standId, waterId:s.waterId, spotId:s.spotId, controlEpoch:s.controlEpoch, standRevision:s.standRevision,
    handGeneration:s.handGeneration, geometryVersion:s.geometryVersion, origin:copyPoint(s.origin),
    heldRig:copyRig(s.heldRig), aimedSlot:s.aimedSlot, pending:s.pending,
    slots:s.slots.map(row => ({ slot:row.slot, rig:copyRig(row.rig), reachable:row.reachable, alarm:row.alarm })).sort((a,b)=>a.slot-b.slot) };
}

// Re-read the player's location and authority on each input. The observed
// stand remains available to a read-only view; a move never changes it locally.
export class StandView {
  #doc; #context; #post; #id; #revision = 0; #stand = null;
  #heldKeys = new Set(); #next = 0; #pending = null;
  constructor({ documentId, readContext, post, newTransportId }) {
    this.#doc=documentId; this.#context=readContext; this.#post=post; this.#id=newTransportId;
  }
  get revision() { return this.#revision; }
  get pending() { return Boolean(this.#pending); }
  state() { this.reconcile(); return copyStand(this.#stand); }
  apply(m) {
    this.reconcile();
    if (m?.kind !== 'game-stand-state' || m.documentId !== this.#doc || !Number.isSafeInteger(m.revision) ||
        m.revision <= this.#revision || !validStand(m.state) || !sameLocation(m.state,this.#context())) return false;
    const next=m.state, old=this.#stand;
    if (old && next.standId === old.standId && (BigInt(next.standRevision)<BigInt(old.standRevision) ||
        BigInt(next.handGeneration)<BigInt(old.handGeneration) ||
        BigInt(next.controlEpoch)<BigInt(old.controlEpoch))) return false;
    // Alarm and aim updates may share a stand revision. Ownership changes may not.
    if (old && next.standId === old.standId && next.standRevision === old.standRevision &&
        (next.heldRig?.rigId !== old.heldRig?.rigId || next.slots.some(row=>row.rig?.rigId !== old.slots[row.slot-1].rig?.rigId))) return false;
    this.#revision=m.revision; this.#stand=copyStand(next); this.reconcile(); return true;
  }
  #allowed(c) { return c.allowed === true && Boolean(c.lease) && c.focused === true && c.menuOpen === false; }
  reconcile() {
    const c=this.#context(), s=this.#stand;
    if (s && !sameLocation(s,c)) this.#stand=null;
    if (!this.#allowed(c) || !this.#stand) { this.clearInput(); return; }
    const p=this.#pending;
    if (p && (p.lease !== c.lease || p.standId !== s.standId || p.controlEpoch !== s.controlEpoch ||
        (p.ack && !s.pending && BigInt(s.standRevision)>BigInt(p.revision)))) this.#pending=null;
  }
  clearInput() { this.#heldKeys.clear(); this.#pending=null; }
  key(event,down) {
    const key=keyAction.get(event.code);
    if (!key) return false;
    if (!down) return this.#heldKeys.delete(event.code);
    this.reconcile();
    const c=this.#context(), s=this.#stand;
    if (!s || !this.#allowed(c) || event.defaultPrevented || event.isComposing || event.altKey || event.ctrlKey ||
        event.metaKey || event.shiftKey || event.target?.isContentEditable || event.target?.closest?.('input,textarea,select')) return false;
    if (event.repeat || this.#heldKeys.has(event.code)) return false;
    this.#heldKeys.add(event.code);
    if (s.pending || this.#pending || s.controlEpoch === '0' || this.#next>=Number.MAX_SAFE_INTEGER) return false;
    const slot=numbers.includes(key) ? numbers.indexOf(key)+1 : s.aimedSlot, target=s.slots[slot-1];
    if (!target?.reachable || (s.heldRig && !movable(s.heldRig))) return false;
    if (key === 'place') {
      if (!s.heldRig || target.rig) return false;
    } else if (!target.rig || (s.heldRig && !s.slots.some(row=>!row.rig && row.reachable))) return false;
    const transportId=this.#id();
    this.#pending={ transportId, lease:c.lease, standId:s.standId, controlEpoch:s.controlEpoch, revision:s.standRevision, ack:false };
    try {
      this.#post({ kind:'game-stand', documentId:this.#doc, lease:c.lease, transportId, key, slot, standId:s.standId,
        expectedRevision:s.standRevision, controlEpoch:s.controlEpoch, expectedHeld:s.heldRig?.rigId??null,
        expectedOccupant:target.rig?.rigId??null, inputSequence:String(++this.#next) });
    } catch { this.#pending=null; return false; }
    return true;
  }
  reply(m) {
    const p=this.#pending;
    if (!p || m?.kind !== 'game-reply' || m.documentId !== this.#doc || m.transportId !== p.transportId) return false;
    if (m.code==='stand-updated') p.ack=true;
    else this.#pending=null;
    this.reconcile(); return true;
  }
}

export function createStandPanel(doc) {
  const element=doc.createElement('section'), title=doc.createElement('h2'), hand=doc.createElement('p'), list=doc.createElement('ol');
  const keys=doc.createElement('small');
  keys.textContent='1 / 2 / 3 选竿 · 空手 E 取最近的竿 · 持竿 X 放入最近空位';
  title.textContent='岸边竿架'; element.hidden=true; element.append(title,hand,list,keys);
  const rows=[];
  for (let slot=1;slot<=3;slot++) {
    const row=doc.createElement('li'), name=doc.createElement('strong'), status=doc.createElement('span');
    row.append(name,status); list.append(row); rows.push({row,name,status});
  }
  return { element, render(view) {
    const s=view.state(); element.hidden=!s;
    if (!s) return;
    hand.textContent=s.heldRig ? `手持 · ${s.heldRig.label} · ${phases.get(s.heldRig.phase)}` : '手中无竿';
    for (const slot of s.slots) {
      const {row,name,status}=rows[slot.slot-1];
      const signal=slot.alarm==='bite' ? '竿尖下压' : slot.alarm==='touch' ? '竿尖轻点' : '';
      name.textContent=`${slot.slot} 号竿位 · ${slot.rig?.label??'空位'}`;
      status.textContent=[signal || (slot.rig && phases.get(slot.rig.phase)), s.aimedSlot===slot.slot && '已瞄准',
        !slot.reachable && '距离过远'].filter(Boolean).map(t=>' · '+t).join('');
      row.dataset.alarm=slot.alarm;
    }
  }};
}

export function bindStandKeys(view,doc,win) {
  function down(event) { if (!doc.hidden && view.key(event,true)) event.preventDefault(); }
  function up(event) { view.key(event,false); }
  function clear() { view.clearInput(); }
  function visibility() { if (doc.hidden) clear(); }
  doc.addEventListener('keydown',down); doc.addEventListener('keyup',up);
  doc.addEventListener('visibilitychange',visibility); win.addEventListener('blur',clear);
  return () => {
    doc.removeEventListener('keydown',down); doc.removeEventListener('keyup',up);
    doc.removeEventListener('visibilitychange',visibility); win.removeEventListener('blur',clear); clear();
  };
}

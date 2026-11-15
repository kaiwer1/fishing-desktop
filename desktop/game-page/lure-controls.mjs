const livePhases = new Set(['in-water','following','strikeable','bottom-recovery']);
const amount = s => typeof s==='string' && /^(0|[1-9]\d{0,18})$/.test(s) && (s.length<19 || s<='9223372036854775807');

// Hold-to-wind and a fresh right press apply only to the current session.
// Taking a fish switches to the separate fight controls on a new left press.
export class LureInput {
  #doc; #read; #post; #id; #seq=0; #held=null; #right=false; #speedKey=null; #strike=null; #speed=null;
  constructor({documentId,readContext,post,newTransportId}){
    this.#doc=documentId;this.#read=readContext;this.#post=post;this.#id=newTransportId;
  }
  #live(c){return c.allowed===true && c.lease && c.sessionId && c.lure?.sessionId===c.sessionId && livePhases.has(c.lure.phase);}
  #same(p,c){return p && p.lease===c.lease && p.sessionId===c.sessionId;}
  #send(action,c,extra={}){
    if(this.#seq>=Number.MAX_SAFE_INTEGER)return null;
    const transportId=this.#id();
    try { this.#post({kind:'game-lure',documentId:this.#doc,lease:c.lease,transportId,action,
      sessionId:c.sessionId,inputSequence:String(++this.#seq),...extra}); }
    catch{return null;}
    return {transportId,lease:c.lease,sessionId:c.sessionId};
  }
  state(){return {held:this.#held && {...this.#held},strikePending:Boolean(this.#strike),speedPending:Boolean(this.#speed)};}
  reconcile(){
    const c=this.#read(), live=this.#live(c);
    if(this.#held && this.#same(this.#held,c) && ['hooked','retrieve-pending','ended'].includes(c.lure?.phase))this.#held=null;
    else if(this.#held && (!live || !this.#same(this.#held,c)))this.stop();
    if(!live || !this.#same(this.#speedKey,c))this.#speedKey=null;
    if(!live || !this.#same(this.#speed,c) || (this.#speed?.ack && c.lure.speedLevel===this.#speed.value))this.#speed=null;
    if(!live || !this.#same(this.#strike,c) || c.lure.phase!=='strikeable' || c.lure.encounterId!==this.#strike?.encounterId)this.#strike=null;
  }
  begin(source='button'){
    this.reconcile();
    const c=this.#read();if(!this.#live(c) || this.#held || this.#strike?.ack)return false;
    const sent=this.#send('reel-start',c);
    if(sent)this.#held={...sent,source};
    return Boolean(sent);
  }
  stop(source,transportId){
    const h=this.#held;
    if(!h || (source!==undefined && h.source!==source) || (transportId!==undefined && h.transportId!==transportId))return false;
    this.#held=null;
    return Boolean(this.#send('reel-stop',h));
  }
  reset(){this.stop();this.#speedKey=null;this.#speed=null;this.#strike=null;this.#right=false;}
  speedKey(down,repeat=false){
    this.reconcile();
    if(!down){this.#speedKey=null;return false;}
    const c=this.#read();if(repeat || !this.#live(c))return false;
    this.#speedKey={lease:c.lease,sessionId:c.sessionId};return true;
  }
  get adjustingSpeed(){
    const c=this.#read();return Boolean(this.#live(c) && this.#same(this.#speedKey,c));
  }
  wheel(deltaY){
    this.reconcile();
    const c=this.#read();
    if(!this.#live(c) || !this.#speedKey || this.#speed || !Number.isFinite(deltaY) || !deltaY)return false;
    const value=c.lure.speedLevel+Math.sign(-deltaY);if(value<0 || value>4)return false;
    const sent=this.#send('speed',c,{value});if(sent)this.#speed={...sent,value,ack:false};
    return Boolean(sent);
  }
  buttons(buttons){if(Number.isInteger(buttons) && buttons>=0 && buttons<=31)this.#right=Boolean(buttons&2);}
  press(buttons){
    if(!Number.isInteger(buttons) || buttons<0 || buttons>31 || !(buttons&2))return false;
    const held=this.#right;this.#right=true;return !held && this.strike();
  }
  strike(){
    this.reconcile();
    const c=this.#read();
    if(!this.#live(c) || c.lure.phase!=='strikeable' || !c.lure.encounterId || this.#strike)return false;
    const encounterId=c.lure.encounterId;
    const sent=this.#send('strike',c,{encounterId});
    if(sent)this.#strike={...sent,encounterId};
    return Boolean(sent);
  }
  reply(m){
    if(m?.kind!=='game-reply' || m.documentId!==this.#doc)return false;
    if(this.#held?.transportId===m.transportId){if(m.code!=='lure-reeling')this.#held=null;return true;}
    if(this.#strike?.transportId===m.transportId){
      if(m.code==='lure-hooked'){this.#strike.ack=true;this.#held=null;}else this.#strike=null;
      return true;
    }
    if(this.#speed?.transportId===m.transportId){
      if(m.code==='lure-speed')this.#speed.ack=true;else this.#speed=null;
      this.reconcile();return true;
    }
    return false;
  }
}

export class LureShop {
  #doc;#read;#post;#id;#revision=0;#state=null;#offer=null;#pending=null;
  constructor({documentId,readContext,post,newTransportId}){
    this.#doc=documentId;this.#read=readContext;this.#post=post;this.#id=newTransportId;
  }
  get busy(){return Boolean(this.#offer || this.#pending);}
  get confirming(){return Boolean(this.#offer);}
  get pending(){return this.#pending?.action??null;}
  state(){return this.#state && {...this.#state};}
  apply(m){
    const s=m?.state;
    if(m?.kind!=='game-lure-shop-state' || m.documentId!==this.#doc || !Number.isSafeInteger(m.revision) || m.revision<=this.#revision ||
       !s || Object.keys(s).length!==5 || s.priceMinor!==5600 || [s.available,s.owned,s.allowed,s.selected].some(v=>typeof v!=='boolean') ||
       (s.selected && (!s.owned || !s.allowed)))return false;
    this.#state={...s};this.#revision=m.revision;this.reconcile();return true;
  }
  #eligible(action,c){
    const s=this.#state;
    if(!s?.available || !c.allowed || !c.lease || !amount(c.sequence))return false;
    if(action==='buy')return !s.owned && amount(c.walletMinor) && BigInt(c.walletMinor)>=5600n;
    if(action==='select')return s.owned && s.allowed && !s.selected;
    return action==='leave' && s.selected;
  }
  can(action){return !this.busy && this.#eligible(action,this.#read());}
  offer(){
    if(!this.can('buy'))return false;
    const c=this.#read();this.#offer={lease:c.lease,sequence:c.sequence};return true;
  }
  cancel(){this.#offer=null;}
  reconcile(){
    const c=this.#read(),p=this.#pending;
    if(this.#offer && (this.#offer.lease!==c.lease || this.#offer.sequence!==c.sequence || !this.#eligible('buy',c)))this.cancel();
    if(!p)return;
    if(c.lease!==p.lease || !c.allowed){this.#pending=null;return;}
    if(p.ack && this.#revision>p.revision && (p.action==='buy' ? this.#state.owned && c.sequence!==p.sequence : this.#state.selected===(p.action==='select')))
      this.#pending=null;
  }
  submit(action){
    this.reconcile();const c=this.#read();
    if(this.#pending || (action==='buy')!==this.confirming || !this.#eligible(action,c))return false;
    const transportId=this.#id();this.cancel();
    this.#pending={transportId,lease:c.lease,sequence:c.sequence,revision:this.#revision,action,ack:false};
    try{this.#post({kind:'game-lure-shop',documentId:this.#doc,transportId,lease:c.lease,action,expectedSequence:c.sequence});}
    catch{this.#pending=null;return false;}
    return true;
  }
  reply(m){
    const p=this.#pending;
    if(!p || m?.kind!=='game-reply' || m.documentId!==this.#doc || m.transportId!==p.transportId)return false;
    if(m.code===(p.action==='buy'?'lure-kit-purchased':'lure-method-selected'))p.ack=true;
    else this.#pending=null;
    this.reconcile();return true;
  }
}

const actions=['buy-stand','buy-2','buy-3','select','leave'];
const money=s=>typeof s==='string' && /^(0|[1-9]\d{0,18})$/.test(s) && (s.length<19 || s<='9223372036854775807');

export class BankKit {
  #doc; #context; #post; #id; #state=null; #revision=0; #offer=null; #pending=null;
  constructor({documentId,readContext,post,newTransportId}){
    this.#doc=documentId;this.#context=readContext;this.#post=post;this.#id=newTransportId;
  }
  get busy(){return Boolean(this.#offer || this.#pending);}
  get offer(){return this.#offer?.action??null;}
  get pending(){return this.#pending?.action??null;}
  get selected(){return this.#state?.selected===true;}
  state(){return this.#state && {...this.#state};}
  apply(m){
    const s=m?.state;
    if (m?.kind!=='game-bank-kit-state' || m.documentId!==this.#doc || !Number.isSafeInteger(m.revision) || m.revision<=this.#revision ||
        !s || [s.available,s.allowed,s.selected,s.standOwned].some(v=>typeof v!=='boolean') || !Number.isInteger(s.kitCount) ||
        s.kitCount<0 || s.kitCount>3 || s.extraPriceMinor!==4800 || s.standPriceMinor!==1600 ||
        (s.selected && (!s.allowed || !s.standOwned || !s.kitCount))) return false;
    this.#state={available:s.available,allowed:s.allowed,selected:s.selected,standOwned:s.standOwned,kitCount:s.kitCount,
      extraPriceMinor:s.extraPriceMinor,standPriceMinor:s.standPriceMinor};
    this.#revision=m.revision;this.reconcile();return true;
  }
  #eligible(action,c){
    const s=this.#state;
    if (!s?.available || !c.allowed || !c.lease || !money(c.sequence)) return false;
    if(action==='leave')return s.selected;
    if(s.selected)return false;
    if(action==='select')return s.allowed && s.standOwned && s.kitCount>0;
    if(!money(c.walletMinor))return false;
    if(action==='buy-stand')return !s.standOwned && BigInt(c.walletMinor)>=BigInt(s.standPriceMinor);
    return (action==='buy-2' && s.kitCount===1 || action==='buy-3' && s.kitCount===2) && BigInt(c.walletMinor)>=BigInt(s.extraPriceMinor);
  }
  can(action){return !this.busy && this.#eligible(action,this.#context());}
  reconcile(){
    const c=this.#context(),p=this.#pending,q=this.#offer;
    if(q && (c.lease!==q.lease || c.sequence!==q.sequence || !this.#eligible(q.action,c))) this.#offer=null;
    if(!p)return;
    if(!c.allowed || c.lease!==p.lease){this.#pending=null;return;}
    if(!p.ack || this.#revision<=p.revision)return;
    const s=this.#state;
    const complete=p.action==='select' ? s.selected : p.action==='leave' ? !s.selected : c.sequence!==p.sequence &&
      (p.action==='buy-stand' ? s.standOwned : s.kitCount===Number(p.action.slice(-1)));
    if(complete)this.#pending=null;
  }
  act(action){
    this.reconcile();
    if(action==='cancel'){this.#offer=null;return false;}
    const c=this.#context(), buying=action==='confirm';
    if(buying)action=this.offer;
    if(!actions.includes(action) || this.#pending || !this.#eligible(action,c) || (!buying && this.#offer))return false;
    if(!buying && action.startsWith('buy-')){this.#offer={action,lease:c.lease,sequence:c.sequence};return true;}
    const transportId=this.#id();this.#offer=null;
    this.#pending={action,transportId,lease:c.lease,sequence:c.sequence,revision:this.#revision,ack:false};
    try{this.#post({kind:'game-bank-kit',documentId:this.#doc,lease:c.lease,transportId,action,expectedSequence:c.sequence});}
    catch{this.#pending=null;return false;}
    return true;
  }
  reply(m){
    const p=this.#pending;
    if(!p || m?.kind!=='game-reply' || m.documentId!==this.#doc || m.transportId!==p.transportId)return false;
    if(m.code===(p.action.startsWith('buy-')?'bank-purchased':'bank-mode-selected'))p.ack=true;
    else this.#pending=null;
    this.reconcile();return true;
  }
}

export function bindBankShop(shop,byId,changed){
  for(const action of [...actions,'confirm','cancel']){
    const button=byId('bank-'+action);
    button.addEventListener('click',e=>{if(e.detail<=1 && !button.disabled){shop.act(action);changed();}});
    button.addEventListener('keydown',e=>{if(e.repeat && ['Space','Enter'].includes(e.code))e.preventDefault();});
  }
  return function render(menuOpen){
    const s=shop.state();
    byId('bank-shop').hidden=!s || !menuOpen;
    if(!s || !menuOpen)return;
    const offer=shop.offer;
    byId('bank-status').textContent=shop.pending?'正在确认操作…':offer?
      `确认购买${offer==='buy-stand'?'三槽竿架（16.00 银币）':offer==='buy-2'?'第二套底竿（48.00 银币）':'第三套底竿（48.00 银币）'}？`:
      `${s.selected?'竿架模式':'单竿模式'} · 已有底竿 ${s.kitCount}/3 · ${s.standOwned?'已拥有竿架':'尚未购买竿架'}`;
    byId('bank-buy-stand').hidden=Boolean(offer || s.standOwned || s.selected);
    byId('bank-buy-2').hidden=Boolean(offer || s.kitCount!==1 || s.selected);
    byId('bank-buy-3').hidden=Boolean(offer || s.kitCount!==2 || s.selected);
    byId('bank-select').hidden=Boolean(offer || s.selected);
    byId('bank-leave').hidden=Boolean(offer || !s.selected);
    byId('bank-confirm').hidden=byId('bank-cancel').hidden=!offer;
    byId('bank-confirm').disabled=!offer;
    for(const a of actions)byId('bank-'+a).disabled=!shop.can(a);
  };
}

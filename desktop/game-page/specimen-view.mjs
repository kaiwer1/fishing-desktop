import { fishName } from './fish-illustration.mjs';

const validId=id=>typeof id==='string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(id) &&
  id!=='00000000-0000-0000-0000-000000000000';
const validMass=m=>typeof m==='string' && /^[1-9]\d{0,18}$/.test(m) && (m.length<19||m<='9223372036854775807');

export function confirmedSpecimen(snapshot,id){
  if(!snapshot?.HasConfirmedState || snapshot.Faulted || snapshot.ShutdownConfirmed || !snapshot.MenuOpen ||
     !validId(id) || !Array.isArray(snapshot.Creel))return null;
  const fish=snapshot.Creel.find(f=>f.Id===id);
  return fish && fishName(fish.SpeciesKey) && validMass(fish.MassMg) ?
    {id,speciesId:fish.SpeciesKey,massMg:fish.MassMg} : null;
}
export function specimenMass(mg){
  if(!validMass(mg))return null;
  const kg=BigInt(mg)>=1000000n,places=kg?6:3,value=mg.padStart(places+1,'0');
  const fraction=value.slice(-places).replace(/0+$/,'');
  return value.slice(0,-places)+(fraction?'.'+fraction:'')+(kg?' 千克':' 克');
}
const styleText=`
#specimen-dialog{position:fixed;inset:0;margin:auto;width:min(760px,90vw);max-height:92vh;box-sizing:border-box;padding:0;color:#eef2e7;background:#282d2a;border:1px solid #788079;border-radius:3px;box-shadow:0 16px 70px #061009ab;overflow:auto}
#specimen-dialog::backdrop{background:#0b1512ba}
#specimen-dialog .specimen-heading{padding:10px 16px;display:flex;align-items:center;gap:16px;border-bottom:1px solid #546559}
#specimen-species{font-size:clamp(19px,3.1vw,29px);margin:0}
#specimen-mass{font-variant-numeric:tabular-nums;color:#d5ddcb}
#specimen-close{margin:0 0 0 auto;padding:5px 12px}
#specimen-canvas{display:block;width:100%;height:min(44vh,350px);touch-action:none;cursor:grab}
#specimen-canvas:active{cursor:grabbing}#specimen-canvas:focus-visible{outline:2px solid #b2c796;outline-offset:-3px}
#specimen-help{margin:0;padding:8px 16px;color:#cfdbcd;font-size:13px}
`;

export function createSpecimenView(doc,{readSnapshot,canOpen=()=>true,findButton,load=()=>import('./specimen-viewer.mjs')}){
  const win=doc.defaultView;
  const dialog=doc.createElement('dialog');dialog.id='specimen-dialog';dialog.setAttribute('aria-labelledby','specimen-species');
  const styles=doc.createElement('style');styles.textContent=styleText;
  const header=doc.createElement('div');header.className='specimen-heading';
  const title=doc.createElement('h2');title.id='specimen-species';
  const mass=doc.createElement('span');mass.id='specimen-mass';
  const dismiss=doc.createElement('button');dismiss.id='specimen-close';dismiss.type='button';dismiss.textContent='返回鱼护';
  const stage=doc.createElement('div'),hint=doc.createElement('p');hint.id='specimen-help';hint.setAttribute('role','status');
  header.append(title,mass,dismiss);dialog.append(styles,header,stage,hint);doc.body.append(dialog);
  let record=null,viewer=null,trigger=null,generation=0,loading=false,error=null,disposed=false,canvas=null;
  function active(){return dialog.open && win.innerWidth>win.innerHeight && !doc.hidden && doc.hasFocus();}
  function close(){
    if(!record)return;
    const id=record.id;
    generation++;record=null;loading=false;error=null;
    viewer?.dispose();viewer=null;canvas=null;stage.replaceChildren();
    if(dialog.open)dialog.close();
    delete dialog.dataset.fishId;delete dialog.dataset.speciesId;delete dialog.dataset.massMg;
    const restore=trigger?.isConnected?trigger:findButton?.(id);
    if(canOpen() && !doc.hidden && doc.hasFocus())restore?.focus({preventScroll:true});
    trigger=null;
  }
  function reconcile(){
    if(!record)return;
    const match=confirmedSpecimen(readSnapshot(),record.id);
    if(!match || match.massMg!==record.massMg || match.speciesId!==record.speciesId || !canOpen() ||
        !dialog.open || win.innerWidth<=win.innerHeight){close();return;}
    viewer?.setActive(active());
  }
  function fail(ticket){
    if(ticket!==generation || !record)return;
    loading=false;error='specimen-unavailable';viewer?.dispose();viewer=null;
    stage.replaceChildren();hint.textContent='当前无法显示三维鱼体。可返回鱼护查看插画。';
  }
  function open(id,button=null){
    if(disposed || win.innerWidth<=win.innerHeight || doc.hidden || !doc.hasFocus() || !canOpen())return false;
    const current=confirmedSpecimen(readSnapshot(),id);if(!current)return false;
    if(record?.id===id)return true;
    if(record)close();
    trigger=button;record=current;error=null;loading=true;const ticket=++generation;
    title.textContent=fishName(current.speciesId);mass.textContent=specimenMass(current.massMg);
    dialog.dataset.fishId=current.id;dialog.dataset.speciesId=current.speciesId;dialog.dataset.massMg=current.massMg;
    canvas=doc.createElement('canvas');canvas.id='specimen-canvas';canvas.tabIndex=0;
    canvas.setAttribute('aria-label',title.textContent+'三维鱼体');canvas.setAttribute('aria-describedby',hint.id);
    stage.replaceChildren(canvas);hint.textContent='正在打开鱼体…';
    try{dialog.showModal();dismiss.focus({preventScroll:true});}catch{close();return false;}
    Promise.resolve().then(load).then(module=>{
      if(ticket!==generation || disposed)return;
      reconcile();if(!record||ticket!==generation)return;
      const next=module.createSpecimenViewer(canvas,current.speciesId,{onFault:()=>fail(ticket)});
      if(ticket!==generation||error){next.dispose();return;}
      viewer=next;loading=false;
      hint.textContent='拖动旋转，滚轮缩放。选中鱼体后也可用方向键和加减键，Esc 返回鱼护。';
      viewer.setActive(active());
    }).catch(()=>fail(ticket));
    return true;
  }
  function key(e){
    if(!record)return;
    e.stopPropagation();
    if(e.type==='keydown' && e.key==='Escape'){e.preventDefault();close();}
  }
  function cancel(e){e.preventDefault();close();}
  function blur(){viewer?.setActive(false);}
  function closed(){if(!dialog.open)close();}
  dismiss.addEventListener('click',close);dialog.addEventListener('cancel',cancel);dialog.addEventListener('close',closed);
  dialog.addEventListener('keydown',key);dialog.addEventListener('keyup',key);
  doc.addEventListener('visibilitychange',reconcile);win.addEventListener('resize',reconcile);win.addEventListener('focus',reconcile);win.addEventListener('blur',blur);
  return {open,close,reconcile,
    state(){return {open:Boolean(record),loading,error,fish:record&&{...record},viewer:viewer?.state()??null};},
    buttonFor(id){
      if(disposed||!confirmedSpecimen(readSnapshot(),id))return null;
      const b=doc.createElement('button');b.type='button';b.dataset.specimenId=id;b.textContent='查看鱼体';
      b.addEventListener('click',e=>{e.stopPropagation();open(id,b);});return b;
    },
    dispose(){
      if(disposed)return;close();disposed=true;
      doc.removeEventListener('visibilitychange',reconcile);win.removeEventListener('resize',reconcile);
      win.removeEventListener('focus',reconcile);win.removeEventListener('blur',blur);dialog.remove();
    }
  };
}

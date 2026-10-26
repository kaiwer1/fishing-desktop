import { InputSession } from './input-session.mjs';
import { ManualEvidence } from './manual-evidence.mjs';

// This panel only observes input. Counter writes still use the owner lease path.
export function mountManualInput({documentId,container,send,onCancel}) {
  const input=new InputSession(),evidence=new ManualEvidence(),captured=new Set();let revision=0;
  container.innerHTML='<h2>桌面集成输入记录</h2><p id="manual-next" role="status" aria-live="polite"></p><ul id="manual-checks"></ul><div id="manual-surface" tabindex="0" role="region" aria-label="集成输入测试区域">在此绿色区域内操作<pre id="manual-state"></pre></div><small>记录仅限本轮输入，不代表图标/右键菜单或最终游戏通过。重载会清零本页提示。</small>';
  const surface=container.querySelector('#manual-surface'),checks=container.querySelector('#manual-checks'),next=container.querySelector('#manual-next');
  const labels={left:'左键',right:'右键',middle:'中键',wheel:'滚轮',key:'普通键',return:'按住切窗返回'};
  for(const [name,label] of Object.entries(labels)){const item=document.createElement('li');item.dataset.check=name;item.textContent=label+'：待操作';checks.append(item);}
  function release() {for(const id of captured)if(surface.hasPointerCapture(id))surface.releasePointerCapture(id);captured.clear();}
  function report(kind,extra={}) {
    const data={kind,documentId,...input.snapshot(),...extra};evidence.add(data);const value=evidence.snapshot();
    const complete={left:value.pointer.left.matchedPairs>0,right:value.pointer.right.matchedPairs>0,middle:value.pointer.middle.matchedPairs>0,
      wheel:value.trustedWheelEvents>0,key:value.trustedKeyEvents>0,return:value.heldLeftCancelObserved && value.returnedDisabledObserved};
    for(const [name,done] of Object.entries(complete)){const item=checks.querySelector(`[data-check="${name}"]`);item.textContent=labels[name]+(done?'：已记录':'：待操作');item.dataset.complete=String(done);}
    next.textContent=value.evidenceComplete?'六项动作已记录；还须正常退出并核对宿主记录。':
      value.heldLeftCancelObserved && !value.returnedDisabledObserved?'已记录按住后取消；松开左键并返回，保持交互关闭。':
      !input.enabled?'点进本窗口，点击上方“开启交互”，再在绿色区域操作。':
      !complete.return && Object.entries(complete).filter(([name])=>name!=='return').every(([,done])=>done)?'在绿色区域按住左键，Alt+Tab 切窗、松开，再返回；不要重新启用。':
      '请依次在绿色区域按下并松开左/右/中键、滚动滚轮，再按普通字母键。';
    container.querySelector('#manual-state').textContent=`交互: ${input.enabled} · 按住: ${input.held.size}\n按下/松开: ${input.presses}/${input.releases} · 滚轮: ${input.wheels} · 键盘: ${input.keys}`;
    send(data);
  }
  function cancel(reason) {input.cancel(reason);release();report('cancel');}
  function pointer(event) {
    const changes=input.pointerButtons(event.pointerId,event.buttons,event.type==='pointerdown');if(!changes.length)return;
    event.preventDefault();
    if(input.hasPointer(event.pointerId) && !surface.hasPointerCapture(event.pointerId)){
      try{surface.setPointerCapture(event.pointerId);captured.add(event.pointerId);}catch{onCancel('capture-failed');return;}
    }
    for(const change of changes)report(change.kind,{button:change.button,sourceEvent:event.type,trusted:event.isTrusted});
    if(!input.hasPointer(event.pointerId) && surface.hasPointerCapture(event.pointerId))surface.releasePointerCapture(event.pointerId);
  }
  for(const name of ['pointerdown','pointermove','pointerup'])surface.addEventListener(name,pointer);
  surface.addEventListener('pointercancel',()=>onCancel('pointercancel'));
  surface.addEventListener('lostpointercapture',event=>{captured.delete(event.pointerId);if(input.hasPointer(event.pointerId))onCancel('lostcapture');});
  surface.addEventListener('contextmenu',event=>{if(input.enabled)event.preventDefault();});
  surface.addEventListener('wheel',event=>{if(input.enabled){event.preventDefault();input.wheel();report('wheel',{trusted:event.isTrusted});}},{passive:false});
  surface.addEventListener('keydown',event=>{if(input.enabled && event.key!=='Escape'){input.key();report('key',{trusted:event.isTrusted});}});
  window.addEventListener('focus',event=>report('focus',{trusted:event.isTrusted}));
  report('ready');
  return {
    cancel,
    setEnabled(enabled,reason='host-revoke') {
      if(enabled){input.setMode(true,++revision,'owner-lease',input.cancellationEpoch);surface.focus();report('mode');}
      else{input.cancel(reason);release();report('mode');}
    },
    state:()=>evidence.snapshot()
  };
}

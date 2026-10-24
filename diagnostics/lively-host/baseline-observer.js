// Load before the built module, only in a disposable exported baseline copy.
// Observes render calls, selected status text and host callbacks; no game input/save access.
(() => {
  const run = crypto.randomUUID();
  let drawCalls = 0, renderedCallbacks = 0, paused = null, drawingBuffer = null;
  const callbackCosts = [];
  const timestampAges = [];
  const p95 = values => values.length ? [...values].sort((a,b)=>a-b)[Math.ceil(values.length*.95)-1] : null;
  const originalRequestAnimationFrame = window.requestAnimationFrame.bind(window);
  // Count callbacks that actually submit GL draws, not browser refresh callbacks.
  // This measures application submission; it is NOT presented-frame FPS.
  window.requestAnimationFrame = callback => originalRequestAnimationFrame(time => {
    const before = drawCalls, started = performance.now();
    try { return callback(time); }
    finally {
      if (drawCalls > before) {
        renderedCallbacks++;
        callbackCosts.push(performance.now() - started);
        if (Number.isFinite(time)) timestampAges.push(started-time);
        if (callbackCosts.length > 120) callbackCosts.shift();
        if (timestampAges.length > 120) timestampAges.shift();
      }
    }
  });
  const snapshot = () => ({
    run, drawCalls, renderedCallbacks, paused, width:innerWidth, height:innerHeight, drawingBuffer,
    callbackCpuMsP95:p95(callbackCosts), callbackTimestampAgeMsP95:p95(timestampAges),
    requestedSettings:{quality:document.querySelector('#quality')?.value,scale:document.querySelector('#resolution')?.value,fps:document.querySelector('#frame-limit')?.value},
    weather:document.querySelector('#map-weather')?.textContent,
    phase:document.querySelector('#phase-title')?.textContent,
    fallbackVisible:document.querySelector('#fallback')?.hidden === false,
  });
  const send = (kind, data = {}) => fetch('http://127.0.0.1:43817/event', {
    method:'POST', body:JSON.stringify({ kind, ...snapshot(), ...data }),
  }).catch(() => {});
  for (const name of ['WebGLRenderingContext', 'WebGL2RenderingContext']) {
    const proto = window[name]?.prototype;
    if (!proto) continue;
    for (const method of ['drawArrays','drawElements','drawArraysInstanced','drawElementsInstanced']) {
      const original = proto[method];
      if (typeof original !== 'function') continue;
      proto[method] = function (...args) {
        drawCalls++;
        drawingBuffer = { width:this.drawingBufferWidth, height:this.drawingBufferHeight };
        return original.apply(this,args);
      };
    }
  }
  addEventListener('error', e => send('error', { message:String(e.message).slice(0,512) }));
  addEventListener('unhandledrejection', e => send('error', { message:String(e.reason).slice(0,512) }));
  const waiter = setInterval(() => {
    const original = window.livelyWallpaperPlaybackChanged;
    if (typeof original !== 'function') return;
    clearInterval(waiter);
    window.livelyWallpaperPlaybackChanged = data => {
      const value = typeof data === 'string' ? JSON.parse(data) : data;
      paused = value.IsPaused;
      original(data);
      send('pause', { callback:value });
    };
    send('boot', { observer:true });
    setInterval(() => send('heartbeat', { observer:true }), 1000);
  }, 25);
})();

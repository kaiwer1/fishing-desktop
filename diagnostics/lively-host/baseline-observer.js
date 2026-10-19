// Load before the built module, only in a disposable exported baseline copy.
// Observes render calls, selected status text and host callbacks; no game input/save access.
(() => {
  const run = crypto.randomUUID();
  let drawCalls = 0, paused = null;
  const snapshot = () => ({
    run, drawCalls, paused, width:innerWidth, height:innerHeight,
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
      proto[method] = function (...args) { drawCalls++; return original.apply(this,args); };
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

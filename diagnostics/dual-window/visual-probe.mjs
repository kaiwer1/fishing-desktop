// Small rendering fixture, not game content. It allocates only while explicitly
// started and has one cancellable RAF. CapturePreview covers this WebView only.
export function createVisualProbe(document, requestFrame = requestAnimationFrame, cancelFrame = cancelAnimationFrame) {
  let canvas = null, context = null, pending = null, running = false, frames = 0, generation = 0;
  const state = () => ({ running, frames });
  const schedule = () => {
    const current = generation;
    pending = requestFrame(() => draw(current));
  };
  const draw = current => {
    if (!running || current !== generation) return;
    pending = null;
    frames++;
    context.fillStyle = '#183035';
    context.fillRect(0, 0, canvas.width, canvas.height);
    context.fillStyle = '#71d9a5';context.fillRect(32, 32, 120, 80);
    context.fillStyle = '#477db7';context.fillRect(200, 32, 120, 80);
    context.fillStyle = '#eff5e7';context.font = '28px sans-serif';
    context.fillText('P0 · desktop render diagnostic', 32, 170);
    context.fillText(`Frames: ${frames}`, 32, 215);
    context.fillStyle = '#71d9a5';context.fillRect(32 + (frames % 180), 260, 28, 28);
    schedule();
  };
  return {
    state,
    start() {
      if (running) return state();
      if (!canvas) {
        canvas = document.createElement('canvas');
        canvas.width = document.documentElement.clientWidth;
        canvas.height = document.documentElement.clientHeight;
        canvas.style.cssText = 'position:fixed;inset:0;width:100%;height:100%;pointer-events:none';
        document.body.append(canvas);context = canvas.getContext('2d');
      }
      running = true;generation++;schedule();return state();
    },
    stop() {
      running = false;generation++;
      if (pending !== null) cancelFrame(pending);
      pending = null;return state();
    },
  };
}

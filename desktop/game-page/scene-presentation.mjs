// The renderer only receives display snapshots. Both views use the same input
// and host commands, including cancellation when a held surface disappears.
export class ScenePresentation {
  #canvas; #load; #projection; #canRender; #cancelHeld; #changed;
  #renderer = null; #enabled = false; #preferScene = true; #loading = false;
  #fault = null; #generation = 0; #active = null; #closed = false;
  constructor({ canvas, load, projection, canRender, cancelHeld, changed }) {
    this.#canvas = canvas; this.#load = load; this.#projection = projection;
    this.#canRender = canRender; this.#cancelHeld = cancelHeld; this.#changed = changed;
  }
  get visible() { return Boolean(this.#enabled && this.#preferScene && this.#renderer && !this.#fault); }
  state() { return { mode: this.visible ? 'scene' : 'map', enabled: this.#enabled, loading: this.#loading,
    fault: this.#fault, renderer: this.#renderer?.state() ?? null }; }
  configure(enabled) {
    if (this.#closed || enabled === this.#enabled) return;
    this.#enabled = enabled;
    if (enabled) { if (this.#preferScene && !this.#fault) this.#open(); }
    else { if (this.visible || this.#renderer) this.#cancelHeld(); this.#drop(); }
    this.#changed();
  }
  showScene(show) {
    if (!this.#enabled || this.#fault || this.#closed || this.#preferScene === show) return;
    this.#cancelHeld(); this.#preferScene = show;
    if (show && !this.#renderer) this.#open();
    else { this.#changed(); if (show) this.resize(); this.refresh(); }
  }
  async #open() {
    if (this.#loading || this.#renderer) return;
    const generation = ++this.#generation;
    this.#loading = true; this.#changed();
    try {
      const { createGameScene } = await this.#load();
      if (generation !== this.#generation || this.#closed) return;
      const renderer = createGameScene(this.#canvas, { onFault: reason => {
        if (generation === this.#generation) this.#fail(reason);
      } });
      if (generation !== this.#generation) { renderer.dispose(); return; }
      if (this.#preferScene) this.#cancelHeld();
      this.#renderer = renderer; this.#loading = false; this.#changed(); this.resize(); this.refresh();
    } catch { if (generation === this.#generation) this.#fail('webgl-unavailable'); }
  }
  refresh() {
    const renderer = this.#renderer;
    if (!renderer) return;
    try {
      if (this.visible) renderer.update(this.#projection());
      if (renderer !== this.#renderer) return;
      const active = this.visible && this.#canRender();
      if (active !== this.#active) { this.#active = active; renderer.setActive(active); }
    } catch { this.#fail('webgl-render-failed'); }
  }
  resize() { if (this.visible) { try { this.#renderer.resize(); } catch { this.#fail('webgl-render-failed'); } } }
  pickAim(x, y) { return this.visible ? this.#renderer.pickAim(x, y) : null; }
  projectAim(u, v) { return this.visible ? this.#renderer.projectAim(u, v) : null; }
  #fail(reason) {
    if (this.#closed || this.#fault) return;
    this.#fault = reason; this.#cancelHeld(); this.#drop(); this.#changed();
  }
  #drop() {
    this.#generation++; this.#loading = false; this.#active = null;
    const renderer = this.#renderer; this.#renderer = null; renderer?.dispose();
  }
  dispose() { this.#closed = true; this.#enabled = false; this.#drop(); this.#changed(); }
}

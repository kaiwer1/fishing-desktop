const validId = id => typeof id === 'string' && /^[a-z0-9][a-z0-9._-]{0,95}$/.test(id);
const validText = (text, limit) => typeof text === 'string' && text.trim().length > 0 &&
  text.length <= limit && !/[\u0000-\u001f\u007f]/.test(text);

function copySpot(s) {
  return { id: s.id, name: s.name, region: s.region, description: s.description, species: [...s.species] };
}

function validCatalog(state) {
  if (!state || !Array.isArray(state.spots) || state.spots.length > 128) return false;
  if (!state.spots.length) return state.activeId === null;
  const ids = new Set();
  for (const spot of state.spots) {
    if (!spot || !validId(spot.id) || ids.has(spot.id) || !validText(spot.name, 96) ||
        !validText(spot.region, 128) || !validText(spot.description, 1024) ||
        !Array.isArray(spot.species) || spot.species.length > 128 || !spot.species.every(fish => validText(fish, 96))) return false;
    ids.add(spot.id);
  }
  return ids.has(state.activeId);
}

export class SpotMenu {
  #documentId; #canSelect; #send; #document; #status; #list; #fishArt;
  #state = null; #revision = 0; #pending = null; #error = ''; #catalog = ''; #buttons = new Map();
  constructor({ documentId, container, canSelect, send, fishArt }) {
    this.#documentId = documentId; this.#canSelect = canSelect; this.#send = send;
    this.#fishArt = fishArt;
    this.#document = container.ownerDocument ?? document;
    const heading = this.#document.createElement('h2'); heading.textContent = '钓点';
    this.#status = this.#document.createElement('p'); this.#status.setAttribute('role', 'status');
    this.#list = this.#document.createElement('div'); this.#list.className = 'spot-list';
    container.replaceChildren(heading, this.#status, this.#list);
    this.render();
  }
  get revision() { return this.#revision; }
  get activeId() { return this.#state?.activeId ?? null; }
  get pending() { return this.#pending; }
  state() { return this.#state && { activeId: this.#state.activeId, spots: this.#state.spots.map(copySpot) }; }
  apply(message) {
    if (message?.kind !== 'game-spot-state' || message.documentId !== this.#documentId ||
        !Number.isSafeInteger(message.revision) || message.revision <= this.#revision || !validCatalog(message.state)) return false;
    this.#revision = message.revision;
    this.#state = { activeId: message.state.activeId, spots: message.state.spots.map(copySpot) };
    if (this.#pending && !this.#state.spots.some(spot => spot.id === this.#pending)) {
      this.#pending = null; this.#error = '所选钓点已关闭，请重新选择。';
    } else if (this.#state.activeId === this.#pending) {
      this.#pending = null; this.#error = '';
    }
    const catalog = JSON.stringify(this.#state.spots);
    if (catalog !== this.#catalog) {
      this.#catalog = catalog; this.#buttons.clear();
      this.#list.replaceChildren(...this.#state.spots.map(spot => this.#card(spot)));
    }
    this.render(); return true;
  }
  reject(message) {
    if (!this.#pending) return false;
    this.#pending = null; this.#error = validText(message, 256) ? message : '未能切换钓点，请再试一次。';
    this.render(); return true;
  }
  cancelPending() { this.#pending = null; this.#error = ''; this.render(); }
  #card(spot) {
    const el = tag => this.#document.createElement(tag);
    const card = el('article'), title = el('h3'), region = el('small'), description = el('p'), species = el('div'), button = el('button');
    title.textContent = spot.name; region.textContent = spot.region; description.textContent = spot.description;
    const plates = spot.species.map(name => this.#fishArt?.(this.#document, name));
    if (plates.some(Boolean)) {
      species.className = 'spot-species'; species.setAttribute('aria-label', '可钓鱼种');
      spot.species.forEach((name, i) => {
        const figure = el('figure'), caption = el('figcaption'); caption.textContent = name;
        if (plates[i]) figure.append(plates[i]); figure.append(caption); species.append(figure);
      });
    } else species.textContent = spot.species.length ? `可钓鱼种 · ${spot.species.join('、')}` : '鱼种待调查';
    button.type = 'button'; button.dataset.spotId = spot.id; button.id = `spot-select-${spot.id}`;
    button.setAttribute('aria-label', `选择钓点 · ${spot.name}`);
    button.addEventListener('click', event => {
      if (event.detail > 1 || this.#buttons.get(spot.id) !== button || this.#canSelect() !== true ||
          this.#pending || this.#state?.activeId === spot.id) return;
      this.#pending = spot.id; this.#error = ''; this.render();
      try { if (this.#send(spot.id) === false) this.reject('未能发送钓点选择，请再试一次。'); }
      catch { this.reject('未能确认钓点选择，请检查当前窗口。'); }
    });
    button.addEventListener('keydown', event => { if (event.repeat && ['Enter','Space'].includes(event.code)) event.preventDefault(); });
    this.#buttons.set(spot.id, button); card.append(title, region, description, species, button); return card;
  }
  render() {
    const gate = this.#state ? this.#canSelect() : false;
    const active = this.#state?.spots.find(spot => spot.id === this.#state.activeId);
    const pending = this.#state?.spots.find(spot => spot.id === this.#pending);
    this.#status.textContent = !this.#state ? '等候钓点信息…' : !active ? '当前没有开放的钓点。' :
      this.#error || (pending ? `正在前往${pending.name}…` : gate === true ? `当前钓点 · ${active.name}` :
        validText(gate, 256) ? gate : '当前只能查看钓点。');
    for (const [id, button] of this.#buttons) {
      const current = id === active?.id;
      button.textContent = current ? '当前钓点' : id === this.#pending ? '切换中…' : '前往钓点';
      button.setAttribute('aria-current', current ? 'location' : 'false');
      button.disabled = current || Boolean(this.#pending) || gate !== true;
    }
  }
}

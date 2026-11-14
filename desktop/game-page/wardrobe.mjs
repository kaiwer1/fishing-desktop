import { formatMinor } from './shop-view.mjs';

const key = s => typeof s === 'string' && /^[a-z0-9][a-z0-9._-]{0,95}$/.test(s);
const label = (s, max) => typeof s === 'string' && s.trim().length > 0 && s.length <= max && !/[\u0000-\u001f\u007f]/.test(s);
const money = s => typeof s === 'string' && /^(0|[1-9]\d{0,18})$/.test(s) && (s.length < 19 || s <= '9223372036854775807');
const cloneItem = i => ({ id: i.id, name: i.name, description: i.description, priceMinor: i.priceMinor, owned: i.owned, equipped: i.equipped, color: i.color });
function valid(s) {
  if (!s || typeof s.available !== 'boolean' || !Array.isArray(s.items) || s.items.length > 128 ||
      !(s.activeId === null || key(s.activeId))) return false;
  const seen = new Set();
  for (const i of s.items) {
    if (!i || !key(i.id) || seen.has(i.id) || !label(i.name, 96) || !label(i.description, 1024) ||
        !Number.isSafeInteger(i.priceMinor) || i.priceMinor <= 0 || typeof i.owned !== 'boolean' || typeof i.equipped !== 'boolean' ||
        i.equipped !== (i.id === s.activeId) || (i.equipped && !i.owned) || typeof i.color !== 'string' || !/^#[0-9a-f]{6}$/i.test(i.color)) return false;
    seen.add(i.id);
  }
  return s.activeId === null || seen.has(s.activeId);
}

export const wardrobeMessages = {
  'wardrobe-busy': '请先结束当前一竿或完成正在进行的菜单操作。', 'wardrobe-unavailable': '当前服装店未开放。',
  'wardrobe-stale': '存档进度已更新，请重新选择。', 'wardrobe-already-owned': '已拥有这件衣服。',
  'wardrobe-not-owned': '请先购买这件衣服。', 'wardrobe-insufficient-funds': '银币不足。',
  'wardrobe-unchanged': '穿戴状态已经相同。', 'wardrobe-not-written': '本次操作未写入，可以重新选择。',
  'save-outcome-unknown': '服装操作的保存结果待核验，请退出后重新打开原存档。',
  'control-required': '请重新开启交互。', 'domain-paused': '当前已暂停。', 'invalid-intent': '服装操作未获确认。',
};

export class Wardrobe {
  #documentId; #read; #post; #newId; #changed; #el;
  #state = null; #revision = 0; #quote = null; #pending = null; #note = ''; #faulted = false;
  #catalog = ''; #cards = new Map(); #status; #list; #confirmation; #price; #confirm;
  constructor({ documentId, container, readContext, post, newTransportId, changed }) {
    this.#documentId = documentId; this.#read = readContext; this.#post = post; this.#newId = newTransportId; this.#changed = changed;
    const doc = container.ownerDocument ?? document;
    this.#el = tag => doc.createElement(tag);
    const heading = this.#el('h2'); heading.textContent = '服装商店';
    this.#status = this.#el('p'); this.#status.id = 'wardrobe-status'; this.#status.setAttribute('role', 'status');
    this.#list = this.#el('div'); this.#list.className = 'wardrobe-list';
    this.#confirmation = this.#el('div'); this.#confirmation.className = 'wardrobe-check'; this.#confirmation.hidden = true;
    this.#price = this.#el('p');
    this.#confirm = this.#button('wardrobe-confirm', '确认购买', () => this.#submit('buy', this.#quote?.id));
    const cancel = this.#button('wardrobe-cancel', '取消', () => { this.#quote = null; this.#note = ''; this.#changed(); });
    this.#confirmation.append(this.#price, this.#confirm, cancel);
    container.replaceChildren(heading, this.#status, this.#list, this.#confirmation);
  }
  get revision() { return this.#revision; }
  get busy() { return Boolean(this.#quote || this.#pending); }
  get pending() { return this.#pending ? { action: this.#pending.action, itemId: this.#pending.itemId } : null; }
  get sleeveColor() { return this.#state?.items.find(i => i.equipped)?.color ?? null; }
  state() { return this.#state && { available: this.#state.available, activeId: this.#state.activeId, items: this.#state.items.map(cloneItem) }; }
  apply(m) {
    if (m?.kind !== 'game-wardrobe-state' || m.documentId !== this.#documentId || !Number.isSafeInteger(m.revision) ||
        m.revision <= this.#revision || !valid(m.state)) return false;
    this.#revision = m.revision; this.#state = { available: m.state.available, activeId: m.state.activeId, items: m.state.items.map(cloneItem) };
    this.reconcile(); return true;
  }
  #canAct(c) { return !this.#faulted && this.#state?.available === true && c.allowed === true && Boolean(c.lease) && money(c.sequence); }
  reconcile() {
    const c = this.#read(), q = this.#quote, p = this.#pending;
    if (q) {
      const item = this.#state?.items.find(i => i.id === q.id);
      if (!this.#canAct(c) || c.lease !== q.lease || c.sequence !== q.sequence || !item || item.owned || item.priceMinor !== q.priceMinor) {
        this.#quote = null; this.#note = '购买条件已变化，请重新查看价格。';
      }
    }
    if (!p) return;
    if (!this.#canAct(c) || p.lease !== c.lease) { this.#pending = null; this.#note = ''; return; }
    if (!p.acknowledged || this.#revision <= p.revision || BigInt(c.sequence) <= BigInt(p.sequence)) return;
    const current = this.#state.items.find(i => i.id === p.itemId);
    if (p.action === 'buy' ? current?.owned : p.action === 'wear' ? current?.equipped : this.#state.activeId === null) {
      this.#pending = null; this.#note = p.action === 'buy' ? '购买已确认，可以穿上新衣。' : p.action === 'wear' ? '已穿上所选服装。' : '已换回普通衣袖。';
    }
  }
  reply(m) {
    if (m?.documentId !== this.#documentId || m.kind !== 'game-reply' || !this.#pending || m.transportId !== this.#pending.transportId) return false;
    if (m.code === 'wardrobe-updated') this.#pending.acknowledged = true;
    else {
      this.#pending = null; this.#faulted = m.code === 'save-outcome-unknown';
      this.#note = wardrobeMessages[m.code] ?? '服装操作未获确认。';
    }
    this.reconcile(); return true;
  }
  #button(id, title, act) {
    const b = this.#el('button'); b.type = 'button'; b.id = id; b.textContent = title;
    b.addEventListener('click', e => { if (e.detail <= 1 && !b.disabled) act(); });
    b.addEventListener('keydown', e => { if (e.repeat && ['Space','Enter'].includes(e.code)) e.preventDefault(); }); return b;
  }
  #select(itemId) {
    this.reconcile(); const c = this.#read(), i = this.#state?.items.find(item => item.id === itemId);
    if (this.busy || !this.#canAct(c) || !i) return;
    if (i.owned) { this.#submit(i.equipped ? 'remove' : 'wear', i.id); return; }
    if (!money(c.walletMinor) || BigInt(c.walletMinor) < BigInt(i.priceMinor)) return;
    this.#quote = { id: i.id, lease: c.lease, sequence: c.sequence, priceMinor: i.priceMinor };
    this.#note = ''; this.#changed();
  }
  #submit(action, itemId) {
    this.reconcile(); const c = this.#read(), item = this.#state?.items.find(i => i.id === itemId);
    if (!item || this.#pending || !this.#canAct(c)) return;
    if (action === 'buy' ? this.#quote?.id !== itemId || item.owned || !money(c.walletMinor) || BigInt(c.walletMinor) < BigInt(item.priceMinor) :
        this.#quote || !item.owned || item.equipped !== (action === 'remove')) return;
    const transportId = this.#newId();
    this.#quote = null; this.#note = '';
    this.#pending = { action, itemId, transportId, lease: c.lease, sequence: c.sequence, revision: this.#revision, acknowledged: false };
    try { this.#post({ kind: 'game-wardrobe', documentId: this.#documentId, lease: c.lease, transportId, action, itemId, expectedSequence: c.sequence }); }
    catch { this.#pending = null; this.#note = '未能发送服装操作。'; }
    this.#changed();
  }
  #card(i) {
    const el = this.#el, card = el('article'), title = el('h3'), description = el('p'), price = el('p');
    title.textContent = i.name; description.textContent = i.description;
    const button = this.#button('', '', () => {
      if (this.#cards.get(i.id)?.button === button) this.#select(i.id);
    });
    card.append(title, description, price, button); this.#cards.set(i.id, { price, button }); return card;
  }
  render() {
    const c = this.#read(), s = this.#state, items = s?.items ?? [];
    const catalog = JSON.stringify(items.map(i => [i.id,i.name,i.description]));
    if (catalog !== this.#catalog) { this.#catalog = catalog; this.#cards.clear(); this.#list.replaceChildren(...items.map(i => this.#card(i))); }
    this.#status.textContent = this.#faulted ? wardrobeMessages['save-outcome-unknown'] : this.#pending ? '等待存档确认…' : this.#note ||
      (!s?.available ? '当前服装店未开放。' : `当前穿戴 · ${items.find(i => i.equipped)?.name ?? '普通衣袖'}`);
    for (const item of items) {
      const { price, button } = this.#cards.get(item.id), action = !item.owned ? 'buy' : item.equipped ? 'remove' : 'wear';
      const poor = !item.owned && (!money(c.walletMinor) || BigInt(c.walletMinor) < BigInt(item.priceMinor));
      price.textContent = item.owned ? item.equipped ? '已穿戴' : '已拥有' : `${formatMinor(String(item.priceMinor))} 银币${poor ? ' · 余额不足' : ''}`;
      button.id = `wardrobe-${action}-${item.id}`; button.textContent = action === 'buy' ? '购买' : action === 'wear' ? '穿上' : '换回普通衣袖';
      button.disabled = !this.#canAct(c) || this.busy || poor;
    }
    this.#confirmation.hidden = !this.#quote;
    if (this.#quote) {
      const item = items.find(i => i.id === this.#quote.id);
      this.#price.textContent = `购买${item.name}，支付 ${formatMinor(String(this.#quote.priceMinor))} 银币？`;
    }
    this.#confirm.disabled = !this.#quote || !this.#canAct(c);
  }
}

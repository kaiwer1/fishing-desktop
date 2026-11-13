import { formatMinor } from './shop-view.mjs';

const names = { 'single-hook': '单钩', 'float-3g': '基础浮漂', 'sinker-2_5g': '2.5 克沉子', 'nylon-018': '0.18 毫米尼龙线' };
const kinds = { hook: '鱼钩', bobber: '浮漂', sinker: '沉子', line: '鱼线' };
const reasons = { 'control-required': '开启交互后可以购买。', 'menu-required': '请先打开菜单。',
  'session-busy': '请先收回这一竿或处理当前鱼。', 'result-pending': '请先处理当前鱼。',
  'spares-not-configured': '此存档尚未配置备件商店。', 'spares-schema-required': '此存档尚未支持备件。',
  'save-outcome-unknown': '保存结果待核验，请退出后重新打开原存档。', 'owner-stopped': '当前存档已关闭。' };
const label = item => names[item.definitionKey] ?? kinds[item.kind];
const size = item => item.kind === 'line' ? ` · ${(item.lineLengthMm / 1000).toLocaleString('zh-CN', { maximumFractionDigits: 3 })} 米整段` : ' · 1 件';

export class SparesPanel {
  #view; #input; #snapshot; #canAct; #changed; #signature = ''; #buttons = [];
  constructor({ view, input, snapshot, canAct, changed }) {
    this.#view = view; this.#input = input; this.#snapshot = snapshot; this.#canAct = canAct; this.#changed = changed;
    for (const [id, action] of [['spares-buy', 'buy'], ['spares-retry', 'retry'], ['spares-cancel', 'cancel']])
      this.#action(document.getElementById(id), event => this.#input[action](event));
  }
  #action(button, action) {
    button.addEventListener('click', event => {
      if (event.detail > 1 || !this.#canAct()) return;
      action(event); this.#changed();
    });
    button.addEventListener('keydown', event => { if (event.repeat && ['Space', 'Enter'].includes(event.code)) event.preventDefault(); });
  }
  render() {
    const $ = id => document.getElementById(id), state = this.#view.state(), input = this.#input.state();
    $('spares').hidden = !this.#snapshot()?.MenuOpen;
    $('spares-status').textContent = !state ? '等待备件信息' : input.faulted ? reasons['save-outcome-unknown'] :
      input.inflight || input.awaitingState ? '正在确认备件操作…' : input.retry ? '上次购买确认未写入，请重试原操作或取消。' :
      state.available ? '购买后放入背包，再到钓组中装配。' : reasons[state.reason] ?? '当前不能购买备件。';
    $('spares-wallet').textContent = `余额 ${formatMinor(state?.walletMinor)} 银币`;
    const items = state?.items ?? [], signature = JSON.stringify(items);
    if (signature !== this.#signature) {
      this.#signature = signature; this.#buttons = [];
      $('spares-items').replaceChildren(...items.map(item => {
        const card = document.createElement('article'), title = document.createElement('h3'), price = document.createElement('p'), button = document.createElement('button');
        title.textContent = label(item); price.textContent = `${formatMinor(item.priceMinor)} 银币${size(item)}`;
        button.id = `spares-quote-${item.sku}`; button.className = 'interactive'; button.textContent = '查看报价';
        this.#action(button, event => this.#input.quote(item.sku, event)); this.#buttons.push(button);
        card.append(title, price, button); return card;
      }));
    }
    const ready = input.ready && !input.inflight && !input.awaitingState && !input.faulted && this.#canAct();
    for (const button of this.#buttons) button.disabled = !ready || Boolean(input.retry);
    $('spares-offer').hidden = !input.quote;
    $('spares-offer-text').textContent = input.quote ? `${label(input.quote)}${size(input.quote)} · 合计 ${formatMinor(input.quote.priceMinor)} 银币。` : '';
    $('spares-buy').disabled = !ready || !input.quote;
    $('spares-retry').hidden = !input.retry; $('spares-retry').disabled = !ready || !input.retry;
    $('spares-cancel').hidden = !input.quote && !input.retry; $('spares-cancel').disabled = !ready;
  }
}

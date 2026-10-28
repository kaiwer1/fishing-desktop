const slotNames = { rod: '鱼竿', reel: '渔轮', hook: '鱼钩', bobber: '浮漂', sinker: '沉子' };
const names = { 'starter-rod': '基础浮漂竿', 'starter-reel': '基础纺车轮', 'nylon-018': '0.18 毫米尼龙线', 'single-hook': '单钩', 'float-3g': '基础浮漂', 'sinker-2_5g': '2.5 克沉子' };
const reasons = { 'control-required': '开启交互后可以换件。', 'menu-required': '打开菜单后可以换件。', 'session-busy': '请先收回这一竿或处理当前鱼。',
  'save-outcome-unknown': '保存结果待核验，请退出后重新打开原存档。', 'equipment-schema-required': '此存档尚未支持装备编辑。', 'owner-stopped': '当前存档已关闭。',
  'result-pending': '请先处理当前尚未确认的结果。', 'rig-busy': '钓组正在使用中', 'persistence-uncertain': '保存结果待核验，暂不能换件',
  'missing-component': '缺少部件', 'missing-line': '渔轮未装鱼线', 'insufficient-line': '已装鱼线长度不足', 'bait-empty': '没有鱼饵', 'stamina-insufficient': '体力不足',
  'configuration-missing': '该装备暂不可用', 'definition-kind-mismatch': '装备类型不匹配', 'incompatible-components': '鱼竿与渔轮不兼容',
  'line-capacity-exceeded': '鱼线超过线杯容量', 'incompatible-bait': '鱼钩不适合当前鱼饵', 'bobber-load-out-of-range': '沉子重量不适合浮漂', 'cast-mass-out-of-range': '钓组重量超出鱼竿适配范围' };
const title = item => names[item.definitionKey] ?? item.definitionKey;
const meters = mm => `${(mm / 1000).toLocaleString('zh-CN', { maximumFractionDigits: 3 })} 米`;

// All displayed ownership comes from the validated host projection. This
// panel sends existing-instance choices only; it never creates equipment.
export class EquipmentPanel {
  #view; #input; #snapshot; #changed; #canAct; #signature = ''; #chosen = new Set();
  constructor({ view, input, snapshot, changed, canAct = () => true }) {
    this.#view = view; this.#input = input; this.#snapshot = snapshot; this.#changed = changed; this.#canAct = canAct;
    const container = document.getElementById('equipment-slots');
    for (const [slot, label] of Object.entries(slotNames)) {
      const row = document.createElement('article'), heading = document.createElement('h3'), current = document.createElement('p'), select = document.createElement('select'), apply = document.createElement('button');
      heading.textContent = label; select.id = `equipment-slot-${slot}`; select.setAttribute('aria-label', label);
      apply.id = `equipment-apply-${slot}`; apply.textContent = '应用'; select.className = apply.className = 'interactive';
      current.id = `equipment-current-${slot}`;
      select.addEventListener('change', () => { this.#chosen.add(select.id); this.render(); });
      this.#action(apply, event => this.#input.replaceSlot(slot, select.value || null, event));
      row.append(heading, current, select, apply); container.append(row);
    }
    const $ = id => document.getElementById(id);
    for (const id of ['equipment-line', 'equipment-reel']) $(id).addEventListener('change', () => { this.#chosen.add(id); this.render(); });
    this.#action($('equipment-move-line'), event => this.#input.moveLine($('equipment-line').value, $('equipment-reel').value || null, event));
    for (const [id, action] of [['equipment-retry', 'retry'], ['equipment-cancel', 'cancel']])
      this.#action($(id), event => this.#input[action](event));
  }
  #action(button, action) {
    button.addEventListener('click', event => {
      if (event.detail > 1 || !this.#canAct()) return;
      action(event); this.render(); this.#changed();
    });
    button.addEventListener('keydown', event => { if (event.repeat && ['Space', 'Enter'].includes(event.code)) event.preventDefault(); });
  }
  #options(id, choices, preferred) {
    const select = document.getElementById(id), old = select.value;
    const preserve = this.#chosen.has(id) && choices.some(([value]) => value === old);
    select.replaceChildren(...choices.map(([value, text]) => { const option = document.createElement('option'); option.value = value; option.textContent = text; return option; }));
    select.value = preserve ? old : choices.some(([value]) => value === preferred) ? preferred : choices[0]?.[0] ?? '';
    if (!preserve || select.value === preferred) this.#chosen.delete(id);
  }
  render() {
    const $ = id => document.getElementById(id), state = this.#view.state(), input = this.#input.state();
    $('equipment').hidden = !this.#snapshot()?.MenuOpen;
    $('equipment-status').textContent = !state ? '等待装备信息' : state.schemaVersion !== 3 ? reasons[state.reason] ?? '此存档尚未支持装备编辑。' :
      input.retry ? '上次换件确认未写入，请重试原操作或取消。' : input.inflight || input.awaitingState ? '正在确认装备变更…' : state.available ? '装配会立即保存；卸下的部件回到背包。' : reasons[state.reason] ?? '当前不能换件。';
    const items = state?.items ?? [], issues = state?.issues ?? [], equipped = slot => items.find(item => item.location === 'rig-slot' && item.slot === slot);
    $('equipment-readiness').textContent = state?.schemaVersion !== 3 ? '' : issues.length ? issues.map(issue => `${slotNames[issue.slot] ? `${slotNames[issue.slot]}：` : ''}${reasons[issue.code] ?? issue.code}`).join('；') : '钓组完整，可在关闭菜单后抛竿。';
    const reel = equipped('reel'), line = items.find(item => item.location === 'reel-spool' && item.reelId === reel?.id);
    $('equipment-line-status').textContent = line ? `当前渔轮：${title(line)} · 剩余 ${meters(line.remainingLineMm)}` : '当前渔轮未装可用鱼线';
    const signature = JSON.stringify(items);
    if (signature !== this.#signature) {
      this.#signature = signature;
      for (const slot of Object.keys(slotNames)) {
        const current = equipped(slot);
        const choices = items.filter(item => item.kind === slot && (item.location === 'inventory' || item.id === current?.id));
        this.#options(`equipment-slot-${slot}`, [['', '卸下 / 空槽'], ...choices.map((item, index) => [item.id, `${title(item)}${item.id === current?.id ? '（已装）' : `（背包 ${index + 1}）`}`])], current?.id ?? '');
      }
      this.#options('equipment-line', items.filter(item => item.kind === 'line' && item.location !== 'lost').map(item => [item.id, `${title(item)} · ${meters(item.remainingLineMm)}${item.location === 'inventory' ? '（背包）' : '（在线杯）'}`]), line?.id);
      this.#options('equipment-reel', [['', '取出到背包'], ...items.filter(item => item.kind === 'reel' && item.location !== 'lost').map((item, index) => [item.id, `${title(item)} ${index + 1}${item.id === reel?.id ? '（已装）' : '（背包）'}`])], reel?.id);
      const inventory = items.filter(item => item.location === 'inventory');
      $('equipment-inventory').textContent = inventory.length ? `背包：${inventory.map(item => `${title(item)}${item.kind === 'line' ? ` ${meters(item.remainingLineMm)}` : ''}`).join('、')}` : '背包暂无卸下的部件';
      const lost = items.filter(item => item.location === 'lost'); $('equipment-lost').textContent = lost.length ? `已损失 ${lost.length} 件，不能再次装备。` : '';
    }
    const ready = input.ready && !input.inflight && !input.awaitingState && !input.faulted && this.#canAct();
    for (const slot of Object.keys(slotNames)) {
      $(`equipment-current-${slot}`).textContent = equipped(slot) ? title(equipped(slot)) : '尚未装配';
      const select = $(`equipment-slot-${slot}`); select.disabled = !ready || Boolean(input.retry);
      $(`equipment-apply-${slot}`).disabled = select.disabled || select.value === (equipped(slot)?.id ?? '');
    }
    $('equipment-line').disabled = $('equipment-reel').disabled = !ready || Boolean(input.retry);
    const selectedLine = items.find(item => item.id === $('equipment-line').value), targetReel = $('equipment-reel').value || null;
    $('equipment-move-line').disabled = !ready || Boolean(input.retry) || !selectedLine || selectedLine.reelId === targetReel || targetReel !== null && items.some(item => item.location === 'reel-spool' && item.reelId === targetReel);
    $('equipment-retry-controls').hidden = !input.retry; $('equipment-retry').disabled = $('equipment-cancel').disabled = !ready;
  }
}

const reasons = { 'control-required': '开启交互后可以帮工。', 'menu-required': '请先打开菜单。',
  'session-busy': '请先结束当前这一竿。', 'result-pending': '请先处理当前鱼。',
  'camp-balance-sufficient': '现有银币可用于商店补给。帮工在余额低于 32 银币时开放。',
  'camp-wallet-ineligible': '现有银币可用于商店补给。帮工在余额低于 32 银币时开放。',
  'camp-creel-not-empty': '鱼护里还有鱼，可以先出售渔获。',
  'camp-schema-required': '此存档尚未支持营地帮工。', 'camp-work-cancelled': '本轮已取消，未结算时间不保留。',
  'save-outcome-unknown': '领取结果待核验，请退出后重新打开原存档。', 'owner-stopped': '当前存档已关闭。' };
const labels = { idle: '可以开始帮工；完成后需点击领取。', working: '正在帮工，请保持当前窗口和菜单开启。',
  ready: '本轮已完成，请领取报酬。', retry: '上次领取确认未写入，请明确重试领取或取消。',
  confirmed: '报酬已确认入账：8.00 银币。', cancelled: '本轮已取消，未结算时间不保留。', unknown: reasons['save-outcome-unknown'] };

export class CampPanel {
  #view; #input; #snapshot; #canAct; #changed;
  constructor({ view, input, snapshot, canAct, changed }) {
    this.#view = view; this.#input = input; this.#snapshot = snapshot; this.#canAct = canAct; this.#changed = changed;
    for (const action of ['start', 'claim', 'cancel']) {
      const button = document.getElementById(`camp-${action}`);
      button.addEventListener('click', event => { if (event.detail > 1 || !this.#canAct()) return; this.#input[action](event); this.#changed(); });
      button.addEventListener('keydown', event => { if (event.repeat && ['Space', 'Enter'].includes(event.code)) event.preventDefault(); });
    }
  }
  render() {
    const $ = id => document.getElementById(id), s = this.#view.state(), input = this.#input.state(), snapshot = this.#snapshot();
    $('camp').hidden = !snapshot?.MenuOpen;
    $('camp-status').textContent = input.faulted ? reasons['save-outcome-unknown'] : !s ? '等待帮工信息' :
      input.inflight || input.awaitingState ? '正在确认帮工操作…' : s.phase === 'confirmed' ? labels.confirmed :
      !s.available ? reasons[s.reason] ?? (snapshot?.CreelCount > 0 ? reasons['camp-creel-not-empty'] : '当前不能开始帮工，可先检查商店补给。') : labels[s.phase];
    $('camp-progress').max = 60000; $('camp-progress').value = s?.elapsedMilliseconds ?? 0;
    $('camp-elapsed').textContent = `${Math.floor((s?.elapsedMilliseconds ?? 0) / 1000)} / 60 秒`;
    const ready = input.ready && !input.inflight && !input.awaitingState && !input.faulted && this.#canAct();
    $('camp-start').disabled = !ready || !['idle', 'cancelled', 'confirmed'].includes(input.phase);
    $('camp-claim').textContent = s?.phase === 'retry' ? '重试领取' : '领取报酬';
    $('camp-claim').disabled = !ready || !['ready', 'retry'].includes(input.phase);
    $('camp-cancel').disabled = !ready || !['working', 'ready', 'retry'].includes(input.phase);
  }
}

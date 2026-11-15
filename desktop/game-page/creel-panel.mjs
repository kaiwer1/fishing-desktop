import { fishFigure, fishName } from './fish-illustration.mjs';
import { formatMinor } from './shop-view.mjs';
import { specimenMass } from './specimen-view.mjs';

const title = fish => fishName(fish.SpeciesKey) ?? fish.SpeciesKey;
const rowKey = fish => JSON.stringify([fish.Id,fish.SpeciesKey,fish.MassMg,fish.SaleValueMinor]);

export function createCreelPanel(doc, inspectButton) {
  const node = id => doc.getElementById(id);
  const list = node('creel'), filter = node('creel-filter'), sort = node('creel-sort'), sell = node('sell');
  const selected = new Map(), controls = new Map();
  let rows = [], filterKey = '', order = 'newest', pageKey, cardsKey, speciesKey, lease, sale;
  let enabled = false, inspect = false, offerInspect = false;

  function visibleRows() {
    const result = rows.filter(fish => !filterKey || fish.SpeciesKey === filterKey);
    // Host rows are in landing order. A later fish wins equal-weight ties.
    if (order !== 'oldest') result.reverse();
    if (order === 'heaviest' || order === 'lightest') result.sort((a,b) => {
      const left = specimenMass(a.MassMg) ? BigInt(a.MassMg) : 0n, right = specimenMass(b.MassMg) ? BigInt(b.MassMg) : 0n;
      return (left < right ? -1 : left > right ? 1 : 0) * (order === 'lightest' ? 1 : -1);
    });
    return result;
  }
  function updateSelection() {
    const picked = [];
    for (const [id,{fish,box,item,view}] of controls) {
      box.checked = selected.has(id); box.disabled = !enabled || Boolean(sale) || !specimenMass(fish.MassMg) || formatMinor(fish.SaleValueMinor) === '—';
      item.dataset.selected = String(box.checked);
      if (view) view.disabled = !inspect;
      if (box.checked) picked.push(fish);
    }
    const total = picked.reduce((value,fish) => value + BigInt(fish.SaleValueMinor),0n);
    const amount = formatMinor(String(total));
    const review = amount === '—' ? '总价超出可结算范围，请减少选择。' :
      picked.length ? `已选 ${picked.length} 条 · 合计 ${amount} 银币` : '勾选要出售的渔获';
    const summary = node('creel-review');
    const line = sale ? '正在核对出售结果…' : review;
    if (summary.textContent !== line) summary.textContent = line;
    const names = node('creel-selected-names');
    names.hidden = !picked.length;
    names.textContent = picked.map(fish => title(fish)+' '+specimenMass(fish.MassMg)).join('、');
    sell.disabled = !enabled || Boolean(sale) || !picked.length || amount === '—';
  }
  function makeCard(fish) {
    const item = doc.createElement('li'), box = doc.createElement('input'), label = doc.createElement('label');
    item.className = 'fish'; item.dataset.fishId = fish.Id; box.type = 'checkbox';
    label.append(box);
    const plate = fishFigure(doc,fish.SpeciesKey);
    if (plate) label.append(plate);
    const name = doc.createElement('strong'), mass = doc.createElement('span'), price = doc.createElement('span');
    name.textContent = title(fish); mass.className = 'fish-mass'; price.className = 'fish-price';
    mass.textContent = specimenMass(fish.MassMg) ?? '重量未知'; price.textContent = formatMinor(fish.SaleValueMinor) + ' 银币';
    label.append(name,mass,price); item.append(label);
    box.setAttribute('aria-label',`选择出售 ${name.textContent}，${mass.textContent}，${price.textContent}`);
    box.addEventListener('change', () => {
      if (!box.disabled && controls.get(fish.Id)?.box === box) {
        if (box.checked) selected.set(fish.Id,rowKey(fish)); else selected.delete(fish.Id);
      }
      updateSelection();
    });
    const view = offerInspect ? inspectButton(fish) : null;
    if (view) item.append(view);
    controls.set(fish.Id,{fish,item,box,view});
    return item;
  }
  function showCards() {
    const visible = visibleRows();
    node('creel-order-label').hidden = visible.length < 2;
    const key = JSON.stringify([offerInspect,visible.map(rowKey)]);
    if (key !== cardsKey) {
      cardsKey = key; controls.clear();
      const cards = visible.map(makeCard);
      if (!cards.length) {
        const empty = doc.createElement('li'); empty.className = 'empty-creel'; empty.textContent = '鱼护为空'; cards.push(empty);
      }
      list.replaceChildren(...cards);
    }
    for (const [id,key] of selected) if (!controls.has(id) || rowKey(controls.get(id).fish) !== key) selected.delete(id);
    updateSelection();
  }
  filter.addEventListener('change', () => { filterKey = filter.value; showCards(); });
  sort.addEventListener('change', () => { order = sort.value; showCards(); });
  return {
    clearSelection() { selected.clear(); updateSelection(); },
    render(snapshot,options) {
      const currentPage = snapshot?.CreelOffset ?? 0;
      if (!options.lease || lease !== options.lease || currentPage !== pageKey) selected.clear();
      lease = options.lease; pageKey = currentPage; enabled = options.enabled;
      inspect = options.inspect; offerInspect = options.offerInspect;
      rows = snapshot?.HasConfirmedState ? (snapshot.Creel ?? []) : [];
      if (sale?.ack && snapshot?.Sequence !== sale.sequence) sale = null;
      const species = [...new Set(rows.map(fish => fish.SpeciesKey))];
      if (!species.includes(filterKey)) filterKey = '';
      const key = JSON.stringify(species);
      if (key !== speciesKey) {
        speciesKey = key;
        filter.replaceChildren(...['',...species].map(id => {
          const opt = doc.createElement('option'); opt.value = id; opt.textContent = id ? fishName(id) ?? id : '全部鱼种'; return opt;
        }));
        filter.value = filterKey;
      }
      const pageSize = rows.length, paginated = pageSize < (snapshot?.CreelCount ?? 0);
      node('creel-filter-label').textContent = paginated ? '本页鱼种' : '鱼种';
      node('creel-sort-label').textContent = paginated ? '本页排列' : '排列';
      node('creel-tools').hidden = pageSize < 2;
      node('creel-species-label').hidden = species.length < 2;
      node('creel-checkout').hidden = !options.offerInspect || !pageSize;
      node('creel-pages').hidden = !paginated;
      sort.value = order;
      showCards();
    },
    begin(transportId,sequence) {
      if (sell.disabled || sale || !enabled || !selected.size) return null;
      const ids = [...selected.keys()];
      sale = {transportId,sequence,ack:false}; updateSelection(); return ids;
    },
    reply(message) {
      if (!sale || message.transportId !== sale.transportId) return;
      if (message.code === 'sale-candidate') { sale.ack = true; selected.clear(); }
      else sale = null;
    }
  };
}

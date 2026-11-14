const prompts = new Map([
  ['sinking', ['钓组下沉', '等铅坠落底。']],
  ['settling', ['触底待稳', '等钓组在水底稳住。']],
  ['settled', ['稳底等口', '看竿尖，留意轻点或下弯。']],
  ['touch', ['竿尖轻点', '鱼在试饵，继续看竿尖。']],
  ['bite', ['竿尖下压', '现在按鼠标右键提竿。']],
  ['hooked', ['已提竿中鱼', '按住鼠标左键收线，松开停止收线。']],
  ['missed', ['错过鱼口', '本次鱼口已过。']],
  ['retrieve-required', ['需要收回钓组', '收回这一竿，再检查钩饵和落点。']],
  ['retrieved', ['钓组已收回', '这一竿已结束。']],
]);

const validSession = id => typeof id === 'string' && id.length === 36 &&
  /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(id) && id !== '00000000-0000-0000-0000-000000000000';

export function bottomPrompt(phase) {
  const prompt=prompts.get(phase);
  return prompt && [...prompt];
}

// The host folds Searching and Approaching into settled. Only a physical
// touch or bite changes the prompt. The amount of bend never selects a phase.
export class BottomView {
  #documentId;
  #revision = 0;
  #state = null;

  constructor(documentId) { this.#documentId = documentId; }
  get revision() { return this.#revision; }
  state() { return this.#state && { ...this.#state }; }

  apply(message) {
    if (!message || message.kind !== 'game-bottom-state' || message.documentId !== this.#documentId ||
        !Number.isSafeInteger(message.revision) || message.revision <= this.#revision) return false;
    const s = message.state;
    if (!s || typeof s !== 'object' || Object.keys(s).length !== 3 ||
        !['sessionId', 'phase', 'tipPermille'].every(key => Object.hasOwn(s, key)) ||
        !Number.isInteger(s.tipPermille) || s.tipPermille < 0 || s.tipPermille > 1000) return false;
    if (s.phase === 'idle') {
      if (s.sessionId !== null || s.tipPermille !== 0) return false;
    } else if (!validSession(s.sessionId) || !prompts.has(s.phase)) return false;
    this.#state = { sessionId: s.sessionId, phase: s.phase, tipPermille: s.tipPermille };
    this.#revision = message.revision;
    return true;
  }

  forSession(currentSessionId) {
    if (!this.#state || this.#state.phase === 'idle' || this.#state.sessionId !== currentSessionId) return null;
    return this.state();
  }
}

function svgElement(doc, tag, attributes) {
  const element = doc.createElementNS('http://www.w3.org/2000/svg', tag);
  for (const [name, value] of Object.entries(attributes)) element.setAttribute(name, String(value));
  return element;
}

export function createBottomTipPanel(doc) {
  const element = doc.createElement('figure');
  element.hidden = true;
  const caption = doc.createElement('figcaption');
  caption.setAttribute('role', 'status');
  const title = doc.createElement('strong');
  const hint = doc.createElement('span');
  caption.append(title, doc.createTextNode('　'), hint);

  const svg = svgElement(doc, 'svg', { viewBox: '0 0 260 88', width: 260, 'aria-hidden': 'true' });
  const background = svgElement(doc, 'rect', { width: 260, height: 88, rx: 7, fill: '#202d2f' });
  const shaft = svgElement(doc, 'path', { d: 'M9 78 Q53 56 105 35', fill: 'none',
    stroke: '#adbab1', 'stroke-width': 4, 'stroke-linecap': 'round' });
  const tip = svgElement(doc, 'path', { fill: 'none', stroke: '#edb879', 'stroke-width': 2.8,
    'stroke-linecap': 'round' });
  const line = svgElement(doc, 'path', { fill: 'none', stroke: '#c9d6d1', 'stroke-width': 1, opacity: 0.75 });
  svg.append(background, line, shaft, tip);
  element.append(svg, caption);
  let phase = null;
  let tipPermille = null;

  function render(view, currentSessionId) {
    const state = view.forSession(currentSessionId);
    element.hidden = state === null;
    if (!state) {
      phase = null;
      tipPermille = null;
      return;
    }
    if (phase !== state.phase) {
      phase = state.phase;
      [title.textContent, hint.textContent] = prompts.get(phase);
      svg.setAttribute('display', phase === 'retrieved' ? 'none' : 'block');
    }
    if (tipPermille !== state.tipPermille) {
      tipPermille = state.tipPermille;
      const bend = tipPermille / 1000;
      const endX = 220 - 16 * bend;
      const endY = 15 + 43 * bend;
      tip.setAttribute('d', `M105 35 C142 20 183 ${10 + 8 * bend} ${endX} ${endY}`);
      line.setAttribute('d', `M${endX} ${endY} L252 76`);
    }
  }
  return { element, render };
}

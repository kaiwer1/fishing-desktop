// Each parked rod has its own bell. A revision of the same bite never rings it twice.
export class StandBells {
  #audio = null;
  #previous = null;
  #voices = new Set();
  #create;

  constructor(createAudio) { this.#create = createAudio; }

  activate() {
    try {
      this.#audio ??= this.#create();
      if (this.#audio.state === 'suspended') this.#audio.resume().catch(() => {});
    } catch { /* The visual tip remains usable when audio is unavailable. */ }
  }

  observe(stand, audible) {
    const bites = new Set();
    const held = stand?.heldRig?.sessionId;
    if (held && this.#previous?.has(held)) bites.add(held);
    for (const { slot, rig, alarm } of stand?.slots ?? []) {
      if (!rig?.sessionId || alarm !== 'bite') continue;
      bites.add(rig.sessionId);
      if (audible && this.#audio?.state === 'running' && this.#previous && !this.#previous.has(rig.sessionId)) this.#ring(slot);
    }
    this.#previous = bites;
  }

  #ring(slot) {
    const audio = this.#audio;
    const start = audio.currentTime;
    const pitch = [740, 932, 1175][slot - 1];
    for (const [ratio, level, decay] of [[1, 0.07, 0.34], [2.73, 0.035, 0.17]]) {
      const voice = audio.createOscillator(), envelope = audio.createGain();
      voice.frequency.value = pitch * ratio;
      envelope.gain.setValueAtTime(0.0001, start);
      envelope.gain.exponentialRampToValueAtTime(level, start + 0.006);
      envelope.gain.exponentialRampToValueAtTime(0.0001, start + decay);
      voice.connect(envelope); envelope.connect(audio.destination);
      this.#voices.add(voice);
      voice.onended = () => { voice.disconnect(); envelope.disconnect(); this.#voices.delete(voice); };
      voice.start(start); voice.stop(start + decay);
    }
  }

  silence() {
    this.#previous = null;
    for (const voice of this.#voices) voice.stop();
    this.#voices.clear();
    if (this.#audio) {
      this.#audio.close().catch(() => {});
      this.#audio = null;
    }
  }
}

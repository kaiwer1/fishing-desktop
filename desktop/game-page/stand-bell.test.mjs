import test from 'node:test';
import assert from 'node:assert/strict';
import { StandBells } from './stand-bell.mjs';

function fakeAudio() {
  const voices = [];
  const audio = {
    state:'running', currentTime:1, destination:{},
    createOscillator() {
      const voice = { frequency:{value:0}, connect(){}, disconnect(){}, start(){}, stop(){} };
      voices.push(voice); return voice;
    },
    createGain: () => ({ connect(){}, disconnect(){}, gain:{ setValueAtTime(){}, exponentialRampToValueAtTime(){} } }),
    async close() { this.state = 'closed'; },
  };
  return {audio, voices};
}
const slot = (number, session, alarm) => ({slot:number, rig:{sessionId:session}, alarm});
const rack = (...slots) => ({slots, heldRig:null});

test('two parked rods ring independently on a bite and keep quiet on a touch, repeated state or hand swap', () => {
  const {audio, voices} = fakeAudio(), bells = new StandBells(() => audio);
  bells.activate();
  bells.observe(rack(slot(1,'A','touch'),slot(2,'B','none')),true);
  assert.equal(voices.length,0);
  const both = rack(slot(1,'A','bite'),slot(2,'B','bite'));
  bells.observe(both,true); bells.observe(both,true);
  assert.deepEqual(voices.map(v => v.frequency.value),[740,740*2.73,932,932*2.73]);
  bells.observe({heldRig:{sessionId:'A'},slots:[slot(2,'B','bite')]},true);
  bells.observe(both,true);
  assert.equal(voices.length,4);
  bells.observe(rack(slot(3,'C','none')),true);
  bells.observe(rack(slot(3,'C','bite')),true);
  assert.equal(voices[4].frequency.value,1175);
});

test('reopening, muted playback and already-active alarms never replay a parked bite', () => {
  const {audio, voices} = fakeAudio(), bells = new StandBells(() => audio);
  const active = rack(slot(1,'A','bite'));
  bells.observe(active,true); bells.activate(); bells.observe(active,true);
  bells.observe(rack(slot(1,'A','none')),true); bells.observe(active,false); bells.observe(active,true);
  assert.equal(voices.length,0);
  bells.silence();
  assert.equal(audio.state,'closed');
  bells.observe(active,true);
  assert.equal(voices.length,0);
});

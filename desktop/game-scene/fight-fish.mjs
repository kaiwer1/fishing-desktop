// Cosmetic coordinates in Three metres. Water draws the underwater body along
// the eye ray through the surface; it never owns a second fish simulation.
const showing = new Set(['fighting','landing-eligible']);
const profiles = new Map([
  ['carp',[.43,.12]],['crucian',[.32,.108]],['grass-carp',[.5,.104]],['blunt-snout-bream',[.38,.119]],
  ['black-seabream',[.38,.12]],['spotted-seabass',[.49,.10]],['yellowfin-seabream',[.35,.102]]
]);

export function sampleFightFish(views,previous) {
  const fish=views.fight,flight=views.flight,w=views.water;
  if(!w?.enabled || !fish?.encounterId || !fish.sessionId || fish.sessionId!==flight?.sessionId ||
      (views.bite?.encounterId && fish.encounterId!==views.bite.encounterId) || !showing.has(fish.phase))return null;
  const {xMm,yMm,zMm}=fish,b=w.waterBounds;
  if(![xMm,yMm,zMm].every(Number.isSafeInteger) || xMm<b.minXmm || xMm>b.maxXmm || zMm<b.minZmm || zMm>b.maxZmm)return null;
  const depth=(w.surfaceYmm-yMm)/1000;
  if(depth<-.025 || depth>=.82)return null;
  const distance=Math.hypot(xMm-w.anchor.xMm,zMm-w.anchor.zMm)/1000;
  if(distance>18)return null;
  const same=previous?.sessionId===fish.sessionId && previous?.encounterId===fish.encounterId;
  const dx=same?(xMm-previous.xMm)/1000:0, dz=same?-(zMm-previous.zMm)/1000:0;
  // In the absence of a measured displacement the last heading is retained.
  const yaw=Math.hypot(dx,dz)>.002?Math.atan2(dz,dx):same?previous.yaw:0;
  const dimensions=profiles.get(fish.speciesKey??fish.speciesId??fish.target?.speciesKey)??[.40,.104];
  return {sessionId:fish.sessionId,encounterId:fish.encounterId,xMm,yMm,zMm,yaw,depth,
    length:dimensions[0],width:dimensions[1],opacity:.8*(1-Math.max(0,depth)/.82)*Math.min(1,(18-distance)/6)};
}

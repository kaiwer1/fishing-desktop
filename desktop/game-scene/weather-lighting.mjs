import * as THREE from 'three';

const mix=(a,b,t)=>a.map((v,i)=>v+(b[i]-v)*t);
const clamp=(n,min,max)=>Math.min(max,Math.max(min,n));
// All colours are linear RGB, as consumed by the lake shaders. The game
// clock sets the sun's bearing; illuminance and precipitation come from the host.
export function sampleLighting(w){
  const hourAngle=(w.clockMinute/1440-.5)*Math.PI*2, altitude=Math.cos(hourAngle);
  const light=w.lightPermille/1000, cloud=w.cloudPercent/100, rain=w.rainPermille/1000;
  const twilight=Math.max(0,1-Math.abs(altitude)/.44)*(1-cloud*.85);
  const veil=clamp(cloud*.58+rain*.25+(w.weatherCode==='mist'?.27:0),0,.94);
  let horizon=mix([.055,.077,.125],[.80,.90,.99],light);
  horizon=mix(horizon,[.98,.48,.24],twilight*.73);
  horizon=mix(horizon,mix([.072,.082,.12],[.64,.69,.70],light),veil);
  const zenith=mix(mix([.008,.017,.054],[.11,.38,.74],light),mix([.028,.036,.068],[.43,.49,.57],light),veil);
  const fogDensity=.0026+.006*cloud+.017*rain+(w.weatherCode==='mist'?.027:0);
  const sunIntensity=Math.max(0,altitude+.05)*2.75*light*(1-.76*cloud)*(1-.7*rain);
  const sunDirection=new THREE.Vector3(Math.sin(hourAngle),altitude*.86,-.52).normalize();
  return {horizon,zenith,cloudColor:mix([.056,.073,.11],[.79,.83,.85],light),fogColor:mix(horizon,zenith,.10),
    sunColor:mix([.99,.57,.29],[1,.94,.82],clamp(altitude*2.2,0,1)),sunIntensity,sunDirection,
    skyIntensity:.29+1.58*light+.16*cloud,groundColor:mix([.075,.086,.11],[.34,.36,.27],light),
    fogDensity,cloudCover:cloud,glow:clamp((altitude+.09)*6,0,1)*(1-.88*cloud)*(1-.82*rain),waterLight:.22+.78*light};
}

export function weatherUniforms(){
  return {weatherOn:{value:0},weatherHorizon:{value:new THREE.Vector3()},weatherZenith:{value:new THREE.Vector3()},
    weatherCloud:{value:new THREE.Vector3()},weatherFog:{value:new THREE.Vector3()},weatherSun:{value:new THREE.Vector3()},
    weatherGlow:{value:0},weatherCover:{value:0},weatherDensity:{value:0},weatherWaterLight:{value:1}};
}
export function setWeatherUniforms(u,s){
  u.weatherOn.value=s?1:0;
  if(!s)return;
  u.weatherHorizon.value.fromArray(s.horizon);u.weatherZenith.value.fromArray(s.zenith);u.weatherCloud.value.fromArray(s.cloudColor);
  u.weatherFog.value.fromArray(s.fogColor);u.weatherSun.value.copy(s.sunDirection);
  u.weatherGlow.value=s.glow;u.weatherCover.value=s.cloudCover;u.weatherDensity.value=s.fogDensity;u.weatherWaterLight.value=s.waterLight;
}
export const skyWeatherGLSL=`
uniform float weatherOn,weatherGlow,weatherCover;
uniform vec3 weatherHorizon,weatherZenith,weatherCloud,weatherSun;
vec3 conditionsSky(vec3 d,float h,float noiseValue){
  vec3 base=mix(weatherHorizon,weatherZenith,pow(h,.46));
  float edge=mix(.78,.25,weatherCover);
  float clouds=smoothstep(edge-.08,edge+.12,noiseValue)*smoothstep(.02,.18,h)*(.25+.70*weatherCover);
  base=mix(base,weatherCloud*(.74+.26*noiseValue),clouds);
  float align=max(dot(d,weatherSun),0.);
  return base+weatherGlow*(vec3(.57,.30,.14)*pow(align,24.)+vec3(2.8,2.35,1.75)*smoothstep(.9996,.99998,align));
}`;
export const waterWeatherGLSL=`
uniform float weatherOn,weatherGlow,weatherWaterLight,weatherDensity;
uniform vec3 weatherSun,weatherFog;
vec3 conditionsWater(vec3 water,vec3 world,float fresnel){
  float distanceToEye=length(world-cameraPosition);
  float d=weatherDensity*distanceToEye;
  return mix(water*mix(weatherWaterLight,1.,fresnel),weatherFog,1.-exp(-d*d));
}`;

export function createWeatherLighting(scene,sun,skyLight){
  const original={sunColor:sun.color.clone(),sunIntensity:sun.intensity,sunPosition:sun.position.clone(),
    skyColor:skyLight.color.clone(),ground:skyLight.groundColor.clone(),skyIntensity:skyLight.intensity,
    background:scene.background.clone(),fog:scene.fog.color.clone(),density:scene.fog.density};
  let last=null,lake=null,result=null;
  return {
    update(weather,nextLake){
      if(weather?.waterId!==nextLake?.waterId)weather=null;
      if(last===weather && lake===nextLake)return;
      lake=nextLake;last=weather;
      if(!weather){
        sun.color.copy(original.sunColor);sun.intensity=original.sunIntensity;sun.position.copy(original.sunPosition);
        skyLight.color.copy(original.skyColor);skyLight.groundColor.copy(original.ground);skyLight.intensity=original.skyIntensity;
        scene.background.copy(original.background);scene.fog.color.copy(original.fog);scene.fog.density=original.density;
        lake?.setConditions(null);result=null;return;
      }
      const s=sampleLighting(weather);
      sun.position.copy(s.sunDirection).multiplyScalar(110);sun.color.fromArray(s.sunColor);sun.intensity=s.sunIntensity;
      skyLight.color.fromArray(s.horizon);skyLight.groundColor.fromArray(s.groundColor);skyLight.intensity=s.skyIntensity;
      scene.fog.color.fromArray(s.fogColor);scene.fog.density=s.fogDensity;scene.background.fromArray(s.horizon);
      lake.setConditions(s);result={...weather,sunIntensity:s.sunIntensity,fogDensity:s.fogDensity,cloudCover:s.cloudCover};
    },
    state(){return result&&{...result};}
  };
}

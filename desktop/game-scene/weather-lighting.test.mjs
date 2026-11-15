import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { createWeatherLighting, sampleLighting, setWeatherUniforms, weatherUniforms } from './weather-lighting.mjs';

const waterId='coastal-bay', noon={waterId,clockMinute:720,weatherCode:'clear',cloudPercent:10,windDeciMs:33,tempDeciC:238,lightPermille:925,rainPermille:0};
test('host illuminance, time and precipitation change direction, skyline, visibility and water shading together',()=>{
  const day=sampleLighting(noon),night=sampleLighting({...noon,clockMinute:0,lightPermille:85});
  const rain=sampleLighting({...noon,weatherCode:'rain',cloudPercent:85,lightPermille:600,rainPermille:780});
  const mist=sampleLighting({...noon,weatherCode:'mist'});
  assert.ok(day.sunDirection.y>0 && night.sunDirection.y<0 && night.sunIntensity===0);
  assert.ok(day.sunIntensity>rain.sunIntensity && day.skyIntensity>night.skyIntensity);
  assert.ok(rain.fogDensity>day.fogDensity && mist.fogDensity>day.fogDensity);
  assert.ok(day.waterLight>night.waterLight && day.zenith[2]>night.zenith[2]);
  const uniforms=weatherUniforms();setWeatherUniforms(uniforms,rain);
  assert.equal(uniforms.weatherOn.value,1);assert.equal(uniforms.weatherDensity.value,rain.fogDensity);
  assert.ok(uniforms.weatherSun.value.equals(rain.sunDirection));setWeatherUniforms(uniforms,null);assert.equal(uniforms.weatherOn.value,0);
});
test('weather updates existing lights without new meshes and restores training lighting on a site change',()=>{
  const scene=new THREE.Scene();scene.background=new THREE.Color(0x8a6a6a);scene.fog=new THREE.FogExp2(0x99aaaa,.007);
  const sun=new THREE.DirectionalLight(0xffe325,2),sky=new THREE.HemisphereLight(0x93c3ff,0x635643,1.5);
  sun.position.set(-47,23,-100);scene.add(sun,sky);
  const original=[sun.color.getHex(),sky.color.getHex(),scene.background.getHex(),scene.fog.density,sky.groundColor.getHex()];
  const changes=[],lake={waterId,setConditions:s=>changes.push(s)},lighting=createWeatherLighting(scene,sun,sky);
  lighting.update(noon,lake);assert.equal(scene.children.length,2);assert.equal(changes.length,1);
  assert.equal(lighting.state().clockMinute,720);assert.equal(sun.intensity,lighting.state().sunIntensity);
  lighting.update(noon,lake);assert.equal(changes.length,1);
  lighting.update(noon,{...lake,waterId:'river-bend'});assert.equal(lighting.state(),null);
  assert.deepEqual([sun.color.getHex(),sky.color.getHex(),scene.background.getHex(),scene.fog.density,sky.groundColor.getHex()],original);
  assert.deepEqual(sun.position.toArray(),[-47,23,-100]);assert.equal(sun.intensity,2);
});

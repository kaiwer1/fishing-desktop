// Compare the same camera, clock and surveyed water against the 0.6.0 lake renderer.
import { execFileSync, spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { rolldown } from 'rolldown';

const folder=fileURLToPath(new URL('.',import.meta.url)),repo=resolve(folder,'../..');
if(!process.argv[2])throw Error('Provide a private output directory.');
const run=mkdtempSync(join(resolve(process.argv[2]),'river-bank-'));
const browser=process.env.SPECIMEN_CHROMIUM||['C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(existsSync);
if(!browser)throw Error('Set SPECIMEN_CHROMIUM to an installed Chromium browser.');
const baseline=execFileSync('git',['-c','safe.directory='+repo.replaceAll('\\','/'),'show',
  'c7b92c4645e89d4d3f8ccc0850ed9769feae6874:desktop/game-scene/lake.mjs'],{cwd:repo,encoding:'utf8'});
const entry=join(folder,'river-photograph-entry.mjs');
const assets={};
for(const file of ['river-pebbles.jpg','coastal-rock.jpg','shudu-ridge.webp']){
  assets[file]='data:image/'+(file.endsWith('webp')?'webp':'jpeg')+';base64,'+readFileSync(join(folder,'assets',file)).toString('base64');
}
const app=`
import * as T from 'three';
import {createLake} from './lake.mjs';
import {createTackle} from './tackle.mjs';
import {createWeatherLighting} from './weather-lighting.mjs';
import {setBankCamera} from './water-space.mjs';
async function run(){
  const w={waterId:'river-bend',spotId:'reed-bank',enabled:true,geometryVersion:5,surfaceYmm:0,
    anchor:{xMm:-5800,yMm:1500,zMm:0},waterBounds:{minXmm:-46000,maxXmm:46000,minZmm:3000,maxZmm:80000},
    bounds:{minXmm:-18800,maxXmm:7200,minZmm:12000,maxZmm:41000},
    bed:[[3000,520],[12000,950],[28000,2750],[80000,4650]].map(([zMm,depthMm])=>({zMm,depthMm}))};
  const canvas=document.querySelector('canvas'),renderer=new T.WebGLRenderer({canvas,antialias:true,preserveDrawingBuffer:true});
  renderer.setSize(1120,674,false);renderer.toneMapping=T.ACESFilmicToneMapping;renderer.toneMappingExposure=1.08;
  const scene=new T.Scene();scene.background=new T.Color(0xc7c5ad);scene.fog=new T.FogExp2(0xc4c7b9,.0068);
  const camera=new T.PerspectiveCamera(47,1,.022,700);camera.layers.enable(1);
  const yaw=setBankCamera(camera,w,1120/674);
  const sun=new T.DirectionalLight(0xffdfae,2.15);sun.position.set(-47,23,-100);
  const sky=new T.HemisphereLight(0xfff1d6,0x79795c,1.77),lighting=createWeatherLighting(scene,sun,sky);
  const ready=new Promise(resolve=>{T.DefaultLoadingManager.onLoad=resolve;});
  const lake=createLake(w,name=>fixtureAssets[name]),tackle=createTackle();
  scene.add(lake.group,tackle.group,sun,sky,camera);
  const fill=new T.PointLight(0xdce7ed,2.8,1.65,2);fill.layers.set(1);fill.position.set(-.21,.16,-.14);camera.add(fill);
  await ready;
  const files={},metrics={};
  for(const [name,clockMinute,lightPermille,cloudPercent,rainPermille] of [
    ['rain',491,460,82,460],['day',738,935,24,0],['night',1410,75,38,0]]){
    lighting.update({waterId:w.waterId,clockMinute,lightPermille,cloudPercent,rainPermille,weatherCode:rainPermille?'rain':'clear'},lake);
    fill.intensity=.05+2.75*lightPermille/1000;
    tackle.animate({water:w},2,0,yaw,camera.fov);lake.animate(2);
    lake.refreshMirror(true);renderer.render(scene,camera);
    if(!lake.mirrorUpdates()||!renderer.info.render.triangles)throw Error('Missing reflection or geometry');
    files[name]=canvas.toDataURL('image/png').split(',')[1];
    metrics[name]={triangles:renderer.info.render.triangles,calls:renderer.info.render.calls,mirrors:lake.mirrorUpdates(),
      textures:lake.loadedMaterials(),position:camera.position.toArray()};
  }
  const result={files,metrics};lake.dispose();tackle.dispose();renderer.dispose();return result;
}
run().then(result=>document.querySelector('output').textContent=JSON.stringify(result),
  error=>document.querySelector('output').textContent=JSON.stringify({error:String(error.stack||error)}));
`;
const report={};
for(const mode of ['before','after']){
  const bundle=await rolldown({input:entry,plugins:[{name:'river-render-comparison',
    resolveId(id){if(id===entry)return entry;},
    load(id){if(id===entry)return app;if(mode==='before'&&resolve(id)===join(folder,'lake.mjs'))return baseline;}
  }]});
  const code=mode+'.js';await bundle.write({file:join(run,code),format:'iife',minify:true});await bundle.close();
  const html=join(run,mode+'.html');
  writeFileSync(html,'<!doctype html><meta charset="utf-8"><output></output><canvas></canvas><script>const fixtureAssets='+
    JSON.stringify(assets)+'</script><script src="'+code+'"></script>');
  const child=spawnSync(browser,['--headless=new','--disable-extensions','--no-first-run','--disable-background-networking',
    '--enable-unsafe-swiftshader','--use-angle=swiftshader','--virtual-time-budget=12000','--dump-dom',
    '--user-data-dir='+join(run,mode+'-profile'),pathToFileURL(html).href],{encoding:'utf8',windowsHide:true,timeout:120000,maxBuffer:48000000});
  if(child.error||child.status!==0)throw Error(child.error?.message||child.stderr.slice(-1500));
  const match=child.stdout.match(/<output>([^<]+)<\/output>/);
  if(!match)throw Error('No rendered river fixture. '+child.stderr.slice(-800));
  const result=JSON.parse(match[1].replaceAll('&amp;','&').replaceAll('&lt;','<').replaceAll('&gt;','>'));
  if(result.error)throw Error(result.error);
  for(const [condition,bytes] of Object.entries(result.files))writeFileSync(join(run,mode+'-'+condition+'.png'),Buffer.from(bytes,'base64'));
  report[mode]=result.metrics;
}
if(report.before.day.textures!==2||report.after.day.textures!==3)throw Error('River textures did not load for both scene versions.');
writeFileSync(join(run,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({run,...report}));

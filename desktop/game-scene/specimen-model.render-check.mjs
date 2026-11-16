// Run with node and a private output directory. This optional offline check
// uses a separate temporary Chromium profile, never an installed player's save.
import {spawnSync} from 'node:child_process';
import {existsSync,mkdtempSync,writeFileSync} from 'node:fs';
import {resolve,join} from 'node:path';
import {pathToFileURL,fileURLToPath} from 'node:url';
import {rolldown} from 'rolldown';

const output=process.argv[2];
if(!output)throw new Error('Provide a private output directory for the WebGL plate.');
const browser=process.env.SPECIMEN_CHROMIUM||
  ['C:/Program Files/Google/Chrome/Application/chrome.exe','C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(existsSync);
if(!browser)throw new Error('Set SPECIMEN_CHROMIUM to an installed Chromium browser.');
const run=mkdtempSync(join(resolve(output),'specimen-render-'));
const entry=fileURLToPath(new URL('./specimen-render-entry.mjs',import.meta.url));
const app=`
import * as T from 'three';
import {createSpecimen,disposeSpecimen} from './specimen-model.mjs';
const species=[['carp','鲤鱼'],['crucian','鲫鱼'],['grass-carp','草鱼'],['blunt-snout-bream','鳊鱼'],
  ['black-seabream','黑鲷'],['spotted-seabass','花鲈'],['yellowfin-seabream','黄鳍鲷'],['yellow-catfish','黄颡鱼'],['black-rockfish','许氏平鲉'],
  ['topmouth-culter','翘嘴鲌'],['large-yellow-croaker','大黄鱼']];
const result=document.querySelector('output');
try{
const renderer=new T.WebGLRenderer({alpha:true,antialias:true,preserveDrawingBuffer:true});
renderer.setSize(640,332,false);renderer.setClearColor(0,0);
renderer.toneMapping=T.ACESFilmicToneMapping;renderer.toneMappingExposure=1;
const gl=renderer.getContext(),ext=gl.getExtension('WEBGL_debug_renderer_info');
const driver=ext?gl.getParameter(ext.UNMASKED_RENDERER_WEBGL):gl.getParameter(gl.RENDERER);
const scene=new T.Scene(),camera=new T.OrthographicCamera(-.67,.67,.348,-.348,.1,10);
scene.add(new T.HemisphereLight(0xe1edf1,0x585344,1.1));
for(const [colour,power,pos] of [[0xffebd7,2.55,[1.0,1.55,2.3]],[0xb7d4e6,1.8,[-1,1.2,-1.5]]]){
  const lamp=new T.DirectionalLight(colour,power);lamp.position.set(...pos);scene.add(lamp);
}
const metrics=[],plates=Array.from({length:2},()=>{
  const plate=document.createElement('canvas');plate.width=1200;plate.height=298*Math.ceil(species.length/2);
  const brush=plate.getContext('2d');brush.fillStyle='#202b2d';brush.fillRect(0,0,plate.width,plate.height);
  return brush;
});
const rgba=new Uint8Array(640*332*4);
for(const [i,[id,label]] of species.entries()){
  const g=createSpecimen(T,id);scene.add(g);const views=[];
  for(const [j,pos] of [[0,[.23,.10,2]],[1,[-.9,.29,-1.7]]]){
    camera.position.set(...pos);camera.lookAt(0,0,0);renderer.render(scene,camera);
    gl.readPixels(0,0,640,332,gl.RGBA,gl.UNSIGNED_BYTE,rgba);
    let count=0,minX=640,maxX=0,minY=332,maxY=0;const palette=new Set();
    for(let y=0;y<332;y++)for(let x=0;x<640;x++){
      const k=(y*640+x)*4;if(rgba[k+3]<240)continue;count++;
      minX=Math.min(minX,x);maxX=Math.max(maxX,x);minY=Math.min(minY,y);maxY=Math.max(maxY,y);
      palette.add((rgba[k]>>3)*1024+(rgba[k+1]>>3)*32+(rgba[k+2]>>3));
    }
    if(count<15000||palette.size<65||minX<2||maxX>=638||minY<2||maxY>=330)throw Error('Incomplete WebGL fish '+id+' view '+j+' '+[count,palette.size,minX,maxX,minY,maxY]);
    views.push({pixels:count,quantisedColours:palette.size,draws:renderer.info.render.calls,triangles:renderer.info.render.triangles});
    const x=(i%2)*600,y=Math.floor(i/2)*298,brush=plates[j];
    brush.drawImage(renderer.domElement,x+37,y+1,526,273);
    brush.fillStyle='#d7dcd6';brush.font='20px "Microsoft YaHei", sans-serif';brush.fillText(label,x+38,y+279);
  }
  metrics.push({speciesId:id,views});scene.remove(g);disposeSpecimen(g);
}
renderer.dispose();
result.textContent=JSON.stringify({passed:true,driver,metrics,plates:plates.map(b=>b.canvas.toDataURL('image/png'))});
}catch(error){result.textContent=JSON.stringify({passed:false,error:String(error.stack||error)});}
`;
const bundle=await rolldown({input:entry,plugins:[{
  name:'standalone-specimen-check',resolveId(id){if(id===entry)return entry;},load(id){if(id===entry)return app;}
}]});
await bundle.write({file:join(run,'check.js'),format:'iife',minify:true});await bundle.close();
const html=join(run,'check.html');
writeFileSync(html,'<!doctype html><meta charset="utf-8"><output id="result"></output><script src="check.js"></script>');
const child=spawnSync(browser,['--headless=new','--disable-extensions','--no-first-run','--no-default-browser-check',
  '--disable-background-networking','--disable-component-update','--enable-unsafe-swiftshader','--use-angle=swiftshader',
  '--user-data-dir='+join(run,'private-browser-profile'),'--dump-dom',pathToFileURL(html).href],
  {encoding:'utf8',windowsHide:true,timeout:90000,maxBuffer:15000000});
if(child.error)throw child.error;
if(child.status!==0)throw new Error('Chromium exited '+child.status+' '+child.stderr.slice(-1800));
const match=child.stdout.match(/<output id="result">([^<]*)<\/output>/);
if(!match)throw new Error('No rendered result. '+child.stderr.slice(-1200));
const {plates,...report}=JSON.parse(match[1].replaceAll('&amp;','&').replaceAll('&gt;','>').replaceAll('&lt;','<'));
writeFileSync(join(run,'report.json'),JSON.stringify(report,null,2));
if(!report.passed)throw new Error(report.error);
for(const [i,name] of ['specimens.png','specimens-reverse.png'].entries()){
  writeFileSync(join(run,name),Buffer.from(plates[i].split(',')[1],'base64'));
}
console.log(JSON.stringify({directory:run,...report}));

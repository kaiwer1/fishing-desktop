import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {build} from 'vite';
const repository=fileURLToPath(new URL('../..',import.meta.url));
export async function buildLakeView(output) {
  const directory=path.resolve(output);let cursor=directory;
  for(;;){const info=await fs.lstat(cursor);if(info.isSymbolicLink())throw Error('Use a direct output directory.');const parent=path.dirname(cursor);if(parent===cursor)break;cursor=parent;}
  const real=await fs.realpath(directory),relative=path.relative(await fs.realpath(repository),real);
  if(relative==='' || (!relative.startsWith('..'+path.sep) && relative!=='..' && !path.isAbsolute(relative)))throw Error('Bundle output must stay outside the repository.');
  if(await fs.readdir(real).then(names=>names.includes('lake-view.mjs') || names.includes('THREE-LICENSE.txt')))throw Error('Do not overwrite an existing lake bundle.');
  await build({configFile:false,root:repository,publicDir:false,logLevel:'warn',build:{outDir:real,emptyOutDir:false,target:'es2022',lib:{entry:fileURLToPath(new URL('./lake-view.mjs',import.meta.url)),formats:['es'],fileName:()=> 'lake-view.mjs'}}});
  await fs.copyFile(path.join(repository,'node_modules/three/LICENSE'),path.join(real,'THREE-LICENSE.txt'));
}
if(process.argv[1] && path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  if(process.argv.length!==3)throw Error('Usage: node build.mjs <existing private directory>');await buildLakeView(process.argv[2]);
}

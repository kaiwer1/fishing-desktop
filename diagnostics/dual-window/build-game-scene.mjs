import {buildLocalScene} from '../three-scene/build.mjs';
if(process.argv.length!==3)throw Error('Usage: node build-game-scene.mjs <existing private directory>');
await buildLocalScene(process.argv[2],new URL('../../desktop/game-scene/game-scene.mjs',import.meta.url),'game-scene.mjs');

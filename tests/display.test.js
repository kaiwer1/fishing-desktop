import { test } from 'node:test';
import assert from 'node:assert/strict';
import { QUALITY, normalizeSettings, renderSize, frameRate } from '../src/display.js';

test('representative display and scaling combinations respect the selected pixel budget', () => {
  const displays = [[1280,720],[1920,1080],[2560,1440],[3840,2160],[2560,1600],[3440,1440],[5120,1440],[1080,1920]];
  for (const quality of Object.keys(QUALITY)) {
    for (const [w,h] of displays) {
      for (const dpr of [1,1.25,1.5,2]) {
        const result = renderSize(w,h,dpr,{quality});
        assert.ok(result.width * result.height <= QUALITY[quality].maxPixels);
        assert.equal(result.aspect, w / h);
        assert.ok(Math.abs(result.width / result.height - w / h) < 0.015);
      }
    }
  }
});

test('lowering the scale reduces renderer work without changing the camera aspect', () => {
  const full = renderSize(1920,1080,1,{scale:1});
  const reduced = renderSize(1920,1080,1,{scale:0.65});
  assert.ok(reduced.width * reduced.height < full.width * full.height);
  assert.equal(full.aspect, reduced.aspect);
});

test('bad persisted values fall back to valid settings and nonzero dimensions', () => {
  for (const value of [null,{},'broken',{quality:'toString',scale:Infinity,fps:0}]) {
    assert.deepEqual(normalizeSettings(value),{quality:'medium',scale:1,fps:30});
  }
  assert.deepEqual(renderSize(0,NaN,Infinity),{width:1,height:1,aspect:1});
});

test('hidden pages and host pause take precedence over foreground frame settings', () => {
  assert.equal(frameRate({hidden:true,fps:60}),0);
  assert.equal(frameRate({paused:true,observe:true}),0);
  assert.equal(frameRate({observe:true,fps:60}),18);
  assert.equal(frameRate({fps:60}),60);
});

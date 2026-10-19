export const QUALITY = Object.freeze({
  low: { pixelRatio: 1, maxPixels: 1280 * 720, waveDetail: 0.45 },
  medium: { pixelRatio: 1.5, maxPixels: 1920 * 1080, waveDetail: 0.75 },
  high: { pixelRatio: 2, maxPixels: 2560 * 1440, waveDetail: 1 },
});

export function normalizeSettings(value = {}) {
  const source = value && typeof value === 'object' ? value : {};
  return {
    quality: Object.hasOwn(QUALITY, source.quality) ? source.quality : 'medium',
    scale: [0.65, 0.85, 1].includes(Number(source.scale)) ? Number(source.scale) : 1,
    fps: Number(source.fps) === 60 ? 60 : 30,
  };
}

/** Canvas pixels are bounded independently of CSS layout and desktop scaling. */
export function renderSize(width, height, devicePixelRatio = 1, settings = {}) {
  const config = normalizeSettings(settings);
  const profile = QUALITY[config.quality];
  const w = Number.isFinite(width) && width > 0 ? width : 1;
  const h = Number.isFinite(height) && height > 0 ? height : 1;
  const dpr = Number.isFinite(devicePixelRatio) && devicePixelRatio > 0 ? devicePixelRatio : 1;
  const ratio = Math.min(dpr, profile.pixelRatio) * config.scale;
  const budgetRatio = Math.sqrt(profile.maxPixels / (w * h));
  const effective = Math.min(ratio, budgetRatio);
  return {
    width: Math.max(1, Math.floor(w * effective)),
    height: Math.max(1, Math.floor(h * effective)),
    aspect: w / h,
  };
}

export function frameRate({ hidden = false, paused = false, portrait = false, observe = false, fps = 30 } = {}) {
  if (hidden || paused || portrait) return 0;
  return observe ? 18 : (fps === 60 ? 60 : 30);
}

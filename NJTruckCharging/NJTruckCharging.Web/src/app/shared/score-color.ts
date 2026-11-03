/** Sequential single-hue (blue) ramp for score, light → dark. Low scores recede, top sites stand out. */
const RAMP: [number, number, number][] = [
  [0x9e, 0xc5, 0xf4], // 200
  [0x86, 0xb6, 0xef], // 250
  [0x6d, 0xa7, 0xec], // 300
  [0x55, 0x98, 0xe7], // 350
  [0x39, 0x87, 0xe5], // 400
  [0x2a, 0x78, 0xd6], // 450
  [0x25, 0x6a, 0xbf], // 500
  [0x1c, 0x5c, 0xab], // 550
  [0x18, 0x4f, 0x95], // 600
  [0x10, 0x42, 0x81], // 650
  [0x0d, 0x36, 0x6b], // 700
];
export const SCORE_MIN = 30;
export const SCORE_MAX = 105;

export function scoreRgb(score: number): [number, number, number] {
  const t = Math.min(1, Math.max(0, (score - SCORE_MIN) / (SCORE_MAX - SCORE_MIN)));
  return RAMP[Math.round(t * (RAMP.length - 1))];
}

export function scoreCss(score: number): string {
  const [r, g, b] = scoreRgb(score);
  return `rgb(${r}, ${g}, ${b})`;
}

export const RAMP_CSS = `linear-gradient(90deg, ${RAMP.map(([r, g, b]) => `rgb(${r},${g},${b})`).join(', ')})`;

/** Power Edison / EV Edison accent (categorical slot 2, orange). */
export const PE_RGB: [number, number, number] = [0xeb, 0x68, 0x34];
/** Status critical – flood zone ring. */
export const FLOOD_RGB: [number, number, number] = [0xd0, 0x3b, 0x3b];

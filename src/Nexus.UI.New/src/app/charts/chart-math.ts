// Axis and time rules ported from Nexus.UI/Charts/Chart.razor.cs.
export const TICKS_PER_SECOND = 10_000_000n;
export const TICKS_PER_DAY = 864_000_000_000n;
export const UNIX_EPOCH_TICKS = 621_355_968_000_000_000n;
export const CHUNK_LENGTH = 4 * 1024 * 1024;
export const SERIES_COLORS = [
  [0, 114, 189], [217, 83, 25], [237, 177, 32], [126, 47, 142],
  [119, 172, 48], [77, 190, 238], [162, 20, 47],
] as const;

export interface Viewport { left: number; top: number; right: number; bottom: number }
export interface Axis { unit: string; originalMin: number; originalMax: number; min: number; max: number }
export type TriggerPeriod = 'second' | 'minute' | 'hour' | 'day' | 'month' | 'year';
export interface TimeAxisConfig {
  interval: bigint;
  fast: string;
  trigger: TriggerPeriod;
  slow1?: string;
  slow2?: string;
  cursor: string;
}

export const FULL_VIEWPORT: Readonly<Viewport> = { left: 0, top: 0, right: 1, bottom: 1 };
export const clamp = (value: number, min = 0, max = 1): number => Math.max(min, Math.min(value, max));
export const roundAway = (value: number): number => Math.sign(value) * Math.floor(Math.abs(value) + 0.5);

// Multiply only the duration, never the absolute .NET timestamp. Integer arithmetic also
// preserves the endpoints of ranges longer than Number.MAX_SAFE_INTEGER ticks.
export function scaleTicks(ticks: bigint, factor: number): bigint {
  if (!Number.isFinite(factor)) throw new RangeError('Time position must be finite.');
  if (factor === 0) return 0n;
  const bits = new DataView(new ArrayBuffer(8));
  bits.setFloat64(0, Math.abs(factor));
  const high = bits.getUint32(0);
  const exponent = (high >>> 20) & 0x7ff;
  const mantissa = (BigInt(high & 0xfffff) << 32n) | BigInt(bits.getUint32(4));
  const numerator = ticks * (exponent === 0 ? mantissa : mantissa | (1n << 52n)) * (factor < 0 ? -1n : 1n);
  const shift = (exponent === 0 ? -1022 : exponent - 1023) - 52;
  if (shift >= 0) return numerator << BigInt(shift);
  const denominator = 1n << BigInt(-shift);
  const quotient = numerator / denominator;
  const remainder = numerator % denominator;
  const twice = (remainder < 0n ? -remainder : remainder) * 2n;
  return twice > denominator || (twice === denominator && quotient % 2n !== 0n)
    ? quotient + (numerator < 0n ? -1n : 1n) : quotient;
}

export function toTime(begin: bigint, end: bigint, position: number): bigint {
  return begin + scaleTicks(end - begin, clamp(position));
}

export function formatTime(ticks: bigint, pattern = 'yyyy-MM-dd HH:mm:ss.fffffff'): string {
  const milliseconds = ticks / 10_000n - UNIX_EPOCH_TICKS / 10_000n;
  const date = new Date(Number(milliseconds));
  const parts: Record<string, string> = {
    yyyy: String(date.getUTCFullYear()).padStart(4, '0'),
    MM: String(date.getUTCMonth() + 1).padStart(2, '0'),
    dd: String(date.getUTCDate()).padStart(2, '0'),
    HH: String(date.getUTCHours()).padStart(2, '0'),
    mm: String(date.getUTCMinutes()).padStart(2, '0'),
    ss: String(date.getUTCSeconds()).padStart(2, '0'),
  };
  const fraction = ((ticks % TICKS_PER_SECOND + TICKS_PER_SECOND) % TICKS_PER_SECOND).toString().padStart(7, '0');
  return pattern.replace(/yyyy|MM|dd|HH|mm|ss|f{1,7}/g, token => token[0] === 'f' ? fraction.slice(0, token.length) : parts[token]);
}

export function formatRange(begin: bigint, end: bigint): string {
  return `${formatTime(begin)}  -  ${formatTime(end)}`;
}

export function formatDuration(ticks: bigint): string {
  for (const [size, unit, digits] of [
    [TICKS_PER_DAY, 'd', 2], [36_000_000_000n, 'h', 2], [600_000_000n, 'min', 2],
    [TICKS_PER_SECOND, 's', 2], [10_000n, 'ms', 3], [10n, 'us', 3],
  ] as const) {
    if (ticks >= size) return `${Number((Number(ticks) / Number(size)).toFixed(digits))} ${unit}`;
  }
  return `${ticks * 100n} ns`;
}

export const TIME_AXIS_CONFIGS: readonly TimeAxisConfig[] = [
  { interval: 1n, fast: '.fffffff', trigger: 'second', slow1: 'HH:mm.ss', slow2: 'yyyy-MM-dd', cursor: 'yyyy-MM-dd HH:mm:ss.fffffff' },
  ...[10n, 50n, 100n, 500n, 1000n, 5000n].map(interval => ({ interval, fast: '.ffffff', trigger: 'second' as const, slow1: 'HH:mm.ss', slow2: 'yyyy-MM-dd', cursor: 'yyyy-MM-dd HH:mm:ss.fffffff' })),
  ...[10_000n, 50_000n, 100_000n, 500_000n, 1_000_000n, 5_000_000n].map((interval, index) => ({ interval, fast: '.fff', trigger: 'minute' as const, slow1: 'HH:mm:ss', slow2: 'yyyy-MM-dd', cursor: `yyyy-MM-dd HH:mm:ss.${'f'.repeat(6 - Math.floor(index / 2))}` })),
  ...[1n, 5n, 10n, 30n].map(seconds => ({ interval: seconds * TICKS_PER_SECOND, fast: 'HH:mm:ss', trigger: 'hour' as const, slow1: 'yyyy-MM-dd', cursor: 'yyyy-MM-dd HH:mm:ss.fff' })),
  ...[1n, 5n, 10n, 30n].map(minutes => ({ interval: minutes * 600_000_000n, fast: 'HH:mm', trigger: 'day' as const, slow1: 'yyyy-MM-dd', cursor: 'yyyy-MM-dd HH:mm:ss' })),
  ...[1n, 3n, 6n, 12n].map(hours => ({ interval: hours * 36_000_000_000n, fast: 'HH', trigger: 'day' as const, slow1: 'yyyy-MM-dd', cursor: 'yyyy-MM-dd HH:mm' })),
  ...[1n, 10n, 30n, 90n].map((days, index) => ({ interval: days * TICKS_PER_DAY, fast: 'dd', trigger: 'month' as const, slow1: 'yyyy-MM', cursor: index === 0 ? 'yyyy-MM-dd HH:mm' : 'yyyy-MM-dd HH' })),
  { interval: 365n * TICKS_PER_DAY, fast: 'yyyy', trigger: 'year', cursor: 'yyyy-MM-dd' },
];

export function roundTimeUp(value: bigint, interval: bigint): bigint {
  const mod = (value % interval + interval) % interval;
  return mod === 0n ? value : value + interval - mod;
}

export function getTimeTicks(begin: bigint, end: bigint, maximumCount: number): { config: TimeAxisConfig; ticks: bigint[] } {
  const count = BigInt(Math.max(1, Math.floor(maximumCount)));
  const duration = end - begin;
  const config = TIME_AXIS_CONFIGS.find(item => (duration + item.interval - 1n) / item.interval <= count) ?? TIME_AXIS_CONFIGS[TIME_AXIS_CONFIGS.length - 1];
  let interval = config.interval;
  while ((duration + interval - 1n) / interval > count) interval *= 2n;
  const ticks: bigint[] = [];
  for (let tick = roundTimeUp(begin, interval); tick < end; tick += interval) ticks.push(tick);
  return { config, ticks };
}

export function isSlowTickRequired(previous: bigint, tick: bigint, trigger: TriggerPeriod): boolean {
  const formats = { second: 'yyyy-MM-dd HH:mm:ss', minute: 'yyyy-MM-dd HH:mm', hour: 'yyyy-MM-dd HH', day: 'yyyy-MM-dd', month: 'yyyy-MM', year: 'yyyy' };
  return formatTime(previous, formats[trigger]) !== formatTime(tick, formats[trigger]);
}

export function getYLimits(min: number, max: number): { min: number; max: number; step: number } {
  if (!Number.isFinite(min) || !Number.isFinite(max) || min > max) { min = 0; max = 0; }
  const floatMax = (2 - 2 ** -23) * 2 ** 127;
  if (min === max) {
    // Preserve C#'s padding unless it collapses at GPU precision. Clamp at the
    // finite float32 endpoints so even float.MaxValue stays drawable.
    const padding = Math.fround(min - 0.5) === Math.fround(max + 0.5)
      ? Math.max(0.5, Math.abs(min) * 2 ** -23) : 0.5;
    min = Math.max(-floatMax, min - padding);
    max = Math.min(floatMax, max + padding);
  }
  const range = max - min;
  const significant = roundAway(Math.log10(range));
  const scale = 10 ** -significant;
  let minLimit = Math.fround(Math.floor(min * scale) / scale);
  let maxLimit = Math.fround(Math.ceil(max * scale) / scale);
  if (min === minLimit) minLimit = Math.fround(Math.floor((min - range / 8) * scale) / scale);
  if (max === maxLimit) maxLimit = Math.fround(Math.ceil((max + range / 8) * scale) / scale);
  return { min: Math.max(-floatMax, minLimit), max: Math.min(floatMax, maxLimit), step: Math.max(2 ** -149, Math.fround(10 ** (significant - 1))) };
}

export function createAxis(unit: string, min: number, max: number, beginAtZero: boolean, viewport: Viewport): Axis {
  const limits = getYLimits(min, max);
  const originalMin = beginAtZero ? Math.min(0, limits.min) : limits.min;
  const originalMax = beginAtZero ? Math.max(0, limits.max) : limits.max;
  const range = Math.fround(originalMax - originalMin);
  min = Math.fround(originalMin + Math.fround(Math.fround(1 - Math.fround(viewport.bottom)) * range));
  max = Math.fround(originalMax - Math.fround(Math.fround(viewport.top) * range));
  // Consumers subtract these bounds in double precision; expose the same span
  // that chart.webgpu.js writes to its float32 range uniform.
  return { unit, originalMin, originalMax, min, max: min + Math.fround(max - min) };
}

export function getYTicks(min: number, max: number, maximumCount: number): number[] {
  if (!Number.isFinite(min) || !Number.isFinite(max) || min > max || !Number.isFinite(maximumCount) || maximumCount < 1) return [];
  maximumCount = Math.min(1000, Math.floor(maximumCount));
  const limits = getYLimits(Math.fround(min), Math.fround(max));
  const originalCount = Math.ceil(Math.fround(Math.fround(Math.fround(limits.max - limits.min) / limits.step) + 1));
  let count = originalCount;
  let step = limits.step;
  for (const factor of [2, 5, 10, 20, 50]) {
    if (count <= maximumCount) break;
    count = Math.ceil(Math.fround(originalCount / factor));
    step = Math.fround(limits.step * factor);
  }
  const ticks = Number.isFinite(count) && count > 0 && count <= maximumCount && Number.isFinite(step)
    ? Array.from({ length: count }, (_, index) => Math.fround(limits.min + Math.fround(index * step))).filter(Number.isFinite) : [];
  if (min === max || maximumCount < 2 || new Set(ticks.filter(tick => tick >= min && tick <= max)).size >= 2) return ticks;

  // Rounded-limit padding can leave only zero visible after thinning. Use the
  // actual range in that case, with indexed, bounded generation at GPU precision.
  const range = max - min;
  if (!Number.isFinite(range)) return ticks;
  const targetStep = Math.max(2 ** -149, range / (maximumCount - 1));
  const scale = 10 ** Math.floor(Math.log10(targetStep));
  step = ([1, 2, 5, 10].find(factor => factor * scale >= targetStep) ?? 10) * scale;
  const first = Math.ceil(min / step);
  const visible = [...new Set(Array.from({ length: maximumCount }, (_, index) => Math.fround((first + index) * step))
    .filter(tick => Number.isFinite(tick) && tick >= min && tick <= max))];
  if (visible.length >= 2) return visible;
  // Adjacent float32 values may have no pair of decimal-aligned ticks.
  return [...new Set([Math.fround(min), Math.fround(max)])]
    .filter(tick => Number.isFinite(tick) && tick >= min && tick <= max);
}

export function toEngineering(value: number): string {
  if (value === 0) return '0';
  const exponent = Math.floor(Math.log10(Math.abs(value)));
  if (Math.abs(value) < 1000) {
    const rounded = Number(value.toPrecision(4));
    if (Math.floor(Math.log10(Math.abs(rounded))) >= -4 && Math.abs(rounded) < 10_000) return String(rounded);
    return rounded.toExponential().replace(/e([+-])(\d+)$/, (_, sign: string, digits: string) => `E${sign}${digits.padStart(2, '0')}`);
  }
  const engineeringExponent = Math.floor(exponent / 3) * 3;
  return `${Number((value / 10 ** engineeringExponent).toFixed(3 - exponent % 3))}e${engineeringExponent}`;
}

export function setViewport(viewport: Viewport, duration: bigint): Viewport | null {
  if (!Object.values(viewport).every(Number.isFinite)) return null;
  const next = { left: clamp(viewport.left), top: clamp(viewport.top), right: clamp(viewport.right), bottom: clamp(viewport.bottom) };
  if (next.right - next.left < 1 / Math.max(1, Number(duration)) || next.bottom - next.top < 1e-6) return null;
  next.top = Math.fround(next.top);
  next.bottom = Math.fround(next.bottom);
  return next;
}

export function applyZoom(current: Viewport, relative: Viewport, duration: bigint): Viewport | null {
  const width = current.right - current.left;
  const height = Math.fround(Math.fround(current.bottom) - Math.fround(current.top));
  const top = Math.fround(Math.fround(current.top) + Math.fround(height * Math.fround(relative.top)));
  const bottom = Math.fround(Math.fround(current.top) + Math.fround(height * Math.fround(relative.bottom)));
  return setViewport({ left: current.left + width * relative.left, right: current.left + width * relative.right, top, bottom }, duration);
}

export function detailWindow(left: number, right: number): { left: number; right: number; windowLeft: number; windowRight: number; visible: boolean } {
  const width = right - left;
  const detailWidth = Math.min(1, width * 8);
  const detailLeft = clamp((left + right - detailWidth) / 2, 0, 1 - detailWidth);
  const detailRight = Math.min(1, detailLeft + width * 8);
  return { left: detailLeft, right: detailRight, windowLeft: (left - detailLeft) / (detailRight - detailLeft), windowRight: (right - detailLeft) / (detailRight - detailLeft), visible: width < 0.125 };
}

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { FULL_VIEWPORT, SERIES_COLORS, TICKS_PER_DAY, TICKS_PER_SECOND, TIME_AXIS_CONFIGS, UNIX_EPOCH_TICKS, applyZoom, createAxis, detailWindow, formatDuration, formatRange, formatTime, getTimeTicks, getYLimits, getYTicks, isSlowTickRequired, roundAway, roundTimeUp, scaleTicks, setViewport, toEngineering, toTime } from './chart-math.ts';

describe('bigint .NET time', () => {
  it('formats the full DateTime range with all seven fraction digits in UTC', () => {
    assert.equal(formatTime(0n), '0001-01-01 00:00:00.0000000');
    assert.equal(formatTime(UNIX_EPOCH_TICKS), '1970-01-01 00:00:00.0000000');
    assert.equal(formatTime(UNIX_EPOCH_TICKS - 1n), '1969-12-31 23:59:59.9999999');
    assert.equal(formatTime(3155378975999999999n), '9999-12-31 23:59:59.9999999');
    assert.equal(formatTime(UNIX_EPOCH_TICKS + 1234567n, '.ffffff'), '.123456');
    assert.equal(formatTime(UNIX_EPOCH_TICKS + 1234567n, '.fff'), '.123');
  });
  it('formats leap days without local timezone conversion', () => {
    const leap = UNIX_EPOCH_TICKS + BigInt(Date.UTC(2024, 1, 29, 23, 59, 59)) * 10000n;
    assert.equal(formatTime(leap + 1n), '2024-02-29 23:59:59.0000001');
    assert.equal(formatTime(leap + TICKS_PER_SECOND), '2024-03-01 00:00:00.0000000');
  });
  it('scales durations with ties-to-even and exact endpoints beyond safe integers', () => {
    assert.equal(scaleTicks(1n, 0.5), 0n);
    assert.equal(scaleTicks(3n, 0.5), 2n);
    assert.equal(scaleTicks(-3n, 0.5), -2n);
    assert.equal(scaleTicks(7n, 0.25), 2n);
    assert.equal(scaleTicks(7n, 2), 14n);
    assert.equal(scaleTicks(100n, Number.MIN_VALUE), 0n);
    const end = 3155378975999999999n;
    assert.equal(toTime(1n, end, 1), end);
    assert.equal(toTime(UNIX_EPOCH_TICKS + 1n, UNIX_EPOCH_TICKS + 11n, 0.5), UNIX_EPOCH_TICKS + 6n);
    assert.throws(() => scaleTicks(1n, NaN));
  });
  it('formats navigator durations and ranges', () => {
    for (const [ticks, expected] of [[0n, '0 ns'], [1n, '100 ns'], [12n, '1.2 us'], [10001n, '1 ms'], [15000000n, '1.5 s'], [900000000n, '1.5 min'], [54000000000n, '1.5 h'], [TICKS_PER_DAY * 2n, '2 d']] as const) {
      assert.equal(formatDuration(ticks), expected);
    }
    assert.equal(formatRange(0n, 1n), '0001-01-01 00:00:00.0000000  -  0001-01-01 00:00:00.0000001');
  });
  it('ports every time-axis interval and selects the first interval that fits', () => {
    assert.equal(TIME_AXIS_CONFIGS.length, 30);
    for (const config of TIME_AXIS_CONFIGS) {
      const result = getTimeTicks(0n, config.interval * 4n, 4);
      assert.equal(result.config, config);
      assert.deepEqual(result.ticks, [0n, config.interval, config.interval * 2n, config.interval * 3n]);
    }
    assert.equal(roundTimeUp(10n, 10n), 10n);
    assert.equal(roundTimeUp(11n, 10n), 20n);
    assert.deepEqual(getTimeTicks(UNIX_EPOCH_TICKS + 1n, UNIX_EPOCH_TICKS + 4n, 5).ticks, [UNIX_EPOCH_TICKS + 1n, UNIX_EPOCH_TICKS + 2n, UNIX_EPOCH_TICKS + 3n]);
    const years = getTimeTicks(0n, 3155378975999999999n, 3);
    assert.ok(years.ticks.length <= 3);
    assert.equal(years.config.fast, 'yyyy');
    assert.deepEqual(getTimeTicks(10n, 10n, 0).ticks, []);
  });
  it('triggers slow labels only on the configured calendar boundary', () => {
    const begin = UNIX_EPOCH_TICKS;
    assert.equal(isSlowTickRequired(begin, begin + 1n, 'second'), false);
    assert.equal(isSlowTickRequired(begin, begin + TICKS_PER_SECOND, 'second'), true);
    assert.equal(isSlowTickRequired(begin, begin + TICKS_PER_SECOND, 'minute'), false);
    assert.equal(isSlowTickRequired(begin, begin + 60n * TICKS_PER_SECOND, 'minute'), true);
    assert.equal(isSlowTickRequired(begin, begin + 3600n * TICKS_PER_SECOND, 'hour'), true);
    assert.equal(isSlowTickRequired(begin, begin + TICKS_PER_DAY, 'day'), true);
    assert.equal(isSlowTickRequired(begin, begin + TICKS_PER_DAY, 'month'), false);
    assert.equal(isSlowTickRequired(begin, begin + 31n * TICKS_PER_DAY, 'month'), true);
    assert.equal(isSlowTickRequired(begin, begin + 365n * TICKS_PER_DAY, 'year'), true);
  });
});

describe('Y axes and colors', () => {
  it('uses midpoint-away-from-zero rounding', () => {
    assert.equal(roundAway(-1.5), -2);
    assert.equal(roundAway(1.5), 2);
  });
  it('ports rounded limits, exact-bound padding and constant ranges', () => {
    assert.deepEqual(getYLimits(0, 32), { min: -100, max: 100, step: 10 });
    assert.deepEqual(getYLimits(968, 1000), { min: 900, max: 1100, step: 10 });
    assert.deepEqual(getYLimits(969, 1000), { min: 960, max: 1010, step: 1 });
    assert.deepEqual(getYLimits(0, 0), { min: -1, max: 1, step: Math.fround(0.1) });
    assert.deepEqual(getYLimits(Math.fround(0.1), Math.fround(0.2)), { min: 0, max: Math.fround(0.3), step: Math.fround(0.01) });
  });
  it('keeps offset float32 samples aligned with the GPU minimum and range uniforms', () => {
    const axis = createAxis('Pa', 1_000_000, 1_000_000.0625, false, FULL_VIEWPORT);
    assert.deepEqual(getYLimits(1_000_000, 1_000_000.0625), { min: 999_999.875, max: 1_000_000.125, step: Math.fround(0.01) });
    const uniforms = new Float32Array([axis.min, axis.max - axis.min]);
    for (const [value, expected] of [[1_000_000, 0.5], [1_000_000.0625, 0.75]]) {
      const pointer = (value - axis.min) / (axis.max - axis.min);
      const gpu = Math.fround(Math.fround(value - uniforms[0]) / uniforms[1]);
      assert.equal(pointer, expected);
      assert.equal(pointer, gpu);
    }
  });
  it('uses float32 padding and finite bounded ticks for huge constant series', () => {
    const floatMax = (2 - 2 ** -23) * 2 ** 127;
    for (const value of [1e7, Math.fround(1e30), -Math.fround(1e30), floatMax, -floatMax]) {
      const limits = getYLimits(value, value);
      assert.ok(Object.values(limits).every(Number.isFinite));
      assert.equal(limits.min, Math.fround(limits.min));
      assert.equal(limits.max, Math.fround(limits.max));
      assert.ok(limits.min < limits.max && limits.min <= value && value <= limits.max);
      const axis = createAxis('Pa', value, value, false, FULL_VIEWPORT);
      const uniforms = new Float32Array([axis.min, axis.max - axis.min]);
      assert.ok(uniforms.every(Number.isFinite) && uniforms[1] > 0);
      assert.equal(axis.max - axis.min, uniforms[1]);
      const ticks = getYTicks(axis.min, axis.max, 7);
      assert.ok(ticks.length > 0 && ticks.length <= 7);
      assert.ok(ticks.every(tick => Number.isFinite(tick) && tick === Math.fround(tick)));
    }
    assert.deepEqual(getYTicks(-floatMax, floatMax, 7), [-2e38, 0, 2e38].map(Math.fround));
  });
  it('uses the original tick thinning factors', () => {
    assert.deepEqual(getYTicks(-1, 1, 5), [-2, -1, 0, 1, 2]);
    assert.deepEqual(getYTicks(900, 1100, 7), [800, 900, 1000, 1100, 1200]);
  });
  it('falls back to nice visible ticks when mobile thinning leaves only zero', () => {
    assert.deepEqual(getYTicks(0, 40, 8), [0, 10, 20, 30, 40]);
    assert.deepEqual(getYTicks(-40, 0, 8), [-40, -30, -20, -10, 0]);
    assert.deepEqual(getYTicks(1000, 1040, 8), [1000, 1010, 1020, 1030, 1040]);
    assert.deepEqual(getYTicks(0, 40, 2), [0, 40]);
  });
  it('bounds fallback ticks and keeps tiny and extreme float32 ranges finite and distinct', () => {
    const tiny = 2 ** -149;
    const floatMax = (2 - 2 ** -23) * 2 ** 127;
    for (const [min, max] of [[0, tiny], [-tiny, tiny], [0, Math.fround(4e-38)], [0, floatMax], [-floatMax, 0], [-floatMax, floatMax], [Math.fround(floatMax - 2 ** 104), floatMax]]) {
      const ticks = getYTicks(min, max, 8);
      const visible = ticks.filter(tick => tick >= min && tick <= max);
      assert.ok(ticks.length <= 8);
      assert.ok(new Set(visible).size >= 2, `${min}..${max}: ${ticks}`);
      assert.ok(ticks.every(tick => Number.isFinite(tick) && tick === Math.fround(tick)));
      assert.ok(visible.every((tick, index) => index === 0 || tick >= visible[index - 1]));
    }
    assert.ok(getYTicks(0, tiny, Number.MAX_VALUE).length <= 1000);
    for (const count of [0, -1, NaN, Infinity]) assert.deepEqual(getYTicks(0, 40, count), []);
    assert.ok(getYTicks(0, 40, 1).length <= 1);
  });
  it('includes zero only as requested and applies vertical viewport to the original range', () => {
    const axis = createAxis('Pa', 1001, 1009, false, FULL_VIEWPORT);
    assert.equal(axis.originalMin, 1000);
    assert.equal(createAxis('Pa', 1001, 1009, true, FULL_VIEWPORT).originalMin, 0);
    assert.equal(createAxis('Pa', -1009, -1001, true, FULL_VIEWPORT).originalMax, 0);
    const zoomed = createAxis('Pa', 1001, 1009, false, { left: 0, right: 1, top: 0.25, bottom: 0.75 });
    assert.equal(zoomed.min, 1002.5);
    assert.equal(zoomed.max, 1007.5);
  });
  it('matches C# float32 vertical zoom operations even in a narrow viewport', () => {
    const viewport = { ...FULL_VIEWPORT, top: 0.1234567, bottom: 0.123458 };
    const axis = createAxis('Pa', 1, 99, false, viewport);
    assert.equal(axis.min, 87.6541976928711);
    assert.equal(axis.max, 87.65432739257812);
    assert.equal(axis.max - axis.min, Math.fround(axis.max - axis.min));
    // A zoom narrower than the sample precision collapses in C# as well.
    const collapsed = createAxis('Pa', 1_000_000, 1_000_000.0625, false, viewport);
    assert.equal(collapsed.min, 1_000_000.125);
    assert.equal(collapsed.max, collapsed.min);
    assert.ok(getYTicks(collapsed.min, collapsed.max, 7).length <= 7);
  });
  it('exposes the GPU float32 span when subtracting float32 bounds is not exact', () => {
    const axis = createAxis('Pa', 1, 99, false, { ...FULL_VIEWPORT, top: 0.1, bottom: 0.9 });
    assert.equal(axis.min, 10.000001907348633);
    // C# Max is 90, but its float32 range is 80, not 79.99999809265137.
    assert.equal(axis.max - axis.min, 80);
    const uniforms = new Float32Array([axis.min, axis.max - axis.min]);
    assert.equal(axis.min, uniforms[0]);
    assert.equal(axis.max - axis.min, uniforms[1]);
  });
  it('preserves engineering notation and all seven UI-accent palette colors', () => {
    for (const [value, expected] of [[0, '0'], [12.34567, '12.35'], [0.00001, '1E-05'], [1000, '1e3'], [12345, '12.35e3'], [-123456, '-123.5e3'], [1234567, '1.235e6']] as const) assert.equal(toEngineering(value), expected);
    assert.deepEqual(SERIES_COLORS, [[34, 211, 238], [167, 139, 250], [163, 230, 53], [251, 191, 36], [251, 113, 133], [56, 189, 248], [52, 211, 153]]);
  });
});

describe('viewport and precision navigator', () => {
  it('composes selection zoom and clamps panned viewports', () => {
    const zoom = applyZoom(FULL_VIEWPORT, { left: 0.25, right: 0.75, top: 0.2, bottom: 0.8 }, 1000n)!;
    assert.equal(zoom.left, 0.25);
    assert.equal(zoom.top, Math.fround(0.2));
    assert.equal(zoom.bottom, Math.fround(0.8));
    assert.equal(applyZoom(zoom, { left: 0.5, right: 1, top: 0, bottom: 1 }, 1000n)!.left, 0.5);
    assert.deepEqual(setViewport({ left: -1, right: 2, top: -1, bottom: 2 }, 1000n), FULL_VIEWPORT);
  });
  it('rejects sub-tick, degenerate, nonfinite and excessively narrow vertical ranges', () => {
    assert.equal(setViewport({ ...FULL_VIEWPORT, right: 0.0001 }, 1000n), null);
    assert.equal(setViewport({ ...FULL_VIEWPORT, bottom: 0.0000001 }, 1000n), null);
    assert.equal(setViewport({ ...FULL_VIEWPORT, left: NaN }, 1000n), null);
    assert.equal(setViewport({ ...FULL_VIEWPORT, left: 1 }, 1000n), null);
    assert.ok(setViewport({ ...FULL_VIEWPORT, right: 0.001 }, 1000n));
  });
  it('shows an eight-window precision domain and fits both boundaries', () => {
    assert.equal(detailWindow(0, 0.125).visible, false);
    const middle = detailWindow(0.49, 0.5);
    assert.equal(middle.visible, true);
    assert.ok(Math.abs(middle.right - middle.left - 0.08) < 1e-12);
    assert.ok(Math.abs(middle.windowLeft - 0.4375) < 1e-12);
    assert.equal(detailWindow(0, 0.01).left, 0);
    assert.equal(detailWindow(0.99, 1).right, 1);
  });
});

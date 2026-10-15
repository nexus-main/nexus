const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const context = { window: { __nexusChartWebGpu: {} }, Uint32Array };
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../src/Nexus.UI/src/charts/chart.webgpu.pyramid.js'), 'utf8'), context);
const { pyramidLayout, planQuery } = context.window.__nexusChartWebGpu;

function summarize(values, start = 0) {
    let minimum = 0, maximum = 0, minIndex = 0, maxIndex = 0, gapIndex = 0, invalidCount = 0, finite = false;
    for (let i = 0; i < values.length; i++) {
        const value = values[i];
        if (Number.isFinite(value)) {
            if (!finite || value < minimum) { minimum = value; minIndex = start + i; }
            if (!finite || value > maximum) { maximum = value; maxIndex = start + i; }
            finite = true;
        } else {
            if (!invalidCount) gapIndex = start + i;
            invalidCount++;
        }
    }
    return { start, count: values.length, first: values[0], last: values.at(-1), minimum, maximum, minIndex, maxIndex, gapIndex, invalidCount, finite };
}

function merge(a, b) {
    if (!a.count) return b;
    if (!b.count) return a;
    const min = a.finite && (!b.finite || a.minimum <= b.minimum) ? a : b;
    const max = a.finite && (!b.finite || a.maximum >= b.maximum) ? a : b;
    return { start: a.start, count: a.count + b.count, first: a.first, last: b.last, minimum: min.minimum, maximum: max.maximum,
        minIndex: min.minIndex, maxIndex: max.maxIndex, gapIndex: a.invalidCount ? a.gapIndex : b.gapIndex,
        invalidCount: a.invalidCount + b.invalidCount, finite: a.finite || b.finite };
}

function project(s) {
    if (s.invalidCount >= s.count - s.invalidCount) return [[s.gapIndex, NaN]];
    const candidates = [[s.start, s.first], [s.start + s.count - 1, s.last], [s.minIndex, s.minimum], [s.maxIndex, s.maximum], ...(s.invalidCount ? [[s.gapIndex, NaN]] : [])];
    return [...new Map(candidates.sort((a, b) => a[0] - b[0]).map(([i, v]) => [i, Number.isFinite(v) ? v : NaN])).entries()];
}

// Direct selection oracle: no summary/merge logic or production threshold expression.
function referenceProjection(values, start = 0) {
    const samples = Array.from(values, (value, index) => ({ value, index }));
    const finite = samples.filter(sample => Number.isFinite(sample.value));
    const invalid = samples.filter(sample => !Number.isFinite(sample.value));
    if (invalid.length >= Math.ceil(values.length / 2)) return [[start + invalid[0].index, NaN]];
    const minimum = finite.toSorted((a, b) => a.value - b.value || a.index - b.index)[0];
    const maximum = finite.toSorted((a, b) => b.value - a.value || a.index - b.index)[0];
    const indices = new Set([0, values.length - 1, minimum.index, maximum.index]);
    if (invalid.length) indices.add(invalid[0].index);
    return [...indices].sort((a, b) => a - b).map(index => [start + index, Number.isFinite(values[index]) ? values[index] : NaN]);
}

const thresholdFixtures = [
    ['below even', 256, 127], ['exact even', 256, 128], ['above even', 256, 129],
    ['below odd', 129, 64], ['above odd', 129, 65],
].map(([name, count, invalidCount]) => ({
    name,
    values: Array.from({ length: count }, (_, i) => i > 0 && i <= invalidCount ? [NaN, Infinity, -Infinity][i % 3] : (i % 17) - 8),
}));
thresholdFixtures.push(
    { name: 'regular sparse gaps', values: Array.from({ length: 256 }, (_, i) => i % 11 === 3 ? [NaN, Infinity, -Infinity][Math.floor(i / 11) % 3] : (i % 17) - 8) },
    { name: 'invalid endpoints below threshold', values: [Infinity, 3, -8, 10, NaN, 5, -Infinity] },
    { name: 'all nonfinite', values: [NaN, Infinity, -Infinity] },
);

test('canonical query plans match direct CPU projection across absolute origins, boundaries and levels', () => {
    for (const origin of [0n, 1n, 255n, 1023n, 9007199254740993123n]) {
        const values = Float32Array.from({ length: 12001 }, (_, i) => i % 727 < 5 ? NaN : i % 97 === 0 ? 100 : (i % 17) - 8);
        const { levels } = pyramidLayout(origin, values.length);
        const summaries = [];
        for (const level of levels) {
            for (let i = 0; i < level.count; i++) {
                const bucket = level.first + BigInt(i);
                const start = Math.max(0, Number(bucket * level.stride - origin));
                const end = Math.min(values.length, Number((bucket + 1n) * level.stride - origin));
                summaries[level.offset + i] = summarize(values.subarray(start, end), start);
            }
        }
        for (const [begin, end, budget] of [[0, 12001, 100], [17, 11987, 50], [255, 1027, 30], [777, 802, 32], [0, 12001, 5]]) {
            const plan = planQuery(origin, values.length, begin, end, budget, levels);
            const actual = [];
            for (let j = 0; j < plan.jobs.length; j += 2) {
                let summary = { count: 0 };
                for (let i = plan.jobs[j]; i < plan.jobs[j] + plan.jobs[j + 1]; i++) {
                    const [index, , start, count] = plan.parts.subarray(i * 4, i * 4 + 4);
                    summary = merge(summary, index === 0xffffffff ? summarize(values.subarray(start, start + count), start) : summaries[index]);
                }
                actual.push(...project(summary));
            }
            const expected = [];
            for (let start = origin + BigInt(begin); start < origin + BigInt(end);) {
                const stop = (start / plan.stride + 1n) * plan.stride;
                const b = Math.min(end, Number(stop - origin));
                expected.push(...referenceProjection(values.subarray(Number(start - origin), b), Number(start - origin)));
                start = origin + BigInt(b);
            }
            assert.deepEqual(actual, expected);
            assert.ok(actual.length <= budget);
            assert.ok(plan.stride < 256n || plan.rawCount <= 510);
        }
    }
});

test('merge preserves invalid counts, earliest signed-zero ties and hidden extrema', () => {
    const fixtures = [[0, -0, 4, 4], [1, NaN, NaN, 2], [NaN, 5, NaN, -8, NaN], [1, 2, 3, 4], ...thresholdFixtures.map(fixture => fixture.values)];
    for (const values of fixtures) {
        for (let split = 1; split < values.length; split++) {
            const combined = merge(summarize(values.slice(0, split)), summarize(values.slice(split), split));
            assert.deepEqual(project(combined), referenceProjection(values));
            assert.equal(combined.invalidCount, values.filter(value => !Number.isFinite(value)).length);
            assert.equal(combined.minimum, summarize(values).minimum);
            assert.equal(combined.maximum, summarize(values).maximum);
        }
    }
});

test('projection uses sample density, not gap runs, at even and odd thresholds', () => {
    for (const { name, values } of thresholdFixtures) {
        const actual = project(summarize(values, 23));
        assert.deepEqual(actual, referenceProjection(values, 23), name);
        const collapsed = name.startsWith('exact') || name.startsWith('above') || name === 'all nonfinite';
        assert.equal(actual.length === 1 && Number.isNaN(actual[0][1]), collapsed, name);
    }
    assert.deepEqual(project(summarize([4, NaN, -8, Infinity, 10, 2])), [[0, 4], [1, NaN], [2, -8], [4, 10], [5, 2]]);
    assert.deepEqual(project(summarize([4, NaN, Infinity, -Infinity, 10, 2])), [[1, NaN]]);
});

test('summary density remains overflow-safe near the u32 count limit', () => {
    for (const count of [0xfffffffe, 0xffffffff]) {
        for (const invalidCount of [Math.ceil(count / 2) - 1, Math.ceil(count / 2), count]) {
            const summary = { ...summarize([3, NaN, -8, 10, 2]), count, invalidCount };
            assert.equal(project(summary).length === 1, invalidCount >= Math.ceil(count / 2));
        }
    }
    const { summaryWgsl, querySummaryShader } = context.window.__nexusChartWebGpu;
    assert.match(summaryWgsl, /s\.invalidCount \+= b\.invalidCount/);
    assert.match(querySummaryShader, /s\.invalidCount >= s\.count - s\.invalidCount/);
    assert.equal(pyramidLayout(0n, 256).bytes, 48);
    assert.equal(planQuery(0n, 256, 0, 256, 5, pyramidLayout(0n, 256).levels).slots, 5);
});

test('query LOD is independent of panning alignment and uses intermediate binary strides', () => {
    for (const origin of [0n, 1n, 255n, 1023n, 9007199254740993123n]) {
        const length = 1000000;
        const { levels } = pyramidLayout(origin, length);
        for (const begin of [0, 1, 255, 1024]) {
            const plan = planQuery(origin, length, begin, begin + 819200, 4000, levels);
            assert.equal(plan.stride, 2048n);
            assert.ok(plan.jobs.length / 2 * plan.slots <= 4000);
            assert.ok(plan.rawCount <= 510);
        }
    }
});

test('query output respects small budgets, raw thresholds and twofold coarse LOD steps', () => {
    for (const origin of [0n, 1n, 1023n, 9007199254740993123n]) {
        const length = 1000000;
        const { levels } = pyramidLayout(origin, length);
        for (const budget of [5, 9, 10, 16, 4000, 32768]) {
            for (const span of [budget, budget + 1, 819199, 819200, 819201]) {
                const plan = planQuery(origin, length, 0, span, budget, levels);
                assert.ok(plan.jobs.length / 2 * plan.slots <= budget);
                assert.equal(plan.stride === 1n, span <= budget);
            }
        }
        let previous;
        for (const span of [817152, 817153, 818176, 818177, 819200, 819201]) {
            const plan = planQuery(origin, length, 0, span, 4000, levels);
            if (previous) assert.ok(plan.stride >= previous && plan.stride <= previous * 2n);
            previous = plan.stride;
        }
    }
});

test('dense periodic signals retain at least two extrema buckets per physical pixel before caps', () => {
    for (const pixels of [320, 1000, 2000]) {
        const budget = 5 * (Math.ceil(pixels * 4) + 1);
        for (const length of [999999, 1000000, 1048576, 2000001]) {
            const { levels } = pyramidLayout(0n, length);
            const plan = planQuery(0n, length, 0, length, budget, levels);
            // A period-32 sine has both extrema in every full bucket at these strides.
            // Subpixel bucket spacing prevents the previous multi-pixel comb spacing.
            assert.ok(Number(plan.stride) * pixels / length <= 0.5);
            assert.ok(plan.jobs.length / 2 * plan.slots <= budget);
        }
    }
});

module.exports = { summarize, merge, project, referenceProjection, thresholdFixtures };

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const context = { window: { __nexusChartWebGpu: {} }, Uint32Array };
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../src/Nexus.UI/src/charts/chart.webgpu.pyramid.js'), 'utf8'), context);
const { pyramidLayout, planQuery } = context.window.__nexusChartWebGpu;

function summarize(values, start = 0) {
    let minimum = 0, maximum = 0, minIndex = 0, maxIndex = 0, gapIndex = 0, gaps = 0, finite = false;
    for (let i = 0; i < values.length; i++) {
        const value = values[i];
        if (Number.isFinite(value)) {
            if (!finite || value < minimum) { minimum = value; minIndex = start + i; }
            if (!finite || value > maximum) { maximum = value; maxIndex = start + i; }
            finite = true;
        } else {
            if (!gaps) gapIndex = start + i;
            if (!i || Number.isFinite(values[i - 1])) gaps = Math.min(2, gaps + 1);
        }
    }
    return { start, count: values.length, first: values[0], last: values.at(-1), minimum, maximum, minIndex, maxIndex, gapIndex, gaps, finite };
}

function merge(a, b) {
    if (!a.count) return b;
    if (!b.count) return a;
    const min = a.finite && (!b.finite || a.minimum <= b.minimum) ? a : b;
    const max = a.finite && (!b.finite || a.maximum >= b.maximum) ? a : b;
    return { start: a.start, count: a.count + b.count, first: a.first, last: b.last, minimum: min.minimum, maximum: max.maximum,
        minIndex: min.minIndex, maxIndex: max.maxIndex, gapIndex: a.gaps ? a.gapIndex : b.gapIndex,
        gaps: Math.min(2, a.gaps + b.gaps - (!Number.isFinite(a.last) && !Number.isFinite(b.first) ? 1 : 0)), finite: a.finite || b.finite };
}

function project(s) {
    if (!s.finite || s.gaps >= 2) return [[s.gapIndex, NaN]];
    const candidates = [[s.start, s.first], [s.start + s.count - 1, s.last], [s.minIndex, s.minimum], [s.maxIndex, s.maximum], ...(s.gaps ? [[s.gapIndex, NaN]] : [])];
    return [...new Map(candidates.sort((a, b) => a[0] - b[0]).map(([i, v]) => [i, Number.isFinite(v) ? v : NaN])).entries()];
}

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
                expected.push(...project(summarize(values.subarray(Number(start - origin), b), Number(start - origin))));
                start = origin + BigInt(b);
            }
            assert.deepEqual(actual, expected);
            assert.ok(actual.length <= budget);
            assert.ok(plan.stride < 256n || plan.rawCount <= 510);
        }
    }
});

test('merge preserves boundary gap runs, earliest signed-zero ties and hidden extrema', () => {
    const fixtures = [[0, -0, 4, 4], [1, NaN, NaN, 2], [NaN, 5, NaN, -8, NaN], [1, 2, 3, 4]];
    for (const values of fixtures) {
        for (let split = 1; split < values.length; split++) {
            const combined = merge(summarize(values.slice(0, split)), summarize(values.slice(split), split));
            assert.deepEqual(project(combined), project(summarize(values)));
            assert.equal(combined.minimum, summarize(values).minimum);
            assert.equal(combined.maximum, summarize(values).maximum);
        }
    }
});

module.exports = { summarize, merge, project };

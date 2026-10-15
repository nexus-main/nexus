const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const { referenceProjection, thresholdFixtures, signedZeroFixtures } = require('./chart.projection.fixtures.js');
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
            const raw = Float32Array.from(plan.ranges.flatMap(({ offset, count }) => {
                assert.ok(offset >= begin && offset + count <= end);
                return [...values.subarray(offset, offset + count)];
            }));
            assert.equal(raw.length, plan.rawCount);
            const actual = [];
            for (let j = 0; j < plan.jobs.length; j += 2) {
                let summary = { count: 0 };
                for (let i = plan.jobs[j]; i < plan.jobs[j] + plan.jobs[j + 1]; i++) {
                    const [index, rawOffset, start, count] = plan.parts.subarray(i * 4, i * 4 + 4);
                    summary = merge(summary, index === 0xffffffff ? summarize(raw.subarray(rawOffset, rawOffset + count), start) : summaries[index]);
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
            assert.deepEqual(actual, expected, `origin=${origin} view=${begin}:${end} budget=${budget}`);
            assert.ok(actual.length <= budget);
            assert.ok(plan.stride < 256n || plan.rawCount <= 510);
        }
    }
});

test('CPU summary helpers match the direct oracle across splits, ties and density thresholds', () => {
    const fixtures = [
        { name: 'endpoint zero tie', values: [0, -0, 4, 4] },
        { name: 'exact half gaps', values: [1, NaN, NaN, 2] },
        { name: 'hidden extrema in mostly-invalid child', values: [2, NaN, -8, 10, NaN, NaN, 2, 2, 2, 2] },
        { name: 'finite ramp', values: [1, 2, 3, 4] },
        ...thresholdFixtures, ...signedZeroFixtures,
    ];
    for (const { name, values } of fixtures) {
        const direct = summarize(values);
        assert.deepEqual(project(summarize(values, 23)), referenceProjection(values, 23), name);
        for (let split = 1; split < values.length; split++) {
            const id = `${name} split=${split}`;
            const combined = merge(summarize(values.slice(0, split)), summarize(values.slice(split), split));
            assert.deepEqual(project(combined), referenceProjection(values), id);
            assert.equal(combined.invalidCount, direct.invalidCount, id);
            assert.equal(combined.minimum, direct.minimum, id);
            assert.equal(combined.maximum, direct.maximum, id);
        }
    }
    for (const { name, values } of thresholdFixtures) {
        const actual = project(summarize(values, 23));
        const collapsed = name.startsWith('exact') || name.startsWith('above') || name === 'all nonfinite';
        assert.equal(actual.length === 1 && Number.isNaN(actual[0][1]), collapsed, name);
    }
    assert.deepEqual(project(summarize([4, NaN, -8, Infinity, 10, 2])), [[0, 4], [1, NaN], [2, -8], [4, 10], [5, 2]]);
    assert.deepEqual(project(summarize([4, NaN, Infinity, -Infinity, 10, 2])), [[1, NaN]]);
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

test('planner density provides at least two buckets per physical pixel before caps', () => {
    for (const pixels of [320, 1000, 2000]) {
        const budget = 5 * (Math.ceil(pixels * 4) + 1);
        for (const length of [999999, 1000000, 1048576, 2000001]) {
            const { levels } = pyramidLayout(0n, length);
            const plan = planQuery(0n, length, 0, length, budget, levels);
            assert.ok(Number(plan.stride) * pixels / length <= 0.5);
            assert.ok(plan.jobs.length / 2 * plan.slots <= budget);
        }
    }
});

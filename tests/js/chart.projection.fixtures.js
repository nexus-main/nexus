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

const signedZeroFixtures = [
    { name: 'interior minimum positive zero first', values: [4, 0, -0, 4] },
    { name: 'interior minimum negative zero first', values: [4, -0, 0, 4] },
    { name: 'interior maximum positive zero first', values: [-4, 0, -0, -4] },
    { name: 'interior maximum negative zero first', values: [-4, -0, 0, -4] },
];

module.exports = { referenceProjection, thresholdFixtures, signedZeroFixtures };

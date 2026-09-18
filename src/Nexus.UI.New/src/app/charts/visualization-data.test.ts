import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { createVisualizationData, setVisualizationSeriesValues } from './visualization-data.ts';

const descriptors = [
    { id: '/a', name: 'A', unit: 'm/s' },
    { id: '/b', name: 'B', unit: 'degC' },
] as const;

describe('createVisualizationData', () => {
    it('creates empty chart series metadata for aligned ranges', () => {
        const data = createVisualizationData(9n, 21n, 3n, descriptors);

        assert.equal(data.begin, 9n);
        assert.equal(data.end, 21n);
        assert.equal(data.series.length, 2);
        assert.equal(data.series[0].id, '/a');
        assert.equal(data.series[0].name, 'A');
        assert.equal(data.series[0].unit, 'm/s');
        assert.equal(data.series[0].samplePeriod, 3n);
        assert.equal(data.series[0].length, 4);
        assert.equal(data.series[0].availableLength, 0);
        assert.equal(data.series[0].version, 0);
        assert.equal(data.series[0].complete, false);
        assert.deepEqual(data.series[0].chunks, []);
    });

    it('rejects invalid ranges and descriptor selections', () => {
        assert.throws(() => createVisualizationData(0n, 1n, 0n, descriptors), /positive/);
        assert.throws(() => createVisualizationData(1n, 1n, 1n, descriptors), /before/);
        assert.throws(() => createVisualizationData(0n, 2n, 3n, descriptors), /align/);
        assert.throws(() => createVisualizationData(0n, 1n, 1n, []), /1 and 100/);
        assert.throws(() => createVisualizationData(0n, 1n, 1n, [descriptors[0], descriptors[0]]), /unique/);
    });

    it('enforces the visualization memory budget', () => {
        const selected = descriptors.slice(0, 1);
        const maxFloat32Values = 2048n * 1024n * 1024n / 4n;

        assert.equal(createVisualizationData(0n, maxFloat32Values, 1n, selected).series[0].length, Number(maxFloat32Values));
        assert.throws(() => createVisualizationData(0n, maxFloat32Values + 1n, 1n, selected), /2048 MiB/);
    });
});

describe('setVisualizationSeriesValues', () => {
    it('uses generated client values as chart chunks', () => {
        const data = createVisualizationData(0n, 3n, 1n, descriptors.slice(0, 1));
        const values = new Float32Array([1, 2, 3]);

        setVisualizationSeriesValues(data.series[0], values);

        assert.deepEqual(data.series[0].chunks, [values]);
        assert.equal(data.series[0].availableLength, 3);
        assert.equal(data.series[0].version, 1);
        assert.equal(data.series[0].complete, true);
    });

    it('rejects mismatched generated client value lengths', () => {
        const data = createVisualizationData(0n, 3n, 1n, descriptors.slice(0, 1));

        assert.throws(() => setVisualizationSeriesValues(data.series[0], new Float32Array([1, 2])), /unexpected sample count/);
    });
});

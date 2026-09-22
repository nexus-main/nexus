import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { CHUNK_LENGTH } from './chart-math.ts';
import { VisualizationBuffers, createVisualizationData, releaseVisualizationData, setVisualizationSeriesValues } from './visualization-data.ts';

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

describe('releaseVisualizationData', () => {
    it('clears unpreserved completed chunks', () => {
        const data = createVisualizationData(0n, 2n, 1n, [{ id: 'a', name: 'A', unit: 'V' }]);
        const values = new Float32Array([1, 2]);
        setVisualizationSeriesValues(data.series[0], values);

        releaseVisualizationData(data);

        assert.deepEqual(data.series[0].chunks, []);
        assert.equal(data.series[0].availableLength, 0);
        assert.equal(data.series[0].complete, false);
    });

    it('keeps chunks selected for reuse', () => {
        const data = createVisualizationData(0n, 2n, 1n, [{ id: 'a', name: 'A', unit: 'V' }]);
        const values = new Float32Array([1, 2]);
        setVisualizationSeriesValues(data.series[0], values);
        const chunks = data.series[0].chunks;

        releaseVisualizationData(data, new Set([chunks]));

        assert.equal(data.series[0].chunks, chunks);
        assert.equal(data.series[0].availableLength, 2);
        assert.equal(data.series[0].complete, true);
    });
});

describe('VisualizationBuffers', () => {
    it('allocates generated-client buffers and publishes them on completion', () => {
        const data = createVisualizationData(0n, 3n, 1n, descriptors.slice(0, 1));
        const buffers = new VisualizationBuffers(data.series);

        const chunk = buffers.provider('/a', 3, 3);

        assert.equal(buffers.provider.length, 3);
        assert.equal(chunk.length, 3);
        assert.deepEqual(data.series[0].chunks, []);
        assert.equal(data.series[0].availableLength, 0);

        buffers.complete();

        assert.deepEqual(data.series[0].chunks, [chunk]);
        assert.equal(data.series[0].availableLength, 3);
        assert.equal(data.series[0].version, 2);
        assert.equal(data.series[0].complete, true);
    });

    it('publishes the previous chart-sized chunk when requesting the next one', () => {
        const data = createVisualizationData(0n, BigInt(CHUNK_LENGTH + 2), 1n, descriptors.slice(0, 1));
        const buffers = new VisualizationBuffers(data.series);

        const first = buffers.createChunk('/a', CHUNK_LENGTH, CHUNK_LENGTH + 2);
        const second = buffers.createChunk('/a', 2, 2);

        assert.equal(first.length, CHUNK_LENGTH);
        assert.equal(second.length, 2);
        assert.deepEqual(data.series[0].chunks, [first]);
        assert.equal(data.series[0].availableLength, CHUNK_LENGTH);

        buffers.complete();

        assert.deepEqual(data.series[0].chunks, [first, second]);
        assert.equal(data.series[0].availableLength, CHUNK_LENGTH + 2);
        assert.equal(data.series[0].complete, true);
    });

    it('rejects unknown series and unexpected lengths', () => {
        const data = createVisualizationData(0n, 3n, 1n, descriptors.slice(0, 1));
        const buffers = new VisualizationBuffers(data.series);

        assert.throws(() => buffers.createChunk('/missing', 3, 3), /unknown visualization series/);
        assert.throws(() => buffers.createChunk('/a', 2, 2), /unexpected sample count/);
    });
});

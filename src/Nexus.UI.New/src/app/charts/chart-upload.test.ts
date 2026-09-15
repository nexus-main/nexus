import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import type { VisualizationSeries } from './visualization-data.ts';
import type { ChartInterop } from './chart-interop.ts';
import { CHUNK_LENGTH } from './chart-math.ts';
import { provideSeriesChunk, uploadSeries, waitForRange } from './chart-upload.ts';

function source(length: number): VisualizationSeries {
  return { id: 'series', name: 'Series', unit: 'K', samplePeriod: 1n, length, chunks: [], availableLength: 0, version: 0, complete: false };
}

function gpu(): { api: ChartInterop['chartWebGpu']; calls: unknown[][] } {
  const calls: unknown[][] = [];
  const api = {
    beginChunkedSeries: async (...args: unknown[]) => { calls.push(['begin', ...args]); return 7; },
    appendChunkedSeries: (...args: unknown[]) => { calls.push(['append', ...args]); },
    processChunkedSeriesUpload: async (...args: unknown[]) => { calls.push(['process', ...args]); },
    completeChunkedSeries: async (...args: unknown[]) => { calls.push(['complete', ...args]); return { hasValue: true, minimum: -1, maximum: 9 }; },
    abortChunkedSeries: (...args: unknown[]) => { calls.push(['abort', ...args]); },
    appendSeriesChunk: (...args: unknown[]) => { calls.push(['raw', ...args]); },
  } as unknown as ChartInterop['chartWebGpu'];
  return { api, calls };
}

describe('progressive GPU uploads', () => {
  it('waits for publication without input reassignment, uploads aligned chunks once, and retains CPU storage', async () => {
    const series = source(CHUNK_LENGTH + 3);
    const { api, calls } = gpu();
    const controller = new AbortController();
    const task = uploadSeries(api, 'chart', series, series.version, controller.signal);
    const first = new Float32Array(CHUNK_LENGTH);
    series.chunks.push(first);
    series.availableLength = first.length;
    series.version++;
    await new Promise(resolve => setTimeout(resolve, 50));
    assert.equal(calls.filter(call => call[0] === 'append').length, 1);
    assert.equal(calls.filter(call => call[0] === 'complete').length, 0);
    const last = new Float32Array([1, -1, 9]);
    series.chunks.push(last);
    series.availableLength += last.length;
    series.version++;
    series.complete = true;
    assert.deepEqual(await task, { hasValue: true, minimum: -1, maximum: 9 });
    assert.equal(calls.filter(call => call[0] === 'begin').length, 1);
    const uploads = calls.filter(call => call[0] === 'append');
    assert.deepEqual(uploads.map(call => [call[3], call[5]]), [[0, CHUNK_LENGTH], [CHUNK_LENGTH, 3]]);
    assert.equal((uploads[0][4] as Float32Array).buffer, first.buffer);
    assert.equal(calls.filter(call => call[0] === 'abort').length, 0);
    assert.equal(series.chunks[0], first);
  });
  it('aborts upload tokens and wakes pending publication waits', async () => {
    const { api, calls } = gpu();
    const controller = new AbortController();
    const task = uploadSeries(api, 'chart', source(10), 0, controller.signal);
    const rejected = assert.rejects(task, /cancelled/);
    await new Promise(resolve => setTimeout(resolve, 5));
    controller.abort(new Error('cancelled'));
    await rejected;
    assert.equal(calls.filter(call => call[0] === 'abort').length, 1);
    assert.equal(calls.filter(call => call[0] === 'append').length, 0);
  });
  it('cleans up a token that arrives after cancellation', async () => {
    const { api, calls } = gpu();
    let resolve!: (token: number) => void;
    api.beginChunkedSeries = () => new Promise<number>(done => { resolve = done; });
    const controller = new AbortController();
    const task = uploadSeries(api, 'chart', source(2), 0, controller.signal);
    const rejected = assert.rejects(task, /cancelled/);
    controller.abort(new Error('cancelled'));
    resolve(99);
    await rejected;
    assert.deepEqual(calls, [['abort', 'chart', 99]]);
  });
  it('rejects malformed completion and cleans up after GPU rejection', async () => {
    const series = source(2);
    series.complete = true;
    const { api, calls } = gpu();
    await assert.rejects(uploadSeries(api, 'chart', series, 0, new AbortController().signal), /without the requested samples/);
    assert.equal(calls.filter(call => call[0] === 'abort').length, 1);
    series.chunks.push(new Float32Array(2));
    series.availableLength = 2;
    api.processChunkedSeriesUpload = async () => { throw new Error('device lost'); };
    await assert.rejects(uploadSeries(api, 'chart', series, 0, new AbortController().signal), /device lost/);
    assert.equal(calls.filter(call => call[0] === 'abort').length, 2);
  });
  it('splits raw requests at CPU chunk boundaries with request-relative offsets', async () => {
    const series = source(CHUNK_LENGTH + 2);
    series.chunks = [new Float32Array(CHUNK_LENGTH), new Float32Array([8, 9])];
    series.availableLength = series.length;
    series.complete = true;
    const { api, calls } = gpu();
    await provideSeriesChunk(api, 'chart', series, CHUNK_LENGTH - 1, 3, 42, new AbortController().signal);
    assert.deepEqual(calls.map(call => [call[2], call[3], call[5]]), [[42, 0, 1], [42, 1, 2]]);
    assert.equal((calls[0][4] as Float32Array).buffer, series.chunks[0].buffer);
    await assert.rejects(waitForRange(series, -1, 2, new AbortController().signal), /Invalid raw data range/);
    await assert.rejects(waitForRange(series, 0, series.length + 1, new AbortController().signal), /Invalid raw data range/);
  });
  it('does not create unsupported GPU buffers for fewer than two samples', async () => {
    const { api, calls } = gpu();
    assert.equal((await uploadSeries(api, 'chart', source(1), 0, new AbortController().signal)).hasValue, false);
    assert.deepEqual(calls, []);
  });
});

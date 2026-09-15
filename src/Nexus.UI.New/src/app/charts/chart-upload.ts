import type { VisualizationSeries } from './visualization-data.ts';
import type { ChartInterop, GpuRange } from './chart-interop.ts';

// The loader and WebGPU transient buffer both publish 4 Mi-sample chunks.
const CHUNK_LENGTH = 4 * 1024 * 1024;

export async function waitForRange(series: VisualizationSeries, offset: number, count: number, signal: AbortSignal): Promise<void> {
  if (!Number.isSafeInteger(offset) || !Number.isSafeInteger(count) || offset < 0 || count < 0 || offset > series.length - count) {
    throw new RangeError(`Invalid raw data range for '${series.id}'.`);
  }
  while (true) {
    signal.throwIfAborted();
    if (offset + count <= series.availableLength) return;
    if (series.complete) throw new Error(`Series '${series.id}' completed without the requested samples.`);
    await new Promise<void>((resolve, reject) => {
      const abort = (): void => { clearTimeout(timer); reject(signal.reason); };
      const timer = setTimeout(() => { signal.removeEventListener('abort', abort); resolve(); }, 32);
      signal.addEventListener('abort', abort, { once: true });
    });
  }
}

export function seriesSegment(series: VisualizationSeries, offset: number, count: number): Float32Array {
  const chunk = series.chunks[Math.floor(offset / CHUNK_LENGTH)];
  const local = offset % CHUNK_LENGTH;
  if (!chunk || local >= chunk.length) throw new Error(`Series '${series.id}' has no published chunk at ${offset}.`);
  return chunk.subarray(local, Math.min(chunk.length, local + count));
}

export async function uploadSeries(api: ChartInterop['chartWebGpu'], chartId: string, series: VisualizationSeries, version: number, signal: AbortSignal): Promise<GpuRange> {
  let token: number | undefined;
  const abort = (): void => {
    if (token !== undefined) {
      api.abortChunkedSeries(chartId, token);
      token = undefined;
    }
  };
  signal.addEventListener('abort', abort, { once: true });
  try {
    signal.throwIfAborted();
    if (series.length < 2) return { hasValue: false, minimum: 0, maximum: 0 };
    token = await api.beginChunkedSeries(chartId, series.id, version, series.length);
    signal.throwIfAborted();
    for (let offset = 0; offset < series.length;) {
      const count = Math.min(CHUNK_LENGTH, series.length - offset);
      await waitForRange(series, offset, count, signal);
      const values = seriesSegment(series, offset, count);
      if (values.length !== count) throw new Error(`Series '${series.id}' contains a short non-final chunk.`);
      api.appendChunkedSeries(chartId, token, offset, values, values.length);
      await api.processChunkedSeriesUpload(chartId, token, offset, values.length);
      signal.throwIfAborted();
      offset += values.length;
    }
    const range = await api.completeChunkedSeries(chartId, token);
    token = undefined;
    signal.throwIfAborted();
    return range;
  } finally {
    signal.removeEventListener('abort', abort);
    abort();
  }
}

export async function provideSeriesChunk(api: ChartInterop['chartWebGpu'], chartId: string, series: VisualizationSeries, offset: number, count: number, requestId: number, signal: AbortSignal): Promise<void> {
  await waitForRange(series, offset, count, signal);
  signal.throwIfAborted();
  for (let written = 0; written < count;) {
    const values = seriesSegment(series, offset + written, count - written);
    api.appendSeriesChunk(chartId, requestId, written, values, values.length);
    written += values.length;
  }
}

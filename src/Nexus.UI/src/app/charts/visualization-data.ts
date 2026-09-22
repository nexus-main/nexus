import { CHUNK_LENGTH } from './chart-math.ts'

export interface VisualizationSeries {
  id: string
  name: string
  unit: string
  samplePeriod: bigint
  length: number
  chunks: Float32Array[]
  availableLength: number
  version: number
  complete: boolean
}

export interface VisualizationData {
  begin: bigint
  end: bigint
  series: VisualizationSeries[]
}

export function createVisualizationData(
  begin: bigint,
  end: bigint,
  samplePeriod: bigint,
  descriptors: readonly { id: string; name: string; unit: string }[],
): VisualizationData {
  if (samplePeriod <= 0n) throw new RangeError('Sample period must be positive')
  if (end <= begin) throw new RangeError('Begin must be before end')
  if (begin % samplePeriod !== 0n || end % samplePeriod !== 0n) {
    throw new RangeError('Begin and end must align with the sample period')
  }
  if (descriptors.length < 1 || descriptors.length > 100) throw new RangeError('Select between 1 and 100 series')
  if (new Set(descriptors.map(({ id }) => id)).size !== descriptors.length) throw new Error('Series ids must be unique')
  const length = (end - begin) / samplePeriod
  if (length > BigInt(Number.MAX_SAFE_INTEGER)) throw new RangeError('Series length must be a safe integer')
  return {
    begin, end,
    series: descriptors.map(({ id, name, unit }) => ({
      id, name, unit, samplePeriod, length: Number(length), chunks: [],
      availableLength: 0, version: 0, complete: false,
    })),
  }
}

export function setVisualizationSeriesValues(series: VisualizationSeries, values: Float32Array): void {
  if (values.length !== series.length) throw new Error('The generated client returned an unexpected sample count')
  series.chunks = values.length ? [values] : []
  series.availableLength = values.length
  series.version++
  series.complete = true
}

export class VisualizationBuffers {
  private readonly seriesById: ReadonlyMap<string, VisualizationSeries>
  private readonly current = new Map<string, Float32Array>()
  private disposed = false

  readonly provider: (resourcePath: string, chunkLength: number, remainingLength: number) => Float32Array = (resourcePath, chunkLength, remainingLength) => {
    return this.createChunk(resourcePath, chunkLength, remainingLength)
  }

  constructor(series: readonly VisualizationSeries[]) {
    this.seriesById = new Map(series.map(item => [item.id, item]))
  }

  createChunk(resourcePath: string, chunkLength: number, remainingLength: number): Float32Array {
    if (this.disposed) throw new Error('Visualization buffers have already been disposed')
    const series = this.seriesById.get(resourcePath)
    if (!series) throw new Error(`The generated client requested an unknown visualization series: ${resourcePath}`)
    if (!Number.isSafeInteger(chunkLength) || chunkLength < 0) throw new Error('The generated client requested an invalid chunk length')
    if (!Number.isSafeInteger(remainingLength) || remainingLength < chunkLength) throw new Error('The generated client requested an invalid remaining sample count')
    const pendingLength = this.current.get(resourcePath)?.length ?? 0
    if (series.availableLength + pendingLength + remainingLength !== series.length) throw new Error('The generated client requested an unexpected sample count')
    if (chunkLength > CHUNK_LENGTH) throw new Error('The generated client requested an oversized visualization chunk')
    if (remainingLength > chunkLength && chunkLength !== CHUNK_LENGTH) throw new Error('The generated client requested an unaligned visualization chunk')

    this.publish(resourcePath)
    const chunk = new Float32Array(chunkLength)
    this.current.set(resourcePath, chunk)
    return chunk
  }

  complete(): void {
    if (this.disposed) throw new Error('Visualization buffers have already been disposed')
    for (const resourcePath of this.seriesById.keys()) this.publish(resourcePath)
    for (const series of this.seriesById.values()) {
      if (series.availableLength !== series.length) throw new Error(`Visualization series '${series.id}' completed with an unexpected sample count`)
      if (!series.complete) {
        series.complete = true
        series.version++
      }
    }
  }

  dispose(): void {
    this.disposed = true
    this.current.clear()
    for (const series of this.seriesById.values()) {
      if (!series.complete) {
        series.chunks = []
        series.availableLength = 0
      }
    }
  }

  private publish(resourcePath: string): void {
    const chunk = this.current.get(resourcePath)
    if (!chunk) return
    this.current.delete(resourcePath)
    const series = this.seriesById.get(resourcePath)!

    series.chunks.push(chunk)
    series.availableLength += chunk.length
    series.version++
  }
}

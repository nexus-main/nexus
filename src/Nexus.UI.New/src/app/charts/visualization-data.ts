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

const maxBytes = 2048n * 1024n * 1024n

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
  if (length * BigInt(descriptors.length) * 4n > maxBytes) throw new RangeError('Float32 data exceeds the 2048 MiB budget')
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

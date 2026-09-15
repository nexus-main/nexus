import { AsyncByteStream, DataType, Precision, RecordBatchReader } from 'apache-arrow'
import type { Float32, Int32, Int64, List, Schema } from 'apache-arrow'

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

const chunkLength = 4 * 1024 * 1024
const maxBytes = 2048n * 1024n * 1024n
type StreamSchema = { resourceIndex: Int32; offset: Int64; values: List<Float32> }

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

function validateSchema(schema: Schema | undefined): void {
  const fields = schema?.fields
  if (!fields || fields.length !== 3) throw new Error('Invalid Arrow schema')
  const [index, offset, values] = fields
  if (index.name !== 'resourceIndex' || !DataType.isInt(index.type) || !index.type.isSigned || index.type.bitWidth !== 32
    || offset.name !== 'offset' || !DataType.isInt(offset.type) || !offset.type.isSigned || offset.type.bitWidth !== 64
    || values.name !== 'values' || !DataType.isList(values.type) || values.type.children.length !== 1
    || !DataType.isFloat(values.type.valueType) || values.type.valueType.precision !== Precision.SINGLE) {
    throw new Error('Invalid Arrow schema: expected resourceIndex int32, offset int64, values list<float32>')
  }
}

/** Mutates a fresh model. Progress counts decoded Float32 bytes, not IPC overhead. */
export async function loadVisualizationData(
  response: Response,
  data: VisualizationData,
  onProgress: (fraction: number) => void,
  signal: AbortSignal,
): Promise<void> {
  const body = response.body?.getReader()
  let reader: RecordBatchReader<StreamSchema> | undefined
  // Cancelling the owned reader resolves pending reads even if the source's cancel promise never settles.
  const cancelBody = () => { void body?.cancel(signal.reason).catch(() => {}) }
  signal.addEventListener('abort', cancelBody, { once: true })
  try {
    if (signal.aborted) cancelBody()
    signal.throwIfAborted()
    if (!response.ok) throw new Error(`Arrow request failed: HTTP ${response.status}`)
    if (!body) throw new Error('Arrow response has no body')
    const expected = createVisualizationData(data.begin, data.end, data.series[0]?.samplePeriod ?? 0n, data.series)
    if (data.series.some((series, index) => series.length !== expected.series[index].length
      || series.samplePeriod !== expected.series[index].samplePeriod || series.chunks.length !== 0
      || series.availableLength !== 0 || series.version !== 0 || series.complete)) {
      throw new Error('Loading requires a fresh visualization model')
    }
    const states = data.series.map(() => ({ consumed: 0, written: 0, chunk: undefined as Float32Array | undefined }))
    const totalBytes = data.series.reduce((sum, series) => sum + series.length * 4, 0)
    let decodedBytes = 0
    let receivedBytes = 0
    const prefix = new Uint8Array(6)
    const tail = new Uint8Array(8)
    async function* bytes(): AsyncGenerator<Uint8Array> {
      while (true) {
        signal.throwIfAborted()
        const result = await body!.read()
        signal.throwIfAborted()
        if (result.done) return
        const value = result.value
        for (let i = 0; i < Math.min(value.length, 6 - receivedBytes); i++) prefix[receivedBytes + i] = value[i]
        receivedBytes += value.length
        if (receivedBytes >= 6 && prefix[0] === 65 && prefix[1] === 82 && prefix[2] === 82
          && prefix[3] === 79 && prefix[4] === 87 && prefix[5] === 49) throw new Error('Expected Arrow IPC stream, not file')
        if (value.length >= 8) tail.set(value.subarray(value.length - 8))
        else {
          tail.copyWithin(0, value.length)
          tail.set(value, 8 - value.length)
        }
        yield value
      }
    }
    const source = new AsyncByteStream(bytes())
    reader = await RecordBatchReader.from<StreamSchema>(source)
    signal.throwIfAborted()
    await reader.open({ autoDestroy: false })
    signal.throwIfAborted()
    validateSchema(reader.schema)
    onProgress(0)
    for await (const batch of reader) {
      signal.throwIfAborted()
      validateSchema(batch.schema)
      const indexes = batch.getChildAt(0)!
      const offsets = batch.getChildAt(1)!
      const lists = batch.getChildAt(2)!
      if (indexes.nullCount || offsets.nullCount || lists.nullCount
        || lists.data.some((list) => list.children.some((child) => child.nullCount !== 0))) {
        throw new Error('Arrow stream contains nulls')
      }
      for (const list of lists.data) {
        const child = list.children[0]
        if (!child || list.valueOffsets.length < list.length + 1) throw new Error('Invalid Arrow list offsets')
        for (let row = 0; row < list.length; row++) {
          const begin = list.valueOffsets[row]
          const end = list.valueOffsets[row + 1]
          if (begin < 0 || end < begin || end > child.length) throw new Error('Invalid Arrow list offsets')
        }
      }
      for (let row = 0; row < batch.numRows; row++) {
        signal.throwIfAborted()
        const index = indexes.get(row)
        const offset = offsets.get(row)
        if (index === null || !Number.isInteger(index) || index < 0 || index >= states.length) {
          throw new Error('Arrow stream contains an invalid resource index')
        }
        const state = states[index]
        const series = data.series[index]
        if (typeof offset !== 'bigint' || offset < 0n || offset !== BigInt(state.consumed)) {
          throw new Error('Arrow stream contains an invalid or out-of-order offset')
        }
        // Arrow's list accessor slices child buffers and validity at their actual offsets.
        const values = lists.get(row)
        if (!values || values.nullCount) throw new Error('Arrow stream contains null values')
        if (values.length > series.length - state.consumed) throw new Error('Arrow stream contains more data than expected')
        for (const part of values.data) {
          const source = part.values
          if (!(source instanceof Float32Array) || source.length < part.length) throw new Error('Invalid Arrow values buffer')
          let position = 0
          while (position < part.length) {
            signal.throwIfAborted()
            state.chunk ??= new Float32Array(Math.min(chunkLength, series.length - state.consumed))
            const count = Math.min(part.length - position, state.chunk.length - state.written)
            state.chunk.set(source.subarray(position, position + count), state.written)
            position += count
            state.written += count
            state.consumed += count
            decodedBytes += count * 4
            if (state.written === state.chunk.length) {
              series.chunks.push(state.chunk)
              series.availableLength += state.chunk.length
              series.version++
              state.chunk = undefined
              state.written = 0
            }
            onProgress(decodedBytes / totalBytes)
          }
        }
      }
    }
    signal.throwIfAborted()
    // The server writes an explicit, aligned IPC end marker. Arrow itself accepts truncated markers as EOF.
    if (receivedBytes < 8 || receivedBytes % 8 !== 0 || tail.some((byte, index) => byte !== (index < 4 ? 255 : 0))) {
      throw new Error('Arrow stream is truncated or missing its end marker')
    }
    // Read through Arrow's byte source so trailing bytes already buffered there are not missed.
    const end = await source.read(1)
    signal.throwIfAborted()
    if (end?.byteLength) throw new Error('Unexpected data after Arrow stream end')
    if (states.some((state, index) => state.consumed !== data.series[index].length)) {
      throw new Error('Arrow stream ended before all data was received')
    }
    onProgress(1)
    signal.throwIfAborted()
    for (const series of data.series) series.complete = true
  } finally {
    signal.removeEventListener('abort', cancelBody)
    cancelBody()
    try { await reader?.cancel() } finally { body?.releaseLock() }
  }
}

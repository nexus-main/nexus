import assert from 'node:assert/strict'
import { describe, it, mock } from 'node:test'
import {
  Field, Float32, Float64, Int16, Int32, Int64, List, RecordBatch,
  RecordBatchReader, Schema, Struct, Table, Uint32, Uint64, Utf8, makeData, tableToIPC,
} from 'apache-arrow'
import type { Data } from 'apache-arrow'
import { createVisualizationData, loadVisualizationData } from './visualization-data.ts'

const chunkLength = 4 * 1024 * 1024
const descriptors = [{ id: 'a', name: 'Temperature', unit: 'K' }, { id: 'b', name: 'Pressure', unit: 'Pa' }]
const listType = new List(new Field('item', new Float32(), true))
const schema = new Schema([
  new Field('resourceIndex', new Int32(), false),
  new Field('offset', new Int64(), false),
  new Field('values', listType, false),
])
type Row = readonly [number, bigint, Float32Array]
const floats = (...values: number[]) => new Float32Array(values)
const model = (length = 2, count = 1) => createVisualizationData(0n, BigInt(length), 1n, descriptors.slice(0, count))

// Like the client fixtures, but supports multi-row batches and typed payloads of GPU-sized chunks.
function batch(rows: readonly Row[]): RecordBatch {
  const indexes = new Int32Array(rows.length)
  const offsets = new BigInt64Array(rows.length)
  const listOffsets = new Int32Array(rows.length + 1)
  const values = new Float32Array(rows.reduce((sum, row) => sum + row[2].length, 0))
  rows.forEach(([index, offset, payload], row) => {
    indexes[row] = index
    offsets[row] = offset
    values.set(payload, listOffsets[row])
    listOffsets[row + 1] = listOffsets[row] + payload.length
  })
  return new RecordBatch(schema, makeData({ type: new Struct(schema.fields), length: rows.length, children: [
    makeData({ type: new Int32(), data: indexes }),
    makeData({ type: new Int64(), data: offsets }),
    makeData({ type: listType, length: rows.length, valueOffsets: listOffsets,
      child: makeData({ type: new Float32(), data: values }) }),
  ] }))
}

function ipc(...batches: RecordBatch[]): Uint8Array {
  return tableToIPC(new Table(batches[0]?.schema ?? schema, batches), 'stream')
}

function fragmented(bytes: Uint8Array, fragmentSize = 17): Response {
  let position = 0
  return new Response(new ReadableStream<Uint8Array>({
    pull(controller) {
      if (position === bytes.length) controller.close()
      else {
        const end = Math.min(bytes.length, position + fragmentSize)
        controller.enqueue(bytes.subarray(position, end))
        position = end
      }
    },
  }))
}

async function load(bytes: Uint8Array, data = model(), onProgress = (_fraction: number) => {}) {
  await loadVisualizationData(fragmented(bytes), data, onProgress, new AbortController().signal)
  return data
}

describe('createVisualizationData', () => {
  it('exports the agreed model without allocating sample buffers and preserves exact ticks', () => {
    const begin = 9007199254740993n
    const data = createVisualizationData(begin, begin + 6n, 3n, descriptors)
    assert.deepEqual(data, { begin, end: begin + 6n, series: descriptors.map((descriptor) => ({
      ...descriptor, samplePeriod: 3n, length: 2, chunks: [], availableLength: 0, version: 0, complete: false,
    })) })
  })

  it('validates positive periods, ordering, both endpoint alignments and safe integer lengths', () => {
    for (const [begin, end, period, message] of [
      [0n, 2n, 0n, /positive/], [0n, 2n, -1n, /positive/], [2n, 2n, 1n, /before/],
      [2n, 1n, 1n, /before/], [1n, 4n, 2n, /align/], [0n, 3n, 2n, /align/],
      [0n, 9007199254740992n, 1n, /safe integer/],
    ] as const) assert.throws(() => createVisualizationData(begin, end, period, descriptors), message)
  })

  it('requires 1 to 100 unique series', () => {
    assert.throws(() => createVisualizationData(0n, 1n, 1n, []), /1 and 100/)
    assert.throws(() => createVisualizationData(0n, 1n, 1n, [descriptors[0], descriptors[0]]), /unique/)
    const many = Array.from({ length: 101 }, (_, index) => ({ id: String(index), name: '', unit: '' }))
    assert.throws(() => createVisualizationData(0n, 1n, 1n, many), /100/)
    assert.equal(createVisualizationData(0n, 1n, 1n, many.slice(0, 100)).series.length, 100)
  })

  it('checks the aggregate 2048 MiB Float32 budget before allocating, including its exact boundary', () => {
    const samples = 2048n * 1024n * 1024n / 4n
    for (const count of [1, 2]) {
      const selected = descriptors.slice(0, count)
      const length = samples / BigInt(count)
      const data = createVisualizationData(0n, length, 1n, selected)
      assert.ok(data.series.every((series) => series.chunks.length === 0 && series.length === Number(length)))
      assert.throws(() => createVisualizationData(0n, length + 1n, 1n, selected), /2048 MiB/)
    }
  })
})

describe('Arrow visualization streaming', () => {
  it('decodes every row across interleaved resources and batches with decoded-byte progress', async () => {
    const data = model(3, 2)
    const progress: number[] = []
    await load(ipc(
      batch([[1, 0n, floats(4)], [0, 0n, floats(1, 2)], [1, 1n, floats()]]),
      batch([[1, 1n, floats(5, 6)], [0, 2n, floats(3)]]),
    ), data, (fraction) => {
      progress.push(fraction)
      assert.ok(data.series.every((series) => !series.complete))
    })
    assert.deepEqual(progress, [0, 1 / 6, 3 / 6, 5 / 6, 1, 1])
    assert.deepEqual(data.series[0].chunks, [floats(1, 2, 3)])
    assert.deepEqual(data.series[1].chunks, [floats(4, 5, 6)])
    for (const series of data.series) {
      assert.equal(series.availableLength, 3)
      assert.equal(series.version, 1)
      assert.equal(series.complete, true)
    }
  })

  it('preserves NaN, infinities, negative zero and Float32 precision without treating them as null', async () => {
    const values = floats(NaN, Infinity, -Infinity, -0, 1 / 3)
    const data = await load(ipc(batch([[0, 0n, values]])), model(values.length))
    assert.deepEqual(data.series[0].chunks[0], values)
  })

  it('handles sliced multi-row lists and nonzero sliced child offsets', async () => {
    const original = batch([[0, 0n, floats(99, 98)], [0, 0n, floats(1, 2)], [0, 2n, floats(3)]])
    const sliced = original.slice(1, 3)
    assert.notEqual(sliced.getChildAt(2)!.data[0].offset, 0)
    assert.deepEqual((await load(ipc(sliced), model(3))).series[0].chunks, [floats(1, 2, 3)])

    const child = makeData({ type: new Float32(), data: floats(99, 1, 2, 3) }).slice(1, 3)
    const list = makeData({ type: listType, length: 2, valueOffsets: new Int32Array([0, 2, 3]), child })
    const custom = new RecordBatch(schema, makeData({ type: new Struct(schema.fields), length: 2, children: [
      makeData({ type: new Int32(), data: new Int32Array([0, 0]) }),
      makeData({ type: new Int64(), data: new BigInt64Array([0n, 2n]) }), list,
    ] }))
    assert.deepEqual((await load(ipc(custom), model(3))).series[0].chunks, [floats(1, 2, 3)])
  })

  it('publishes only filled aligned chunks, crosses row/chunk boundaries and allocates resources lazily', async () => {
    const length = chunkLength * 2 + 3
    const data = model(length, 2)
    const payload = new Float32Array(chunkLength * 2).fill(7)
    payload[chunkLength - 2] = 8
    const content = ipc(batch([[0, 0n, floats(1)]]), batch([[0, 1n, payload]]),
      batch([[0, BigInt(length - 2), floats(9, 10)]]), batch([[1, 0n, new Float32Array(length).fill(11)]]))
    let firstChunk: Float32Array | undefined
    let calls = 0
    await loadVisualizationData(fragmented(content, 256 * 1024), data, (fraction) => {
      calls++
      const series = data.series[0]
      if (fraction === 1 / (length * 2)) {
        assert.deepEqual(series.chunks, [])
        assert.equal(series.availableLength, 0)
        assert.equal(series.version, 0)
        assert.deepEqual(data.series[1].chunks, [])
      }
      if (series.chunks.length) {
        firstChunk ??= series.chunks[0]
        assert.equal(series.chunks[0], firstChunk)
        assert.equal(firstChunk.length, chunkLength)
        assert.equal(firstChunk[0], 1)
        assert.equal(firstChunk[chunkLength - 1], 8)
      }
      for (const current of data.series) {
        assert.equal(current.availableLength, current.chunks.reduce((sum, chunk) => sum + chunk.length, 0))
        assert.equal(current.version, current.chunks.length)
        assert.ok(current.chunks.every((chunk, index) => chunk.length === chunkLength
          || (index === 2 && chunk.length === 3)))
        assert.equal(current.complete, false)
      }
    }, new AbortController().signal)
    assert.ok(calls > 6)
    assert.deepEqual(data.series[0].chunks[2], floats(7, 9, 10))
    assert.deepEqual(data.series[1].chunks[2], floats(11, 11, 11))
    assert.ok(data.series.every((series) => series.complete && series.availableLength === length && series.version === 3))
  })

  it('never uses Response.arrayBuffer', async () => {
    const response = fragmented(ipc(batch([[0, 0n, floats(1, 2)]])), 1)
    response.arrayBuffer = () => { throw new Error('Must stream') }
    await loadVisualizationData(response, model(), () => {}, new AbortController().signal)
  })

  it('rejects invalid indexes, exact bigint offsets, gaps, repeats and excess samples', async () => {
    for (const [rows, message] of [
      [[[1, 0n, floats(1)]], /resource index/], [[[-1, 0n, floats(1)]], /resource index/],
      [[[0, -1n, floats(1)]], /offset/], [[[0, 1n, floats(1)]], /offset/],
      [[[0, 9007199254740993n, floats(1)]], /offset/], [[[0, 9223372036854775807n, floats(1)]], /offset/],
      [[[0, 0n, floats(1)], [0, 0n, floats(2)]], /offset/],
      [[[0, 0n, floats(1)], [0, 2n, floats(2)]], /offset/],
      [[[0, 0n, floats(1, 2, 3)]], /more data/],
      [[[0, 0n, floats(1, 2)], [0, 2n, floats(3)]], /more data/],
    ] as [Row[], RegExp][]) {
      const data = model()
      await assert.rejects(load(ipc(batch(rows)), data), message)
      assert.equal(data.series[0].complete, false)
    }
  })

  it('rejects missing resources, empty streams and incomplete final chunks', async () => {
    for (const bytes of [ipc(), ipc(batch([[0, 0n, floats(1)]])), new Uint8Array(), new Uint8Array([1, 2, 3])]) {
      const data = model()
      await assert.rejects(load(bytes, data))
      assert.equal(data.series[0].complete, false)
      assert.deepEqual(data.series[0].chunks, [])
    }
    await assert.rejects(load(ipc(batch([[0, 0n, floats(1, 2)]])), model(2, 2)), /before all data/)
  })

  it('rejects truncated schema, batch bodies and end markers even when all samples arrived', async () => {
    const bytes = ipc(batch([[0, 0n, floats(1, 2)]]))
    for (const end of [1, 8, 24, bytes.length - 20, bytes.length - 8, bytes.length - 4, bytes.length - 1]) {
      const data = model()
      await assert.rejects(load(bytes.subarray(0, end), data))
      assert.equal(data.series[0].complete, false)
    }
  })

  it('validates schema names, order, field count, integer signedness/width and list precision', async () => {
    const fields = schema.fields
    const invalid = [
      fields.slice(0, 2), [...fields, new Field('extra', new Int32())],
      [fields[1], fields[0], fields[2]],
      [new Field('resource', new Int32()), ...fields.slice(1)],
      ...[new Int16(), new Uint32(), new Int64(), new Float32()].map((type) => [new Field('resourceIndex', type), ...fields.slice(1)]),
      ...[new Int32(), new Uint64(), new Float64()].map((type) => [fields[0], new Field('offset', type), fields[2]]),
      ...[new Float32(), new Utf8(), new List(new Field('item', new Float64()))].map((type) => [...fields.slice(0, 2), new Field('values', type)]),
    ]
    for (const entries of invalid) {
      const bytes = tableToIPC(new Table(new Schema(entries)), 'stream')
      await assert.rejects(load(bytes), /schema/)
    }
  })

  it('rejects IPC files and trailing bytes, including a second buffered stream with a valid end marker', async () => {
    const valid = ipc(batch([[0, 0n, floats(1, 2)]]))
    const joined = new Uint8Array(valid.length * 2)
    joined.set(valid)
    joined.set(valid, valid.length)
    const file = tableToIPC(new Table(schema, [batch([[0, 0n, floats(1, 2)]])]), 'file')
    for (const bytes of [joined, file]) {
      for (const size of [17, bytes.length]) {
        const data = model()
        await assert.rejects(loadVisualizationData(fragmented(bytes, size), data, () => {}, new AbortController().signal), /stream|Unexpected/)
        assert.equal(data.series[0].complete, false)
      }
    }
  })

  it('rejects malformed list offsets instead of silently clipping child buffers', async () => {
    const bytes = ipc(batch([[0, 0n, floats(1, 2)]]))
    // Locate the real IPC list-offset buffer through Arrow, then mutate its wire bytes.
    const reader = RecordBatchReader.from(bytes)
    reader.open()
    const decoded = reader.next().value as RecordBatch
    const offsets = decoded.getChildAt(2)!.data[0].valueOffsets as Int32Array
    const location = offsets.byteOffset - bytes.byteOffset
    assert.equal(offsets.buffer, bytes.buffer)
    reader.cancel()
    for (const [begin, end] of [[-1, 2], [2, 1], [0, 3]]) {
      const invalid = bytes.slice()
      const view = new DataView(invalid.buffer)
      view.setInt32(location, begin, true)
      view.setInt32(location + 4, end, true)
      await assert.rejects(load(invalid), /list offsets/)
    }
  })

  it('rejects null indexes, offsets, lists and child values, including a sliced child bitmap', async () => {
    for (const column of [0, 1, 2, 3, 4]) {
      const original = batch([[0, 0n, floats(1, 2)]])
      const children: Data[] = [...original.data.children]
      if (column < 3) children[column] = children[column].clone(undefined, 0, 1, 1,
        [children[column].valueOffsets, children[column].values, new Uint8Array([0]), children[column].typeIds])
      else {
        const child = column === 3
          ? makeData({ type: new Float32(), data: floats(1, 2), nullBitmap: new Uint8Array([1]), nullCount: 1 })
          : makeData({ type: new Float32(), data: floats(99, 1, 2), nullBitmap: new Uint8Array([3]), nullCount: 1 }).slice(1, 2)
        children[2] = makeData({ type: listType, length: 1, valueOffsets: new Int32Array([0, 2]), child })
      }
      const invalid = new RecordBatch(schema, makeData({ type: new Struct(schema.fields), length: 1, children }))
      const data = model()
      await assert.rejects(load(ipc(invalid), data), /null/)
      assert.deepEqual(data.series[0].chunks, [])
    }
  })

  it('rejects failed HTTP responses, missing bodies and reused or altered models', async () => {
    for (const response of [new Response('failed', { status: 500 }), new Response(null)]) {
      await assert.rejects(loadVisualizationData(response, model(), () => {}, new AbortController().signal), /HTTP 500|no body/)
    }
    const bytes = ipc(batch([[0, 0n, floats(1, 2)]]))
    const data = await load(bytes)
    await assert.rejects(load(bytes, data), /fresh/)
    const altered = model()
    altered.series[0].length++
    await assert.rejects(load(bytes, altered), /fresh/)
  })

  it('propagates transport and progress errors and cancels the response', async () => {
    const error = new Error('transport failed')
    const response = new Response(new ReadableStream({ start(controller) { controller.error(error) } }))
    await assert.rejects(loadVisualizationData(response, model(), () => {}, new AbortController().signal), error)
    const data = model()
    await assert.rejects(load(ipc(batch([[0, 0n, floats(1, 2)]])), data, () => { throw error }), error)
    assert.equal(data.series[0].complete, false)
  })
})

describe('Arrow cancellation and disposal', () => {
  for (const stage of ['before start', 'initial bytes', 'schema', 'batch body', 'between batches', 'HTTP EOF'] as const) {
    it(`aborts promptly during ${stage}, releases the body lock and disposes Arrow`, { timeout: 2000 }, async () => {
      const abort = new AbortController()
      const data = model()
      const bytes = ipc(batch([[0, 0n, floats(1)]]))
      const schemaLength = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getInt32(4, true) + 8
      const prefixLength = stage === 'schema' ? 8 : stage === 'batch body' ? bytes.length - 20
        : stage === 'between batches' ? bytes.length - 8 : stage === 'HTTP EOF' ? bytes.length : 0
      let cancelled = 0
      let requested!: () => void
      const blocked = new Promise<void>((resolve) => { requested = resolve })
      const response = new Response(new ReadableStream<Uint8Array>({
        start(controller) { if (prefixLength) controller.enqueue(bytes.subarray(0, prefixLength)) },
        pull() { requested(); return new Promise<void>(() => {}) },
        cancel() { cancelled++; return new Promise<void>(() => {}) },
      }))
      const readers: RecordBatchReader[] = []
      const original = RecordBatchReader.from
      const spy = mock.method(RecordBatchReader, 'from', async (...args: Parameters<typeof original>) => {
        const reader = await original.apply(RecordBatchReader, args)
        readers.push(reader)
        return reader
      })
      try {
        if (stage === 'before start') abort.abort()
        const promise = loadVisualizationData(response, data, () => {}, abort.signal)
        const rejected = assert.rejects(promise, { name: 'AbortError' })
        if (stage !== 'before start') {
          await blocked
          // Let buffered metadata/batches reach their pending read rather than only aborting from().
          await new Promise((resolve) => setTimeout(resolve, 10))
          abort.abort()
        }
        await rejected
        assert.equal(cancelled, 1)
        assert.equal(response.body!.locked, false)
        assert.ok(readers.every((reader) => reader.closed))
        if (prefixLength >= schemaLength) assert.equal(readers.length, 1)
        assert.equal(data.series[0].complete, false)
      } finally { spy.mock.restore() }
    })
  }

  it('aborts from decoded progress without publishing a partial chunk or processing subsequent rows', async () => {
    const abort = new AbortController()
    const data = model()
    const response = fragmented(ipc(batch([[0, 0n, floats(1)], [0, 1n, floats(2)]])))
    await assert.rejects(loadVisualizationData(response, data, (fraction) => {
      if (fraction > 0) abort.abort()
    }, abort.signal), { name: 'AbortError' })
    assert.deepEqual(data.series[0].chunks, [])
    assert.equal(data.series[0].availableLength, 0)
    assert.equal(data.series[0].version, 0)
    assert.equal(response.body!.locked, false)
  })
})

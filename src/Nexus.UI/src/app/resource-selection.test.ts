import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import type { ResourceRow } from './nexus.service'
import {
  alignRangeEndpoint, defaultKind, executionRangeError, formatFilePeriod, formatPeriod, hydrateSelections, kindValid, parseFilePeriod, parsePeriod, parseResourcePath, readSelectionState,
  representationKinds, representationRows, requestPath, resourceAvailableForRange, selectionKey,
  storeSelectionReference, toTimeSpan,
} from './resource-selection.ts'
import type { ResourceSelection, StoredSelectionReference, StoredSelectionState } from './resource-selection.ts'

const second = 10000000n
const maxTicks = 9223372036854775807n
const resource: ResourceRow = {
  catalogId: '/catalog', id: 'resource', path: '/catalog/resource',
  description: '', unit: '', groups: [],
  representations: [{ samplePeriod: '00:00:01' }],
  properties: null,
}
const row = representationRows([resource])[0]
const selection: ResourceSelection = { ...row, parameters: { z: 'last', a: 'first' }, kinds: ['Original'] }

describe('periods', () => {
  it('parses TimeSpan, Blazor units and ISO durations exactly', () => {
    for (const [text, expected] of [
      ['00:00:00', 0n], ['PT0S', 0n], ['0 ns', 0n], ['100 ns', 1n], ['1_US', 10n],
      [' 40 ms ', 400000n], ['1s', second], ['2 min', 120n * second],
      ['1 h', 3600n * second], ['1 d', 86400n * second], ['PT1H', 3600n * second],
      ['1.02:03:04.0000001', 93784n * second + 1n], ['00:00:00.1', 1000000n],
      ['P1DT2H3M4.0000001S', 93784n * second + 1n], ['PT0.0000001S', 1n],
    ] as const) assert.equal(parsePeriod(text), expected, text)
  })

  it('rejects malformed, negative and sub-tick periods rather than rounding', () => {
    for (const text of ['', '1', '-1 s', '1.5 s', '1 ns', '101 ns', '1 month',
      '24:00:00', '00:60:00', '00:00:60', '00:00:00.00000001', 'P', 'PT',
      'P1DT', 'P1M', 'PT0.00000001S', 'PT-1H', 'Infinity s', '1e3 ns']) {
      assert.equal(parsePeriod(text), null, text)
    }
  })

  it('enforces Int64 limits without Number precision loss', () => {
    for (const text of ['10675199.02:48:05.4775807', '922337203685477580700 ns', 'PT922337203685.4775807S']) {
      assert.equal(parsePeriod(text), maxTicks)
    }
    for (const text of ['10675199.02:48:05.4775808', '922337203685477580800 ns', 'PT922337203685.4775808S']) {
      assert.equal(parsePeriod(text), null)
    }
  })

  it('formats whole units using the largest exact divisor', () => {
    for (const [ticks, expected] of [
      [0n, '0 s'], [1n, '100 ns'], [10n, '1 us'], [10000n, '1 ms'],
      [second, '1 s'], [90n * second, '90 s'], [60n * second, '1 min'],
      [3600n * second, '1 h'], [86400n * second, '1 d'], [second + 1n, '1000000100 ns'],
      [maxTicks, '922337203685477580700 ns'],
    ] as const) {
      assert.equal(formatPeriod(ticks), expected)
      assert.equal(parsePeriod(formatPeriod(ticks)), ticks)
    }
    assert.equal(formatPeriod(60n * second, '_'), '1_min')
  })

  it('labels zero file period as a single file without changing normal period formatting', () => {
    assert.equal(parseFilePeriod('Single file'), 0n)
    assert.equal(parseFilePeriod(' single FILE '), 0n)
    assert.equal(parseFilePeriod('1 h'), 3600n * second)
    assert.equal(formatFilePeriod(0n), 'Single file')
    assert.equal(formatFilePeriod(60n * second), '1 min')
  })

  it('serializes export TimeSpans with seven-digit tick precision', () => {
    assert.equal(toTimeSpan(0n), '00:00:00')
    assert.equal(toTimeSpan(3600n * second), '01:00:00')
    assert.equal(toTimeSpan(second + 1n), '00:00:01.0000001')
    assert.equal(toTimeSpan(maxTicks), '10675199.02:48:05.4775807')
    for (const ticks of [0n, 1n, 10n, second, 86401n * second + 123n, maxTicks]) {
      assert.equal(parsePeriod(toTimeSpan(ticks)), ticks)
    }
    for (const ticks of [-1n, maxTicks + 1n]) {
      assert.throws(() => toTimeSpan(ticks), RangeError)
      assert.throws(() => formatPeriod(ticks), RangeError)
    }
  })
})

describe('representations and request paths', () => {
  it('flattens every positive valid representation without changing source resources', () => {
    const multi = { ...resource, representations: [
      { samplePeriod: '1 s' }, { samplePeriod: '40 ms' }, { samplePeriod: '0 s' },
      { samplePeriod: 'bad' }, {}, { samplePeriod: '101 ns' }, { samplePeriod: '-1 s' },
    ] }
    const rows = representationRows([multi, { ...resource, representations: [] }, resource])
    assert.deepEqual(rows.map((entry) => entry.basePeriod), [second, 400000n, second])
    assert.equal(rows[0].representation, multi.representations[0])
    assert.equal(rows[1].representation, multi.representations[1])
    assert.equal(rows[0].key, selectionKey(rows[0], {}))
    assert.notEqual(rows[0].key, rows[1].key)
    assert.equal(multi.representations.length, 7)
    assert.equal(representationRows([]).length, 0)
  })

  it('uses canonical, delimiter-safe identities that include arguments and base period', () => {
    assert.equal(selectionKey(row, { a: '1', b: '2' }), selectionKey(row, { b: '2', a: '1' }))
    assert.notEqual(selectionKey(row, {}), selectionKey(row, { a: '' }))
    assert.notEqual(selectionKey(row, { a: '1,b=2' }), selectionKey(row, { a: '1', b: '2' }))
    assert.notEqual(selectionKey(row, {}), selectionKey({ ...row, basePeriod: 1n }, {}))
    assert.notEqual(selectionKey(row, {}), selectionKey({ ...row, path: '/catalog/other' }, {}))
    assert.notEqual(selectionKey(row, {}), selectionKey({ ...row, catalogId: '/other' }, {}))
  })

  it('chooses defaults and validates exact divisibility in both directions', () => {
    assert.equal(defaultKind(second, second), 'Original')
    assert.equal(defaultKind(second / 2n, second), 'Resampled')
    assert.equal(defaultKind(second * 2n, second), 'Mean')
    for (const kind of representationKinds) {
      assert.equal(kindValid(kind, second, second), kind === 'Original')
      assert.equal(kindValid(kind, second / 2n, second), kind === 'Resampled')
      assert.equal(kindValid(kind, second * 2n, second), kind !== 'Original' && kind !== 'Resampled')
      assert.equal(kindValid(kind, 3n, 10n), false)
      assert.equal(kindValid(kind, 10n, 3n), false)
      for (const [period, base] of [[0n, second], [second, 0n], [-1n, second], [maxTicks + 1n, second]]) {
        assert.equal(kindValid(kind, period, base), false)
      }
    }
    assert.equal(kindValid('Mean', maxTicks - 1n, (maxTicks - 1n) / 2n), true)
    assert.equal(kindValid('Mean', maxTicks, (maxTicks - 1n) / 2n), false)
  })

  it('builds every canonical method suffix and sorted parameter list', () => {
    const suffixes = ['', '_resampled', '_mean', '_mean_polar_deg', '_min', '_max',
      '_std', '_rms', '_min_bitwise', '_max_bitwise', '_sum']
    representationKinds.forEach((kind, index) => {
      assert.equal(requestPath(selection, kind, second),
        `/catalog/resource/1_s${suffixes[index]}(a=first,z=last)#base=1_s`)
    })
    assert.equal(requestPath({ ...selection, parameters: {} }, 'Original', second), '/catalog/resource/1_s#base=1_s')
    assert.equal(requestPath(selection, 'Resampled', 400000n), '/catalog/resource/40_ms_resampled(a=first,z=last)#base=1_s')
    assert.equal(requestPath({ ...selection, basePeriod: 1n }, 'Mean', 60n * second),
      '/catalog/resource/1_min_mean(a=first,z=last)#base=100_ns')
  })

  it('parses canonical request paths back to resource selections', () => {
    assert.deepEqual(parseResourcePath('/catalog/resource/1_s_mean_polar_deg(a=first,z=last)#base=40_ms'), {
      path: '/catalog/resource', period: second, basePeriod: 400000n, kind: 'MeanPolarDeg', parameters: { a: 'first', z: 'last' },
    })
    assert.deepEqual(parseResourcePath('/catalog/resource/40_ms_resampled#base=1_s'), {
      path: '/catalog/resource', period: 400000n, basePeriod: second, kind: 'Resampled', parameters: {},
    })
    assert.equal(parseResourcePath('/catalog/resource/1_s#base=bad'), null)
    assert.equal(parseResourcePath('/catalog/resource/1_s_unknown#base=1_s'), null)
  })
})

describe('stored selections', () => {
  const reference = storeSelectionReference(selection)
  const state: StoredSelectionState = { version: 1, period: '1 s', automaticPeriod: false, selections: [reference] }
  const fallback: StoredSelectionState = { version: 1, period: '1 s', automaticPeriod: true, selections: [] }

  it('validates unknown envelopes and falls back to an automatic second', () => {
    for (const value of [undefined, null, true, 1, 'bad', {}, { version: 2 }, { version: '1' }]) {
      assert.deepEqual(readSelectionState(value), fallback)
    }
    assert.deepEqual(readSelectionState({ version: 1, period: '0 s', automaticPeriod: false, selections: {} }), fallback)
    assert.deepEqual(readSelectionState({ version: 1, period: 'bad', automaticPeriod: false }), fallback)
    assert.deepEqual(readSelectionState({ version: 1, period: 'PT1H', automaticPeriod: 'false' }),
      { ...fallback, period: '1 h' })
  })

  it('migrates and deduplicates legacy paths without guessing periods or kinds', () => {
    const legacy = { catalogId: '/catalog', path: '/catalog/resource' }
    assert.deepEqual(readSelectionState([legacy, legacy, null, {}, { catalogId: '/catalog', path: '/other/resource' }]), {
      ...fallback, selections: [{ ...legacy, basePeriod: null, parameters: {}, kinds: [] }],
    })
    assert.equal(readSelectionState([{ catalogId: '/', path: '/resource' }]).selections.length, 1)
  })

  it('rejects malformed version-one references and inconsistent legacy sentinel values', () => {
    const malformed = [null, {}, { ...reference, catalogId: 1 }, { ...reference, path: '' },
      { ...reference, path: '/other/resource' }, { ...reference, basePeriod: null },
      { ...reference, basePeriod: null, kinds: ['Unknown'] }, { ...reference, basePeriod: undefined, kinds: [] },
      { ...reference, basePeriod: '0 s' }, { ...reference, basePeriod: 'bad' },
      { ...reference, basePeriod: '922337203685477580800 ns' },
      { ...reference, kinds: 'Original' },
      { ...reference, parameters: null }, { ...reference, parameters: [] },
      { ...reference, parameters: { x: 1 } },
    ]
    assert.deepEqual(readSelectionState({ ...state, selections: malformed }).selections, [])
    assert.deepEqual(readSelectionState({ ...state, selections: [{ ...reference, kinds: [] }] }).selections, [{ ...reference, kinds: [] }])
    assert.deepEqual(readSelectionState({ ...state, selections: [{ ...reference, kinds: ['Unknown'] }] }).selections, [{ ...reference, kinds: [] }])
  })

  it('deduplicates references and kinds but preserves recognized incompatible choices', () => {
    const restored = readSelectionState({ ...state, selections: [
      { ...reference, kinds: ['Mean', 'Mean', 'Unknown', null] },
      { ...reference, basePeriod: '00:00:01', parameters: { a: 'first', z: 'last' }, kinds: ['Original', 'Resampled'] },
      { ...reference, basePeriod: '40 ms' },
      { ...reference, parameters: { a: 'different' } },
    ] })
    assert.equal(restored.selections.length, 3)
    assert.deepEqual(restored.selections[0].kinds, ['Mean', 'Original', 'Resampled'])
    assert.equal(restored.selections[0].basePeriod, '1 s')
  })

  it('roundtrips JSON without bigint or representation metadata and copies mutable values', () => {
    assert.deepEqual(readSelectionState(JSON.parse(JSON.stringify(state))), state)
    assert.deepEqual(Object.keys(reference).sort(), ['basePeriod', 'catalogId', 'kinds', 'parameters', 'path'])
    assert.notEqual(reference.parameters, selection.parameters)
    assert.notEqual(reference.kinds, selection.kinds)
    const restored = readSelectionState(state)
    restored.selections[0].kinds.push('Mean')
    restored.selections[0].parameters['a'] = 'changed'
    assert.deepEqual(reference.kinds, ['Original'])
    assert.equal(reference.parameters['a'], 'first')
    assert.deepEqual(storeSelectionReference({ ...selection, kinds: ['Mean', 'Mean'] }).kinds, ['Mean'])
  })
})

describe('selection hydration', () => {
  const legacy: StoredSelectionReference = {
    catalogId: resource.catalogId, path: resource.path, basePeriod: null, parameters: {}, kinds: [],
  }
  const rows = representationRows([{ ...resource, representations: [{ samplePeriod: '40 ms' }, { samplePeriod: '1 s' }] }])
  const catalogs = new Map([[resource.catalogId, rows]])
  const versioned = storeSelectionReference({ ...selection, parameters: {}, kinds: ['Mean', 'Resampled'] })

  it('preserves unresolved legacy references through repeated offline version-one roundtrips', () => {
    let state = readSelectionState([{ catalogId: resource.catalogId, path: resource.path }])
    state.selections[0].parameters = { z: 'last', a: 'first' }
    for (let reload = 0; reload < 3; reload++) {
      const result = hydrateSelections(state.selections, new Map(), second, true)
      assert.equal(result.selections.size, 0)
      assert.equal(result.period, second)
      assert.deepEqual(result.references, result.unresolved)
      state = readSelectionState(JSON.parse(JSON.stringify({ ...state, selections: result.references })))
      assert.deepEqual(state.selections, [{ ...legacy, parameters: { a: 'first', z: 'last' } }])
      assert.deepEqual(Object.keys(state.selections[0].parameters), ['a', 'z'])
    }
    const online = hydrateSelections(state.selections, catalogs, second, true)
    assert.equal(online.period, 400000n)
    assert.deepEqual(online.references[0].kinds, ['Original'])
    assert.equal(online.references[0].basePeriod, '40 ms')
  })

  it('drops loaded missing resources or base periods but retains unavailable catalogs', () => {
    const offline = { ...versioned, catalogId: '/offline', path: '/offline/resource' }
    const result = hydrateSelections([
      { ...legacy, path: '/catalog/deleted' }, { ...versioned, basePeriod: '2 s' }, offline, versioned,
    ], catalogs, second, true)
    assert.deepEqual(result.references, [offline, versioned])
    assert.deepEqual(result.unresolved, [offline])
    assert.equal(result.selections.size, 1)
    assert.deepEqual(hydrateSelections([legacy], new Map([[resource.catalogId, []]]), second, true).references, [])
  })

  it('uses original reference order, not catalog order, to initialize legacy-only input once', () => {
    const otherResource = { ...resource, catalogId: '/other', path: '/other/resource', representations: [{ samplePeriod: '2 s' }] }
    const otherRows = representationRows([otherResource])
    const other = { ...legacy, catalogId: '/other', path: '/other/resource' }
    const offline = { ...legacy, catalogId: '/offline', path: '/offline/resource' }
    const allCatalogs = new Map([...catalogs, ['/other', otherRows] as const])
    const result = hydrateSelections([offline, other, legacy], allCatalogs, second, true)
    assert.equal(result.period, 2n * second)
    assert.deepEqual(result.references.map((reference) => reference.catalogId), ['/offline', '/other', '/catalog'])
    assert.deepEqual([...result.selections.values()].map((item) => item.kinds), [['Original'], ['Mean']])
    assert.deepEqual([...result.selections.values()].map((item) => item.path), [other.path, legacy.path])
    assert.equal(result.references[2].basePeriod, '40 ms')
  })

  it('does not reinitialize mixed legacy/versioned input even if the versioned catalog is offline or its row is missing', () => {
    for (const reference of [versioned, { ...versioned, catalogId: '/offline', path: '/offline/resource' },
      { ...versioned, path: '/catalog/deleted' }]) {
      const result = hydrateSelections([legacy, reference], catalogs, second, true)
      assert.equal(result.period, second)
      assert.deepEqual([...result.selections.values()][0].kinds, ['Mean'])
    }
    const manual = hydrateSelections([legacy], catalogs, 200000n, false)
    assert.equal(manual.period, 200000n)
    assert.deepEqual(manual.references[0].kinds, ['Resampled'])
  })

  it('merges colliding hydrated identities in first-seen order without removing incompatible kinds', () => {
    const sameBase = { ...versioned, basePeriod: '40 ms', parameters: { z: 'last', a: 'first' } }
    const input: StoredSelectionReference[] = [
      { ...legacy, parameters: { a: 'first', z: 'last' } },
      versioned,
      sameBase,
      { ...sameBase, basePeriod: '00:00:00.0400000', kinds: ['Original', 'Mean', 'Mean'] },
    ]
    const before = JSON.stringify(input)
    const result = hydrateSelections(input, catalogs, second, true)
    assert.equal(result.selections.size, 2)
    assert.deepEqual(result.references[0].kinds, ['Mean', 'Resampled', 'Original'])
    assert.deepEqual(result.references[1].kinds, ['Mean', 'Resampled'])
    for (const [key, selected] of result.selections) {
      assert.equal(key, selectionKey(selected, selected.parameters))
      assert.equal(selected.key, key)
    }
    assert.deepEqual([...result.selections.values()][0].kinds, result.references[0].kinds)
    assert.equal(JSON.stringify(input), before)
    const reloaded = hydrateSelections(result.references, catalogs, result.period, true)
    assert.deepEqual(reloaded, result)
  })

  it('merges interleaved legacy and explicit duplicates in original method order, online and offline', () => {
    const explicit: StoredSelectionReference = { ...legacy, basePeriod: '40 ms', kinds: ['Resampled'] }
    const input: StoredSelectionReference[] = [explicit, legacy, { ...explicit, kinds: ['Original'] }]
    const online = hydrateSelections(input, catalogs, second, true)
    assert.deepEqual(online.references[0].kinds, ['Resampled', 'Mean', 'Original'])
    const offline = hydrateSelections([...input, legacy], new Map(), second, true)
    assert.deepEqual(offline.references, [{ ...explicit, kinds: ['Resampled', 'Original'] }, legacy])
    assert.deepEqual(offline.unresolved, offline.references)
  })
})

describe('range endpoint alignment', () => {
  it('floors milliseconds for the default one-second period and normalizes offsets to UTC', () => {
    assert.equal(alignRangeEndpoint('2024-01-01T00:00:01.987Z', second), '2024-01-01T00:00:01.000Z')
    assert.equal(alignRangeEndpoint('2024-01-01T05:30:01.9876543+05:30', second), '2024-01-01T00:00:01.000Z')
    assert.equal(alignRangeEndpoint('2024-01-01T00:00:01.1230000Z', 10000n), '2024-01-01T00:00:01.123Z')
  })

  it('aligns seven-day periods to the .NET epoch rather than the Unix epoch', () => {
    const week = 7n * 86400n * second
    const begin = alignRangeEndpoint('2024-01-07T23:59:59.9999999Z', week)
    const end = alignRangeEndpoint('2024-01-10T12:00:00Z', week)
    assert.equal(begin, '2024-01-01T00:00:00.000Z')
    assert.equal(end, '2024-01-08T00:00:00.000Z')
    assert.equal(executionRangeError(begin, end, week, 1), '')
    assert.equal(alignRangeEndpoint('0001-01-07T23:59:59Z', week), '0001-01-01T00:00:00.000Z')
  })

  it('preserves 100 ns precision and floors 300 ns periods exactly', () => {
    assert.equal(alignRangeEndpoint('2024-01-01T00:00:00.1234567Z', 1n), '2024-01-01T00:00:00.1234567Z')
    assert.equal(alignRangeEndpoint('2024-01-01T00:00:00.1234567Z', 3n), '2024-01-01T00:00:00.1234566Z')
    assert.equal(alignRangeEndpoint('2024-01-01T00:00:00.0000002Z', 3n), '2024-01-01T00:00:00.000Z')
    assert.equal(alignRangeEndpoint('2024-01-01T00:00:01Z', 3n), '2024-01-01T00:00:00.9999999Z')
    assert.equal(alignRangeEndpoint('1969-12-31T23:59:59.9999999Z', 1n), '1969-12-31T23:59:59.9999999Z')
    const begin = alignRangeEndpoint('2024-01-01T00:00:00.0000004Z', 3n)
    const end = alignRangeEndpoint('2024-01-01T00:00:00.0000008Z', 3n)
    assert.equal(executionRangeError(begin, end, 3n, 1), '')
    assert.equal(alignRangeEndpoint(begin, 3n), begin)
  })

  it('stays inside the .NET date range and leaves invalid dates or periods unchanged', () => {
    assert.equal(alignRangeEndpoint('0001-01-01T00:00:00.0000001Z', 3n), '0001-01-01T00:00:00.000Z')
    assert.equal(alignRangeEndpoint('9999-12-31T23:59:59.9999999Z', 1n), '9999-12-31T23:59:59.9999999Z')
    assert.equal(alignRangeEndpoint('9999-12-31T23:59:59.9999999Z', maxTicks), '0001-01-01T00:00:00.000Z')
    for (const value of ['', 'invalid', '2024-02-30T00:00:00Z', '2024-01-01T00:00:00',
      '2024-01-01T00:00:00.00000001Z', '2024-01-01T00:00:00+15:00',
      '0001-01-01T00:00:00+00:01', '9999-12-31T23:59:59-00:01']) {
      assert.equal(alignRangeEndpoint(value, second), value)
    }
    const value = '2024-01-01T00:00:00.1234567Z'
    for (const period of [0n, -1n, maxTicks + 1n]) assert.equal(alignRangeEndpoint(value, period), value)
  })
})

describe('execution range validation', () => {
  const begin = '2024-01-01T00:00:00Z'
  const end = '2024-01-01T00:00:01Z'

  it('accepts UTC and equivalent offsets, including fractional seconds', () => {
    assert.equal(executionRangeError(begin, end, second, 100), '')
    assert.equal(executionRangeError('2024-01-01T01:00:00+01:00', '2023-12-31T19:00:01-05:00', second, 1), '')
    assert.equal(executionRangeError('2024-01-01T14:00:00+14:00', end, second, 1), '')
    assert.equal(executionRangeError('2024-01-01T05:30:00.1234567+05:30', '2024-01-01T00:00:00.1234568Z', 1n, 1), '')
    assert.equal(executionRangeError('2024-01-01T00:00:00.1Z', '2024-01-01T00:00:00.2Z', 1000000n, 1), '')
  })

  it('checks exact sub-millisecond alignment and ordering without Date.parse truncation', () => {
    assert.equal(executionRangeError('2024-01-01T00:00:00.0000001Z', '2024-01-01T00:00:00.0000002Z', 1n, 1), '')
    assert.match(executionRangeError('2024-01-01T00:00:00.0000001Z', end, 10000n, 1), /align/)
    assert.match(executionRangeError(begin, '2024-01-01T00:00:01.0000001Z', second, 1), /align/)
    assert.match(executionRangeError('2024-01-01T00:00:00.0000002Z', '2024-01-01T00:00:00.0000001Z', 1n, 1), /before/)
    assert.match(executionRangeError(begin, '2024-01-01T01:00:00+01:00', 1n, 1), /before/)
    assert.match(executionRangeError(end, begin, second, 1), /before/)
  })

  it('aligns relative to the .NET epoch and supports the full DateTime range', () => {
    assert.equal(executionRangeError('0001-01-01T00:00:00Z', '0001-01-01T00:00:07Z', 7n * second, 1), '')
    assert.equal(executionRangeError('0001-01-01T00:00:00.0000001Z', '9999-12-31T23:59:59.9999999Z', 1n, 1), '')
    assert.equal(executionRangeError('1969-12-31T23:59:59.9999998Z', '1969-12-31T23:59:59.9999999Z', 1n, 1), '')
    assert.match(executionRangeError('0001-01-01T00:00:00+00:01', end, 1n, 1), /valid UTC range/)
    assert.match(executionRangeError(begin, '9999-12-31T23:59:59-00:01', 1n, 1), /valid UTC range/)
  })

  it('rejects malformed dates, impossible calendar values, ambiguous local times and invalid offsets', () => {
    for (const date of ['', 'bad', '2024-01-01', '2024-01-01T00:00:00', '2024-01-01T00:00Z',
      '0000-01-01T00:00:00Z', '10000-01-01T00:00:00Z', '2023-02-29T00:00:00Z',
      '2024-02-30T00:00:00Z', '2024-04-31T00:00:00Z', '2024-13-01T00:00:00Z',
      '2024-01-00T00:00:00Z', '2024-01-01T24:00:00Z', '2024-01-01T00:60:00Z',
      '2024-01-01T00:00:60Z', '2024-01-01T00:00:00.00000001Z',
      '2024-01-01T00:00:00+14:01', '2024-01-01T00:00:00-15:00',
      '2024-01-01T00:00:00+01:60', '2024-01-01T00:00:00+0100']) {
      assert.match(executionRangeError(date, end, 1n, 1), /valid UTC range/, date)
      assert.match(executionRangeError(begin, date, 1n, 1), /valid UTC range/, date)
    }
    assert.equal(executionRangeError('2024-02-29T00:00:00Z', '2024-03-01T00:00:00Z', second, 1), '')
  })

  it('requires a positive bounded period and between one and 100 integer output series', () => {
    for (const period of [0n, -1n, maxTicks + 1n]) assert.match(executionRangeError(begin, end, period, 1), /Period/)
    for (const count of [0, -1, 0.5, NaN, Infinity]) assert.notEqual(executionRangeError(begin, end, second, count), '')
    assert.match(executionRangeError(begin, end, second, 101), /100 output series/)
  })
})

describe('resource availability', () => {
  const avRow: ResourceRow = { ...resource, id: 'P1', path: '/catalog/P1' }
  const otherRow: ResourceRow = { ...resource, id: 'P2', path: '/catalog/P2' }
  const selectedBegin = '2020-06-01T00:00:00Z'
  const selectedEnd = '2020-07-01T00:00:00Z'

  it('returns available when there is no availability metadata or the metadata is malformed', () => {
    assert.equal(resourceAvailableForRange(avRow, null, selectedBegin, selectedEnd), true)
    assert.equal(resourceAvailableForRange(avRow, {}, selectedBegin, selectedEnd), true)
    assert.equal(resourceAvailableForRange(avRow, { resources: null }, selectedBegin, selectedEnd), true)
    assert.equal(resourceAvailableForRange(avRow, { resources: 'bad' }, selectedBegin, selectedEnd), true)
    assert.equal(resourceAvailableForRange(avRow, { resources: { availability: null } }, selectedBegin, selectedEnd), true)
    assert.equal(resourceAvailableForRange(avRow, { resources: { availability: 'bad' } }, selectedBegin, selectedEnd), true)
  })

  it('returns available when no rule matches the resource path', () => {
    const properties = { resources: { availability: [
      { pattern: '^/catalog/P2$', begin: '2021-01-01T00:00:00Z', end: '2022-01-01T00:00:00Z' },
    ] } }
    assert.equal(resourceAvailableForRange(avRow, properties, selectedBegin, selectedEnd), true)
    assert.equal(resourceAvailableForRange(otherRow, properties, selectedBegin, selectedEnd), false)
  })

  it('returns available when a matching rule overlaps the selected range', () => {
    const properties = { resources: { availability: [
      { pattern: '^/catalog/P1$', begin: '2020-01-01T00:00:00Z', end: '2021-01-01T00:00:00Z' },
    ] } }
    assert.equal(resourceAvailableForRange(avRow, properties, selectedBegin, selectedEnd), true)
  })

  it('returns unavailable when a matching rule does not overlap the selected range', () => {
    const properties = { resources: { availability: [
      { pattern: '^/catalog/P1$', begin: '2021-01-01T00:00:00Z', end: '2022-01-01T00:00:00Z' },
    ] } }
    assert.equal(resourceAvailableForRange(avRow, properties, selectedBegin, selectedEnd), false)
  })

  it('treats missing begin as unbounded past and missing end as unbounded future', () => {
    const beginOnly = { resources: { availability: [{ pattern: '^/catalog/P1$', begin: '2021-01-01T00:00:00Z' }] } }
    assert.equal(resourceAvailableForRange(avRow, beginOnly, selectedBegin, selectedEnd), false)
    const endOnly = { resources: { availability: [{ pattern: '^/catalog/P1$', end: '2020-01-01T00:00:00Z' }] } }
    assert.equal(resourceAvailableForRange(avRow, endOnly, selectedBegin, selectedEnd), false)
    const noBounds = { resources: { availability: [{ pattern: '^/catalog/P1$' }] } }
    assert.equal(resourceAvailableForRange(avRow, noBounds, selectedBegin, selectedEnd), true)
  })

  it('returns available when at least one matching rule overlaps even if others do not', () => {
    const properties = { resources: { availability: [
      { pattern: '^/catalog/P1$', begin: '2021-01-01T00:00:00Z', end: '2022-01-01T00:00:00Z' },
      { pattern: '^/catalog/P1$', begin: '2020-01-01T00:00:00Z', end: '2021-01-01T00:00:00Z' },
    ] } }
    assert.equal(resourceAvailableForRange(avRow, properties, selectedBegin, selectedEnd), true)
  })

  it('ignores invalid rules without affecting valid ones', () => {
    const properties = { resources: { availability: [
      { pattern: '^/catalog/P1$', begin: 'bad' },
      { pattern: '[invalid', begin: '2020-01-01T00:00:00Z', end: '2021-01-01T00:00:00Z' },
      { pattern: '^/catalog/P1$', begin: 42 },
      'not-a-rule',
      null,
      { pattern: '^/catalog/P1$', begin: '2020-01-01T00:00:00Z', end: '2021-01-01T00:00:00Z' },
    ] } }
    assert.equal(resourceAvailableForRange(avRow, properties, selectedBegin, selectedEnd), true)
  })

  it('returns available for any resource when the selected range is invalid', () => {
    const properties = { resources: { availability: [
      { pattern: '^/catalog/P1$', begin: '2021-01-01T00:00:00Z', end: '2022-01-01T00:00:00Z' },
    ] } }
    assert.equal(resourceAvailableForRange(avRow, properties, 'bad', selectedEnd), true)
    assert.equal(resourceAvailableForRange(avRow, properties, selectedEnd, selectedBegin), true)
  })

  it('uses inclusive begin and exclusive end for overlap', () => {
    const properties = { resources: { availability: [
      { pattern: '^/catalog/P1$', begin: '2020-07-01T00:00:00Z', end: '2021-01-01T00:00:00Z' },
    ] } }
    assert.equal(resourceAvailableForRange(avRow, properties, selectedBegin, selectedEnd), false)
    const properties2 = { resources: { availability: [
      { pattern: '^/catalog/P1$', begin: '2020-01-01T00:00:00Z', end: '2020-06-01T00:00:00Z' },
    ] } }
    assert.equal(resourceAvailableForRange(avRow, properties2, selectedBegin, selectedEnd), false)
  })
})

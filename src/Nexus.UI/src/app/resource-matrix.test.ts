import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import type { CatalogMetadata } from '@nexus-api/V1'
import type { ResourceRow } from './nexus.service'
import { groupResourceRows, mergeResourceMetadata } from './resource-matrix.ts'
import type { MetadataDrafts, MetadataField } from './resource-matrix.ts'
import { representationRows } from './resource-selection.ts'

function resource(id: string, options: Partial<ResourceRow> & { warning?: string } = {}): ResourceRow {
  return {
    catalogId: '/catalog', id, path: `/catalog/${id}`, groups: [], unit: '', description: '',
    representations: [{ samplePeriod: '1 s' }], properties: null, ...options,
  }
}

function freeze(value: unknown): void {
  if (value && typeof value === 'object') {
    Object.values(value).forEach(freeze)
    Object.freeze(value)
  }
}

describe('resource matrix grouping', () => {
  it('keeps more than 450 representation rows and counts unique resources after filtering', () => {
    const rows = representationRows(Array.from({ length: 501 }, (_, index) => resource(`sensor-${String(index).padStart(3, '0')}`, {
      groups: ['Sensors'], representations: [{ samplePeriod: '10 s' }, { samplePeriod: '1 s' }],
    }))).reverse()
    const before = structuredClone(rows)
    freeze(rows)
    const groups = groupResourceRows(rows, '  ')
    assert.equal(groups.length, 1)
    assert.equal(groups[0].rows.length, 1002)
    assert.equal(groups[0].resourceCount, 501)
    assert.deepEqual(groups[0].rows.slice(0, 2).map((row) => [row.id, row.basePeriod]), [
      ['sensor-000', 10000000n], ['sensor-000', 100000000n],
    ])
    assert.equal(groups[0].rows.at(-1)!.id, 'sensor-500')
    const filtered = groupResourceRows(rows, ' SENSOR-500 ')
    assert.equal(filtered[0].rows.length, 2)
    assert.equal(filtered[0].resourceCount, 1)
    assert.deepEqual(rows, before)
    assert.deepEqual(groupResourceRows([...rows].reverse(), ''), groups)
  })

  it('deduplicates labels, retains every multi-group representation and skips empty labels', () => {
    const rows = representationRows([
      resource('z', { groups: ['Beta', 'Alpha', 'Beta', '', ' \t '], representations: [{ samplePeriod: '10 s' }, { samplePeriod: '2 s' }] }),
      resource('a', { groups: ['Beta'] }),
      resource('empty', { groups: ['', ' \n '] }),
      resource('none'),
    ])
    const groups = groupResourceRows(rows, '')
    assert.deepEqual(groups.map((group) => [group.name, group.rows.length, group.resourceCount]), [
      ['Alpha', 2, 1], ['Beta', 3, 2], ['Ungrouped', 2, 2],
    ])
    assert.deepEqual(groups[1].rows.map((row) => [row.id, row.basePeriod]), [
      ['a', 10000000n], ['z', 20000000n], ['z', 100000000n],
    ])
    assert.deepEqual(groupResourceRows(rows, 'alpha').map((group) => [group.name, group.rows.length, group.resourceCount]), [
      ['Alpha', 2, 1], ['Beta', 2, 1],
    ])
    assert.equal(groups[0].rows[0], rows[1])
  })

  it('keeps literal Ungrouped and key-like labels separate from the synthetic group', () => {
    const rows = representationRows([
      resource('synthetic'), resource('literal', { groups: ['Ungrouped'] }),
      resource('key', { groups: ['ungrouped', 'group:Ungrouped', '__proto__'] }),
    ])
    const groups = groupResourceRows(rows, '')
    assert.equal(groups.length, 5)
    assert.equal(new Set(groups.map((group) => group.key)).size, 5)
    const ungrouped = groups.filter((group) => group.name === 'Ungrouped')
    assert.equal(ungrouped.length, 2)
    assert.deepEqual(ungrouped.map((group) => group.rows[0].id).sort(), ['literal', 'synthetic'])
    for (const group of groups) {
      assert.equal(group.resourceCount, 1)
      assert.equal(group.rows.length, 1)
    }
    assert.deepEqual(groupResourceRows([...rows].reverse(), ''), groups)
  })

  it('searches the whole catalog by id, path, groups, unit, description, optional warning and formatted period', () => {
    const rows = representationRows([
      resource('temperature', { path: '/elsewhere/channel', groups: ['Thermal', 'Safety'], unit: 'degC', description: 'Ambient probe', warning: 'Calibration overdue',
        representations: [{ samplePeriod: '00:01:00' }, { samplePeriod: '00:00:00.0400000' }] }),
      resource('other', { groups: ['Other'] }),
    ])
    for (const query of ['TEMPERATURE', '/ELSEWHERE/CHANNEL', 'thermal', 'safety', 'DEGC', 'ambient PROBE', ' CALIBRATION OVERDUE ']) {
      const groups = groupResourceRows(rows, query)
      assert.deepEqual(groups.map((group) => [group.name, group.rows.length, group.resourceCount]), [
        ['Safety', 2, 1], ['Thermal', 2, 1],
      ], query)
    }
    for (const [query, period] of [['1 MIN', 600000000n], ['40 ms', 400000n]] as const) {
      const groups = groupResourceRows(rows, query)
      assert.equal(groups.length, 2)
      for (const group of groups) {
        assert.equal(group.rows.length, 1)
        assert.equal(group.rows[0].basePeriod, period)
        assert.equal(group.resourceCount, 1)
      }
    }
    assert.equal(groupResourceRows(rows, '/catalog').length, 3)
    assert.deepEqual(groupResourceRows(rows, 'not found'), [])
    assert.deepEqual(groupResourceRows([], ''), [])
  })

  it('counts same-id resources in different catalogs separately and sorts ties deterministically', () => {
    const rows = representationRows([
      resource('same', { catalogId: '/z', path: '/z/same' }),
      resource('same', { catalogId: '/a', path: '/a/same', representations: [{ samplePeriod: '2 s' }, { samplePeriod: '1 s' }] }),
    ])
    const [group] = groupResourceRows(rows, '')
    assert.equal(group.rows.length, 3)
    assert.equal(group.resourceCount, 2)
    assert.deepEqual(group.rows.map((row) => [row.path, row.basePeriod]), [
      ['/a/same', 10000000n], ['/a/same', 20000000n], ['/z/same', 10000000n],
    ])
    assert.deepEqual(groupResourceRows([...rows].reverse(), ''), [group])
  })
})

describe('resource metadata drafts', () => {
  it('creates missing override objects without copying effective resources or representations', () => {
    for (const metadata of [{}, { overrides: null }, { overrides: {} }, { overrides: { resources: null } }] satisfies CatalogMetadata[]) {
      const before = structuredClone(metadata)
      freeze(metadata)
      const result = mergeResourceMetadata(metadata, '/catalog', { temperature: { unit: 'degC', warning: 'Review' } })
      assert.deepEqual(result, { overrides: { id: '/catalog', resources: [
        { id: 'temperature', properties: { unit: 'degC', warning: 'Review' } },
      ] } })
      assert.deepEqual(metadata, before)
    }
    for (const properties of [undefined, null]) {
      const metadata = { overrides: { resources: [{ id: 'temperature', properties, representations: null }] } }
      assert.deepEqual(mergeResourceMetadata(metadata, '/catalog', { temperature: { description: 'Probe' } }).overrides!.resources, [
        { id: 'temperature', properties: { description: 'Probe' }, representations: null },
      ])
    }
  })

  it('preserves unrelated envelope, catalog, resource and representation data immutably', () => {
    const metadata = {
      contact: 'owner@example.org', groupMemberships: ['Readers'], extraEnvelope: { revision: 7 },
      overrides: {
        id: '/wrong', properties: { title: 'Custom title', nested: { values: [1, 2] } }, extraCatalog: ['keep'],
        resources: [
          { id: 'temperature', properties: { unit: 'K', description: 'Original override', warning: 'Old warning', groups: ['Thermal'], custom: { keep: true } },
            representations: [{ samplePeriod: '1 s', parameters: { height: { minimum: 1 } }, extraRepresentation: true }], extraResource: 'keep' },
          { id: 'untouched', properties: { unit: 'm/s' }, representations: [{ samplePeriod: '40 ms' }] },
          { properties: { custom: 'no id' } },
        ],
      },
    }
    const drafts: MetadataDrafts = {
      temperature: { unit: ' degC ', description: 'Updated', warning: '\t\n' },
      added: { description: 'New override' },
    }
    const before = structuredClone(metadata)
    const draftsBefore = structuredClone(drafts)
    freeze(metadata)
    freeze(drafts)
    const result = mergeResourceMetadata(metadata, '/catalog', drafts)
    const expected = structuredClone(before)
    expected.overrides.id = '/catalog'
    const properties: Record<string, unknown> = expected.overrides.resources[0].properties
    properties['unit'] = ' degC '
    properties['description'] = 'Updated'
    delete properties['warning']
    assert.deepEqual(result, { ...expected, overrides: { ...expected.overrides, resources: [
      ...expected.overrides.resources, { id: 'added', properties: { description: 'New override' } },
    ] } })
    assert.notEqual(result, metadata)
    assert.notEqual(result.overrides, metadata.overrides)
    assert.notEqual(result.overrides!.resources, metadata.overrides.resources)
    assert.notEqual(result.overrides!.resources![0], metadata.overrides.resources[0])
    assert.notEqual(result.overrides!.resources![0].properties, metadata.overrides.resources[0].properties)
    assert.deepEqual(metadata, before)
    assert.deepEqual(drafts, draftsBefore)
  })

  it('clears multiple fields by deleting overrides rather than storing blank or source values', () => {
    const fields: MetadataField[] = ['unit', 'description', 'warning']
    const metadata: CatalogMetadata = { overrides: { id: '/catalog', resources: [
      { id: 'first', properties: { unit: 'K', description: 'Override', warning: 'Warning', custom: 42 } },
      { id: 'second', properties: { unit: 'm', description: 'Keep', warning: 'Warning' }, representations: [] },
    ] } }
    freeze(metadata)
    const result = mergeResourceMetadata(metadata, '/catalog', {
      first: { unit: '', description: ' \t ', warning: '\r\n' }, second: { unit: ' ', warning: '' },
    })
    assert.deepEqual(result.overrides!.resources, [
      { id: 'first', properties: { custom: 42 } },
      { id: 'second', properties: { description: 'Keep' }, representations: [] },
    ])
    for (const field of fields) assert.equal(Object.hasOwn(result.overrides!.resources![0].properties!, field), false)
    assert.deepEqual(mergeResourceMetadata(result, '/catalog', { first: { unit: '', description: ' ', warning: '' } }), result)
  })

  it('forces the catalog id even without drafts and preserves absent or null resource lists', () => {
    for (const metadata of [{}, { overrides: null }, { overrides: { id: '/wrong' } },
      { overrides: { id: '/wrong', resources: null } }, { overrides: { resources: [] } }] satisfies CatalogMetadata[]) {
      const expected = { ...metadata, overrides: { ...metadata.overrides, id: '/catalog' } }
      assert.deepEqual(mergeResourceMetadata(metadata, '/catalog', {}), expected)
      assert.deepEqual(mergeResourceMetadata(metadata, '/catalog', { missing: {}, blank: { unit: ' ', warning: '' } }), expected)
    }
  })

  it('uses resource ids as literal keys and ignores inherited drafts', () => {
    const drafts: MetadataDrafts = Object.assign(Object.create({ inherited: { unit: 'bad' } }), {
      constructor: { unit: 'm' }, ['/catalog/path']: { warning: 'Review' },
    })
    Object.defineProperty(drafts, '__proto__', { value: { description: 'Literal id' }, enumerable: true })
    const result = mergeResourceMetadata({}, '/catalog', drafts)
    assert.deepEqual(result.overrides!.resources, [
      { id: 'constructor', properties: { unit: 'm' } },
      { id: '/catalog/path', properties: { warning: 'Review' } },
      { id: '__proto__', properties: { description: 'Literal id' } },
    ])
  })
})

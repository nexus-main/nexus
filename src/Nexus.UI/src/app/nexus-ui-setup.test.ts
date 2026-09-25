import assert from 'node:assert/strict'
import { test } from 'node:test'
import { parseNexusUiSetup } from './nexus-ui-setup.ts'

test('parses legacy dev ui settings', () => {
  const parsed = parseNexusUiSetup({
    fileType: 'Nexus.Writers.Csv',
    requestConfiguration: { threshold: 7 },
    catalogHidePatterns: ['/internal/*', null],
    chartGpuCacheBudgetMiB: 8,
  })

  assert.equal(parsed.source, 'legacy-ui-settings')
  assert.equal(parsed.legacyUiSettings?.fileType, 'Nexus.Writers.Csv')
  assert.deepEqual(parsed.legacyUiSettings?.requestConfiguration, { threshold: 7 })
  assert.deepEqual(parsed.legacyUiSettings?.catalogHidePatterns, ['/internal/*', null])
  assert.equal(parsed.legacyUiSettings?.chartGpuCacheBudgetMiB, 8)
  assert.deepEqual(parsed.setup, {
    type: 'Nexus.Writers.Csv',
    configuration: { threshold: 7 },
  })
})

test('parses flat export parameters', () => {
  const exportParameters = {
    begin: '2024-01-01T00:00:00.000Z',
    end: '2024-01-02T00:00:00.000Z',
    filePeriod: 'PT0S',
    type: 'Nexus.Writers.Csv',
    resourcePaths: ['/SAMPLE/LOCAL/P1?p=7'],
    configuration: { delimiter: ',' },
    precision: 4,
  }

  const parsed = parseNexusUiSetup(exportParameters)

  assert.equal(parsed.source, 'setup')
  assert.equal(parsed.legacyUiSettings, undefined)
  assert.deepEqual(parsed.setup, exportParameters)
})

test('rejects unrelated json objects', () => {
  assert.throws(() => parseNexusUiSetup({ version: 1 }), /neither a Nexus setup export nor legacy UI settings/)
})

import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { acceptPipelineSave, addRegistration, createPipelineDraft, editRegistrationText, moveRegistration,
  pipelineIsDirty, preparePipeline, reconcilePipelineDraft, removeRegistration, resolveUnsavedChoice, sourceSchema, updateRegistration } from './data-source-pipelines.ts'
import type { DataSourcePipeline, ExtensionDescription } from '../../../clients/typescript/V1.ts'

const schema = { type: 'object', properties: { name: { type: 'string', minLength: 1 }, count: { type: 'integer', default: 5 } }, required: ['name'] }
const descriptions: ExtensionDescription[] = [{ type: 'Source', version: '1.0', additionalInformation: { 'source-configuration-schema': schema } }]
const pipeline = (): DataSourcePipeline => ({ releasePattern: null, visibilityPattern: '', registrations: [
  { type: 'Source', resourceLocator: 'file:///data', infoUrl: null, configuration: { name: 'first', extra: { enabled: true } } },
  { type: 'Source', resourceLocator: null, infoUrl: '', configuration: { name: 'second' } },
] })

describe('pipeline draft round trips', () => {
  it('preserves unknown JSON at every level and sends complete registrations in order', () => {
    const original = { ...pipeline(), futurePipelineOption: { enabled: false }, registrations: pipeline().registrations!.map((registration, index) => ({ ...registration, futureRegistrationOption: index })) }
    const snapshot = structuredClone(original)
    let draft = createPipelineDraft('id', original)
    assert.equal(pipelineIsDirty(draft), false)
    draft = editRegistrationText(draft, 0, '{"name":"edited","extra":{"enabled":true}}')
    const result = preparePipeline(draft, descriptions)
    assert.equal(result.valid, true)
    if (!result.valid) return
    assert.deepEqual(result.payload, { ...original, registrations: [{ ...original.registrations[0], configuration: { name: 'edited', extra: { enabled: true } } }, original.registrations[1]] })
    assert.deepEqual(original, snapshot)
    assert.ok(result.payload.registrations!.every(registration => Object.hasOwn(registration, 'type') && Object.hasOwn(registration, 'configuration')))
    assert.equal(Object.hasOwn(result.payload, 'baseline'), false)
    assert.equal(Object.hasOwn(result.payload.registrations![0], 'key'), false)
  })

  it('does not inject defaults or create a configuration while loading or selecting types', () => {
    let draft = createPipelineDraft('id', pipeline())
    const before = structuredClone(draft)
    preparePipeline(draft, descriptions)
    sourceSchema(descriptions, 'Source')
    assert.deepEqual(draft, before)
    assert.equal(pipelineIsDirty(draft), false)
    draft = addRegistration(draft)
    draft = updateRegistration(draft, 2, { type: 'Source' })
    assert.equal(draft.registrations[2].configuration, null)
    assert.equal(draft.registrations[2].rawText, undefined)
    assert.equal(preparePipeline(draft, descriptions).valid, false)
  })

  it('initializes new registrations with explicit null, without injecting schema defaults', () => {
    let draft = addRegistration(createPipelineDraft())
    draft = updateRegistration(draft, 0, { type: 'Source' })
    const result = preparePipeline(draft, [{ type: 'Source', additionalInformation: {
      'source-configuration-schema': { type: ['object', 'null'], default: { name: 'not injected' } },
    } }])
    assert.equal(result.valid, true)
    if (result.valid) {
      assert.equal(Object.hasOwn(result.payload.registrations![0], 'configuration'), true)
      assert.equal(result.payload.registrations![0].configuration, null)
    }
    assert.equal(draft.registrations[0].configuration, null)
  })

  it('keeps invalid buffers and stable identity through adding, removing and reordering duplicate types', () => {
    let draft = editRegistrationText(createPipelineDraft('id', pipeline()), 0, '{"name":')
    draft = editRegistrationText(draft, 1, '{ "name": "second edited" }')
    draft = addRegistration(draft)
    draft = moveRegistration(draft, 0, 1)
    draft = removeRegistration(draft, 2)
    draft = addRegistration(draft)
    assert.deepEqual(draft.registrations.map(registration => registration.key), [1, 0, 3])
    assert.equal(draft.registrations[1].rawText, '{"name":')
    assert.deepEqual(draft.registrations[1].configuration, pipeline().registrations![0].configuration)
    assert.equal(draft.registrations[0].rawText, '{ "name": "second edited" }')
    assert.equal(preparePipeline(draft, descriptions).valid, false)
    assert.equal(moveRegistration(draft, 1, -1), draft)
    assert.equal(moveRegistration(draft, 999, 1), draft)
  })

  it('treats even an empty raw buffer as authoritative and never silently uses the last valid value', () => {
    for (const rawText of ['', '{', '{"name":3}', '{"name":"ok","count":9007199254740993}']) {
      const draft = editRegistrationText(createPipelineDraft('id', pipeline()), 1, rawText)
      const result = preparePipeline(draft, descriptions)
      assert.equal(result.valid, false, rawText)
      assert.ok(result.issues.some(issue => issue.key === 1))
      assert.equal(draft.registrations[1].rawText, rawText)
      assert.equal(pipelineIsDirty(draft), true)
    }
  })

  it('validates all stages rather than just the visible editor', () => {
    let draft = createPipelineDraft('id', pipeline())
    draft = editRegistrationText(draft, 0, '{"name":false}')
    draft = editRegistrationText(draft, 1, '{"name":""}')
    const result = preparePipeline(draft, descriptions)
    assert.equal(result.valid, false)
    assert.deepEqual([...new Set(result.issues.flatMap(issue => issue.key === undefined ? [] : [issue.key]))], [0, 1])
  })

  it('blocks unknown types, missing schemas, bad schemas, and failed schema references', () => {
    const draft = createPipelineDraft('id', pipeline())
    for (const available of [[], [{ type: 'Source' }], ...[null, true, 'not JSON', { type: 'bogus' }, { $ref: '#/definitions/missing' }].map(value => [{ type: 'Source', additionalInformation: { 'source-configuration-schema': value } }])]) {
      assert.equal(preparePipeline(draft, available).valid, false)
      assert.deepEqual(draft.registrations[0].configuration, pipeline().registrations![0].configuration)
    }
  })

  it('retains raw edits and identities when descriptions fail and later become available', () => {
    let draft = editRegistrationText(createPipelineDraft('id', pipeline()), 0, '{ "name": "edited" }')
    draft = editRegistrationText(draft, 1, '{')
    const snapshot = structuredClone(draft)
    assert.equal(preparePipeline(draft, []).valid, false)
    assert.equal(preparePipeline(draft, descriptions).valid, false)
    assert.deepEqual(draft, snapshot)
    draft = editRegistrationText(draft, 1, '{"name":"repaired"}')
    assert.equal(preparePipeline(draft, []).valid, false)
    assert.equal(preparePipeline(draft, descriptions).valid, true)
    assert.equal(draft.registrations[0].rawText, '{ "name": "edited" }')
    assert.equal(pipelineIsDirty(draft), true)
  })

  it('removes only the identified stage after reordering, preserving other invalid edits', () => {
    let draft = editRegistrationText(createPipelineDraft('id', pipeline()), 0, '{')
    draft = moveRegistration(draft, 1, -1)
    const next = removeRegistration(draft, 1)
    assert.deepEqual(next.registrations.map(registration => registration.key), [0])
    assert.equal(next.registrations[0].rawText, '{')
    assert.equal(draft.registrations.length, 2)
  })

  it('preserves null, false, zero, strings and arrays when permitted by the schema', () => {
    for (const configuration of [null, false, 0, '', [], ['hello']]) {
      const draft = createPipelineDraft('id', { registrations: [{ type: 'Source', configuration }] })
      const result = preparePipeline(draft, [{ type: 'Source', additionalInformation: { 'source-configuration-schema': {} } }])
      assert.equal(result.valid, true)
      if (result.valid) assert.deepEqual(result.payload.registrations![0].configuration, configuration)
    }
  })

  it('leaves .NET regex text and nullable metadata untouched', () => {
    const draft = { ...createPipelineDraft('id', pipeline()), releasePattern: '(?i)^/CAT/(?<name>.+)$', visibilityPattern: null }
    const result = preparePipeline(draft, descriptions)
    assert.equal(result.valid, true)
    if (result.valid) {
      assert.equal(result.payload.releasePattern, draft.releasePattern)
      assert.equal(result.payload.visibilityPattern, null)
      assert.equal(result.payload.registrations![0].infoUrl, null)
      assert.equal(result.payload.registrations![1].infoUrl, '')
    }
  })

  it('round trips locators verbatim, leaving URI validation to the server', () => {
    for (const locator of [null, '', 'relative/data', '../data', '/relative', 'file:///data', 'https://example.test/data', 'custom:source', 'not a uri']) {
      const draft = updateRegistration(createPipelineDraft('id', pipeline()), 0, { resourceLocator: locator })
      const result = preparePipeline(draft, descriptions)
      assert.equal(result.valid, true)
      if (result.valid) assert.equal(result.payload.registrations![0].resourceLocator, locator)
    }
  })

  it('rejects empty pipelines, missing types, and unsafe unknown envelope properties', () => {
    assert.equal(preparePipeline(createPipelineDraft(), descriptions).valid, false)
    assert.equal(preparePipeline(updateRegistration(createPipelineDraft('id', pipeline()), 0, { type: '' }), descriptions).valid, false)
    const original = { ...pipeline(), future: 9007199254740992 }
    assert.equal(preparePipeline(createPipelineDraft('id', original), descriptions).valid, false)
  })

  it('preserves prototype-named unknown JSON properties without prototype mutation', () => {
    const original = JSON.parse('{"__proto__":{"safe":true},"registrations":[{"type":"Source","constructor":"retained","configuration":{"name":"a","__proto__":{"safe":true}}}]}')
    const result = preparePipeline(createPipelineDraft('id', original), descriptions)
    assert.equal(result.valid, true)
    if (result.valid) {
      assert.deepEqual(JSON.parse(JSON.stringify(result.payload))['__proto__'], { safe: true })
      assert.equal(Object.getPrototypeOf(result.payload), Object.prototype)
      assert.equal(result.payload.registrations![0].constructor, 'retained')
    }
  })

  it('acknowledges a successful save without changing stage keys, next identity or raw formatting', () => {
    let draft = addRegistration(createPipelineDraft('id', pipeline()))
    draft = removeRegistration(draft, 2)
    draft = moveRegistration(draft, 0, 1)
    draft = editRegistrationText(draft, 0, '{ "name" : "saved" }')
    const result = preparePipeline(draft, descriptions)
    assert.equal(result.valid, true)
    if (!result.valid) return
    const saved = acceptPipelineSave(draft, 'id', result.payload)
    assert.equal(pipelineIsDirty(saved), false)
    assert.deepEqual(saved.registrations.map(registration => registration.key), [1, 0])
    assert.equal(saved.nextKey, 3)
    assert.equal(saved.registrations[1].rawText, '{ "name" : "saved" }')
    assert.equal(pipelineIsDirty(editRegistrationText(saved, 0, '{')), true)
    assert.equal(pipelineIsDirty(createPipelineDraft()), true)
  })
})

describe('pipeline reload reconciliation', () => {
  const upgradedPipeline = () => ({ ...pipeline(), registrations: pipeline().registrations!.map(registration => ({
    ...registration, configuration: { ...(registration.configuration as Record<string, unknown>), upgrade: { version: 2 } },
  })) })

  it('adopts server upgrades in a clean draft before descriptions are available', () => {
    const original = createPipelineDraft('id', pipeline())
    const server = upgradedPipeline()
    const next = reconcilePipelineDraft(original, { id: server })!
    assert.deepEqual(next.original, server)
    assert.equal(pipelineIsDirty(next), false)
    assert.equal(next.serverDiverged, false)
    assert.equal(preparePipeline(next, []).valid, false)
    // A later descriptions-only retry and metadata edit must send the upgraded configuration.
    const result = preparePipeline({ ...next, releasePattern: '^/new' }, descriptions)
    assert.equal(result.valid, true)
    if (result.valid) assert.deepEqual(result.payload.registrations, server.registrations)
    assert.deepEqual(original.original, pipeline())
  })

  it('preserves dirty buffers and keys while blocking saves after server upgrades, including description retries', () => {
    let draft = editRegistrationText(createPipelineDraft('id', pipeline()), 0, '{')
    draft = moveRegistration(draft, 0, 1)
    const next = reconcilePipelineDraft(draft, { id: upgradedPipeline() })!
    assert.equal(next.registrations, draft.registrations)
    assert.equal(next.original, draft.original)
    assert.equal(next.serverDiverged, true)
    assert.equal(next.registrations[1].rawText, '{')
    const repaired = editRegistrationText(next, 0, '{"name":"repaired"}')
    for (const available of [[], descriptions]) {
      const result = preparePipeline(repaired, available)
      assert.equal(result.valid, false)
      assert.ok(result.issues.some(issue => issue.message.includes('server')))
    }
    assert.equal(draft.serverDiverged, false)
  })

  it('clears divergence only after adopting the latest pipeline on deliberate discard', () => {
    const server = upgradedPipeline()
    const dirty = { ...createPipelineDraft('id', pipeline()), visibilityPattern: '^/local' }
    const diverged = reconcilePipelineDraft(dirty, { id: server })!
    assert.equal(preparePipeline(diverged, descriptions).valid, false)
    const discarded = reconcilePipelineDraft(diverged, { id: server }, true)!
    assert.equal(discarded.serverDiverged, false)
    assert.equal(pipelineIsDirty(discarded), false)
    assert.deepEqual(discarded.original, server)
    assert.equal(preparePipeline(discarded, descriptions).valid, true)
  })

  it('does not confuse object property order with server changes, but detects stage and unknown-property changes', () => {
    const draft = { ...createPipelineDraft('id', pipeline()), releasePattern: '^/local' }
    const reorderedProperties = JSON.parse(JSON.stringify(pipeline(), (_key, value) =>
      value && typeof value === 'object' && !Array.isArray(value) ? Object.fromEntries(Object.entries(value).reverse()) : value))
    assert.equal(reconcilePipelineDraft(draft, { id: reorderedProperties }), draft)
    assert.equal(reconcilePipelineDraft(draft, { id: { ...pipeline(), registrations: [...pipeline().registrations!].reverse() } })!.serverDiverged, true)
    const unknown = { ...pipeline(), future: { enabled: true } }
    assert.equal(reconcilePipelineDraft(draft, { id: unknown })!.serverDiverged, true)
  })

  it('handles server deletions without silently dropping dirty drafts', () => {
    const clean = createPipelineDraft('id', pipeline())
    assert.equal(reconcilePipelineDraft(clean, {}), null)
    const dirty = { ...clean, releasePattern: '^/local' }
    const next = reconcilePipelineDraft(dirty, {})!
    assert.equal(next.serverDiverged, true)
    assert.equal(next.registrations, dirty.registrations)
    assert.equal(preparePipeline(next, descriptions).valid, false)
    assert.equal(reconcilePipelineDraft(next, {}, true), null)
  })

  it('preserves new drafts and unchanged clean stage identities unless discard is explicit', () => {
    const fresh = addRegistration(createPipelineDraft())
    assert.equal(reconcilePipelineDraft(fresh, {}), fresh)
    assert.equal(reconcilePipelineDraft(fresh, {}, true), null)
    assert.equal(reconcilePipelineDraft(null, { id: pipeline() }), null)
    const clean = createPipelineDraft('id', pipeline())
    assert.equal(reconcilePipelineDraft(clean, { id: pipeline() }), clean)
  })
})

describe('unsaved action decisions', () => {
  it('only saves explicitly; discard proceeds and stay never proceeds', async () => {
    let calls = 0
    const save = async () => { calls++; return true }
    assert.equal(await resolveUnsavedChoice('stay', save), false)
    assert.equal(await resolveUnsavedChoice('discard', save), true)
    assert.equal(calls, 0)
    assert.equal(await resolveUnsavedChoice('save', save), true)
    assert.equal(calls, 1)
  })

  it('does not continue after a failed or rejected save, and leaves the draft intact', async () => {
    const draft = editRegistrationText(createPipelineDraft('id', pipeline()), 0, '{"name":"pending"}')
    const snapshot = structuredClone(draft)
    assert.equal(await resolveUnsavedChoice('save', async () => false), false)
    assert.equal(await resolveUnsavedChoice('save', async () => { throw new Error('403') }), false)
    assert.deepEqual(draft, snapshot)
    assert.equal(pipelineIsDirty(draft), true)
  })

  it('awaits save completion before allowing the pending action', async () => {
    let finish!: (saved: boolean) => void
    let proceeded = false
    const decision = resolveUnsavedChoice('save', () => new Promise(resolve => { finish = resolve })).then(allowed => { proceeded = allowed })
    await Promise.resolve()
    assert.equal(proceeded, false)
    finish(true)
    await decision
    assert.equal(proceeded, true)
  })
})

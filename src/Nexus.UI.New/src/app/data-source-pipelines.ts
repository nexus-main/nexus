import type { DataSourcePipeline, DataSourceRegistration, ExtensionDescription } from '../../../clients/typescript/V1.ts'
import { isJsonObject, parseConfigurationText, validateConfiguration } from './json-schema.ts'

const jsonEnvelopeSchema = {}

export interface RegistrationDraft {
  key: number
  original: DataSourceRegistration
  type: string
  resourceLocator: string | null
  infoUrl: string | null
  configuration: unknown
  rawText: string | undefined
}

export interface PipelineDraft {
  id: string | null
  original: DataSourcePipeline
  releasePattern: string | null
  visibilityPattern: string | null
  registrations: RegistrationDraft[]
  nextKey: number
  baseline: string | null
  serverDiverged: boolean
}

export type DraftIssue = { key?: number; message: string }
export type PreparedPipeline =
  | { valid: true; payload: DataSourcePipeline; issues: [] }
  | { valid: false; issues: DraftIssue[] }

export function sourceSchema(descriptions: ExtensionDescription[], type: string): unknown {
  return descriptions.find(description => description.type === type)?.additionalInformation?.['source-configuration-schema']
}

export function createPipelineDraft(id: string | null = null, pipeline: DataSourcePipeline = { registrations: [], releasePattern: null, visibilityPattern: null }): PipelineDraft {
  const original = structuredClone(pipeline)
  const registrations = (original.registrations ?? []).map((registration, key) => ({
    key, original: registration, type: registration.type ?? '',
    resourceLocator: registration.resourceLocator ?? null, infoUrl: registration.infoUrl ?? null,
    configuration: registration.configuration, rawText: undefined,
  }))
  const draft: PipelineDraft = { id, original, releasePattern: original.releasePattern ?? null,
    visibilityPattern: original.visibilityPattern ?? null, registrations, nextKey: registrations.length, baseline: null, serverDiverged: false }
  if (id !== null) draft.baseline = draftFingerprint(draft)
  return draft
}

function draftFingerprint(draft: PipelineDraft): string {
  return JSON.stringify([draft.releasePattern, draft.visibilityPattern,
    draft.registrations.map(({ original: _original, ...registration }) => registration)])
}

export function pipelineIsDirty(draft: PipelineDraft): boolean {
  return draft.baseline === null || draft.baseline !== draftFingerprint(draft)
}

/** Call only after a successful pipeline read. Description retries must not clear divergence. */
export function reconcilePipelineDraft(draft: PipelineDraft | null, pipelines: Record<string, DataSourcePipeline>, discardDirty = false): PipelineDraft | null {
  if (!draft) return null
  if (draft.id === null) return discardDirty ? null : draft
  const server = Object.hasOwn(pipelines, draft.id) ? pipelines[draft.id] : undefined
  // Object property order is not a server change; array/stage order is.
  const fingerprint = (value: unknown) => JSON.stringify(value, (_key, item: unknown) =>
    isJsonObject(item) ? Object.fromEntries(Object.entries(item).sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)) : item)
  const serverDiverged = server === undefined || fingerprint(server) !== fingerprint(draft.original)
  if (pipelineIsDirty(draft) && !discardDirty) {
    return draft.serverDiverged === serverDiverged ? draft : { ...draft, serverDiverged }
  }
  if (server === undefined) return null
  return serverDiverged || discardDirty || draft.serverDiverged ? createPipelineDraft(draft.id, server) : draft
}

/** Acknowledge only a successful write, without remounting stages or losing raw formatting. */
export function acceptPipelineSave(draft: PipelineDraft, id: string, payload: DataSourcePipeline): PipelineDraft {
  const saved = { ...draft, id, original: structuredClone(payload), registrations: draft.registrations.map((registration, index) => ({
    ...registration, original: structuredClone(payload.registrations![index]),
  })) }
  return { ...saved, baseline: draftFingerprint(saved) }
}

export function updateRegistration(draft: PipelineDraft, key: number, patch: Partial<Pick<RegistrationDraft, 'type' | 'resourceLocator' | 'infoUrl' | 'configuration' | 'rawText'>>): PipelineDraft {
  return { ...draft, registrations: draft.registrations.map(registration => registration.key === key ? { ...registration, ...patch } : registration) }
}

export function editRegistrationText(draft: PipelineDraft, key: number, rawText: string): PipelineDraft {
  const parsed = parseConfigurationText(rawText)
  return updateRegistration(draft, key, { rawText, ...(parsed.valid ? { configuration: parsed.value } : {}) })
}

export function addRegistration(draft: PipelineDraft): PipelineDraft {
  return { ...draft, nextKey: draft.nextKey + 1, registrations: [...draft.registrations, {
    key: draft.nextKey, original: {}, type: '', resourceLocator: null, infoUrl: null,
    configuration: null, rawText: undefined,
  }] }
}

export function removeRegistration(draft: PipelineDraft, key: number): PipelineDraft {
  return { ...draft, registrations: draft.registrations.filter(registration => registration.key !== key) }
}

export function moveRegistration(draft: PipelineDraft, key: number, direction: -1 | 1): PipelineDraft {
  const index = draft.registrations.findIndex(registration => registration.key === key)
  const target = index + direction
  if (index < 0 || target < 0 || target >= draft.registrations.length) return draft
  const registrations = [...draft.registrations]
  ;[registrations[index], registrations[target]] = [registrations[target], registrations[index]]
  return { ...draft, registrations }
}

/** Always checks every registration, including editors that have never been mounted. */
export function preparePipeline(draft: PipelineDraft, descriptions: ExtensionDescription[]): PreparedPipeline {
  const issues: DraftIssue[] = []
  if (draft.serverDiverged) issues.push({ message: 'The saved pipeline changed or was removed on the server. Reload and discard local edits before saving to avoid overwriting server changes.' })
  if (!draft.registrations.length) issues.push({ message: 'Add at least one registration.' })
  const registrations = draft.registrations.map((registration, index) => {
    const issue = (message: string) => issues.push({ key: registration.key, message: `Registration ${index + 1}: ${message}` })
    if (!registration.type.trim()) issue('Select a source type.')
    else if (!descriptions.some(description => description.type === registration.type)) issue('The source description is unavailable. Configuration is available for inspection, but saving is blocked.')
    const parsed = registration.rawText === undefined ? { valid: true as const, value: registration.configuration } : parseConfigurationText(registration.rawText)
    const configuration = parsed.valid ? parsed.value : registration.configuration
    if (!parsed.valid) parsed.errors.forEach(issue)
    // Do not substitute {} for missing or invalid schemas, even when the JSON parses.
    const validation = validateConfiguration(sourceSchema(descriptions, registration.type), configuration)
    if (!validation.valid) validation.errors.forEach(issue)
    return { ...registration.original, type: registration.type, resourceLocator: registration.resourceLocator,
      infoUrl: registration.infoUrl, configuration }
  })
  const payload = { ...draft.original, releasePattern: draft.releasePattern, visibilityPattern: draft.visibilityPattern, registrations }
  // Also protect unknown envelope properties from unsafe values on a round trip.
  const envelope = validateConfiguration(jsonEnvelopeSchema, payload)
  if (!envelope.valid) envelope.errors.forEach(message => issues.push({ message }))
  return issues.length ? { valid: false, issues } : { valid: true, payload, issues: [] }
}

export type UnsavedChoice = 'save' | 'discard' | 'stay'

export async function resolveUnsavedChoice(choice: UnsavedChoice, save: () => Promise<boolean>): Promise<boolean> {
  if (choice === 'stay') return false
  if (choice === 'discard') return true
  try { return await save() } catch { return false }
}

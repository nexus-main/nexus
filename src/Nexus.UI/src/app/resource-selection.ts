import type { ResourceRow } from './nexus.service'

export type RepresentationRow = ResourceRow & {
  key: string
  representation: ResourceRow['representations'][number]
  basePeriod: bigint
}

export type ResourceSelection = RepresentationRow & {
  parameters: Record<string, string>
  kinds: RepresentationKind[]
}

export const representationKinds = [
  'Original', 'Resampled', 'Mean', 'MeanPolarDeg', 'Min', 'Max',
  'Std', 'Rms', 'MinBitwise', 'MaxBitwise', 'Sum',
] as const

export type RepresentationKind = typeof representationKinds[number]

export type StoredSelectionReference = {
  catalogId: string
  path: string
  basePeriod: string | null
  parameters: Record<string, string>
  kinds: RepresentationKind[]
}

export type StoredSelectionState = {
  version: 1
  period: string
  automaticPeriod: boolean
  selections: StoredSelectionReference[]
}

export type ParsedResourcePath = {
  path: string
  period: bigint
  basePeriod: bigint
  kind: RepresentationKind
  parameters: Record<string, string>
}

const maxTicks = 9223372036854775807n
const ticksPerSecond = 10000000n
const units = [
  ['d', 86400000000000n], ['h', 3600000000000n], ['min', 60000000000n],
  ['s', 1000000000n], ['ms', 1000000n], ['us', 1000n], ['ns', 1n],
] as const

export function parsePeriod(value: string): bigint | null {
  const text = value.trim()
  let ticks: bigint
  const unitMatch = /^(\d+)(?:\s|_)*([a-z]+)$/i.exec(text)
  const timeSpan = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?$/.exec(text)
  const iso = /^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)(?:\.(\d{1,7}))?S)?)?$/.exec(text)

  if (unitMatch) {
    const unit = units.find(([name]) => name === unitMatch[2].toLowerCase())
    if (!unit) return null
    const nanoseconds = BigInt(unitMatch[1]) * unit[1]
    if (nanoseconds % 100n !== 0n) return null
    ticks = nanoseconds / 100n
  } else if (timeSpan) {
    const [, days, hours, minutes, seconds, fraction] = timeSpan
    if (Number(hours) > 23 || Number(minutes) > 59 || Number(seconds) > 59) return null
    ticks = ((BigInt(days ?? '0') * 24n + BigInt(hours)) * 3600n
      + BigInt(minutes) * 60n + BigInt(seconds)) * ticksPerSecond
      + BigInt((fraction ?? '').padEnd(7, '0'))
  } else if (iso && iso.slice(1).some((part) => part !== undefined) && !text.endsWith('T')) {
    const [, days, hours, minutes, seconds, fraction] = iso
    ticks = ((BigInt(days ?? '0') * 24n + BigInt(hours ?? '0')) * 3600n
      + BigInt(minutes ?? '0') * 60n + BigInt(seconds ?? '0')) * ticksPerSecond
      + BigInt((fraction ?? '').padEnd(7, '0'))
  } else {
    return null
  }

  return ticks <= maxTicks ? ticks : null
}

export function formatPeriod(ticks: bigint, separator = ' '): string {
  if (ticks < 0n || ticks > maxTicks) throw new RangeError('Period is outside the TimeSpan range')
  if (ticks === 0n) return `0${separator}s`
  const nanoseconds = ticks * 100n
  const [name, scale] = units.find(([, scale]) => nanoseconds % scale === 0n)!
  return `${nanoseconds / scale}${separator}${name}`
}

export function parseFilePeriod(value: string): bigint | null {
  return value.trim().toLowerCase() === 'single file' ? 0n : parsePeriod(value)
}

export function formatFilePeriod(ticks: bigint): string {
  return ticks === 0n ? 'Single file' : formatPeriod(ticks)
}

export function toTimeSpan(ticks: bigint): string {
  if (ticks < 0n || ticks > maxTicks) throw new RangeError('Period is outside the TimeSpan range')
  const seconds = ticks / ticksPerSecond
  const days = seconds / 86400n
  const hours = (seconds / 3600n % 24n).toString().padStart(2, '0')
  const minutes = (seconds / 60n % 60n).toString().padStart(2, '0')
  const remainder = (seconds % 60n).toString().padStart(2, '0')
  const fraction = ticks % ticksPerSecond
  return `${days ? `${days}.` : ''}${hours}:${minutes}:${remainder}`
    + (fraction ? `.${fraction.toString().padStart(7, '0')}` : '')
}

export function representationRows(resources: ResourceRow[]): RepresentationRow[] {
  return resources.flatMap((resource) => resource.representations.flatMap((representation) => {
    const basePeriod = parsePeriod(representation.samplePeriod ?? '')
    if (basePeriod === null || basePeriod <= 0n) return []
    const row = { ...resource, representation, basePeriod, key: '' }
    row.key = selectionKey(row, {})
    return [row]
  }))
}

function parameterEntries(parameters: Record<string, string>): [string, string][] {
  return Object.keys(parameters).sort().map((name) => [name, parameters[name]])
}

export function selectionKey(row: RepresentationRow, parameters: Record<string, string>): string {
  return JSON.stringify([row.catalogId, row.path, row.basePeriod.toString(), parameterEntries(parameters)])
}

export function defaultKind(period: bigint, base: bigint): RepresentationKind {
  return period === base ? 'Original' : period < base ? 'Resampled' : 'Mean'
}

export function kindValid(kind: RepresentationKind, period: bigint, base: bigint): boolean {
  if (period <= 0n || base <= 0n || period > maxTicks || base > maxTicks) return false
  if (kind === 'Original') return period === base
  if (kind === 'Resampled') return period < base && base % period === 0n
  return representationKinds.includes(kind) && period > base && period % base === 0n
}

export function requestPath(selection: ResourceSelection, kind: RepresentationKind, period: bigint): string {
  const suffix = kind === 'Original' ? '' : `_${kind.replace(/([a-z])([A-Z])/g, '$1_$2').toLowerCase()}`
  const entries = parameterEntries(selection.parameters)
  const parameters = entries.length ? `(${entries.map(([name, value]) => `${name}=${value}`).join(',')})` : ''
  return `${selection.path}/${formatPeriod(period, '_')}${suffix}${parameters}#base=${formatPeriod(selection.basePeriod, '_')}`
}

export function parseResourcePath(value: string): ParsedResourcePath | null {
  const baseIndex = value.indexOf('#base=')
  if (baseIndex <= 0 || value.indexOf('#base=', baseIndex + 1) !== -1) return null
  const basePeriod = parsePeriod(value.slice(baseIndex + '#base='.length))
  if (basePeriod === null || basePeriod <= 0n) return null

  const beforeBase = value.slice(0, baseIndex)
  const slashIndex = beforeBase.lastIndexOf('/')
  if (slashIndex <= 0 || slashIndex === beforeBase.length - 1) return null
  const path = beforeBase.slice(0, slashIndex)
  let method = beforeBase.slice(slashIndex + 1)
  let parameters: Record<string, string> = {}

  if (method.endsWith(')')) {
    const parameterIndex = method.lastIndexOf('(')
    if (parameterIndex < 0) return null
    const parsedParameters = parseResourcePathParameters(method.slice(parameterIndex + 1, -1))
    if (!parsedParameters) return null
    parameters = parsedParameters
    method = method.slice(0, parameterIndex)
  }

  const originalPeriod = parsePeriod(method)
  if (originalPeriod !== null && originalPeriod > 0n) return { path, period: originalPeriod, basePeriod, kind: 'Original', parameters }

  for (const [suffix, kind] of resourcePathMethodSuffixes) {
    if (!method.endsWith(suffix)) continue
    const period = parsePeriod(method.slice(0, -suffix.length))
    if (period !== null && period > 0n) return { path, period, basePeriod, kind, parameters }
  }

  return null
}

const resourcePathMethodSuffixes = representationKinds
  .filter((kind) => kind !== 'Original')
  .map((kind) => [`_${kind.replace(/([a-z])([A-Z])/g, '$1_$2').toLowerCase()}`, kind] as const)
  .sort((left, right) => right[0].length - left[0].length)

function parseResourcePathParameters(value: string): Record<string, string> | null {
  if (!value) return {}
  const entries: [string, string][] = []
  for (const part of value.split(',')) {
    const separatorIndex = part.indexOf('=')
    if (separatorIndex <= 0) return null
    const name = part.slice(0, separatorIndex)
    const parameterValue = part.slice(separatorIndex + 1)
    if (!name || entries.some(([entryName]) => entryName === name)) return null
    entries.push([name, parameterValue])
  }
  return Object.fromEntries(entries.sort(([left], [right]) => left.localeCompare(right)))
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

export function readSelectionState(value: unknown): StoredSelectionState {
  const state: StoredSelectionState = { version: 1, period: '1 s', automaticPeriod: true, selections: [] }
  const legacy = Array.isArray(value)
  if (!legacy && (!isRecord(value) || value['version'] !== 1)) return state
  const stored = legacy ? undefined : value as Record<string, unknown>
  const period = typeof stored?.['period'] === 'string' ? parsePeriod(stored['period']) : null
  if (period !== null && period > 0n) {
    state.period = formatPeriod(period)
    state.automaticPeriod = typeof stored?.['automaticPeriod'] === 'boolean' ? stored['automaticPeriod'] : true
  }
  const references = legacy ? value : stored?.['selections']
  if (!Array.isArray(references)) return state
  const unique = new Map<string, StoredSelectionReference>()

  for (const reference of references) {
    if (!isRecord(reference)) continue
    const catalogId = reference['catalogId']
    const path = reference['path']
    if (typeof catalogId !== 'string' || !catalogId.startsWith('/') || typeof path !== 'string') continue
    const prefix = catalogId === '/' ? '/' : `${catalogId}/`
    if (!path.startsWith(prefix) || path.length <= prefix.length) continue
    let basePeriod: string | null = null
    let parameters: Record<string, string> = {}
    let kinds: RepresentationKind[] = []

    if (!legacy) {
      if (!isRecord(reference['parameters']) || !Array.isArray(reference['kinds'])) continue
      if (!Object.values(reference['parameters']).every((entry) => typeof entry === 'string')) continue
      parameters = Object.fromEntries(parameterEntries(reference['parameters'] as Record<string, string>))
      if (reference['basePeriod'] === null) {
        if (reference['kinds'].length) continue
      } else {
        const base = typeof reference['basePeriod'] === 'string' ? parsePeriod(reference['basePeriod']) : null
        if (base === null || base <= 0n) continue
        basePeriod = formatPeriod(base)
        kinds = [...new Set(reference['kinds'].filter((kind): kind is RepresentationKind => representationKinds.includes(kind)))]
    }
    }

    const key = JSON.stringify([catalogId, path, basePeriod, parameterEntries(parameters)])
    const previous = unique.get(key)
    if (previous) previous.kinds = [...new Set([...previous.kinds, ...kinds])]
    else unique.set(key, { catalogId, path, basePeriod, parameters, kinds })
  }

  state.selections = [...unique.values()]
  return state
}

export function storeSelectionReference(selection: ResourceSelection): StoredSelectionReference {
  return {
    catalogId: selection.catalogId,
    path: selection.path,
    basePeriod: formatPeriod(selection.basePeriod),
    parameters: Object.fromEntries(parameterEntries(selection.parameters)),
    kinds: [...new Set(selection.kinds)],
  }
}

export function hydrateSelections(
  references: StoredSelectionReference[],
  catalogs: ReadonlyMap<string, RepresentationRow[]>,
  period: bigint,
  automaticPeriod: boolean,
): {
  selections: Map<string, ResourceSelection>
  references: StoredSelectionReference[]
  unresolved: StoredSelectionReference[]
  period: bigint
} {
  const selections = new Map<string, ResourceSelection>()
  const retained: StoredSelectionReference[] = []
  const unresolved: StoredSelectionReference[] = []
  const resolved = new Map<string, StoredSelectionReference>()
  const pending = new Map<string, StoredSelectionReference>()
  let initialized = !automaticPeriod || references.some((reference) => reference.basePeriod !== null)

  for (const input of references) {
    const reference = readSelectionState({ version: 1, selections: [input] }).selections[0]
    if (!reference) continue
    const rows = catalogs.get(reference.catalogId)
    if (!rows) {
      const key = JSON.stringify([reference.catalogId, reference.path, reference.basePeriod, parameterEntries(reference.parameters)])
      const previous = pending.get(key)
      if (previous) previous.kinds = [...new Set([...previous.kinds, ...reference.kinds])]
      else {
        pending.set(key, reference)
        retained.push(reference)
        unresolved.push(reference)
      }
      continue
    }
    const base = reference.basePeriod === null ? null : parsePeriod(reference.basePeriod)
    const row = rows.find((row) => row.catalogId === reference.catalogId && row.path === reference.path
      && (base === null || row.basePeriod === base))
    if (!row) continue
    if (!initialized) {
      period = row.basePeriod
      initialized = true
    }
    const key = selectionKey(row, reference.parameters)
    const kinds = reference.kinds.length ? reference.kinds : reference.basePeriod === null ? [defaultKind(period, row.basePeriod)] : []
    const previous = selections.get(key)
    if (previous) {
      previous.kinds = [...new Set([...previous.kinds, ...kinds])]
      resolved.get(key)!.kinds = [...previous.kinds]
    } else {
      const selection = { ...row, key, parameters: { ...reference.parameters }, kinds: [...kinds] }
      selections.set(key, selection)
      const stored = storeSelectionReference(selection)
      resolved.set(key, stored)
      retained.push(stored)
    }
  }

  return { selections, references: retained, unresolved, period }
}

export function dateTicks(value: string): bigint | null {
  const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})$/.exec(value)
  if (!match || match[1].startsWith('0000-')) return null
  const [, date, fraction, zone] = match
  const local = Date.parse(`${date}Z`)
  // Date.parse normalizes impossible dates and 24:00; require the original calendar fields.
  if (!Number.isFinite(local) || new Date(local).toISOString().slice(0, 19) !== date) return null
  if (zone !== 'Z') {
    const hours = Number(zone.slice(1, 3))
    const minutes = Number(zone.slice(4, 6))
    if (hours > 14 || minutes > 59 || (hours === 14 && minutes !== 0)) return null
  }
  const milliseconds = Date.parse(`${date}${zone}`)
  if (!Number.isFinite(milliseconds)) return null
  // Parse whole seconds separately so sub-millisecond ticks are never rounded or truncated.
  const ticks = BigInt(milliseconds) * 10000n + 621355968000000000n
    + BigInt((fraction ?? '').padEnd(7, '0'))
  return ticks >= 0n && ticks <= 3155378975999999999n ? ticks : null
}

export function alignRangeEndpoint(value: string, period: bigint): string {
  if (period <= 0n || period > maxTicks) return value
  const ticks = dateTicks(value)
  if (ticks === null) return value
  const aligned = ticks - ticks % period
  const iso = new Date(Number(aligned / 10000n - 62135596800000n)).toISOString()
  return aligned % 10000n === 0n
    ? iso
    : `${iso.slice(0, 19)}.${(aligned % ticksPerSecond).toString().padStart(7, '0')}Z`
}

export function executionRangeError(begin: string, end: string, period: bigint, seriesCount: number): string {
  if (!Number.isInteger(seriesCount) || seriesCount < 1) return 'Select at least one output series.'
  if (seriesCount > 100) return 'Select no more than 100 output series.'
  if (period <= 0n || period > maxTicks) return 'Choose a positive Period within the TimeSpan range.'
  const beginTicks = dateTicks(begin)
  const endTicks = dateTicks(end)
  if (beginTicks === null || endTicks === null || beginTicks >= endTicks) return 'Choose a valid UTC range with From before To.'
  if (beginTicks % period !== 0n || endTicks % period !== 0n) return 'From and To must align with Period.'
  return ''
}

export function resourceAvailableForRange(
  resource: ResourceRow | RepresentationRow,
  catalogProperties: Record<string, unknown> | null | undefined,
  selectedBegin: string,
  selectedEnd: string,
): boolean {
  const resources = catalogProperties?.['resources']
  if (!isRecord(resources)) return true
  const availability = resources['availability']
  if (!Array.isArray(availability)) return true

  const selectedBeginTicks = dateTicks(selectedBegin)
  const selectedEndTicks = dateTicks(selectedEnd)
  if (selectedBeginTicks === null || selectedEndTicks === null || selectedBeginTicks >= selectedEndTicks) return true

  let matched = false
  for (const rule of availability) {
    if (!isRecord(rule)) continue
    const pattern = rule['pattern']
    if (typeof pattern !== 'string') continue
    let regex: RegExp
    try { regex = new RegExp(pattern) } catch { continue }

    const begin = rule['begin']
    const end = rule['end']
    let beginTicks: bigint | null = null
    let endTicks: bigint | null = null
    if (begin != null) {
      if (typeof begin !== 'string') continue
      beginTicks = dateTicks(begin)
      if (beginTicks === null) continue
    }
    if (end != null) {
      if (typeof end !== 'string') continue
      endTicks = dateTicks(end)
      if (endTicks === null) continue
    }

    if (!regex.test(resourceAvailabilityPath(resource))) continue
    matched = true

    const beginOk = beginTicks === null || beginTicks < selectedEndTicks
    const endOk = endTicks === null || selectedBeginTicks < endTicks
    if (beginOk && endOk) return true
  }

  return !matched
}

function resourceAvailabilityPath(resource: ResourceRow | RepresentationRow): string {
  if (!('basePeriod' in resource)) return resource.path

  const period = formatPeriod(resource.basePeriod, '_')
  return `${resource.path}/${period}#base=${period}`
}

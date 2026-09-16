import Ajv from 'ajv-draft-04'
import addFormats from 'ajv-formats'
import type { ValidateFunction } from 'ajv'

export interface ConfigurationValidation {
  valid: boolean
  errors: string[]
}

export type JsonParseResult =
  | { valid: true; value: unknown; errors: [] }
  | { valid: false; errors: string[] }

type Schema = Record<string, unknown>
type ValidatorEntry = { ajv: Ajv; validate: ValidateFunction; fragments: Map<string, ValidateFunction> }
const validators = new WeakMap<object, ValidatorEntry | string>()
const schemaId = 'urn:nexus:configuration'

// NJsonSchema uses both names for .NET TimeSpan. Do not substitute Date objects.
function validTimeSpan(text: string): boolean {
  const match = /^(-)?(?:(\d+)\.)?(\d{2}):([0-5]\d):([0-5]\d)(?:\.(\d{1,7}))?$/.exec(text)
  if (!match || match[0] !== text || Number(match[3]) > 23) return false
  const ticks = ((BigInt(match[2] ?? '0') * 24n + BigInt(match[3])) * 3600n +
    BigInt(match[4]) * 60n + BigInt(match[5])) * 10000000n + BigInt((match[6] ?? '').padEnd(7, '0'))
  return ticks <= (match[1] ? 9223372036854775808n : 9223372036854775807n)
}

function validDotNetTime(text: string, withDate: boolean): boolean {
  let time = text
  if (withDate) {
    const date = /^(\d{4})-(\d{2})-(\d{2})T/.exec(text)
    if (!date) return false
    const year = Number(date[1])
    const month = Number(date[2])
    const day = Number(date[3])
    const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0)
    const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
    if (year < 1 || month < 1 || month > 12 || day < 1 || day > days[month - 1]) return false
    time = text.slice(date[0].length)
  }
  const match = /^(\d{2}):([0-5]\d):([0-5]\d)(?:\.(\d{1,7}))?(?:Z|([+-])(\d{2}):([0-5]\d))?$/.exec(time)
  if (!match || match[0] !== time || Number(match[1]) > 23) return false
  const offsetHour = Number(match[6] ?? 0)
  const offsetMinute = Number(match[7] ?? 0)
  return offsetHour < 14 || (offsetHour === 14 && offsetMinute === 0)
}

export function isJsonObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function validatorEntry(schema: unknown): ValidatorEntry | string {
  if (!isJsonObject(schema)) return 'A Draft 4 schema object is required.'
  const cached = validators.get(schema)
  if (cached) return cached
  try {
    // One Ajv per root avoids collisions between registration schemas with the same id.
    const ajv = new Ajv({ allErrors: true, strict: false, ownProperties: true, ignoreKeywordsWithRef: true,
      logger: false, coerceTypes: false, useDefaults: false, removeAdditional: false })
    addFormats(ajv)
    ajv.addFormat('guid', addFormats.get('uuid'))
    ajv.addFormat('uint64', { type: 'number', validate: (value: number) => Number.isSafeInteger(value) && value >= 0 })
    ajv.addFormat('decimal', { type: 'number', validate: Number.isFinite })
    ajv.addFormat('time', (text: string) => validDotNetTime(text, false))
    ajv.addFormat('date-time', (text: string) => validDotNetTime(text, true))
    ajv.removeKeyword('multipleOf')
    ajv.addKeyword({ keyword: 'multipleOf', type: 'number', schemaType: 'number', errors: false,
      metaSchema: { type: 'number', minimum: 0, exclusiveMinimum: true },
      validate: (divisor: number, value: number) => {
        if (!Number.isFinite(value) || !Number.isFinite(divisor) || divisor <= 0) return false
        if (value === 0) return true
        // Number's round-trip decimal tokens are the parser's supported numeric domain.
        const [numerator, numeratorPower] = decimalIdentity(String(value)).split('e')
        const [denominator, denominatorPower] = decimalIdentity(String(divisor)).split('e')
        const power = BigInt(numeratorPower) - BigInt(denominatorPower)
        return power >= 0n
          ? (BigInt(numerator) * 10n ** power) % BigInt(denominator) === 0n
          : BigInt(numerator) % (BigInt(denominator) * 10n ** -power) === 0n
      },
    })
    ajv.addFormat('time-span', validTimeSpan)
    const isoDuration = addFormats.get('duration') as RegExp
    ajv.addFormat('duration', (text: string) => validTimeSpan(text) || isoDuration.test(text))
    const pending: unknown[] = [schema]
    const seen = new Set<object>()
    while (pending.length) {
      const node = pending.pop()
      if (!isJsonObject(node) || seen.has(node)) continue
      seen.add(node)
      // Ajv reads this OpenAPI extension directly in its type compiler, even if removed as a keyword.
      if (Object.hasOwn(node, 'nullable')) {
        throw new Error('OpenAPI nullable is unsupported in Draft 4. Express nullability with type unions or anyOf/oneOf instead.')
      }
      // Ajv's legacy sibling-ignore option still checks type before processing $ref.
      if (typeof node['$ref'] === 'string' && node['type'] !== undefined) {
        throw new Error('Draft 4 $ref with a sibling type is not supported by this validator. Put the type on the referenced schema.')
      }
      const format = node['format']
      if (typeof node['$ref'] !== 'string' && typeof format === 'string' && !Object.hasOwn(ajv.formats, format)) {
        throw new Error(`Unsupported format "${format}". Validation cannot safely ignore this constraint.`)
      }
      for (const keyword of ['properties', 'patternProperties', 'definitions', 'dependencies']) {
        if (isJsonObject(node[keyword])) pending.push(...Object.values(node[keyword]))
      }
      for (const keyword of ['items', 'additionalItems', 'additionalProperties', 'not', 'allOf', 'anyOf', 'oneOf']) {
        const child = node[keyword]
        if (Array.isArray(child)) pending.push(...child)
        else if (isJsonObject(child)) pending.push(child)
      }
    }
    ajv.addSchema(schema, schemaId)
    const validate = ajv.getSchema(schemaId)
    if (!validate) throw new Error('Schema could not be compiled.')
    const entry = { ajv, validate, fragments: new Map<string, ValidateFunction>() }
    validators.set(schema, entry)
    return entry
  } catch (error) {
    const message = `Schema unavailable: ${error instanceof Error ? error.message : String(error)}`
    validators.set(schema, message)
    return message
  }
}

function jsonValueError(value: unknown, seen = new Set<object>()): string | undefined {
  if (value === null || typeof value === 'string' || typeof value === 'boolean') return undefined
  if (typeof value === 'number') {
    return !Number.isFinite(value) || (Number.isInteger(value) && !Number.isSafeInteger(value)) || Object.is(value, -0)
      ? 'Configuration contains an unsafe JSON number.' : undefined
  }
  if (typeof value !== 'object') return 'Configuration must contain only JSON values; the root must be present.'
  if (seen.has(value)) return 'Configuration contains a circular reference.'
  if (!Array.isArray(value) && Object.getPrototypeOf(value) !== Object.prototype && Object.getPrototypeOf(value) !== null) {
    return 'Configuration must contain plain JSON objects, not class instances or dates.'
  }
  seen.add(value)
  for (const item of Array.isArray(value) ? value : Object.values(value)) {
    const error = jsonValueError(item, seen)
    if (error) return error
  }
  seen.delete(value)
  return undefined
}

/** Schemas are cached by identity. Treat a supplied schema as immutable. No data is modified. */
export function validateConfiguration(schema: unknown, value: unknown): ConfigurationValidation {
  const entry = validatorEntry(schema)
  if (typeof entry === 'string') return { valid: false, errors: [entry] }
  try {
    const error = jsonValueError(value)
    if (error) return { valid: false, errors: [error] }
    const valid = entry.validate(value) === true
    return { valid, errors: valid ? [] : (entry.validate.errors ?? []).map(error =>
      `${error.instancePath || '/'}: ${error.message ?? 'Invalid value'}${error.params['missingProperty'] ? ` (${error.params['missingProperty']})` : ''}${error.params['additionalProperty'] ? ` (${error.params['additionalProperty']})` : ''}`) }
  } catch (error) {
    return { valid: false, errors: [`Validation failed: ${String(error)}`] }
  }
}

function decimalIdentity(token: string): string {
  const [mantissa, exponent = '0'] = token.toLowerCase().split('e')
  const negative = mantissa.startsWith('-')
  const unsigned = negative ? mantissa.slice(1) : mantissa
  const [whole, fraction = ''] = unsigned.split('.')
  const digits = (whole + fraction).replace(/^0+/, '')
  if (!digits) return negative ? '-0' : '0'
  const coefficient = digits.replace(/0+$/, '')
  const power = BigInt(exponent) - BigInt(fraction.length) + BigInt(digits.length - coefficient.length)
  return `${negative ? '-' : ''}${coefficient}e${power}`
}

/**
 * Rejects unsafe integers and decimal tokens changed by Number's decimal round trip,
 * before JSON.parse. Ordinary decimals such as 0.1 are supported, not arbitrary precision.
 * Does not depend on the recent JSON.parse reviver context (works in older browsers).
 * Use on response.text() BEFORE transport JSON.parse for GET /api/v1/sources/pipelines.
 * Do not apply to description/schema responses: their Int64 bounds commonly exceed safe integers.
 * Already-rounded values cannot be recovered. See json-schema.md for the numeric/format policy.
 */
export function parseJsonSafely(text: string): JsonParseResult {
  try {
    const tokens = /"(?:\\[\s\S]|[^"\\])*"|(-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)/g
    for (const match of text.matchAll(tokens)) {
      const token = match[1]
      if (token === undefined) continue
      const number = Number(token)
      if (!Number.isFinite(number) || (Number.isInteger(number) && !Number.isSafeInteger(number)) ||
        decimalIdentity(token) !== decimalIdentity(String(number))) {
        return { valid: false, errors: [`Number ${token} cannot be represented safely without precision loss (offset ${match.index}). Use a string only if the schema permits it.`] }
      }
    }
    return { valid: true, value: JSON.parse(text) as unknown, errors: [] }
  } catch (error) {
    return { valid: false, errors: [error instanceof Error ? error.message : String(error)] }
  }
}

export function configurationText(value: unknown): string {
  try {
    return jsonValueError(value) ? '' : JSON.stringify(value, null, 2)
  } catch {
    return ''
  }
}

export function configurationResetNeedsConfirmation(value: unknown, rawText: string | undefined): boolean {
  return value !== undefined || rawText !== undefined
}

/** null means untouched; an empty string is an edited (invalid) numeric token. */
export class SchemaNumberSession {
  private token: string | null = null

  edit(text: string): void {
    this.token = text
  }

  finish(): string | null {
    const token = this.token
    this.token = null
    return token
  }
}

/** Own-property copies make __proto__, constructor and prototype ordinary dictionary keys. */
export function setConfigurationProperty(object: Record<string, unknown>, key: string, value: unknown, present = true): Record<string, unknown> {
  const entries = Object.entries(object).filter(([name]) => name !== key)
  if (present) entries.push([key, value])
  return Object.fromEntries(entries)
}

/** Embed an invalid field draft in the full document so a parent can retain it verbatim. */
export function configurationWithRawMember(value: Record<string, unknown> | unknown[], key: string | number, text: string): string {
  let draft: string
  if (Array.isArray(value)) draft = `[${value.map((item, index) => index === key ? text : configurationText(item)).join(',')}]`
  else {
    const keys = Object.keys(value)
    if (!Object.hasOwn(value, key)) keys.push(String(key))
    draft = `{${keys.map(name => `${JSON.stringify(name)}:${name === key ? text : configurationText(value[name])}`).join(',')}}`
  }
  // Empty singleton array items or "1,2" must not silently become [] or two items.
  if (!parseJsonSafely(text).valid && parseJsonSafely(draft).valid) {
    draft += '\n/* Invalid field JSON: repair the field above and remove this comment. */'
  }
  return draft
}

export interface SchemaProperty {
  key: string
  paths: string[]
  required: boolean
}

export interface SchemaView {
  kind: 'raw' | 'string' | 'number' | 'integer' | 'boolean' | 'object' | 'array' | 'enum' | 'null'
  nullable: boolean
  title?: string
  description?: string
  enumValues: unknown[]
  enumLabels: string[]
  properties: SchemaProperty[]
  itemPaths: string[]
  additionalPaths: string[]
  allowAdditional: boolean
}

export function schemaPointer(path: string, key: string): string {
  return `${path}/${key.replace(/~/g, '~0').replace(/\//g, '~1')}`
}

function atPointer(root: unknown, path: string): unknown {
  if (path === '#') return root
  if (!path.startsWith('#/')) return undefined
  let value = root
  for (const part of decodeURIComponent(path.slice(2)).split('/')) {
    const key = part.replace(/~1/g, '/').replace(/~0/g, '~')
    if ((!isJsonObject(value) && !Array.isArray(value)) || !Object.hasOwn(value, key)) return undefined
    value = (value as Record<string, unknown>)[key]
  }
  return value
}

function validAtPaths(root: unknown, paths: string[], value: unknown): boolean {
  const entry = validatorEntry(root)
  if (typeof entry === 'string') return false
  try {
    return paths.every(path => {
      if (path === '#') return entry.validate(value) === true
      let validate = entry.fragments.get(path)
      if (!validate) {
        validate = entry.ajv.compile({ $ref: `${schemaId}${path}` })
        entry.fragments.set(path, validate)
      }
      return validate(value) === true
    })
  } catch {
    return false
  }
}

/** Rendering is deliberately conservative; original-schema Ajv validation remains authoritative. */
export function getSchemaView(root: unknown, paths: string[] = ['#']): SchemaView {
  const view: SchemaView = { kind: 'raw', nullable: false, enumValues: [], enumLabels: [], properties: [],
    itemPaths: [], additionalPaths: [], allowAdditional: false }
  if (typeof validatorEntry(root) === 'string') return view
  view.nullable = validAtPaths(root, paths, null)
  const parts: { schema: Schema; path: string }[] = []
  const flatten = (path: string, seen: Set<string>): boolean => {
    if (seen.has(path) || seen.size > 64) return false
    const schema = atPointer(root, path)
    if (!isJsonObject(schema)) return false
    const next = new Set(seen).add(path)
    // Nested ids change reference scope; leave these less common schemas to raw editing.
    if (path !== '#' && schema['id'] !== undefined) return false
    if (typeof schema['$ref'] === 'string') return flatten(schema['$ref'], next)
    for (const union of ['anyOf', 'oneOf']) {
      const branches = schema[union]
      if (!Array.isArray(branches)) continue
      if (branches.length === 1) {
        if (!flatten(schemaPointer(schemaPointer(path, union), '0'), next)) return false
        continue
      }
      if (branches.length !== 2) return false
      const nullIndex = branches.findIndex(branch => isJsonObject(branch) &&
        (branch['type'] === 'null' || (Array.isArray(branch['enum']) && branch['enum'].length === 1 && branch['enum'][0] === null)))
      if (nullIndex < 0 || !flatten(schemaPointer(schemaPointer(path, union), String(1 - nullIndex)), next)) return false
    }
    parts.push({ schema, path })
    if (Array.isArray(schema['allOf'])) {
      return schema['allOf'].every((_, index) => flatten(schemaPointer(schemaPointer(path, 'allOf'), String(index)), next))
    }
    return true
  }
  try {
    if (!paths.every(path => flatten(path, new Set()))) return view
  } catch {
    return view
  }
  let types: string[] | undefined
  let enumeration: unknown[] | undefined
  let enumSchema: Schema | undefined
  for (const { schema } of parts) {
    if (typeof schema['title'] === 'string') view.title = schema['title']
    if (typeof schema['description'] === 'string') view.description = schema['description']
    if (!enumeration && Array.isArray(schema['enum'])) {
      enumeration = schema['enum']
      enumSchema = schema
    }
    const type = schema['type']
    let candidates = typeof type === 'string' ? [type] : Array.isArray(type) ? type.filter((item): item is string => typeof item === 'string') : undefined
    candidates = candidates?.filter(item => item !== 'null')
    if (candidates) types = types ? [...new Set(types.flatMap(item => candidates.includes(item) ? [item] :
      (item === 'integer' && candidates.includes('number')) || (item === 'number' && candidates.includes('integer')) ? ['integer'] : []))] : candidates
    if (schema['patternProperties'] !== undefined || schema['extends'] !== undefined) return view
  }
  if (enumeration) {
    view.kind = 'enum'
    const displayNames = enumSchema?.['x-enumDisplayNames']
    const names = enumSchema?.['x-enumNames']
    for (const [index, value] of enumeration.entries()) {
      if (value === null || !validAtPaths(root, paths, value)) continue
      const display = Array.isArray(displayNames) && displayNames.length === enumeration.length ? displayNames[index] : undefined
      const name = Array.isArray(names) && names.length === enumeration.length ? names[index] : undefined
      view.enumValues.push(value)
      view.enumLabels.push(typeof display === 'string' && display ? display : typeof name === 'string' && name ? name : configurationText(value))
    }
    return view
  }
  if (types?.length === 0) {
    if (view.nullable) view.kind = 'null'
    return view
  }
  const type = types?.length === 1 ? types[0] : types ? undefined :
    parts.some(({ schema }) => schema['properties'] !== undefined || schema['additionalProperties'] !== undefined) ? 'object' :
      parts.some(({ schema }) => schema['items'] !== undefined) ? 'array' : undefined
  if (!type || !['string', 'number', 'integer', 'boolean', 'object', 'array'].includes(type)) return view
  view.kind = type as SchemaView['kind']
  if (type === 'object') {
    const properties = new Map<string, SchemaProperty>()
    const required = new Set<string>()
    view.allowAdditional = true
    for (const { schema, path } of parts) {
      if (Array.isArray(schema['required'])) for (const key of schema['required']) if (typeof key === 'string') required.add(key)
      if (isJsonObject(schema['properties'])) {
        for (const key of Object.keys(schema['properties'])) {
          const property = properties.get(key) ?? { key, paths: [], required: false }
          property.paths.push(schemaPointer(schemaPointer(path, 'properties'), key))
          properties.set(key, property)
        }
      }
      if (schema['additionalProperties'] === false) view.allowAdditional = false
      if (schema['additionalProperties'] === false || isJsonObject(schema['additionalProperties'])) view.additionalPaths.push(schemaPointer(path, 'additionalProperties'))
    }
    // An allOf branch's additionalProperties also constrains properties declared in other branches.
    for (const property of properties.values()) {
      for (const { schema, path } of parts) {
        if (isJsonObject(schema['properties']) && Object.hasOwn(schema['properties'], property.key)) continue
        if (schema['additionalProperties'] === false) return { ...view, kind: 'raw' }
        if (isJsonObject(schema['additionalProperties'])) property.paths.push(schemaPointer(path, 'additionalProperties'))
      }
      property.required = required.has(property.key)
    }
    // Required dictionary keys need a visible field even without a properties declaration.
    for (const key of required) if (!properties.has(key)) properties.set(key, { key, paths: view.additionalPaths, required: true })
    view.properties = [...properties.values()]
  }
  if (type === 'array') {
    for (const { schema, path } of parts) {
      if (Array.isArray(schema['items'])) return { ...view, kind: 'raw' }
      if (isJsonObject(schema['items'])) view.itemPaths.push(schemaPointer(path, 'items'))
    }
  }
  return view
}

export function createSchemaValue(view: SchemaView): unknown {
  switch (view.kind) {
    case 'enum': return view.enumValues.length ? structuredClone(view.enumValues[0]) : null
    case 'string': return ''
    case 'number': case 'integer': return 0
    case 'boolean': return false
    case 'array': return []
    case 'null': return null
    default: return {}
  }
}

export function schemaPresenceOptions(required: boolean, nullable: boolean): { label: string; value: string }[] {
  return [
    ...(!required ? [{ label: 'Not set', value: 'unset' }] : []),
    ...(nullable ? [{ label: 'Null', value: 'null' }] : []),
    { label: 'Value', value: 'value' },
  ]
}

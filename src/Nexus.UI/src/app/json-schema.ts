import Ajv from 'ajv-draft-04'
import addFormats from 'ajv-formats'
import type { ValidateFunction } from 'ajv'
import { parseDocument, stringify as stringifyYaml, visit } from 'yaml'

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

export function parseConfigurationText(text: string): JsonParseResult {
  if (text.trim() === '') return { valid: false, errors: ['Configuration YAML is empty.'] }
  try {
    const document = parseDocument(text, { schema: 'core', uniqueKeys: true })
    const errors = [...document.errors, ...document.warnings].map(error => error.message)
    if (errors.length) return { valid: false, errors }
    // Check scalar sources (including map keys) before toJS can discard rounded tokens.
    visit(document, { Scalar(_key, node) {
      if (typeof node.value !== 'number') return
      const token = node.source!
      const number = node.value
      if (!Number.isFinite(number) || (Number.isInteger(number) && !Number.isSafeInteger(number)) ||
        decimalIdentity(node.format === 'HEX' || node.format === 'OCT' ? BigInt(token).toString() : token.replace(/^\+/, '')) !== decimalIdentity(String(number))) {
        errors.push(`Number ${token} cannot be represented safely without precision loss (offset ${node.range?.[0]}). Use a string only if the schema permits it.`)
      }
    } })
    if (errors.length) return { valid: false, errors }
    const value = document.toJS({ maxAliasCount: 0 }) as unknown
    const error = jsonValueError(value)
    return error ? { valid: false, errors: [error] } : { valid: true, value, errors: [] }
  } catch (error) {
    return { valid: false, errors: [error instanceof Error ? error.message : String(error)] }
  }
}

export function configurationYamlText(value: unknown): string {
  try {
    return jsonValueError(value) ? '' : stringifyYaml(value, { indent: 2, lineWidth: 0 })
  } catch {
    return ''
  }
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

/** Creates an editable example, not a guaranteed-valid instance. See json-schema.md. */
export function createSchemaScaffold(schema: unknown): unknown {
  const maxDepth = 24
  const maxNodes = 4096
  const maxStringLength = 4096
  let remaining = maxNodes
  let inspected = 0
  // Do not hand cyclic JS objects or excessively large schemas/annotations to Ajv or cloning.
  const bounded = (value: unknown, depth = 0, ancestors = new Set<object>()): boolean => {
    if (++inspected > maxNodes || depth > maxDepth) return false
    if (typeof value === 'string') return value.length <= maxStringLength
    if (value === null || typeof value !== 'object') return true
    if (ancestors.has(value)) return false
    const next = new Set(ancestors).add(value)
    for (const key of Object.keys(value)) {
      if (key.length > maxStringLength || !bounded((value as Schema)[key], depth + 1, next)) return false
    }
    return true
  }
  const canValidate = bounded(schema)
  type Part = { schema: Schema; path: string }
  const build = (paths: string[], depth: number, ancestors: Set<object>): unknown => {
    if (depth >= maxDepth || remaining-- <= 0) return null
    const visited = new Set<object>()
    const expand = (path: string, trail: Set<object>, level: number): Part[] | null => {
      if (level >= maxDepth || remaining-- <= 0) return null
      let node: unknown
      try { node = atPointer(schema, path) } catch { return null }
      if (!isJsonObject(node)) return node === true ? [] : null
      if (trail.has(node) || (path !== '#' && node['id'] !== undefined)) return null
      visited.add(node)
      const next = new Set(trail).add(node)
      if (typeof node['$ref'] === 'string') return expand(node['$ref'], next, level + 1)
      const parts: Part[] = [{ schema: node, path }]
      if (Array.isArray(node['allOf'])) {
        for (let index = 0; index < node['allOf'].length; index++) {
          const child = expand(schemaPointer(schemaPointer(path, 'allOf'), String(index)), next, level + 1)
          if (!child) return null
          parts.push(...child)
        }
      }
      for (const union of ['oneOf', 'anyOf']) {
        const branches = node[union]
        if (!Array.isArray(branches)) continue
        let selected: Part[] | null = null
        for (let index = 0; index < branches.length && remaining > 0; index++) {
          const child = expand(schemaPointer(schemaPointer(path, union), String(index)), next, level + 1)
          if (!child) continue
          selected ??= child
          const nullOnly = child.some(part => part.schema['type'] === 'null' ||
            (Array.isArray(part.schema['type']) && part.schema['type'].every(type => type === 'null')) ||
            (Array.isArray(part.schema['enum']) && part.schema['enum'].every(value => value === null)))
          if (!nullOnly) { selected = child; break }
        }
        if (!selected) return null
        parts.push(...selected)
      }
      return parts
    }
    const parts: Part[] = []
    for (const path of paths) {
      const expanded = expand(path, ancestors, depth)
      if (!expanded) return null
      parts.push(...expanded)
    }
    const next = new Set([...ancestors, ...visited])
    const accepts = (value: unknown): boolean => {
      if (!canValidate || remaining <= 0) return false
      inspected = 0
      if (!bounded(value, depth)) return false
      remaining -= inspected
      return remaining >= 0 && !jsonValueError(value) && validAtPaths(schema, paths, value)
    }
    // Valid explicit examples are authoritative literals, not seeds for generic expansion.
    for (const keyword of ['default', 'examples', 'example']) {
      for (const part of parts) {
        const annotation = part.schema[keyword]
        const candidates = keyword === 'examples' ? (Array.isArray(annotation) ? annotation : []) : [annotation]
        for (const candidate of candidates) {
          if (!canValidate || remaining-- <= 0) break
          if (candidate !== undefined && candidate !== null && accepts(candidate)) return structuredClone(candidate)
        }
      }
    }
    // Enum members are literal values: expanding an object enum could leave the enumeration.
    const enumeration = parts.find(part => Array.isArray(part.schema['enum']))?.schema['enum'] as unknown[] | undefined
    if (enumeration) {
      for (const candidate of enumeration) {
        if (!canValidate || remaining-- <= 0) break
        if (candidate !== null && accepts(candidate)) return structuredClone(candidate)
      }
      return null
    }
    let types: string[] | undefined
    for (const part of parts) {
      const type = part.schema['type']
      const candidates = typeof type === 'string' ? [type] : Array.isArray(type) ? type.filter((item): item is string => typeof item === 'string') : undefined
      if (candidates) types = types ? [...new Set(types.flatMap(item => candidates.includes(item) ? [item] :
        (item === 'integer' && candidates.includes('number')) || (item === 'number' && candidates.includes('integer')) ? ['integer'] : []))] : candidates
    }
    const type = types?.find(type => type !== 'null') ?? (types ? 'null' :
      parts.some(part => ['properties', 'additionalProperties', 'patternProperties', 'required'].some(key => part.schema[key] !== undefined)) ? 'object' :
        parts.some(part => part.schema['items'] !== undefined) ? 'array' : undefined)
    if (type === 'object') {
      const properties = new Map<string, string[]>()
      const additional: string[] = []
      const patterns: string[] = []
      let allowAdditional = true
      for (const part of parts) {
        const declared = part.schema['properties']
        if (isJsonObject(declared)) for (const key of Object.keys(declared)) {
          if (properties.size >= maxNodes) break
          const paths = properties.get(key) ?? []
          paths.push(schemaPointer(schemaPointer(part.path, 'properties'), key))
          properties.set(key, paths)
        }
        if (Array.isArray(part.schema['required'])) for (const key of part.schema['required']) {
          if (properties.size >= maxNodes) break
          if (typeof key === 'string' && !properties.has(key)) properties.set(key, [])
        }
        if (part.schema['additionalProperties'] === false || part.schema['maxProperties'] === 0) allowAdditional = false
        if (isJsonObject(part.schema['additionalProperties']) || part.schema['additionalProperties'] === true) {
          additional.push(schemaPointer(part.path, 'additionalProperties'))
        }
        if (isJsonObject(part.schema['patternProperties'])) {
          const key = Object.keys(part.schema['patternProperties'])[0]
          if (key !== undefined) patterns.push(schemaPointer(schemaPointer(part.path, 'patternProperties'), key))
        }
      }
      const entries = new Map<string, unknown>()
      for (const [key, propertyPaths] of properties) {
        if (remaining <= 0) break
        for (const part of parts) {
          if (isJsonObject(part.schema['properties']) && Object.hasOwn(part.schema['properties'], key)) continue
          if (isJsonObject(part.schema['additionalProperties'])) propertyPaths.push(schemaPointer(part.path, 'additionalProperties'))
        }
        entries.set(key, build(propertyPaths, depth + 1, next))
      }
      if (allowAdditional && !entries.size && (additional.length || patterns.length) && remaining > 0) {
        entries.set('example_key', build(additional.length ? additional : patterns, depth + 1, next))
      }
      return Object.fromEntries(entries)
    }
    if (type === 'array') {
      if (parts.some(part => part.schema['maxItems'] === 0 || part.schema['items'] === false ||
        (Array.isArray(part.schema['items']) && !part.schema['items'].length && part.schema['additionalItems'] === false))) return []
      const itemPaths = parts.flatMap(part => {
        const items = part.schema['items']
        const path = schemaPointer(part.path, 'items')
        return Array.isArray(items) ? (items.length ? [schemaPointer(path, '0')] :
          isJsonObject(part.schema['additionalItems']) ? [schemaPointer(part.path, 'additionalItems')] : []) :
          isJsonObject(items) ? [path] : []
      })
      return remaining > 0 ? [build(itemPaths, depth + 1, next)] : []
    }
    if (type === 'boolean') return false
    if (type === 'null') return null
    if (type === 'string') {
      const formats: Record<string, string> = { 'date-time': '2000-01-01T00:00:00Z', date: '2000-01-01', time: '00:00:00',
        duration: '00:00:00', 'time-span': '00:00:00', uuid: '00000000-0000-0000-0000-000000000000',
        guid: '00000000-0000-0000-0000-000000000000', email: 'user@example.com', hostname: 'example.com',
        uri: 'https://example.com', url: 'https://example.com', ipv4: '127.0.0.1', ipv6: '::1' }
      const format = parts.find(part => typeof part.schema['format'] === 'string')?.schema['format'] as string | undefined
      let value = format && Object.hasOwn(formats, format) ? formats[format] : ''
      let minimum = 0
      let maximum = maxStringLength
      for (const part of parts) {
        if (typeof part.schema['minLength'] === 'number' && Number.isFinite(part.schema['minLength'])) minimum = Math.max(minimum, part.schema['minLength'])
        if (typeof part.schema['maxLength'] === 'number' && Number.isFinite(part.schema['maxLength'])) maximum = Math.min(maximum, part.schema['maxLength'])
      }
      value = value.padEnd(Math.min(maxStringLength, Math.ceil(minimum)), 'x')
      return value.slice(0, Math.max(0, maximum))
    }
    if (type === 'integer' || type === 'number') {
      let minimum = -Number.MAX_SAFE_INTEGER
      let maximum = Number.MAX_SAFE_INTEGER
      let step = 1
      let exclusiveMinimum = false
      let exclusiveMaximum = false
      for (const part of parts) {
        const node = part.schema
        if (typeof node['multipleOf'] === 'number' && Number.isFinite(node['multipleOf']) && node['multipleOf'] > 0) step = node['multipleOf']
        if (typeof node['minimum'] === 'number' && node['minimum'] >= minimum) {
          exclusiveMinimum = node['exclusiveMinimum'] === true || (node['minimum'] === minimum && exclusiveMinimum)
          minimum = node['minimum']
        }
        if (typeof node['maximum'] === 'number' && node['maximum'] <= maximum) {
          exclusiveMaximum = node['exclusiveMaximum'] === true || (node['maximum'] === maximum && exclusiveMaximum)
          maximum = node['maximum']
        }
      }
      const value = Math.min(maximum, Math.max(minimum, 0))
      const candidates = [value,
        (exclusiveMinimum && value === minimum ? Math.floor(value / step) + 1 : Math.ceil(value / step)) * step,
        (exclusiveMaximum && value === maximum ? Math.ceil(value / step) - 1 : Math.floor(value / step)) * step,
        type === 'integer' ? Math.ceil(minimum) : minimum / 2 + maximum / 2]
      for (const candidate of candidates) if (accepts(candidate)) return candidate
      const fallback = type === 'integer' ? Math.ceil(value) : value
      return Number.isFinite(fallback) && Math.abs(fallback) <= Number.MAX_SAFE_INTEGER ? (fallback || 0) : 0
    }
    return null
  }
  return build(['#'], 0, new Set())
}

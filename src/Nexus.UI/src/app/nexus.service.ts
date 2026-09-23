import { Injectable, signal } from '@angular/core'
import { NexusClient, type BufferProvider } from '@nexus-api/_client'
import * as V1 from '@nexus-api/V1'
import * as V2 from '@nexus-api/V2'

export type CatalogNode = V1.CatalogInfo & {
  nodeKey: string
  depth: number
  parentId: string
  isFake: boolean
  groupedChildren?: V1.CatalogInfo[]
}

export type PreparedCatalogNode = Omit<CatalogNode, 'depth' | 'parentId'>

export type ResourceRow = {
  catalogId: string
  id: string
  path: string
  description: string
  warning?: string
  unit: string
  groups: string[]
  properties: Record<string, unknown> | null
  representations: V1.Representation[]
}

export type WriterOption = {
  type?: string
  label?: string
  default?: unknown
  items?: Record<string, string>
  minimum?: number
  maximum?: number
}

export type WriterDescription = V1.ExtensionDescription & {
  additionalInformation?: {
    label?: string
    options?: Record<string, WriterOption>
  }
}

export type CatalogBundle = {
  catalog: V1.ResourceCatalog
  timeRange?: V1.CatalogTimeRange
  metadata?: V1.CatalogMetadata
  attachments: string[]
}

export type SessionOverview = {
  system: V1.SystemResponse
  me: V1.MeResponse
  writers: WriterDescription[]
  jobs: V1.Job[]
  roots: V1.CatalogInfo[]
}

@Injectable({ providedIn: 'root' })
export class NexusService {
  readonly endpoint = globalThis.location?.origin ?? 'http://localhost:4200'
  readonly apiAvailable = signal(false)
  readonly system = signal<V1.SystemResponse | null>(null)
  readonly currentUser = signal<V1.MeResponse | null>(null)
  private readonly client = new NexusClient(this.endpoint)
  readonly v1 = new V1.V1(this.invoke.bind(this))
  readonly v2 = new V2.V2(this.invoke.bind(this))

  async getCatalogChildren(catalogId = '/') {
    const children = await this.v1.catalogs.getChildCatalogInfos(catalogId)
    this.apiAvailable.set(true)
    return [...children].sort((a, b) => (a.id ?? '').localeCompare(b.id ?? ''))
  }

  async getCatalogBundle(catalogId: string): Promise<CatalogBundle> {
    const [catalog, timeRange, metadata, attachments] = await Promise.all([
      this.v1.catalogs.get(catalogId),
      this.v1.catalogs.getTimeRange(catalogId).catch(() => undefined),
      this.v1.catalogs.getMetadata(catalogId).catch(() => undefined),
      this.v1.catalogs.getAttachments(catalogId).catch(() => [] as string[]),
    ])

    this.apiAvailable.set(true)
    return { catalog, timeRange, metadata, attachments }
  }

  async getCatalogLicense(catalogId: string) {
    const license = await this.v1.catalogs.getLicense(catalogId)
    this.apiAvailable.set(true)
    return license ?? ''
  }

  async acceptCatalogLicense(catalogId: string) {
    await this.v1.catalogs.acceptLicense(catalogId)
    this.apiAvailable.set(true)
  }

  async uploadCatalogAttachment(catalogId: string, attachmentId: string, content: BodyInit) {
    await this.v1.catalogs.uploadAttachment(catalogId, attachmentId, content)
    this.apiAvailable.set(true)
  }

  async deleteCatalogAttachment(catalogId: string, attachmentId: string) {
    await this.v1.catalogs.deleteAttachment(catalogId, attachmentId)
    this.apiAvailable.set(true)
  }

  async getSessionOverview(): Promise<SessionOverview> {
    const [system, me, writers, jobs, roots] = await Promise.all([
      this.v1.system.get().then(system => {
        this.system.set(system)
        return system
      }),
      this.v1.users.getMe().then(me => {
        this.currentUser.set(me)
        return me
      }),
      this.v1.writers.getDescriptions() as Promise<WriterDescription[]>,
      this.v1.jobs.getJobs(),
      this.getCatalogChildren('/'),
    ])

    this.apiAvailable.set(true)
    return { system, me, writers, jobs, roots }
  }

  async getPersonalAccessTokens() {
    const tokens = await this.v1.users.getTokens()
    this.apiAvailable.set(true)
    return tokens
  }

  async createPersonalAccessToken(token: V1.PersonalAccessToken) {
    const value = await this.v1.users.createToken(token)
    this.apiAvailable.set(true)
    return value
  }

  async deletePersonalAccessToken(tokenId: string) {
    await this.v1.users.deleteToken(tokenId)
    this.apiAvailable.set(true)
  }

  async exportResources(parameters: V2.ExportParameters) {
    return this.v2.jobs.export(parameters)
  }

  async loadResources(
    begin: string,
    end: string,
    resourcePaths: string[],
    precision: V2.Precision,
    onProgress?: ((progress: number) => void) | undefined,
    signal?: AbortSignal,
  ) {
    const result = await this.client.load(begin, end, resourcePaths, precision, onProgress, signal)
    this.apiAvailable.set(true)
    return result
  }

  async loadResourcesIntoBuffers(
    begin: string,
    end: string,
    resourcePaths: string[],
    precision: V2.Precision,
    bufferProvider: BufferProvider,
    onProgress?: ((progress: number) => void) | undefined,
    signal?: AbortSignal,
  ) {
    const result = await this.client.load(begin, end, resourcePaths, precision, bufferProvider, onProgress, signal)
    this.apiAvailable.set(true)
    return result
  }

  private async invoke<T>(method: string, url: string, accept?: string, contentType?: string, body?: BodyInit | null, signal?: AbortSignal): Promise<T> {
    const headers = new Headers()
    if (accept) headers.set('Accept', accept)
    if (contentType) headers.set('Content-Type', contentType)

    const response = await fetch(`${this.endpoint}${url}`, { method, headers, body, signal })
    if (!response.ok) throw new Error(`Nexus request failed: ${response.status} ${response.statusText}`)

    if (accept === 'application/octet-stream' || accept === 'application/vnd.apache.arrow.stream') {
      return response as T
    }

    const text = await response.text()
    if (!text) return undefined as T

    // Reject unrepresentable configuration numbers before JSON.parse can round a saved value.
    if (method === 'GET' && url.split('?')[0] === '/api/v1/sources/pipelines') {
      const { parseJsonSafely } = await import('./json-schema')
      const parsed = parseJsonSafely(text)
      if (!parsed.valid) throw new Error(`Cannot safely edit these pipelines: ${parsed.errors.join(' ')}`)
      return parsed.value as T
    }

    return accept?.includes('json') ? JSON.parse(text) as T : text as T
  }
}

export function prepareChildCatalogs(parentId: string, childInfos: V1.CatalogInfo[]): PreparedCatalogNode[] {
  const normalizedParentId = parentId === '/' ? '' : parentId
  const groups = new Map<string, V1.CatalogInfo[]>()

  for (const info of childInfos) {
    if (!(info.isReleased && info.isVisible)) continue
    const remainder = (info.id ?? '').slice(normalizedParentId.length)
    const nextSegment = remainder.split('/').filter(Boolean)[0] ?? ''
    groups.set(nextSegment, [...(groups.get(nextSegment) ?? []), info])
  }

  const result: PreparedCatalogNode[] = []
  for (const [segment, group] of groups) {
    if (group.length > 1) {
      const fakeId = `${normalizedParentId}/${segment}`
      result.push({
        nodeKey: `fake:${normalizedParentId || '/'}:${fakeId}`,
        id: fakeId,
        title: null,
        contact: null,
        readme: null,
        license: null,
        isReadable: true,
        isWritable: false,
        isReleased: true,
        isVisible: true,
        packageReferenceIds: [],
        pipelineInfo: { id: '', types: [], infoUrls: [] },
        isFake: true,
        groupedChildren: group,
      })
    } else {
      result.push({ ...group[0], nodeKey: `real:${group[0].id ?? ''}`, isFake: false })
    }
  }

  return result.sort((a, b) => (a.id ?? '').localeCompare(b.id ?? ''))
}

export function mapResources(catalog: V1.ResourceCatalog | undefined): ResourceRow[] {
  const catalogId = catalog?.id ?? '/'
  return (catalog?.resources ?? []).map((resource) => {
    const properties = resource.properties

    return {
      catalogId,
      id: resource.id ?? 'unnamed-resource',
      path: `${catalogId}/${resource.id ?? ''}`.replace(/\/+/g, '/'),
      description: getString(properties, 'description') ?? '',
      warning: getString(properties, 'warning') ?? '',
      unit: getString(properties, 'unit') ?? '',
      groups: getStringArray(properties, 'groups'),
      properties: properties ?? null,
      representations: resource.representations ?? [],
    }
  })
}

export function buildExportParameters(
  begin: string,
  end: string,
  filePeriod: string,
  writer: WriterDescription | undefined,
  resourcePaths: string[],
  configuration: Record<string, unknown>,
  precision: V2.Precision,
): V2.ExportParameters {
  return {
    begin,
    end,
    filePeriod,
    type: writer?.type ?? null,
    resourcePaths,
    configuration,
    precision,
  }
}

function getString(record: Record<string, unknown> | null | undefined, key: string) {
  const value = record?.[key]
  return typeof value === 'string' ? value : undefined
}

function getStringArray(record: Record<string, unknown> | null | undefined, key: string) {
  const value = record?.[key]
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []
}

export { V1, V2 }

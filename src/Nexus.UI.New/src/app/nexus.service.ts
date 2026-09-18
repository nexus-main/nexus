import { Injectable, signal } from '@angular/core'
import { NexusClient } from '@nexus-api/_client'
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
  me: V1.MeResponse
  writers: WriterDescription[]
  jobs: V1.Job[]
  roots: V1.CatalogInfo[]
}

export const fallbackCatalogInfos: V1.CatalogInfo[] = [
  {
    id: '/SCADA_OLD',
    title: 'Messdaten der HLB SPS',
    isReadable: true,
    isWritable: false,
    isReleased: true,
    isVisible: true,
    isOwner: true,
    pipelineInfo: { types: ['IwesNexus.SimpleHdf5'] },
  },
  {
    id: '/STIESDAL/SHORT_TEST_CAMPAIGN_MADE_202501/HBM',
    title: 'Short campaign HBM',
    isReadable: true,
    isWritable: false,
    isReleased: true,
    isVisible: true,
    isOwner: true,
    pipelineInfo: { types: ['IwesNexus.PerceptionPnrf'] },
  },
  {
    id: '/SAMPLE/LOCAL',
    title: 'Simulates a local catalog',
    isReadable: true,
    isWritable: false,
    isReleased: true,
    isVisible: true,
    isOwner: false,
    pipelineInfo: { types: ['Nexus.Sources.Sample'] },
  },
]

export const fallbackResources: ResourceRow[] = [
  {
    catalogId: '/SAMPLE/LOCAL',
    id: 'T1',
    path: '/SAMPLE/LOCAL/T1',
    description: 'Test Resource A',
    unit: 'degC',
    groups: ['Group 1'],
    representations: [{ dataType: V1.NexusDataType.Float32, samplePeriod: '00:00:01' }],
  },
  {
    catalogId: '/SAMPLE/LOCAL',
    id: 'unix_time1',
    path: '/SAMPLE/LOCAL/unix_time1',
    description: 'High-resolution timestamp channel',
    unit: '-',
    groups: ['Group 2'],
    representations: [{ dataType: V1.NexusDataType.Float64, samplePeriod: '00:00:00.0400000' }],
  },
  {
    catalogId: '/SAMPLE/LOCAL',
    id: 'V1',
    path: '/SAMPLE/LOCAL/V1',
    description: 'Test Resource B',
    unit: 'm/s',
    groups: ['Group 1'],
    representations: [{ dataType: V1.NexusDataType.Float32, samplePeriod: '00:00:01' }],
  },
]

export const fallbackWriters: WriterDescription[] = [
  {
    type: 'Nexus.Writers.Csv',
    description: 'Exports comma-separated values following the frictionless data standard',
    additionalInformation: {
      label: 'CSV + Schema (*.csv)',
      options: {
        'row-index-format': {
          type: 'select',
          label: 'Row index format',
          default: 'excel',
          items: { excel: 'Excel time', index: 'Index-based', unix: 'Unix time', 'iso-8601': 'ISO 8601' },
        },
        'significant-figures': {
          type: 'input-integer',
          label: 'Significant figures',
          default: 4,
          minimum: 0,
          maximum: 30,
        },
      },
    },
  },
  { type: 'Nexus.Writers.Hdf5', description: 'Store data in HDF5.', additionalInformation: { label: 'HDF5 1.10 (*.h5)' } },
  { type: 'Nexus.Writers.Mat73', description: 'Store data in Matlab v7.3.', additionalInformation: { label: 'Matlab v7.3 (*.mat)' } },
]

@Injectable({ providedIn: 'root' })
export class NexusService {
  readonly endpoint = globalThis.location?.origin ?? 'http://localhost:4200'
  readonly apiAvailable = signal(false)
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

  async getSessionOverview(): Promise<SessionOverview> {
    const [me, writers, jobs, roots] = await Promise.all([
      this.v1.users.getMe().then(me => {
        this.currentUser.set(me)
        return me
      }),
      this.v1.writers.getDescriptions() as Promise<WriterDescription[]>,
      this.v1.jobs.getJobs(),
      this.getCatalogChildren('/'),
    ])

    this.apiAvailable.set(true)
    return { me, writers, jobs, roots }
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
    if (!((info.isReleased && info.isVisible) || info.isOwner)) continue
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
        isOwner: false,
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

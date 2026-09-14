import { NexusClient, V1, V2 } from '@nexus-api'

export type CatalogNode = V1.CatalogInfo & {
  depth: number
  parentId: string
}

export type ResourceRow = {
  catalogId: string
  id: string
  path: string
  description: string
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

declare const __NEXUS_PROXY_ENABLED__: boolean
declare const __NEXUS_ENDPOINT__: string

const configuredEndpoint = import.meta.env.VITE_NEXUS_ENDPOINT as string | undefined

export const nexusEndpoint = __NEXUS_ENDPOINT__ || configuredEndpoint?.replace(/\/$/, '') || 'https://nexus.hlb.iwes.fraunhofer.de'
export const hasConfiguredToken = __NEXUS_PROXY_ENABLED__

export function createNexusClient() {
  const clientBaseUrl = __NEXUS_PROXY_ENABLED__ ? window.location.origin : nexusEndpoint
  return new NexusClient(clientBaseUrl)
}

export const nexusClient = createNexusClient()

export async function getCatalogChildren(catalogId = '/') {
  const children = await nexusClient.v1.catalogs.getChildCatalogInfos(catalogId)
  return [...children].sort((a, b) => (a.id ?? '').localeCompare(b.id ?? ''))
}

export async function getCatalogBundle(catalogId: string) {
  const [catalog, timeRange, metadata, attachments] = await Promise.all([
    nexusClient.v1.catalogs.get(catalogId),
    nexusClient.v1.catalogs.getTimeRange(catalogId).catch(() => undefined),
    nexusClient.v1.catalogs.getMetadata(catalogId).catch(() => undefined),
    nexusClient.v1.catalogs.getAttachments(catalogId).catch(() => [] as string[]),
  ])

  return { catalog, timeRange, metadata, attachments }
}

export async function getSessionOverview() {
  const [me, writers, jobs, roots] = await Promise.all([
    nexusClient.v1.users.getMe(),
    nexusClient.v1.writers.getDescriptions() as Promise<WriterDescription[]>,
    nexusClient.v1.jobs.getJobs(),
    getCatalogChildren('/'),
  ])

  return { me, writers, jobs, roots }
}

export function mapResources(catalog: V1.ResourceCatalog | undefined): ResourceRow[] {
  const catalogId = catalog?.id ?? '/'
  return (catalog?.resources ?? []).map((resource) => {
    const properties = resource.properties
    const description = getString(properties, 'description') ?? 'No description supplied'
    const unit = getString(properties, 'unit') ?? '-'
    const groups = getStringArray(properties, 'groups')

    return {
      catalogId,
      id: resource.id ?? 'unnamed-resource',
      path: `${catalogId}/${resource.id ?? ''}`.replace(/\/+/g, '/'),
      description,
      unit,
      groups,
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

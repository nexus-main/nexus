import type { CatalogMetadata } from '@nexus-api/V1'
import { formatPeriod } from './resource-selection.ts'
import type { RepresentationRow } from './resource-selection.ts'

export type MetadataField = 'unit' | 'description' | 'warning'
export type MetadataDrafts = Record<string, Partial<Record<MetadataField, string>>>

export function mergeResourceMetadata(metadata: CatalogMetadata, catalogId: string, drafts: MetadataDrafts): CatalogMetadata {
  const overrides = { ...metadata.overrides, id: catalogId }

  for (const [id, draft] of Object.entries(drafts)) {
    const fields = (['unit', 'description', 'warning'] as const).filter((field) => draft[field] !== undefined)
    if (!fields.length) continue
    const index = overrides.resources?.findIndex((resource) => resource.id === id) ?? -1
    if (index < 0 && fields.every((field) => !draft[field]!.trim())) continue
    const resource = index < 0 ? { id } : overrides.resources![index]
    const properties = { ...resource.properties }

    for (const field of fields) {
      const value = draft[field]!
      if (value.trim()) properties[field] = value
      else delete properties[field]
    }

    const resources = [...(overrides.resources ?? [])]
    if (index < 0) resources.push({ ...resource, properties })
    else resources[index] = { ...resource, properties }
    overrides.resources = resources
  }

  return { ...metadata, overrides }
}

export function groupResourceRows(rows: RepresentationRow[], search: string): {
  key: string; name: string; rows: RepresentationRow[]; resourceCount: number
}[] {
  const query = search.trim().toLowerCase()
  const groups = new Map<string, { key: string; name: string; rows: RepresentationRow[]; resourceCount: number }>()
  const compare = (a: string, b: string) => a < b ? -1 : a > b ? 1 : 0

  for (const row of rows) {
    const labels = [...new Set(row.groups.filter((label) => label.trim()))]
    if (query && ![row.catalogId, row.id, row.path, ...labels, row.unit, row.description,
      row.warning, formatPeriod(row.basePeriod),
    ].join('\n').toLowerCase().includes(query)) continue

    for (const label of labels.length ? labels : [null]) {
      // Real labels always have a prefix, including labels named "Ungrouped" or "ungrouped".
      const key = label === null ? 'ungrouped' : `group:${label}`
      let group = groups.get(key)
      if (!group) {
        group = { key, name: label ?? 'Ungrouped', rows: [], resourceCount: 0 }
        groups.set(key, group)
      }
      group.rows.push(row)
    }
  }

  for (const group of groups.values()) {
    group.rows.sort((a, b) => compare(a.id, b.id) || compare(a.catalogId, b.catalogId)
      || compare(a.path, b.path) || (a.basePeriod < b.basePeriod ? -1 : a.basePeriod > b.basePeriod ? 1 : 0)
      || compare(a.key, b.key))
    group.resourceCount = new Set(group.rows.map((row) => JSON.stringify([row.catalogId, row.path]))).size
  }

  return [...groups.values()].sort((a, b) => compare(a.name, b.name) || compare(a.key, b.key))
}

import { CommonModule } from '@angular/common'
import { Component, HostListener, computed, effect, inject, signal } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { LucideFolder } from '@lucide/angular'
import { BrowserStorageService } from './browser-storage.service'
import { AppHeaderComponent } from './components/app-header.component'
import { ExportComposerComponent } from './components/export-composer.component'
import { MarkdownPipe } from './markdown.pipe'
import {
  CatalogBundle,
  CatalogNode,
  NexusService,
  ResourceRow,
  SessionOverview,
  V1,
  V2,
  WriterDescription,
  buildExportParameters,
  fallbackCatalogInfos,
  fallbackResources,
  fallbackWriters,
  mapResources,
  prepareChildCatalogs,
} from './nexus.service'
import { abbreviateMiddle, compactPath, formatNumber, getStringProperty, lastSegment } from './utils'

const defaultCatalogId = '/SAMPLE/LOCAL'
const catalogExpansionStorageKey = 'nexus.catalog.expandedNodeKeys'

const quickRanges = [
  { label: 'Last 10 min', begin: '-PT10M', end: 'now' },
  { label: 'Last hour', begin: '-PT1H', end: 'now' },
  { label: 'Campaign day', begin: '2025-01-01T00:00:00Z', end: '2025-01-02T00:00:00Z' },
]

type SelectedResourceGroup = {
  catalogId: string
  resources: ResourceRow[]
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule, MarkdownPipe, LucideFolder, AppHeaderComponent, ExportComposerComponent],
  templateUrl: './app.component.html',
})
export class AppComponent {
  private readonly nexus = inject(NexusService)
  private readonly storage = inject(BrowserStorageService)

  readonly selectedCatalogId = signal(getSelectedCatalogIdFromUrl())
  readonly selectedCatalogNodeKey = signal(getRealCatalogNodeKey(getSelectedCatalogIdFromUrl()))
  readonly expandedCatalogNodeKeys = signal<ReadonlySet<string>>(getInitialExpandedCatalogNodeKeys(this.storage, getSelectedCatalogIdFromUrl()))
  readonly catalogSearch = signal('')
  readonly resourceSearch = signal('')
  readonly selectedResourceRows = signal<ReadonlyMap<string, ResourceRow>>(new Map())
  readonly activeResourcePath = signal('/SAMPLE/LOCAL/T1')
  readonly isExportOpen = signal(false)
  readonly isReadmeOpen = signal(false)
  readonly isMobileCatalogOpen = signal(false)
  readonly activeSidebarTab = signal<'catalogs' | 'selectedResources'>('catalogs')
  readonly overviewLoading = signal(true)
  readonly catalogLoading = signal(false)
  readonly overviewError = signal<unknown>(null)
  readonly catalogError = signal<unknown>(null)
  readonly overview = signal<SessionOverview | null>(null)
  readonly childMap = signal<ReadonlyMap<string, V1.CatalogInfo[]>>(new Map())
  readonly selectedBundle = signal<CatalogBundle | null>(null)
  readonly exportBegin = signal(quickRanges[0].begin)
  readonly exportEnd = signal(quickRanges[0].end)
  readonly exportFilePeriod = signal('PT0S')
  readonly selectedWriterType = signal('Nexus.Writers.Csv')
  readonly exportConfiguration = signal<Record<string, unknown>>({ 'row-index-format': 'excel', 'significant-figures': 4 })
  readonly exportPrecision = signal<V2.Precision>(V2.Precision.Float32)
  readonly exportStatus = signal('')
  readonly exportBusy = signal(false)

  readonly quickRanges = quickRanges
  readonly apiAvailable = this.nexus.apiAvailable.asReadonly()

  readonly rootCatalogInfos = computed(() => this.overview()?.roots ?? fallbackCatalogInfos)
  readonly writerDescriptions = computed(() => this.overview()?.writers ?? fallbackWriters)
  readonly jobs = computed(() => this.overview()?.jobs ?? [])
  readonly userName = computed(() => this.overview()?.me.user?.name ?? 'Prototype user')
  readonly endpointHost = computed(() => new URL(this.nexus.endpoint).host)
  readonly userInitials = computed(() => getInitials(this.userName()))

  readonly catalogNodes = computed(() => {
    const nodes: CatalogNode[] = []
    const expandedNodeKeys = this.expandedCatalogNodeKeys()
    const childMap = this.childMap()
    const appendPreparedNodes = (prepared: ReturnType<typeof prepareChildCatalogs>, parentId: string, depth: number) => {
      for (const node of prepared) {
        const catalogNode: CatalogNode = { ...node, depth, parentId }
        nodes.push(catalogNode)

        if (node.id && expandedNodeKeys.has(node.nodeKey)) {
          const children = node.isFake && node.groupedChildren ? node.groupedChildren : (childMap.get(node.id) ?? [])
          appendPreparedNodes(prepareChildCatalogs(node.id, children), node.id, depth + 1)
        }
      }
    }

    appendPreparedNodes(prepareChildCatalogs('/', this.rootCatalogInfos()), '/', 0)
    return nodes
  })

  readonly filteredCatalogNodes = computed(() => {
    const term = this.catalogSearch().trim().toLowerCase()
    if (!term) return this.catalogNodes()

    return this.catalogNodes().filter((node) => `${node.id ?? ''} ${node.title ?? ''} ${node.pipelineInfo?.types?.join(' ') ?? ''}`.toLowerCase().includes(term))
  })

  readonly selectedNode = computed(() => this.catalogNodes().find((node) => node.nodeKey === this.selectedCatalogNodeKey()))
  readonly isSelectedFake = computed(() => this.selectedNode()?.isFake ?? this.selectedCatalogNodeKey().startsWith('fake:'))
  readonly selectedCatalog = computed(() => this.selectedBundle()?.catalog)
  readonly selectedCatalogTitle = computed(() => getStringProperty(this.selectedCatalog()?.properties, 'title') ?? this.selectedNode()?.title ?? lastSegment(this.selectedCatalogId()))
  readonly selectedCatalogReadme = computed(() => getStringProperty(this.selectedCatalog()?.properties, 'readme') ?? this.selectedNode()?.readme ?? '')
  readonly selectedCatalogRange = computed(() => formatRange(this.selectedBundle()?.timeRange))

  readonly resourceRows = computed(() => {
    if (!this.apiAvailable()) return fallbackResources
    if (this.isSelectedFake()) return []
    return mapResources(this.selectedCatalog())
  })

  readonly filteredResources = computed(() => {
    const term = this.resourceSearch().trim().toLowerCase()
    const rows = term
      ? this.resourceRows().filter((row) => `${row.id} ${row.path} ${row.description} ${row.groups.join(' ')} ${row.unit}`.toLowerCase().includes(term))
      : [...this.resourceRows()]

    return rows.sort((a, b) => a.id.localeCompare(b.id))
  })

  readonly activeResource = computed<ResourceRow | undefined>(() => this.resourceRows().find((resource) => resource.path === this.activeResourcePath()) ?? this.filteredResources()[0])
  readonly selectedResourcePaths = computed(() => new Set(this.selectedResourceRows().keys()))
  readonly selectedResources = computed(() => [...this.selectedResourceRows().values()].sort(compareResources))
  readonly groupedSelectedResources = computed<SelectedResourceGroup[]>(() => {
    const groups = new Map<string, ResourceRow[]>()
    for (const resource of this.selectedResources()) groups.set(resource.catalogId, [...(groups.get(resource.catalogId) ?? []), resource])
    return [...groups.entries()].map(([catalogId, resources]) => ({ catalogId, resources }))
  })
  readonly selectedDataTypes = computed(() => new Set(this.selectedResources().flatMap((resource) => resource.representations.map((rep) => rep.dataType).filter(Boolean))))
  readonly groupCount = computed(() => new Set(this.resourceRows().flatMap((resource) => resource.groups)).size)
  readonly selectedWriter = computed(() => this.writerDescriptions().find((writer) => writer.type === this.selectedWriterType()) ?? this.writerDescriptions()[0])
  readonly writerOptions = computed(() => Object.entries(this.selectedWriter()?.additionalInformation?.options ?? {}))
  readonly previewResources = computed(() => this.selectedResources().length > 0 ? this.selectedResources() : this.activeResource() ? [this.activeResource()!] : [])
  readonly exportPreview = computed(() => buildExportParameters(
    this.exportBegin(),
    this.exportEnd(),
    this.exportFilePeriod(),
    this.selectedWriter(),
    this.selectedResources().map((resource) => resource.path),
    this.exportConfiguration(),
    this.exportPrecision(),
  ))

  constructor() {
    writeSelectedCatalogToUrl(this.selectedCatalogId(), true)
    void this.loadOverview()

    effect(() => {
      const catalogId = this.selectedCatalogId()
      const isFake = this.isSelectedFake()
      const apiAvailable = this.apiAvailable()
      void this.loadSelectedCatalog(catalogId, isFake, apiAvailable)
    })

    effect(() => {
      this.storage.setJson(catalogExpansionStorageKey, [...this.expandedCatalogNodeKeys()].sort())
    })

    effect(() => {
      const catalogId = this.selectedCatalogId()
      const apiAvailable = this.apiAvailable()
      this.expandCatalogPath(catalogId)
      if (apiAvailable) void this.loadCatalogPathChildren(catalogId)
    })
  }

  @HostListener('window:popstate')
  onPopState() {
    const catalogId = getSelectedCatalogIdFromUrl()
    this.selectedCatalogId.set(catalogId)
    this.selectedCatalogNodeKey.set(getRealCatalogNodeKey(catalogId))
    this.expandCatalogPath(catalogId)
    this.isMobileCatalogOpen.set(false)
    this.activeResourcePath.set('')
  }

  async loadOverview() {
    this.overviewLoading.set(true)
    this.overviewError.set(null)
    try {
      this.overview.set(await this.nexus.getSessionOverview())
    } catch (error) {
      this.overviewError.set(error)
      this.nexus.apiAvailable.set(false)
    } finally {
      this.overviewLoading.set(false)
    }
  }

  async loadSelectedCatalog(catalogId: string, isFake: boolean, apiAvailable: boolean) {
    if (!catalogId || isFake || !apiAvailable) {
      this.selectedBundle.set(null)
      return
    }

    this.catalogLoading.set(true)
    this.catalogError.set(null)
    try {
      this.selectedBundle.set(await this.nexus.getCatalogBundle(catalogId))
    } catch (error) {
      this.catalogError.set(error)
      this.selectedBundle.set(null)
    } finally {
      this.catalogLoading.set(false)
    }
  }

  selectCatalog(catalog: CatalogNode) {
    if (catalog.isFake) {
      this.toggleExpanded(catalog)
      return
    }

    const catalogId = catalog.id ?? '/'
    this.selectedCatalogId.set(catalogId)
    this.selectedCatalogNodeKey.set(catalog.nodeKey)
    writeSelectedCatalogToUrl(catalogId)
    this.isMobileCatalogOpen.set(false)
    this.activeResourcePath.set('')
  }

  toggleExpanded(catalog: CatalogNode) {
    this.expandedCatalogNodeKeys.update((current) => toggleSetValue(current, catalog.nodeKey))

    if (!catalog.isFake && catalog.id) void this.loadChildren(catalog.id)
  }

  expandCatalogPath(catalogId: string) {
    this.expandedCatalogNodeKeys.update((current) => mergeSets(current, getCatalogPathNodeKeys(catalogId)))
  }

  async loadCatalogPathChildren(catalogId: string) {
    await Promise.all(getCatalogAncestorPaths(catalogId).map((path) => this.loadChildren(path)))
  }

  async loadChildren(catalogId: string) {
    if (!this.apiAvailable() || this.childMap().has(catalogId)) return

    try {
      const children = await this.nexus.getCatalogChildren(catalogId)
      this.childMap.update((current) => new Map(current).set(catalogId, children))
    } catch {
      this.childMap.update((current) => new Map(current).set(catalogId, []))
    }
  }

  toggleResource(resource: ResourceRow) {
    this.selectedResourceRows.update((current) => {
      const next = new Map(current)
      if (next.has(resource.path)) next.delete(resource.path)
      else next.set(resource.path, resource)
      return next
    })
    this.activeResourcePath.set(resource.path)
  }

  resourceSelected(resource: ResourceRow) {
    return this.selectedResourcePaths().has(resource.path)
  }

  activateResource(resource: ResourceRow) {
    this.activeResourcePath.set(resource.path)
  }

  applyQuickRange(range: { begin: string; end: string }) {
    this.exportBegin.set(range.begin)
    this.exportEnd.set(range.end)
  }

  updateConfig(key: string, value: unknown) {
    this.exportConfiguration.update((current) => ({ ...current, [key]: value }))
  }

  async createExportJob() {
    if (!this.selectedResources().length || !this.apiAvailable()) {
      this.exportStatus.set('Select at least one resource and connect to the Nexus API before creating an export job.')
      return
    }

    this.exportBusy.set(true)
    this.exportStatus.set('')
    try {
      const job = await this.nexus.exportResources(this.exportPreview())
      this.exportStatus.set(`Job created: ${job.id ?? 'pending'}`)
    } catch (error) {
      this.exportStatus.set(this.errorMessage(error))
    } finally {
      this.exportBusy.set(false)
    }
  }

  copyCatalogPath() {
    void navigator.clipboard?.writeText(this.selectedCatalogId())
  }

  compactPath(path: string | undefined, maxSegments = 3) {
    return compactPath(path, maxSegments)
  }

  lastSegment(path: string | undefined) {
    return lastSegment(path)
  }

  abbreviateMiddle(value: string, maxLength: number) {
    return abbreviateMiddle(value, maxLength)
  }

  formatNumber(value: number | undefined) {
    return formatNumber(value)
  }

  errorMessage(error: unknown) {
    return error instanceof Error ? error.message : 'The Nexus API request failed.'
  }
}

function getSelectedCatalogIdFromUrl() {
  const catalogId = new URLSearchParams(window.location.search).get('catalog')?.trim()
  return catalogId || defaultCatalogId
}

function compareResources(left: ResourceRow, right: ResourceRow) {
  return left.catalogId.localeCompare(right.catalogId) || left.id.localeCompare(right.id)
}

function getRealCatalogNodeKey(catalogId: string) {
  return `real:${catalogId}`
}

function getFakeCatalogNodeKey(parentId: string, catalogId: string) {
  return `fake:${parentId}:${catalogId}`
}

function getInitialExpandedCatalogNodeKeys(storage: BrowserStorageService, catalogId: string) {
  return mergeSets(new Set(getStoredCatalogNodeKeys(storage)), getCatalogPathNodeKeys(catalogId))
}

function getStoredCatalogNodeKeys(storage: BrowserStorageService) {
  const storedKeys = storage.getJson<unknown>(catalogExpansionStorageKey, [])
  return Array.isArray(storedKeys) ? storedKeys.filter((key): key is string => typeof key === 'string') : []
}

function getCatalogPathNodeKeys(catalogId: string) {
  const keys = new Set<string>()
  const ancestors = getCatalogAncestorPaths(catalogId)
  for (const path of ancestors) keys.add(getRealCatalogNodeKey(path))

  const segments = getCatalogSegments(catalogId)
  for (let index = 0; index < segments.length - 1; index += 1) {
    const parentPath = index === 0 ? '/' : `/${segments.slice(0, index).join('/')}`
    const path = `/${segments.slice(0, index + 1).join('/')}`
    keys.add(getFakeCatalogNodeKey(parentPath, path))
  }

  return keys
}

function getCatalogAncestorPaths(catalogId: string) {
  const segments = getCatalogSegments(catalogId)
  const ancestors: string[] = []
  for (let index = 1; index < segments.length; index += 1) ancestors.push(`/${segments.slice(0, index).join('/')}`)
  return ancestors
}

function getCatalogSegments(catalogId: string) {
  return catalogId.split('/').filter(Boolean)
}

function writeSelectedCatalogToUrl(catalogId: string, replace = false) {
  const url = new URL(window.location.href)
  if (url.searchParams.get('catalog') === catalogId) return

  url.searchParams.set('catalog', catalogId)
  window.history[replace ? 'replaceState' : 'pushState'](null, '', `${url.pathname}${url.search}${url.hash}`)
}

function toggleSetValue<T>(current: ReadonlySet<T>, value: T) {
  const next = new Set(current)
  if (next.has(value)) next.delete(value)
  else next.add(value)
  return next
}

function mergeSets<T>(current: ReadonlySet<T>, values: Iterable<T>) {
  const next = new Set(current)
  for (const value of values) next.add(value)
  return next
}

function getInitials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]?.toUpperCase()).join('') || 'NU'
}

function formatRange(timeRange: V1.CatalogTimeRange | undefined) {
  if (!timeRange?.begin || !timeRange.end) return ''
  if (timeRange.begin.startsWith('0001-01-01') && timeRange.end.startsWith('9999-12-31')) return ''

  const begin = formatRangeDate(timeRange.begin)
  const end = formatRangeDate(timeRange.end)
  return begin && end ? `${begin} -> ${end}` : begin || end
}

function formatRangeDate(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? value.slice(0, 10) : date.toISOString().slice(0, 10)
}

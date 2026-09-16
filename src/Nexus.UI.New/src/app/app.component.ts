import { CommonModule, DOCUMENT } from '@angular/common'
import { Component, HostListener, OnDestroy, computed, effect, inject, signal } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { LucideChartLine, LucideCopy, LucideExternalLink, LucideFileText, LucideX } from '@lucide/angular'
import { MenuItem } from 'primeng/api'
import { ButtonModule } from 'primeng/button'
import { CheckboxModule } from 'primeng/checkbox'
import { DialogModule } from 'primeng/dialog'
import { DrawerModule } from 'primeng/drawer'
import { InputTextModule } from 'primeng/inputtext'
import { MenuModule } from 'primeng/menu'
import { ProgressBarModule } from 'primeng/progressbar'
import { TableModule } from 'primeng/table'
import { TabsModule } from 'primeng/tabs'
import { DrawerPassThrough } from 'primeng/types/drawer'
import { BrowserStorageService } from './browser-storage.service'
import { VisualizationChartComponent } from './charts/visualization-chart.component'
import { VisualizationData, createVisualizationData, loadVisualizationData } from './charts/visualization-data'
import { dateTicks } from './resource-selection'
import { AppHeaderComponent } from './components/app-header.component'
import { CatalogTreeComponent } from './components/catalog-tree.component'
import { ExportComposerComponent } from './components/export-composer.component'
import { PinnedResourceComponent } from './components/pinned-resource.component'
import { PackageReferencesComponent } from './components/package-references.component'
import { RepresentationRow, ResourceSelection, RepresentationKind, StoredSelectionReference, alignRangeEndpoint, defaultKind, executionRangeError, formatPeriod, hydrateSelections, kindValid, parsePeriod, readSelectionState, representationRows, requestPath, selectionKey, storeSelectionReference, toTimeSpan } from './resource-selection'
import { MarkdownPipe } from './markdown.pipe'
import { RestoreFocusDirective } from './restore-focus.directive'
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
const selectedResourcesStorageKey = 'nexus.selectedResources'
const themeModeStorageKey = 'nexus.themeMode'
type ThemeMode = 'dark' | 'light'

const quickRanges = [
  { label: 'Last 10 min', begin: '-PT10M', end: 'now' },
  { label: 'Last hour', begin: '-PT1H', end: 'now' },
  { label: 'Campaign day', begin: '2025-01-01T00:00:00Z', end: '2025-01-02T00:00:00Z' },
]

const timeRangePresets = [
  { label: 'Last hour', kind: 'rolling', unit: 'hour', amount: 1 },
  { label: 'Last 24 hours', kind: 'rolling', unit: 'day', amount: 1 },
  { label: 'Last 7 days', kind: 'rolling', unit: 'day', amount: 7 },
  { label: 'Today so far', kind: 'calendarToNow', unit: 'day', amount: 0 },
  { label: 'Yesterday', kind: 'previousCalendar', unit: 'day', amount: 1 },
  { label: 'This week so far', kind: 'calendarToNow', unit: 'week', amount: 0 },
  { label: 'Previous week', kind: 'previousCalendar', unit: 'week', amount: 1 },
  { label: 'This month so far', kind: 'calendarToNow', unit: 'month', amount: 0 },
  { label: 'Previous month', kind: 'previousCalendar', unit: 'month', amount: 1 },
  { label: 'This year so far', kind: 'calendarToNow', unit: 'year', amount: 0 },
  { label: 'Previous year', kind: 'previousCalendar', unit: 'year', amount: 1 },
] as const

type TimeRangePreset = typeof timeRangePresets[number]

const defaultExportBegin = getUtcMidnightDaysAgo(2)
const defaultExportEnd = getUtcMidnightDaysAgo(1)

type SelectedResourceGroup = {
  catalogId: string
  resources: ResourceSelection[]
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, CheckboxModule, DialogModule, DrawerModule, InputTextModule, MenuModule, ProgressBarModule, TableModule, TabsModule, LucideChartLine, LucideCopy, LucideExternalLink, LucideFileText, LucideX, MarkdownPipe, RestoreFocusDirective, AppHeaderComponent, CatalogTreeComponent, ExportComposerComponent, PinnedResourceComponent, PackageReferencesComponent, VisualizationChartComponent],
  templateUrl: './app.component.html',
})
export class AppComponent implements OnDestroy {
  private readonly nexus = inject(NexusService)
  private readonly storage = inject(BrowserStorageService)
  private readonly document = inject(DOCUMENT)
  private readonly catalogBundleCache = new Map<string, CatalogBundle>()
  private readonly catalogBundleRequests = new Map<string, Promise<CatalogBundle>>()
  private readonly childRequests = new Map<string, Promise<void>>()
  private readonly storedSelectionState = readSelectionState(this.storage.getJson<unknown>(selectedResourcesStorageKey, null))
  private readonly selectionReferences = signal(this.storedSelectionState.selections)
  private readonly selectedResourcesRestored = signal(false)
  private catalogLoadGeneration = 0
  readonly selectionLoading = signal(true)
  readonly unresolvedSelections = signal<StoredSelectionReference[]>([])
  readonly pinnedCount = computed(() => this.selectionReferences().length)

  readonly selectedCatalogId = signal(getSelectedCatalogIdFromUrl())
  readonly selectedCatalogNodeKey = signal(getRealCatalogNodeKey(getSelectedCatalogIdFromUrl()))
  readonly activeCatalogDetailsOpen = signal(false)
  readonly expandedCatalogNodeKeys = signal<ReadonlySet<string>>(getInitialExpandedCatalogNodeKeys(this.storage, getSelectedCatalogIdFromUrl()))
  readonly searchCollapsedCatalogNodeKeys = signal<ReadonlySet<string>>(new Set())
  readonly catalogSearch = signal('')
  readonly resourceSearch = signal('')
  readonly selectedResourceRows = signal<ReadonlyMap<string, ResourceSelection>>(new Map())
  readonly activeResourcePath = signal('/SAMPLE/LOCAL/T1')
  readonly isExportOpen = signal(false)
  readonly isPackageReferencesOpen = signal(false)
  readonly isClearPinnedOpen = signal(false)
  readonly isReadmeOpen = signal(false)
  readonly isMobileCatalogOpen = signal(false)
  readonly visualizationOpen = signal(false)
  readonly wideLayout = signal(window.innerWidth >= 1536)
  readonly visualizationData = signal<VisualizationData | null>(null)
  readonly visualizationLoading = signal(false)
  readonly visualizationProgress = signal(0)
  readonly visualizationError = signal('')
  readonly visualizationBeginAtZero = signal(false)
  readonly visualizationCacheMiB = signal(2048)
  private visualizationController?: AbortController
  private readonly loadedVisualizationKey = signal('')
  readonly themeMode = signal<ThemeMode>(getInitialThemeMode(this.storage))
  readonly activeSidebarTab = signal<'catalogs' | 'selectedResources'>('catalogs')
  readonly overviewLoading = signal(true)
  readonly catalogLoading = signal(false)
  readonly overviewError = signal<unknown>(null)
  readonly catalogError = signal<unknown>(null)
  readonly overview = signal<SessionOverview | null>(null)
  readonly childMap = signal<ReadonlyMap<string, V1.CatalogInfo[]>>(new Map())
  readonly selectedCatalogInfo = signal<V1.CatalogInfo | null>(null)
  readonly selectedBundle = signal<CatalogBundle | null>(null)
  readonly exportBegin = signal(defaultExportBegin)
  readonly exportEnd = signal(defaultExportEnd)
  readonly samplePeriod = signal(parsePeriod(this.storedSelectionState.period)!)
  readonly periodDraft = signal(this.storedSelectionState.period)
  readonly automaticPeriod = signal(this.storedSelectionState.automaticPeriod)
  readonly periodError = computed(() => {
    const period = parsePeriod(this.periodDraft())
    return period === null || period <= 0n ? 'Enter a positive period, for example 100 ms, 1 s, or 10 min (100 ns minimum).' : ''
  })
  readonly exportFilePeriod = signal('PT0S')
  readonly selectedWriterType = signal('Nexus.Writers.Csv')
  readonly exportConfiguration = signal<Record<string, unknown>>({ 'row-index-format': 'excel', 'significant-figures': 4 })
  readonly exportPrecision = signal<V2.Precision>(V2.Precision.Float32)
  readonly exportStatus = signal('')
  readonly exportBusy = signal(false)

  readonly quickRanges = quickRanges
  readonly timeRangeMenuItems: MenuItem[] = timeRangePresets.flatMap((preset) => {
    const item: MenuItem = { label: preset.label, command: () => this.applyTimeRangePreset(preset) }
    return preset.kind === 'calendarToNow' ? [{ separator: true }, item] : [item]
  })
  readonly catalogDrawerPt: DrawerPassThrough = {
    root: {
      // Keep Escape local; the drawer's document listener also closes nested overlays.
      onkeydown: (event: KeyboardEvent) => {
        if (event.key !== 'Escape') return
        event.stopPropagation()
        this.isMobileCatalogOpen.set(false)
      },
    },
  }
  readonly apiAvailable = this.nexus.apiAvailable.asReadonly()

  readonly rootCatalogInfos = computed(() => this.overview()?.roots ?? fallbackCatalogInfos)
  readonly writerDescriptions = computed(() => this.overview()?.writers ?? fallbackWriters)
  readonly jobs = computed(() => this.overview()?.jobs ?? [])
  readonly userName = computed(() => this.nexus.currentUser()?.user?.name ?? 'Prototype user')
  readonly isAdministrator = computed(() => this.nexus.currentUser()?.user?.claims?.some(claim => claim.type === 'role' && claim.value === 'Administrator') ?? false)
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

  readonly searchableCatalogNodes = computed(() => {
    const nodes: CatalogNode[] = []
    const childMap = this.childMap()
    const appendPreparedNodes = (prepared: ReturnType<typeof prepareChildCatalogs>, parentId: string, depth: number) => {
      for (const node of prepared) {
        nodes.push({ ...node, depth, parentId })

        if (!node.id) continue

        const children = node.isFake && node.groupedChildren ? node.groupedChildren : childMap.get(node.id)
        if (children?.length) appendPreparedNodes(prepareChildCatalogs(node.id, children), node.id, depth + 1)
      }
    }

    appendPreparedNodes(prepareChildCatalogs('/', this.rootCatalogInfos()), '/', 0)
    return nodes
  })

  readonly filteredCatalogNodes = computed(() => {
    const term = this.catalogSearch().trim().toLowerCase()
    if (!term) return this.catalogNodes()

    const nodes = this.searchableCatalogNodes()
    const nodeById = new Map(nodes.flatMap((node) => node.id ? [[node.id, node] as const] : []))
    const includedNodeKeys = new Set<string>()
    const collapsedNodeKeys = this.searchCollapsedCatalogNodeKeys()

    for (const node of nodes) {
      if (!catalogNodeMatchesSearch(node, term)) continue

      let current: CatalogNode | undefined = node
      while (current && !includedNodeKeys.has(current.nodeKey)) {
        includedNodeKeys.add(current.nodeKey)
        current = current.parentId === '/' ? undefined : nodeById.get(current.parentId)
      }
    }

    return nodes.filter((node) => includedNodeKeys.has(node.nodeKey) && !hasCollapsedSearchAncestor(node, nodeById, collapsedNodeKeys))
  })

  readonly expandedFilteredNodeKeys = computed(() => new Set(this.filteredCatalogNodes().filter((node) => this.catalogNodeIsExpanded(node)).map((node) => node.nodeKey)))
  readonly expandableFilteredNodeKeys = computed(() => new Set(this.filteredCatalogNodes().filter((node) => this.catalogHasExpandableChildren(node)).map((node) => node.nodeKey)))

  readonly selectedNode = computed(() => this.catalogNodes().find((node) => node.nodeKey === this.selectedCatalogNodeKey()))
  readonly isSelectedFake = computed(() => this.selectedNode()?.isFake ?? this.selectedCatalogNodeKey().startsWith('fake:'))
  readonly selectedCatalog = computed(() => this.selectedBundle()?.catalog)
  readonly selectedCatalogTitle = computed(() => getStringProperty(this.selectedCatalog()?.properties, 'title') ?? this.selectedCatalogInfo()?.title ?? '')
  readonly selectedCatalogReadme = computed(() => getStringProperty(this.selectedCatalog()?.properties, 'readme') ?? this.selectedCatalogInfo()?.readme ?? this.selectedNode()?.readme ?? '')
  readonly selectedCatalogDisplayPath = computed(() => formatCatalogDisplayPath(this.selectedCatalogId()))
  readonly selectedCatalogRange = computed(() => formatRange(this.selectedBundle()?.timeRange))

  readonly resourceRows = computed(() => {
    if (!this.apiAvailable()) return representationRows(fallbackResources)
    if (this.isSelectedFake()) return []
    return representationRows(mapResources(this.selectedCatalog()))
  })

  readonly filteredResources = computed(() => {
    const term = this.resourceSearch().trim().toLowerCase()
    const rows = term
      ? this.resourceRows().filter((row) => `${row.id} ${row.path} ${row.description} ${row.groups.join(' ')} ${row.unit}`.toLowerCase().includes(term))
      : [...this.resourceRows()]

    return rows.sort((a, b) => a.id.localeCompare(b.id))
  })

  readonly activeResource = computed<RepresentationRow | undefined>(() => this.resourceRows().find((resource) => resource.key === this.activeResourcePath()) ?? this.filteredResources()[0])
  readonly selectedResourcePaths = computed(() => new Set(this.selectedResourceRows().keys()))
  readonly selectedResources = computed(() => [...this.selectedResourceRows().values()].sort(compareResources))
  readonly groupedSelectedResources = computed<SelectedResourceGroup[]>(() => {
    const groups = new Map<string, ResourceSelection[]>()
    for (const resource of this.selectedResources()) groups.set(resource.catalogId, [...(groups.get(resource.catalogId) ?? []), resource])
    return [...groups.entries()].map(([catalogId, resources]) => ({ catalogId, resources }))
  })
  readonly selectedDataTypes = computed(() => new Set(this.selectedResources().map((resource) => resource.representation.dataType)))
  readonly groupCount = computed(() => new Set(this.resourceRows().flatMap((resource) => resource.groups)).size)
  readonly selectedWriter = computed(() => this.writerDescriptions().find((writer) => writer.type === this.selectedWriterType()) ?? this.writerDescriptions()[0])
  readonly writerOptions = computed(() => Object.entries(this.selectedWriter()?.additionalInformation?.options ?? {}))
  readonly visualizationResources = computed(() => this.selectedResources().flatMap(resource => resource.kinds.map(kind => ({ id: resource.id, unit: resource.unit, kind, path: requestPath(resource, kind, this.samplePeriod()), valid: kindValid(kind, this.samplePeriod(), resource.basePeriod) }))))
  readonly exportBeginInput = computed(() => toDateTimeLocalValue(this.exportBegin()))
  readonly exportEndInput = computed(() => toDateTimeLocalValue(this.exportEnd()))
  readonly formattedSamplePeriod = computed(() => formatPeriod(this.samplePeriod()))
  readonly requestPaths = computed(() => this.visualizationResources().map(resource => resource.path))
  readonly selectionError = computed(() => {
    if (this.selectionLoading()) return 'Restoring pinned representations...'
    if (this.unresolvedSelections().length) return 'Some pinned representations could not be loaded. Retry or clear them before loading data.'
    if (this.periodError()) return this.periodError()
    if (!this.selectedResources().length) return 'Select at least one representation.'
    if (this.visualizationResources().some(resource => !resource.valid)) return 'Remove the invalid methods or choose a compatible Period.'
    if (this.selectedResources().some(resource => !this.parametersValid(resource))) return 'A selected representation requires parameter values that this UI cannot edit yet.'
    return executionRangeError(this.exportBegin(), this.exportEnd(), this.samplePeriod(), this.requestPaths().length)
  })
  readonly exportError = computed(() => {
    if (this.selectionError()) return this.selectionError()
    const period = parsePeriod(this.exportFilePeriod())
    if (period === null || period % this.samplePeriod() !== 0n) return 'File period must be zero or an integer multiple of Period.'
    if (!this.apiAvailable()) return 'Connect to the Nexus API before creating an export job.'
    return ''
  })
  readonly visualizationValidation = computed(() => this.selectionError() || (!this.apiAvailable() ? 'Connect to the Nexus API before visualizing data.' : ''))
  readonly visualizationKey = computed(() => JSON.stringify([this.exportBegin(), this.exportEnd(), this.samplePeriod().toString(), this.requestPaths()]))
  readonly visualizationStale = computed(() => !!this.visualizationData() && this.loadedVisualizationKey() !== this.visualizationKey())
  readonly exportPreview = computed(() => buildExportParameters(
    this.exportBegin(),
    this.exportEnd(),
    toTimeSpan(parsePeriod(this.exportFilePeriod()) ?? 0n),
    this.selectedWriter(),
    this.requestPaths(),
    this.exportConfiguration(),
    this.exportPrecision(),
  ))

  formatSamplePeriod(samplePeriod: string | null | undefined) {
    const ticks = parsePeriod(samplePeriod ?? '')
    return ticks === null ? 'no cadence' : formatPeriod(ticks)
  }

  setPeriod(value: string) {
    if (this.selectionLoading()) return
    this.periodDraft.set(value)
    const period = parsePeriod(value)
    if (period === null || period <= 0n || period === this.samplePeriod()) return
    this.samplePeriod.set(period)
    this.automaticPeriod.set(false)
  }

  normalizePeriod() {
    if (!this.periodError()) this.periodDraft.set(formatPeriod(this.samplePeriod()))
  }

  setCatalogSearch(value: string) {
    this.catalogSearch.set(value)
    this.searchCollapsedCatalogNodeKeys.set(new Set())
  }

  setSidebarTab(value: unknown) {
    if (value === 'catalogs' || value === 'selectedResources') this.activeSidebarTab.set(value)
  }

  applyTimeRangePreset(preset: TimeRangePreset) {
    const reference = new Date()
    let begin: Date
    let end: Date

    switch (preset.kind) {
      case 'rolling':
        end = reference
        begin = new Date(end)
        subtractUtcRange(begin, preset.unit, preset.amount)
        break
      case 'calendarToNow':
        end = reference
        begin = getUtcPeriodStart(reference, preset.unit)
        break
      case 'previousCalendar':
        end = getUtcPeriodStart(reference, preset.unit)
        begin = new Date(end)
        subtractUtcRange(begin, preset.unit, preset.amount)
        break
    }

    this.exportBegin.set(alignRangeEndpoint(begin.toISOString(), this.samplePeriod()))
    this.exportEnd.set(alignRangeEndpoint(end.toISOString(), this.samplePeriod()))
  }

  toggleTheme() {
    this.themeMode.update((value) => value === 'dark' ? 'light' : 'dark')
  }

  constructor() {
    writeSelectedCatalogToUrl(this.selectedCatalogId(), true)
    void this.loadOverview().then(() => this.restoreSelectedResources())

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
      if (!this.selectedResourcesRestored()) return
      this.storage.setJson(selectedResourcesStorageKey, {
        version: 1,
        period: formatPeriod(this.samplePeriod()),
        automaticPeriod: this.automaticPeriod(),
        selections: this.selectionReferences(),
      })
    })

    effect(() => {
      const catalogId = this.selectedCatalogId()
      const apiAvailable = this.apiAvailable()
      this.expandCatalogPath(catalogId)
      if (apiAvailable) void this.loadCatalogPathChildren(catalogId)
    })

    effect(() => {
      const themeMode = this.themeMode()
      this.document.documentElement.dataset['theme'] = themeMode
      this.storage.setJson(themeModeStorageKey, themeMode)
    })
  }

  @HostListener('window:resize')
  onResize() {
    if (window.innerWidth >= 1024) this.isMobileCatalogOpen.set(false)
    this.wideLayout.set(window.innerWidth >= 1536)
    if (!this.wideLayout() && !this.visualizationOpen()) {
      if (this.visualizationData() || this.visualizationController) this.visualizationOpen.set(true)
      else this.cancelVisualization()
    }
  }

  async visualize(open = true) {
    if (open) {
      this.isMobileCatalogOpen.set(false)
      this.visualizationOpen.set(true)
      if (this.visualizationData() && !this.visualizationStale()) return
    }
    this.cancelVisualization()
    this.visualizationError.set('')
    if (this.visualizationValidation()) {
      this.visualizationError.set(this.visualizationValidation())
      return
    }

    const controller = new AbortController()
    this.visualizationController = controller
    const key = this.visualizationKey()
    const begin = this.exportBegin()
    const end = this.exportEnd()
    const resources = this.visualizationResources()
    this.visualizationData.set(null)
    this.visualizationProgress.set(0)
    this.visualizationLoading.set(true)
    try {
      const data = createVisualizationData(dateTicks(begin)!, dateTicks(end)!, this.samplePeriod(), resources.map(resource => ({
        id: resource.path,
        name: resource.kind === 'Original' ? resource.id : `${resource.id} (${resource.kind.replace(/[A-Z]/g, (letter, index) => `${index ? '_' : ''}${letter.toLowerCase()}`)})`,
        unit: resource.unit,
      })))
      if (data.series.some(series => series.length < 2)) throw new Error('A line chart needs at least two samples. Extend the time range or reduce Period.')
      controller.signal.throwIfAborted()
      const response = await this.nexus.v2.data.getStream({ begin, end, resourcePaths: resources.map(resource => resource.path), precision: V2.Precision.Float32 }, controller.signal)
      let lastUpdate = 0
      await loadVisualizationData(response, data, fraction => {
        const now = performance.now()
        if (this.visualizationController === controller && (fraction === 1 || now - lastUpdate >= 100)) {
          this.visualizationProgress.set(Math.floor(fraction * 100))
          lastUpdate = now
        }
      }, controller.signal)
      controller.signal.throwIfAborted()
      this.loadedVisualizationKey.set(key)
      this.visualizationData.set(data)
    } catch (error) {
      if (this.visualizationController === controller) {
        this.visualizationData.set(null)
        if (!controller.signal.aborted) this.visualizationError.set(this.errorMessage(error))
      }
    } finally {
      if (this.visualizationController === controller) {
        this.visualizationLoading.set(false)
        this.visualizationController = undefined
      }
    }
  }

  cancelVisualization() {
    this.visualizationController?.abort()
    this.visualizationController = undefined
    if (this.visualizationLoading()) this.visualizationData.set(null)
    this.visualizationLoading.set(false)
  }

  closeVisualization() {
    this.visualizationOpen.set(false)
    this.cancelVisualization()
  }

  visualizationGpuFailed(message: string) {
    if (!this.visualizationLoading()) return
    this.cancelVisualization()
    this.visualizationError.set(message)
  }

  setVisualizationCache(value: number | null) {
    if (value !== null && Number.isFinite(value) && value >= 16) this.visualizationCacheMiB.set(Math.floor(value))
  }

  ngOnDestroy() {
    this.cancelVisualization()
  }

  @HostListener('window:popstate')
  onPopState() {
    const catalogId = getSelectedCatalogIdFromUrl()
    this.selectedCatalogId.set(catalogId)
    this.selectedCatalogNodeKey.set(getRealCatalogNodeKey(catalogId))
    this.selectedCatalogInfo.set(null)
    this.expandCatalogPath(catalogId)
    this.isMobileCatalogOpen.set(false)
    this.activeResourcePath.set('')
  }

  async loadOverview() {
    this.overviewLoading.set(true)
    this.overviewError.set(null)
    try {
      this.overview.set(await this.nexus.getSessionOverview())
      const roots = this.overview()?.roots ?? []
      this.childMap.update((current) => current.has('/') ? current : new Map(current).set('/', roots))
      await this.loadExpandedDescendants('/', roots)
    } catch (error) {
      this.overviewError.set(error)
      this.nexus.apiAvailable.set(false)
    } finally {
      this.overviewLoading.set(false)
    }
  }

  private async loadExpandedDescendants(parentId: string, infos: V1.CatalogInfo[]) {
    const prepared = prepareChildCatalogs(parentId, infos)
    for (const node of prepared) {
      if (!node.id) continue
      if (node.isFake) {
        if (this.expandedCatalogNodeKeys().has(node.nodeKey) && node.groupedChildren) {
          await this.loadExpandedDescendants(node.id, node.groupedChildren)
        }
      } else {
        if (this.expandedCatalogNodeKeys().has(node.nodeKey)) {
          await this.loadChildren(node.id)
          const children = this.childMap().get(node.id) ?? []
          await this.loadExpandedDescendants(node.id, children)
        }
      }
    }
  }

  async loadSelectedCatalog(catalogId: string, isFake: boolean, apiAvailable: boolean) {
    const generation = ++this.catalogLoadGeneration
    if (!catalogId || isFake || !apiAvailable) {
      this.catalogLoading.set(false)
      this.selectedBundle.set(null)
      return
    }

    const cachedBundle = this.catalogBundleCache.get(catalogId)
    if (cachedBundle) {
      this.catalogLoading.set(false)
      this.catalogError.set(null)
      this.selectedBundle.set(cachedBundle)
      return
    }

    this.catalogLoading.set(true)
    this.selectedBundle.set(null)
    this.catalogError.set(null)
    try {
      const bundle = await this.getCatalogBundle(catalogId)
      if (generation === this.catalogLoadGeneration) this.selectedBundle.set(bundle)
    } catch (error) {
      if (generation === this.catalogLoadGeneration) {
        this.catalogError.set(error)
        this.selectedBundle.set(null)
      }
    } finally {
      if (generation === this.catalogLoadGeneration) this.catalogLoading.set(false)
    }
  }

  async getCatalogBundle(catalogId: string) {
    const cachedBundle = this.catalogBundleCache.get(catalogId)
    if (cachedBundle) return cachedBundle

    const pendingRequest = this.catalogBundleRequests.get(catalogId)
    if (pendingRequest) return pendingRequest

    const request = this.nexus.getCatalogBundle(catalogId)
      .then((bundle) => {
        this.catalogBundleCache.set(catalogId, bundle)
        return bundle
      })
      .finally(() => this.catalogBundleRequests.delete(catalogId))

    this.catalogBundleRequests.set(catalogId, request)
    return request
  }

  async restoreSelectedResources() {
    if (this.selectedResourcesRestored() && this.selectionLoading()) return
    this.selectionLoading.set(true)
    const references = this.selectionReferences()
    const catalogs = new Map<string, RepresentationRow[]>()
    await Promise.all([...new Set(references.map(reference => reference.catalogId))].map(async catalogId => {
      try {
        catalogs.set(catalogId, representationRows(mapResources((await this.getCatalogBundle(catalogId)).catalog)))
      } catch {
        // A failed request is not evidence that a saved representation was deleted.
      }
    }))
    const restored = hydrateSelections(references, catalogs, this.samplePeriod(), this.automaticPeriod())
    this.samplePeriod.set(restored.period)
    this.periodDraft.set(formatPeriod(restored.period))
    this.selectedResourceRows.set(restored.selections)
    this.selectionReferences.set(restored.references)
    this.unresolvedSelections.set(restored.unresolved)
    if (!restored.selections.size && !restored.unresolved.length) this.automaticPeriod.set(true)
    const activeBundle = this.catalogBundleCache.get(this.selectedCatalogId())
    if (activeBundle && !this.isSelectedFake()) {
      this.selectedBundle.set(activeBundle)
      this.catalogError.set(null)
    }
    this.selectionLoading.set(false)
    this.selectedResourcesRestored.set(true)
  }

  selectCatalog(catalog: CatalogNode) {
    if (catalog.isFake) {
      this.toggleExpanded(catalog)
      return
    }

    const catalogId = catalog.id ?? '/'
    this.selectedCatalogId.set(catalogId)
    this.selectedCatalogNodeKey.set(catalog.nodeKey)
    this.selectedCatalogInfo.set(catalog)
    writeSelectedCatalogToUrl(catalogId)
    this.isMobileCatalogOpen.set(false)
    this.activeResourcePath.set('')
  }

  activateCatalogNode(catalog: CatalogNode) {
    if (catalog.isFake) {
      this.toggleExpanded(catalog)
      return
    }

    this.selectCatalog(catalog)
    if (this.catalogHasExpandableChildren(catalog)) this.toggleExpanded(catalog)
  }

  catalogHasExpandableChildren(catalog: CatalogNode) {
    if (catalog.isFake) return (catalog.groupedChildren?.length ?? 0) > 0
    if (!catalog.id || !this.apiAvailable()) return false

    const children = this.childMap().get(catalog.id)
    return children === undefined || children.length > 0
  }

  catalogNodeIsExpanded(catalog: CatalogNode) {
    if (!this.catalogSearch().trim()) return this.expandedCatalogNodeKeys().has(catalog.nodeKey)
    if (this.searchCollapsedCatalogNodeKeys().has(catalog.nodeKey)) return false

    return this.filteredCatalogNodes().some((node) => node.parentId === catalog.id)
  }

  toggleExpanded(catalog: CatalogNode) {
    if (this.catalogSearch().trim()) {
      this.searchCollapsedCatalogNodeKeys.update((current) => toggleSetValue(current, catalog.nodeKey))
      if (!catalog.isFake && catalog.id) void this.loadChildren(catalog.id)
      return
    }

    this.expandedCatalogNodeKeys.update((current) => toggleSetValue(current, catalog.nodeKey))

    if (!catalog.isFake && catalog.id) void this.loadChildren(catalog.id)
  }

  expandCatalogPath(catalogId: string) {
    this.expandedCatalogNodeKeys.update((current) => mergeSets(current, getCatalogPathNodeKeys(catalogId)))
  }

  async loadCatalogPathChildren(catalogId: string) {
    const ancestors = getCatalogAncestorPaths(catalogId)
    let currentInfos: V1.CatalogInfo[] = this.rootCatalogInfos()
    let currentParent = '/'
    for (const ancestor of ancestors) {
      const prepared = prepareChildCatalogs(currentParent, currentInfos)
      const node = prepared.find(n => n.id === ancestor)
      if (!node) break
      if (node.isFake) {
        currentInfos = node.groupedChildren ?? []
        currentParent = ancestor
        continue
      }
      await this.loadChildren(ancestor)
      currentInfos = this.childMap().get(ancestor) ?? []
      currentParent = ancestor
    }
  }

  async loadChildren(catalogId: string) {
    if (!this.apiAvailable() || this.childMap().has(catalogId)) return

    const existing = this.childRequests.get(catalogId)
    if (existing) return existing

    const request = (async () => {
      try {
        const children = await this.nexus.getCatalogChildren(catalogId)
        this.childMap.update((current) => new Map(current).set(catalogId, children))
      } catch {
        this.childMap.update((current) => new Map(current).set(catalogId, []))
      } finally {
        this.childRequests.delete(catalogId)
      }
    })()

    this.childRequests.set(catalogId, request)
    return request
  }

  toggleResource(resource: RepresentationRow) {
    if (this.selectionLoading()) return
    if (this.resourceSelected(resource)) this.removeResource(resource.key)
    else {
      if (this.requiresParameters(resource)) return
      if (this.pinnedCount() === 0 && this.automaticPeriod()) {
        this.samplePeriod.set(resource.basePeriod)
        this.periodDraft.set(formatPeriod(resource.basePeriod))
      }
      const selection: ResourceSelection = { ...resource, parameters: {}, kinds: [defaultKind(this.samplePeriod(), resource.basePeriod)] }
      this.selectedResourceRows.update(current => new Map(current).set(selection.key, selection))
      this.selectionReferences.update(current => [...current, storeSelectionReference(selection)])
    }
    this.activeResourcePath.set(resource.key)
  }

  removeResource(key: string) {
    if (this.selectionLoading()) return
    const selection = this.selectedResourceRows().get(key)
    if (!selection) return
    this.selectedResourceRows.update(current => {
      const next = new Map(current)
      next.delete(key)
      if (!next.size) this.automaticPeriod.set(true)
      return next
    })
    this.selectionReferences.update(current => current.filter(reference => !this.referenceMatches(reference, selection)))
  }

  toggleKind(selection: ResourceSelection, kind: RepresentationKind) {
    if (this.selectionLoading()) return
    const kinds = selection.kinds.includes(kind) ? selection.kinds.filter(value => value !== kind)
      : kindValid(kind, this.samplePeriod(), selection.basePeriod) ? [...selection.kinds, kind] : selection.kinds
    const updated = { ...selection, kinds }
    this.selectedResourceRows.update(current => new Map(current).set(updated.key, updated))
    this.selectionReferences.update(current => current.map(reference => this.referenceMatches(reference, selection) ? storeSelectionReference(updated) : reference))
  }

  private referenceMatches(reference: StoredSelectionReference, selection: ResourceSelection) {
    return reference.catalogId === selection.catalogId && reference.path === selection.path
      && parsePeriod(reference.basePeriod ?? '') === selection.basePeriod && selectionKey(selection, reference.parameters) === selection.key
  }

  requiresParameters(resource: RepresentationRow) {
    return Object.keys(resource.representation.parameters ?? {}).length > 0
  }

  private parametersValid(resource: ResourceSelection) {
    return !this.requiresParameters(resource) && Object.keys(resource.parameters).length === 0
  }

  requestClearPinnedResources() {
    if (!this.selectionLoading() && this.pinnedCount() > 0) this.isClearPinnedOpen.set(true)
  }

  clearPinnedResources() {
    if (this.selectionLoading()) return
    this.selectedResourceRows.set(new Map())
    this.selectionReferences.set([])
    this.unresolvedSelections.set([])
    this.automaticPeriod.set(true)
    this.isClearPinnedOpen.set(false)
  }

  resourceSelected(resource: RepresentationRow) {
    return this.selectedResourcePaths().has(resource.key)
  }

  activateResource(resource: RepresentationRow) {
    this.activeResourcePath.set(resource.key)
  }

  applyQuickRange(range: { begin: string; end: string }) {
    const reference = new Date()
    this.exportBegin.set(alignRangeEndpoint(resolveRangeEndpoint(range.begin, reference), this.samplePeriod()))
    this.exportEnd.set(alignRangeEndpoint(resolveRangeEndpoint(range.end, reference), this.samplePeriod()))
  }

  setExportBeginFromInput(value: string) {
    this.exportBegin.set(fromDateTimeLocalValue(value))
  }

  setExportEndFromInput(value: string) {
    this.exportEnd.set(fromDateTimeLocalValue(value))
  }

  updateConfig(key: string, value: unknown) {
    this.exportConfiguration.update((current) => ({ ...current, [key]: value }))
  }

  async createExportJob() {
    if (this.exportBusy()) return
    if (this.exportError()) {
      this.exportStatus.set(this.exportError())
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

function getUtcPeriodStart(reference: Date, unit: TimeRangePreset['unit']) {
  const start = new Date(reference)

  switch (unit) {
    case 'hour':
      start.setUTCMinutes(0, 0, 0)
      break
    case 'day':
      start.setUTCHours(0, 0, 0, 0)
      break
    case 'week': {
      const daysSinceMonday = (start.getUTCDay() + 6) % 7
      start.setUTCDate(start.getUTCDate() - daysSinceMonday)
      start.setUTCHours(0, 0, 0, 0)
      break
    }
    case 'month':
      start.setUTCDate(1)
      start.setUTCHours(0, 0, 0, 0)
      break
    case 'year':
      start.setUTCMonth(0, 1)
      start.setUTCHours(0, 0, 0, 0)
      break
  }

  return start
}

function subtractUtcRange(date: Date, unit: TimeRangePreset['unit'], amount: number) {
  switch (unit) {
    case 'hour':
      date.setUTCHours(date.getUTCHours() - amount)
      break
    case 'day':
      date.setUTCDate(date.getUTCDate() - amount)
      break
    case 'week':
      date.setUTCDate(date.getUTCDate() - (7 * amount))
      break
    case 'month':
      date.setUTCMonth(date.getUTCMonth() - amount)
      break
    case 'year':
      date.setUTCFullYear(date.getUTCFullYear() - amount)
      break
  }
}

function getSelectedCatalogIdFromUrl() {
  const catalogId = new URLSearchParams(window.location.search).get('catalog')?.trim()
  return catalogId || defaultCatalogId
}

function compareResources(left: ResourceRow, right: ResourceRow) {
  return left.catalogId.localeCompare(right.catalogId) || left.id.localeCompare(right.id)
}

function catalogNodeMatchesSearch(node: CatalogNode, term: string) {
  return `${node.id ?? ''} ${node.title ?? ''} ${node.pipelineInfo?.types?.join(' ') ?? ''}`.toLowerCase().includes(term)
}

function hasCollapsedSearchAncestor(node: CatalogNode, nodeById: ReadonlyMap<string, CatalogNode>, collapsedNodeKeys: ReadonlySet<string>) {
  let parent = node.parentId === '/' ? undefined : nodeById.get(node.parentId)
  while (parent) {
    if (collapsedNodeKeys.has(parent.nodeKey)) return true
    parent = parent.parentId === '/' ? undefined : nodeById.get(parent.parentId)
  }

  return false
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

function getInitialThemeMode(storage: BrowserStorageService): ThemeMode {
  const value = storage.getJson<string>(themeModeStorageKey, 'dark')
  return value === 'light' ? 'light' : 'dark'
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

function getUtcMidnightDaysAgo(daysAgo: number) {
  const now = new Date()
  const date = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() - daysAgo))
  return toUtcSecondString(date)
}

function toDateTimeLocalValue(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? '' : date.toISOString().slice(0, 19)
}

function fromDateTimeLocalValue(value: string) {
  if (!value) return ''
  const withSeconds = value.length === 16 ? `${value}:00` : value
  return `${withSeconds}Z`
}

function resolveRangeEndpoint(value: string, reference: Date) {
  if (value === 'now') return toUtcSecondString(reference)

  const relativeDuration = /^-PT(\d+)([HM])$/.exec(value)
  if (!relativeDuration) return value

  const amount = Number(relativeDuration[1])
  const unit = relativeDuration[2]
  const offsetMs = amount * (unit === 'H' ? 60 : 1) * 60 * 1000
  return toUtcSecondString(new Date(reference.valueOf() - offsetMs))
}

function toUtcSecondString(date: Date) {
  return date.toISOString().slice(0, 19) + 'Z'
}

function formatCatalogDisplayPath(path: string) {
  if (path === '/') return ''

  return path.replaceAll('/', ' / ').replace(/^ \/ /, '')
}

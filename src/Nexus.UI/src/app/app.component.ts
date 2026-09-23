import { CommonModule, DOCUMENT } from '@angular/common'
import { Component, HostListener, OnDestroy, computed, effect, inject, signal, viewChild } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { LucideCodeXml, LucideCopy, LucideExternalLink, LucideFileText, LucidePaperclip, LucidePilcrow, LucideX } from '@lucide/angular'
import { MenuItem, MessageService } from 'primeng/api'
import { ButtonModule } from 'primeng/button'
import { CheckboxModule } from 'primeng/checkbox'
import { DialogModule } from 'primeng/dialog'
import { DrawerModule } from 'primeng/drawer'
import { InputTextModule } from 'primeng/inputtext'
import { MenuModule } from 'primeng/menu'
import { ProgressBarModule } from 'primeng/progressbar'
import { TabsModule } from 'primeng/tabs'
import { ToastModule } from 'primeng/toast'
import { TooltipModule } from 'primeng/tooltip'
import { DrawerPassThrough } from 'primeng/types/drawer'
import { BrowserStorageService } from './browser-storage.service'
import { VisualizationChartComponent } from './charts/visualization-chart.component'
import { VisualizationBuffers, VisualizationData, createVisualizationData, releaseVisualizationData } from './charts/visualization-data'
import { dateTicks } from './resource-selection'
import { AppHeaderComponent } from './components/app-header.component'
import { CatalogTreeComponent } from './components/catalog-tree.component'
import { ExportComposerComponent } from './components/export-composer.component'
import { PinnedResourceComponent } from './components/pinned-resource.component'
import { PackageReferencesComponent } from './components/package-references.component'
import { AccessTokensComponent } from './components/access-tokens.component'
import { DataSourcePipelinesComponent } from './components/data-source-pipelines.component'
import { GitComponent } from './components/git.component'
import { PropertiesDialogComponent } from './components/properties-dialog.component'
import { ResourceMatrixComponent } from './components/resource-matrix.component'
import { MetadataDrafts, mergeResourceMetadata } from './resource-matrix'
import { RepresentationRow, ResourceSelection, RepresentationKind, StoredSelectionReference, alignRangeEndpoint, defaultKind, executionRangeError, formatFilePeriod, formatPeriod, hydrateSelections, kindValid, parseFilePeriod, parsePeriod, parseResourcePath, readSelectionState, representationRows, requestPath, selectionKey, storeSelectionReference, toTimeSpan } from './resource-selection'
import type { ParsedResourcePath } from './resource-selection'
import { parseNexusUiSetupJson } from './nexus-ui-setup'
import type { NexusUiSetup, ParsedNexusUiSetup } from './nexus-ui-setup'
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
  mapResources,
  prepareChildCatalogs,
} from './nexus.service'
import { abbreviateMiddle, compactPath, formatNumber, getStringProperty, lastSegment } from './utils'

const defaultCatalogId = '/SAMPLE/LOCAL'
const catalogExpansionStorageKey = 'nexus.catalog.expandedNodeKeys'
const selectedResourcesStorageKey = 'nexus.selectedResources'
const themeModeStorageKey = 'nexus.themeMode'
const exportSettingsStorageKey = 'nexus.exportSettings'
const defaultWriterType = 'Nexus.Writers.Csv'
type ResolvedThemeMode = 'dark' | 'light'
type ThemeMode = ResolvedThemeMode | 'system'

type ParameterField = {
  key: string
  label: string
  kind: 'input-integer' | 'select'
  defaultValue: string
  minimum?: number
  maximum?: number
  items?: Record<string, string>
}

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

function formatDateForDownloadName(value: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return 'export'
  return date.toISOString().slice(0, 19).replace(/:/g, '-')
}

type ExportJobHistoryEntry = {
  id: string
  job: V1.Job | V2.Job
  parameters?: V2.ExportParameters
  status?: V1.JobStatus
  error: string
  downloading: boolean
  downloadName: string
}

type StoredExportSettings = {
  selectedWriterType: string
  exportFilePeriod: string
  exportPrecision: V2.Precision
  configurationByWriter: Record<string, Record<string, unknown>>
  begin?: string
  end?: string
  period?: string
  automaticPeriod?: boolean
  resourcePaths?: string[]
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, CheckboxModule, DialogModule, DrawerModule, InputTextModule, MenuModule, ProgressBarModule, TabsModule, ToastModule, TooltipModule, LucideCodeXml, LucideCopy, LucideExternalLink, LucideFileText, LucidePaperclip, LucidePilcrow, LucideX, MarkdownPipe, RestoreFocusDirective, AppHeaderComponent, CatalogTreeComponent, ExportComposerComponent, PinnedResourceComponent, PackageReferencesComponent, AccessTokensComponent, DataSourcePipelinesComponent, GitComponent, PropertiesDialogComponent, VisualizationChartComponent, ResourceMatrixComponent],
  providers: [MessageService],
  templateUrl: './app.component.html',
})
export class AppComponent implements OnDestroy {
  private readonly nexus = inject(NexusService)
  private readonly storage = inject(BrowserStorageService)
  private readonly messageService = inject(MessageService)
  private readonly document = inject(DOCUMENT)
  private readonly catalogBundleCache = new Map<string, CatalogBundle>()
  private readonly catalogBundleRequests = new Map<string, Promise<CatalogBundle>>()
  private readonly childRequests = new Map<string, Promise<void>>()
  private readonly storedSelectionState = readSelectionState(this.storage.getJson<unknown>(selectedResourcesStorageKey, null))
  private readonly storedExportSettings = getStoredExportSettings(this.storage)
  private readonly selectionReferences = signal(this.storedSelectionState.selections)
  private readonly selectedResourcesRestored = signal(false)
  private catalogLoadGeneration = 0
  private catalogCacheGeneration = 0
  private selectionRestoreGeneration: number | null = null
  private readonly refreshController = new AbortController()
  private catalogHistoryPosition = 0
  private restoreHistory: (() => void) | null = null
  private acceptedHistoryNavigation = false
  private readonly resourceMatrix = viewChild(ResourceMatrixComponent)
  readonly revealResourceKey = signal('')
  readonly revealResourceSequence = signal(0)
  readonly selectionLoading = signal(true)
  readonly unresolvedSelections = signal<StoredSelectionReference[]>([])
  readonly pinnedCount = computed(() => this.selectionReferences().length)

  readonly selectedCatalogId = signal(getSelectedCatalogIdFromUrl())
  readonly selectedCatalogNodeKey = signal(getRealCatalogNodeKey(getSelectedCatalogIdFromUrl()))
  readonly activeCatalogDetailsOpen = signal(false)
  readonly expandedCatalogNodeKeys = signal<ReadonlySet<string>>(getInitialExpandedCatalogNodeKeys(this.storage, getSelectedCatalogIdFromUrl()))
  readonly searchCollapsedCatalogNodeKeys = signal<ReadonlySet<string>>(new Set())
  readonly catalogSearch = signal('')
  readonly selectedResourceRows = signal<ReadonlyMap<string, ResourceSelection>>(new Map())
  readonly activeResourcePath = signal('/SAMPLE/LOCAL/T1')
  readonly isExportOpen = signal(false)
  readonly isJobsOpen = signal(false)
  readonly isPackageReferencesOpen = signal(false)
  readonly isDataSourcePipelinesOpen = signal(false)
  readonly isGitOpen = signal(false)
  readonly isAccessTokensOpen = signal(false)
  readonly isClearPinnedOpen = signal(false)
  readonly parameterResource = signal<RepresentationRow | null>(null)
  readonly parameterDraft = signal<Record<string, string>>({})
  readonly isReadmeOpen = signal(false)
  readonly isAboutOpen = signal(false)
  readonly isLicenseOpen = signal(false)
  readonly isCatalogFilesOpen = signal(false)
  readonly isCatalogPropertiesOpen = signal(false)
  readonly isMobileCatalogOpen = signal(false)
  readonly visualizationOpen = signal(false)
  readonly compactLayout = signal(window.innerWidth < 640)
  readonly wideLayout = signal(window.innerWidth >= 1536)
  readonly visualizationPanelVisible = computed(() => this.wideLayout() && !this.resourceMatrix()?.editing())
  readonly visualizationDialogStyle = computed(() => this.compactLayout()
    ? { width: '100vw', height: '100dvh', maxHeight: '100dvh', margin: '0', borderRadius: '0' }
    : { width: 'calc(100vw - 2rem)', height: 'calc(100dvh - 2rem)', maxHeight: 'calc(100dvh - 2rem)' })
  readonly visualizationData = signal<VisualizationData | null>(null)
  readonly visualizationLoading = signal(false)
  readonly visualizationProgress = signal(0)
  readonly visualizationError = signal('')
  readonly visualizationBeginAtZero = signal(false)
  readonly visualizationCacheMiB = signal(2048)
  private visualizationController?: AbortController
  private visualizationBuffers?: VisualizationBuffers
  private exportController?: AbortController
  private readonly loadedVisualizationKey = signal('')
  private readonly systemThemeQuery = window.matchMedia('(prefers-color-scheme: dark)')
  private readonly systemThemeDark = signal(this.systemThemeQuery.matches)
  private readonly systemThemeListener = (event: MediaQueryListEvent) => this.systemThemeDark.set(event.matches)
  readonly themeMode = signal<ThemeMode>(getInitialThemeMode(this.storage))
  readonly resolvedThemeMode = computed<ResolvedThemeMode>(() => {
    const themeMode = this.themeMode()
    if (themeMode === 'system') return this.systemThemeDark() ? 'dark' : 'light'
    return themeMode
  })
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
  readonly exportFilePeriod = signal(this.storedExportSettings.exportFilePeriod)
  readonly exportFilePeriodDraft = signal(formatFilePeriod(parsePeriod(this.storedExportSettings.exportFilePeriod) ?? 0n))
  readonly exportFilePeriodError = computed(() => {
    const period = parseFilePeriod(this.exportFilePeriodDraft())
    if (period === null) return 'Enter a file period, for example Single file, 100 ms, 1 s, or 10 min.'
    return period % this.samplePeriod() === 0n ? '' : 'File period must be zero or an integer multiple of Period.'
  })
  readonly selectedWriterType = signal(this.storedExportSettings.selectedWriterType)
  readonly configurationByWriter = signal(this.storedExportSettings.configurationByWriter)
  readonly exportConfiguration = signal<Record<string, unknown>>(this.storedExportSettings.configurationByWriter[this.storedExportSettings.selectedWriterType] ?? {})
  readonly exportPrecision = signal<V2.Precision>(this.storedExportSettings.exportPrecision)
  readonly exportStatus = signal('')
  readonly exportBusy = signal(false)
  readonly licenseText = signal('')
  readonly licenseLoading = signal(false)
  readonly licenseAccepting = signal(false)
  readonly licenseError = signal('')
  readonly catalogFilesBusy = signal(false)
  readonly catalogFilesError = signal('')
  readonly catalogFilesDragActive = signal(false)
  readonly deletingAttachmentId = signal('')
  readonly pendingDeleteAttachmentId = signal('')
  readonly currentExportJobId = signal('')
  readonly currentExportJobStatus = signal<V1.JobStatus | null>(null)
  readonly currentExportJobError = signal('')
  readonly currentExportDownloading = signal(false)
  readonly jobHistory = signal<ExportJobHistoryEntry[]>([])
  readonly setupDragActive = signal(false)
  private setupDragDepth = 0

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

  readonly rootCatalogInfos = computed(() => this.overview()?.roots ?? [])
  readonly writerDescriptions = computed(() => this.overview()?.writers ?? [])
  readonly jobs = computed(() => this.overview()?.jobs ?? [])
  readonly exportJobHistory = computed(() => this.jobHistory().filter(entry => this.isExportJobEntry(entry)))
  readonly jobHistoryCount = computed(() => this.exportJobHistory().length)
  readonly userName = computed(() => this.nexus.currentUser()?.name ?? 'Prototype user')
  readonly isAdministrator = computed(() => this.nexus.currentUser()?.claims?.some(claim => claim.type === 'role' && claim.value === 'Administrator') ?? false)
  readonly endpointHost = computed(() => new URL(this.nexus.endpoint).host)
  readonly helpLink = computed(() => this.nexus.system()?.helpLink ?? null)
  readonly logoutUrl = computed(() => this.nexus.system()?.logoutUrl ?? null)
  readonly nexusVersion = computed(() => this.nexus.system()?.version ?? '')
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
  readonly selectedCatalogHasLicense = computed(() => !!(this.selectedCatalogInfo()?.license || this.selectedNode()?.license || this.selectedBundle()?.attachments.includes('LICENSE.md')))
  readonly selectedCatalogReadable = computed(() => this.selectedCatalogInfo()?.isReadable ?? this.selectedNode()?.isReadable)
  readonly selectedCatalogAttachments = computed(() => [...(this.selectedBundle()?.attachments ?? [])].sort((a, b) => a.localeCompare(b)))
  readonly licenseAcceptanceVisible = computed(() => this.apiAvailable() && !this.isSelectedFake() && this.selectedCatalogHasLicense() && this.selectedCatalogReadable() === false)
  readonly acceptedLicenseVisible = computed(() => this.apiAvailable() && !this.isSelectedFake() && this.selectedCatalogHasLicense() && this.selectedCatalogReadable() === true)
  readonly resourceMetadataWritable = computed(() => {
    const id = this.selectedCatalogId()
    const info = [...this.rootCatalogInfos(), ...[...this.childMap().values()].flat(), this.selectedCatalogInfo()]
      .find(info => info?.id === id)
    return this.apiAvailable() && !this.isSelectedFake() && this.selectedCatalog()?.id === id
      && info?.isReadable === true && info.isWritable === true
  })
  readonly selectedCatalogWritable = computed(() => this.resourceMetadataWritable())

  readonly resourceRows = computed(() => {
    if (!this.apiAvailable()) return []
    if (this.isSelectedFake()) return []
    return representationRows(mapResources(this.selectedCatalog()))
  })

  readonly selectedResourcePaths = computed(() => new Set(this.selectedResourceRows().keys()))
  readonly selectedResources = computed(() => [...this.selectedResourceRows().values()].sort(compareResources))
  readonly groupedSelectedResources = computed<SelectedResourceGroup[]>(() => {
    const groups = new Map<string, ResourceSelection[]>()
    for (const resource of this.selectedResources()) groups.set(resource.catalogId, [...(groups.get(resource.catalogId) ?? []), resource])
    return [...groups.entries()].map(([catalogId, resources]) => ({ catalogId, resources }))
  })
  readonly selectedDataTypes = computed(() => new Set(this.selectedResources().map((resource) => resource.representation.dataType)))
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
  readonly parameterFields = computed(() => this.toParameterFields(this.parameterResource()))
  readonly unsupportedParameterKeys = computed(() => {
    const resource = this.parameterResource()
    if (!resource) return []
    const supported = new Set(this.parameterFields().map(field => field.key))
    return Object.keys(resource.representation.parameters ?? {}).filter(key => !supported.has(key))
  })
  readonly parameterDialogError = computed(() => {
    const unsupportedKeys = this.unsupportedParameterKeys()
    if (unsupportedKeys.length) return `Unsupported parameter schema: ${unsupportedKeys.join(', ')}`
    const draft = this.parameterDraft()

    for (const field of this.parameterFields()) {
      const value = draft[field.key]
      if (value === undefined || value === '') return `${field.label} is required.`

      if (field.kind === 'input-integer') {
        const parsed = Number(value)
        if (!Number.isInteger(parsed)) return `${field.label} must be an integer.`
        if (field.minimum !== undefined && parsed < field.minimum) return `${field.label} must be at least ${field.minimum}.`
        if (field.maximum !== undefined && parsed > field.maximum) return `${field.label} must be at most ${field.maximum}.`
      } else if (!Object.hasOwn(field.items ?? {}, value)) {
        return `${field.label} has an invalid value.`
      }
    }

    return ''
  })
  readonly exportError = computed(() => {
    if (this.selectionError()) return this.selectionError()
    if (this.exportFilePeriodError()) return this.exportFilePeriodError()
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
  readonly currentExportProgress = computed(() => this.jobProgress(this.currentExportJobStatus() ?? undefined))
  readonly currentExportStatusText = computed(() => {
    const status = this.currentExportJobStatus()
    if (this.exportBusy()) return 'Creating export job...'
    if (!this.currentExportJobId()) return ''
    return status ? this.formatJobStatus(status) : 'Export job queued...'
  })
  readonly currentExportCanCancel = computed(() => !!this.currentExportJobId() && !this.isTerminalStatus(this.currentExportJobStatus()?.status))
  readonly currentExportCanDownload = computed(() => !!this.artifactIdFromStatus(this.currentExportJobStatus() ?? undefined))

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

  applySelectedCatalogRange() {
    const timeRange = this.selectedBundle()?.timeRange
    if (!formatRange(timeRange) || !timeRange?.begin || !timeRange.end) return

    this.exportBegin.set(alignRangeEndpoint(timeRange.begin, this.samplePeriod()))
    this.exportEnd.set(alignRangeEndpoint(timeRange.end, this.samplePeriod()))
  }

  toggleTheme() {
    this.themeMode.update((value) => value === 'dark' ? 'light' : value === 'light' ? 'system' : 'dark')
  }

  constructor() {
    this.systemThemeQuery.addEventListener('change', this.systemThemeListener)

    this.catalogHistoryPosition = writeSelectedCatalogToUrl(this.selectedCatalogId(), true)
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
      this.document.documentElement.dataset['theme'] = this.resolvedThemeMode()
      this.storage.setJson(themeModeStorageKey, themeMode)
    })

    effect(() => {
      this.mergeJobHistory(this.jobs())
    })

    effect(() => {
      this.storage.setJson(exportSettingsStorageKey, {
        version: 1,
        selectedWriterType: this.selectedWriterType(),
        exportFilePeriod: this.exportFilePeriod(),
        exportPrecision: this.exportPrecision(),
        configurationByWriter: {
          ...this.configurationByWriter(),
          [this.selectedWriterType()]: this.exportConfiguration(),
        },
      })
    })
  }

  @HostListener('window:resize')
  onResize() {
    if (window.innerWidth >= 1024) this.isMobileCatalogOpen.set(false)
    this.compactLayout.set(window.innerWidth < 640)
    this.wideLayout.set(window.innerWidth >= 1536)
    if (!this.wideLayout() && !this.visualizationOpen()) this.cancelVisualization()
  }

  readonly visualizationByteCount = computed(() => {
    const beginTicks = dateTicks(this.exportBegin())
    const endTicks = dateTicks(this.exportEnd())
    const samplePeriod = this.samplePeriod()
    if (beginTicks === null || endTicks === null || beginTicks >= endTicks || samplePeriod <= 0n) return 0n
    const elementCount = (endTicks - beginTicks) / samplePeriod
    return elementCount * BigInt(this.visualizationResources().length) * 4n
  })

  readonly visualizationByteCountLabel = computed(() => this.formatByteCount(this.visualizationByteCount()))

  readonly exportByteCount = computed(() => {
    const beginTicks = dateTicks(this.exportBegin())
    const endTicks = dateTicks(this.exportEnd())
    const samplePeriod = this.samplePeriod()
    if (beginTicks === null || endTicks === null || beginTicks >= endTicks || samplePeriod <= 0n) return 0n
    const elementCount = (endTicks - beginTicks) / samplePeriod
    const elementSize = this.exportPrecision() === V2.Precision.Float64 ? 8n : 4n
    return elementCount * BigInt(this.requestPaths().length) * elementSize
  })

  readonly exportByteCountLabel = computed(() => this.formatByteCount(this.exportByteCount()))

  private formatByteCount(byteCount: bigint): string {
    if (byteCount <= 0n) return ''
    if (byteCount >= 1000n * 1000n * 1000n) return `${this.formatSignificantDigits(Number(byteCount) / 1000 / 1000 / 1000)} GB`
    if (byteCount >= 1000n * 1000n) return `${this.formatSignificantDigits(Number(byteCount) / 1000 / 1000)} MB`
    if (byteCount >= 1000n) return `${this.formatSignificantDigits(Number(byteCount) / 1000)} kB`
    return `${byteCount} B`
  }

  private formatSignificantDigits(value: number): string {
    return value.toPrecision(3).replace(/(\.\d*?[1-9])0+$|\.0+$/, '$1')
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
    const samplePeriod = this.samplePeriod()
    const resources = this.visualizationResources()
    const descriptors = resources.map(resource => ({
      id: resource.path,
      name: resource.kind === 'Original' ? resource.id : `${resource.id} (${resource.kind.replace(/[A-Z]/g, (letter, index) => `${index ? '_' : ''}${letter.toLowerCase()}`)})`,
      unit: resource.unit,
    }))
    const existing = this.visualizationData()
    this.visualizationData.set(null)
    this.visualizationProgress.set(0)
    this.visualizationLoading.set(true)
    try {
      const data = createVisualizationData(dateTicks(begin)!, dateTicks(end)!, samplePeriod, descriptors)
      if (data.series.some(series => series.length < 2)) throw new Error('A line chart needs at least two samples. Extend the time range or reduce Period.')
      controller.signal.throwIfAborted()

      const canIncremental = !!existing
        && existing.begin === data.begin
        && existing.end === data.end
        && existing.series[0]?.samplePeriod === samplePeriod
      const loadedIds = canIncremental
        ? new Set(existing!.series.filter(s => s.complete).map(s => s.id))
        : new Set<string>()

      const preservedChunks = new Set<readonly Float32Array[]>()

      if (canIncremental) {
        for (const series of data.series) {
          const existingSeries = existing!.series.find(s => s.id === series.id)
          if (existingSeries && existingSeries.complete) {
            series.chunks = existingSeries.chunks
            series.availableLength = existingSeries.availableLength
            series.version = existingSeries.version
            series.complete = true
            preservedChunks.add(existingSeries.chunks)
          }
        }
      }
      releaseVisualizationData(existing, preservedChunks)

      const currentUnits = new Map(this.visualizationResources().map(resource => [resource.path, resource.unit]))
      for (const series of data.series) series.unit = currentUnits.get(series.id) ?? series.unit
      this.visualizationData.set(data)

      const newDescriptors = descriptors.filter(d => !loadedIds.has(d.id))
      if (newDescriptors.length > 0) {
        const newPaths = resources.filter(r => !loadedIds.has(r.path)).map(r => r.path)
        const newSeries = data.series.filter(series => !loadedIds.has(series.id))
        const buffers = new VisualizationBuffers(newSeries)
        this.visualizationBuffers = buffers
        let lastUpdate = 0
        await this.nexus.loadResourcesIntoBuffers(begin, end, newPaths, V2.Precision.Float32, buffers.provider, fraction => {
          const now = performance.now()
          if (this.visualizationController === controller && (fraction === 1 || now - lastUpdate >= 100)) {
            this.visualizationProgress.set(Math.floor(fraction * 100))
            lastUpdate = now
          }
        }, controller.signal)
        controller.signal.throwIfAborted()
        buffers.complete()
        if (this.visualizationBuffers === buffers) this.visualizationBuffers = undefined
      } else {
        this.visualizationProgress.set(100)
      }

      this.loadedVisualizationKey.set(key)
    } catch (error) {
      this.visualizationBuffers?.dispose()
      this.visualizationBuffers = undefined
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
    this.visualizationBuffers?.dispose()
    this.visualizationBuffers = undefined
    if (this.visualizationLoading()) {
      const data = this.visualizationData()
      this.visualizationData.set(null)
      releaseVisualizationData(data)
    }
    this.visualizationLoading.set(false)
  }

  closeVisualization() {
    this.visualizationOpen.set(false)
    this.cancelVisualization()
    const data = this.visualizationData()
    this.visualizationData.set(null)
    releaseVisualizationData(data)
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
    this.systemThemeQuery.removeEventListener('change', this.systemThemeListener)
    this.cancelVisualization()
    this.resetCurrentExportJob()
    this.refreshController.abort()
  }

  openDataSourcePipelines() {
    if (this.isAdministrator()) this.requestCatalogNavigation(() => this.isDataSourcePipelinesOpen.set(true))
  }

  async previewSetupImportFromInput(event: Event) {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0]
    input.value = ''
    if (file) await this.previewSetupImport(file)
  }

  openSetupImportPicker() {
    this.document.getElementById('nexus-setup-import-input')?.click()
  }

  async previewSetupImport(file: File) {
    this.messageService.clear('app-status')
    try {
      const parsed = parseNexusUiSetupJson(await file.text())
      if (this.resourceMatrix()?.hasUnsavedChanges() || this.resourceMatrix()?.saving()) {
        this.messageService.add({ key: 'app-status', severity: 'error', summary: 'Save or discard resource metadata edits before importing a setup.', life: 5000 })
        return
      }

      await this.applySetupImport(parsed)
      this.showSetupStatus(`Imported ${file.name || 'setup.json'}.`)
    } catch (error) {
      this.messageService.add({ key: 'app-status', severity: 'error', summary: this.errorMessage(error), life: 5000 })
    }
  }

  exportSetup() {
    const setup = this.createSetupExport()
    const blob = new Blob([`${JSON.stringify(setup, null, 2)}\n`], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const link = this.document.createElement('a')
    link.href = url
    link.download = `setup-${new Date().toISOString().slice(0, 16).replace(/[:-]/g, '')}.nexus.json`
    link.click()
    URL.revokeObjectURL(url)
    this.showSetupStatus('Setup exported.')
  }

  private showSetupStatus(message: string) {
    this.messageService.add({ key: 'app-status', severity: 'success', summary: message, life: 1800 })
  }

  @HostListener('window:dragenter', ['$event'])
  onSetupDragEnter(event: DragEvent) {
    if (!hasSetupFile(event.dataTransfer)) return
    event.preventDefault()
    this.setupDragDepth += 1
    this.setupDragActive.set(true)
  }

  @HostListener('window:dragover', ['$event'])
  onSetupDragOver(event: DragEvent) {
    if (!hasSetupFile(event.dataTransfer)) return
    event.preventDefault()
    this.setupDragActive.set(true)
  }

  @HostListener('window:dragleave', ['$event'])
  onSetupDragLeave(event: DragEvent) {
    if (!hasSetupFile(event.dataTransfer)) return
    this.setupDragDepth = Math.max(0, this.setupDragDepth - 1)
    if (this.setupDragDepth === 0 || isOutsideViewport(event)) this.clearSetupDragState()
  }

  @HostListener('window:dragend')
  onSetupDragEnd() {
    this.clearSetupDragState()
  }

  @HostListener('window:blur')
  onSetupDragBlur() {
    this.clearSetupDragState()
  }

  @HostListener('window:drop', ['$event'])
  async onSetupDrop(event: DragEvent) {
    if (!hasSetupFile(event.dataTransfer)) return
    event.preventDefault()
    this.clearSetupDragState()
    const file = Array.from(event.dataTransfer?.files ?? []).find(isSetupFile)
    if (file) await this.previewSetupImport(file)
  }

  private clearSetupDragState() {
    this.setupDragDepth = 0
    this.setupDragActive.set(false)
  }

  private createSetupExport(): NexusUiSetup {
    return {
      begin: this.exportBegin(),
      end: this.exportEnd(),
      filePeriod: this.exportFilePeriod(),
      type: this.selectedWriterType(),
      resourcePaths: getStoredSelectionRequestPaths(this.selectionReferences(), this.samplePeriod()),
      configuration: this.exportConfiguration(),
      precision: this.exportPrecision(),
    }
  }

  private async applySetupImport(parsed: ParsedNexusUiSetup) {
    const exportSettings = readSetupExportSettings(parsed.setup)
    if (exportSettings.begin && isValidDateString(exportSettings.begin)) this.exportBegin.set(exportSettings.begin)
    if (exportSettings.end && isValidDateString(exportSettings.end)) this.exportEnd.set(exportSettings.end)
    const writerType = exportSettings.selectedWriterType
    if (writerType) this.selectedWriterType.set(writerType)
    if (exportSettings.exportPrecision) this.exportPrecision.set(exportSettings.exportPrecision)
    if (exportSettings.exportFilePeriod && parseFilePeriod(exportSettings.exportFilePeriod) !== null) {
      const filePeriod = parseFilePeriod(exportSettings.exportFilePeriod)!
      this.exportFilePeriod.set(exportSettings.exportFilePeriod)
      this.exportFilePeriodDraft.set(formatFilePeriod(filePeriod))
    }

    let configurationByWriter = exportSettings.configurationByWriter
      ? getStoredWriterConfigurations(exportSettings.configurationByWriter, this.configurationByWriter())
      : this.configurationByWriter()
    this.configurationByWriter.set(configurationByWriter)
    this.exportConfiguration.set(configurationByWriter[this.selectedWriterType()] ?? {})
    if (exportSettings.resourcePaths?.length) await this.restoreSetupResourcePaths(exportSettings.resourcePaths)
  }

  private async restoreSetupResourcePaths(resourcePaths: string[]) {
    const catalogs = new Map<string, RepresentationRow[]>()
    const references: StoredSelectionReference[] = []
    let commonPeriod: bigint | null = null

    for (const resourcePath of resourcePaths) {
      const parsed = parseResourcePath(resourcePath)
      if (!parsed) continue
      if (commonPeriod === null) commonPeriod = parsed.period
      else if (parsed.period !== commonPeriod) continue

      const reference = await this.resolveSetupResourcePath(parsed, catalogs)
      if (reference) references.push(reference)
    }

    if (commonPeriod === null) return
    const restored = hydrateSelections(references, catalogs, commonPeriod, false)
    this.samplePeriod.set(restored.period)
    this.periodDraft.set(formatPeriod(restored.period))
    this.automaticPeriod.set(false)
    this.selectedResourceRows.set(restored.selections)
    this.selectionReferences.set(restored.references)
    this.unresolvedSelections.set(restored.unresolved)
  }

  private async resolveSetupResourcePath(parsed: ParsedResourcePath, catalogs: Map<string, RepresentationRow[]>): Promise<StoredSelectionReference | null> {
    let failedLoads = 0
    const candidates = getResourcePathCatalogCandidates(parsed.path)
    for (const catalogId of candidates) {
      let rows = catalogs.get(catalogId)
      if (!rows) {
        try {
          rows = representationRows(mapResources((await this.getCatalogBundle(catalogId)).catalog))
          catalogs.set(catalogId, rows)
        } catch {
          failedLoads += 1
          continue
        }
      }

      if (rows.some(row => row.path === parsed.path && row.basePeriod === parsed.basePeriod)) {
        return {
          catalogId,
          path: parsed.path,
          basePeriod: formatPeriod(parsed.basePeriod),
          parameters: parsed.parameters,
          kinds: [parsed.kind],
        }
      }
    }

    if (failedLoads === candidates.length && candidates[0]) {
      return {
        catalogId: candidates[0],
        path: parsed.path,
        basePeriod: formatPeriod(parsed.basePeriod),
        parameters: parsed.parameters,
        kinds: [parsed.kind],
      }
    }

    return null
  }

  private describeSetupImportWarnings(parsed: ParsedNexusUiSetup) {
    const warnings: string[] = []
    if (parsed.legacyUiSettings?.catalogHidePatterns?.length) warnings.push('Catalog hide patterns are preserved for compatibility, but this UI has no catalog hiding setting to apply.')
    if (parsed.legacyUiSettings?.chartGpuCacheBudgetMiB !== undefined) warnings.push('Chart cache budget is not part of setup export settings and will not be imported.')
    if (parsed.source === 'legacy-ui-settings') warnings.push('Legacy settings do not include time range, export period, precision, or resource paths.')
    return warnings
  }

  readonly refreshPipelineDatabase = async (): Promise<boolean> => {
    // Metadata drafts are guarded before opening the pipeline dialog, not beneath its modal.
    if (this.resourceMatrix()?.hasUnsavedChanges() || this.resourceMatrix()?.saving())
      throw new Error('Close this dialog and save or discard resource metadata edits before refreshing.')
    const signal = this.refreshController.signal
    const job = await this.nexus.v1.jobs.refreshDatabase(signal)
    if (!job.id) throw new Error('The refresh job did not return an ID.')
    for (;;) {
      await new Promise<void>((resolve, reject) => {
        const abort = () => { clearTimeout(timer); reject(signal.reason) }
        const timer = setTimeout(() => { signal.removeEventListener('abort', abort); resolve() }, 1000)
        signal.addEventListener('abort', abort, { once: true })
        if (signal.aborted) abort()
      })
      const status = await this.nexus.v1.jobs.getJobStatus(job.id, signal)
      if (status.status === V1.TaskStatus.RanToCompletion) break
      if (status.status === V1.TaskStatus.Canceled) throw new Error('Database refresh was canceled.')
      if (status.status === V1.TaskStatus.Faulted) throw new Error(status.exceptionMessage || 'Database refresh failed.')
    }
    this.catalogCacheGeneration++
    this.catalogLoadGeneration++
    this.catalogBundleCache.clear()
    this.catalogBundleRequests.clear()
    this.childRequests.clear()
    this.childMap.set(new Map())
    this.selectedCatalogInfo.set(null)
    this.selectedBundle.set(null)
    this.cancelVisualization()
    this.visualizationData.set(null)
    this.loadedVisualizationKey.set('')
    await this.loadOverview()
    if (this.overviewError()) throw new Error(`Database refreshed, but catalogs could not be reloaded: ${this.errorMessage(this.overviewError())}`)
    await this.loadSelectedCatalog(this.selectedCatalogId(), this.isSelectedFake(), this.apiAvailable())
    await this.restoreSelectedResources()
    return true
  }

  @HostListener('window:popstate')
  onPopState() {
    if (this.restoreHistory) {
      const restored = this.restoreHistory
      this.restoreHistory = null
      restored()
      return
    }
    const catalogId = getSelectedCatalogIdFromUrl()
    const position = window.history.state?.nexusCatalogPosition ?? 0
    const delta = position - this.catalogHistoryPosition
    const matrix = this.resourceMatrix()
    if (!this.acceptedHistoryNavigation && delta && (matrix?.hasUnsavedChanges() || matrix?.saving())) {
      // Return to the original entry before asking; Stay must not overwrite the destination.
      this.restoreHistory = () => this.requestCatalogNavigation(() => {
        this.acceptedHistoryNavigation = true
        window.history.go(delta)
      })
      window.history.go(-delta)
      return
    }
    this.acceptedHistoryNavigation = false
    this.catalogHistoryPosition = position
    this.selectedCatalogId.set(catalogId)
    this.selectedCatalogNodeKey.set(getRealCatalogNodeKey(catalogId))
    this.selectedCatalogInfo.set(null)
    this.expandCatalogPath(catalogId)
    this.isMobileCatalogOpen.set(false)
    this.activeResourcePath.set('')
    this.revealResourceKey.set('')
  }

  @HostListener('window:beforeunload', ['$event'])
  onBeforeUnload(event: BeforeUnloadEvent) {
    if (!this.resourceMatrix()?.hasUnsavedChanges()) return
    event.preventDefault()
    event.returnValue = ''
  }

  private requestCatalogNavigation(action: () => void) {
    this.isMobileCatalogOpen.set(false)
    const matrix = this.resourceMatrix()
    if (matrix) matrix.requestNavigation(action)
    else action()
  }

  async loadOverview() {
    const generation = this.catalogCacheGeneration
    this.overviewLoading.set(true)
    this.overviewError.set(null)
    try {
      const overview = await this.nexus.getSessionOverview()
      if (generation !== this.catalogCacheGeneration) return
      this.overview.set(overview)
      const roots = this.overview()?.roots ?? []
      this.childMap.update((current) => current.has('/') ? current : new Map(current).set('/', roots))
      await this.loadExpandedDescendants('/', roots)
    } catch (error) {
      if (generation !== this.catalogCacheGeneration) return
      this.overviewError.set(error)
      this.nexus.apiAvailable.set(false)
    } finally {
      if (generation === this.catalogCacheGeneration) this.overviewLoading.set(false)
    }
  }

  private async loadExpandedDescendants(parentId: string, infos: V1.CatalogInfo[]) {
    const generation = this.catalogCacheGeneration
    const prepared = prepareChildCatalogs(parentId, infos)
    for (const node of prepared) {
      if (generation !== this.catalogCacheGeneration) return
      if (!node.id) continue
      if (node.isFake) {
        if (this.expandedCatalogNodeKeys().has(node.nodeKey) && node.groupedChildren) {
          await this.loadExpandedDescendants(node.id, node.groupedChildren)
        }
      } else {
        if (this.expandedCatalogNodeKeys().has(node.nodeKey)) {
          await this.loadChildren(node.id)
          if (generation !== this.catalogCacheGeneration) return
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

    if (this.licenseAcceptanceVisible()) {
      this.catalogLoading.set(false)
      this.catalogError.set(null)
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

    const generation = this.catalogCacheGeneration
    const request = this.nexus.getCatalogBundle(catalogId)
      .then((bundle) => {
        if (generation === this.catalogCacheGeneration) this.catalogBundleCache.set(catalogId, bundle)
        return bundle
      })
      .finally(() => { if (this.catalogBundleRequests.get(catalogId) === request) this.catalogBundleRequests.delete(catalogId) })

    this.catalogBundleRequests.set(catalogId, request)
    return request
  }

  readonly saveResourceMetadata = async (catalogId: string, drafts: MetadataDrafts): Promise<{ warning?: string }> => {
    if (catalogId !== this.selectedCatalogId() || !this.resourceMetadataWritable())
      throw new Error('This catalog is not writable.')

    // Never use the optional bundle metadata: a failed GET must not erase existing overrides.
    const metadata = await this.nexus.v1.catalogs.getMetadata(catalogId)
    const updated = mergeResourceMetadata(metadata, catalogId, drafts)
    await this.nexus.v1.catalogs.setMetadata(catalogId, updated)

    // Drain older loads before invalidating, so they cannot repopulate the cache after saving.
    await this.catalogBundleRequests.get(catalogId)?.catch(() => undefined)
    this.catalogBundleCache.delete(catalogId)
    try {
      const bundle = await this.getCatalogBundle(catalogId)
      if (this.selectedCatalogId() === catalogId) this.selectedBundle.set(bundle)
      const rows = new Map(mapResources(bundle.catalog).map(row => [row.id, row]))
      this.selectedResourceRows.update(current => new Map([...current].map(([key, selection]) => {
        const row = selection.catalogId === catalogId ? rows.get(selection.id) : undefined
        return [key, row ? { ...selection, unit: row.unit, description: row.description, warning: row.warning } : selection]
      })))
      const units = new Map(this.visualizationResources().map(resource => [resource.path, resource.unit]))
      this.visualizationData.update(data => data ? {
        ...data, series: data.series.map(series => units.has(series.id) ? { ...series, unit: units.get(series.id)! } : series),
      } : data)
      return {}
    } catch (error) {
      // Persistence succeeded: report refresh separately rather than inviting another write.
      if (this.selectedCatalogId() === catalogId) {
        this.selectedBundle.set(null)
        this.catalogError.set(new Error('Metadata saved, but catalog refresh failed. Select the catalog again to retry.'))
      }
      return { warning: `Could not refresh catalog metadata: ${this.errorMessage(error)} Select the catalog again to retry.` }
    }
  }

  async restoreSelectedResources() {
    const generation = this.catalogCacheGeneration
    if (this.selectionRestoreGeneration === generation) return
    this.selectionRestoreGeneration = generation
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
    if (generation !== this.catalogCacheGeneration) return
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
    this.selectionRestoreGeneration = null
    this.selectedResourcesRestored.set(true)
  }

  selectCatalog(catalog: CatalogNode) {
    if (catalog.isFake) {
      this.toggleExpanded(catalog)
      return
    }

    const catalogId = catalog.id ?? '/'
    const select = () => {
      this.selectedCatalogId.set(catalogId)
      this.selectedCatalogNodeKey.set(catalog.nodeKey)
      this.selectedCatalogInfo.set(catalog)
      this.catalogHistoryPosition = writeSelectedCatalogToUrl(catalogId)
      this.isMobileCatalogOpen.set(false)
      this.activeResourcePath.set('')
      this.revealResourceKey.set('')
      if (!this.selectedBundle() && !this.catalogLoading())
        void this.loadSelectedCatalog(catalogId, false, this.apiAvailable())
      void this.loadChildren(catalogId)
    }
    if (catalogId === this.selectedCatalogId()) select()
    else this.requestCatalogNavigation(select)
  }

  selectPinnedResourceCatalog(resource: ResourceSelection) {
    const catalogId = resource.catalogId
    const select = () => {
      const catalog = this.searchableCatalogNodes().find(node => !node.isFake && node.id === catalogId)
      this.selectedCatalogId.set(catalogId)
      this.selectedCatalogNodeKey.set(getRealCatalogNodeKey(catalogId))
      this.selectedCatalogInfo.set(catalog ?? null)
      this.catalogHistoryPosition = writeSelectedCatalogToUrl(catalogId)
      this.isMobileCatalogOpen.set(false)
      this.expandCatalogPath(catalogId)
      if (this.apiAvailable()) void this.loadCatalogPathChildren(catalogId)
      const key = selectionKey(resource, {})
      this.activeResourcePath.set(key)
      this.revealResourceKey.set(key)
      this.revealResourceSequence.update(value => value + 1)
    }
    if (catalogId === this.selectedCatalogId()) select()
    else this.requestCatalogNavigation(select)
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
    const generation = this.catalogCacheGeneration
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
      if (generation !== this.catalogCacheGeneration) return
      currentInfos = this.childMap().get(ancestor) ?? []
      currentParent = ancestor
    }
  }

  async loadChildren(catalogId: string) {
    if (!this.apiAvailable() || this.childMap().has(catalogId)) return

    const existing = this.childRequests.get(catalogId)
    if (existing) return existing

    const generation = this.catalogCacheGeneration
    const request = (async () => {
      try {
        const children = await this.nexus.getCatalogChildren(catalogId)
        if (generation === this.catalogCacheGeneration) this.childMap.update((current) => new Map(current).set(catalogId, children))
      } catch {
        if (generation === this.catalogCacheGeneration) this.childMap.update((current) => new Map(current).set(catalogId, []))
      } finally {
        if (generation === this.catalogCacheGeneration) this.childRequests.delete(catalogId)
      }
    })()

    this.childRequests.set(catalogId, request)
    return request
  }

  toggleResource(resource: RepresentationRow) {
    if (this.selectionLoading()) return
    if (this.resourceSelected(resource)) this.removeResource(resource.key)
    else {
      if (this.requiresParameters(resource)) {
        this.openParameterDialog(resource)
        return
      }
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
    if (!this.requiresParameters(resource)) return Object.keys(resource.parameters).length === 0

    const expectedKeys = Object.keys(resource.representation.parameters ?? {}).sort()
    const actualKeys = Object.keys(resource.parameters).sort()
    return expectedKeys.length === actualKeys.length
      && expectedKeys.every((key, index) => key === actualKeys[index] && resource.parameters[key] !== '')
  }

  openParameterDialog(resource: RepresentationRow) {
    this.parameterResource.set(resource)
    this.parameterDraft.set(Object.fromEntries(this.toParameterFields(resource).map(field => [field.key, field.defaultValue])))
  }

  closeParameterDialog() {
    this.parameterResource.set(null)
    this.parameterDraft.set({})
  }

  onParameterDialogVisible(visible: boolean) {
    if (!visible) this.closeParameterDialog()
  }

  setParameterValue(key: string, value: string) {
    this.parameterDraft.update(current => ({ ...current, [key]: value }))
  }

  inputValue(event: Event) {
    return (event.target as HTMLInputElement | HTMLSelectElement).value
  }

  selectOptions(field: ParameterField) {
    return Object.entries(field.items ?? {})
  }

  parameterSummary(resource: ResourceSelection) {
    return Object.entries(resource.parameters).map(([key, value]) => `${key}=${value}`).join(', ')
  }

  addParameterizedResource() {
    const resource = this.parameterResource()
    if (!resource || this.parameterDialogError()) return

    if (this.pinnedCount() === 0 && this.automaticPeriod()) {
      this.samplePeriod.set(resource.basePeriod)
      this.periodDraft.set(formatPeriod(resource.basePeriod))
    }

    const parameters = Object.fromEntries(this.parameterFields().map(field => [field.key, this.parameterDraft()[field.key]]))
    const key = selectionKey(resource, parameters)
    const selection: ResourceSelection = { ...resource, key, parameters, kinds: [defaultKind(this.samplePeriod(), resource.basePeriod)] }

    this.selectedResourceRows.update(current => new Map(current).set(selection.key, selection))
    this.selectionReferences.update(current => current.some(reference => this.referenceMatches(reference, selection))
      ? current
      : [...current, storeSelectionReference(selection)])
    this.activeResourcePath.set(selection.key)
    this.closeParameterDialog()
  }

  private toParameterFields(resource: RepresentationRow | null): ParameterField[] {
    return Object.entries(resource?.representation.parameters ?? {}).flatMap<ParameterField>(([key, value]) => {
      if (!this.isRecord(value) || typeof value['type'] !== 'string') return []

      const label = typeof value['label'] === 'string' ? value['label'] : key

      if (value['type'] === 'input-integer') {
        const minimum = typeof value['minimum'] === 'number' ? value['minimum'] : undefined
        const maximum = typeof value['maximum'] === 'number' ? value['maximum'] : undefined
        const defaultValue = typeof value['default'] === 'number'
          ? String(value['default'])
          : String(minimum ?? 0)

        return [{ key, label, kind: 'input-integer', defaultValue, minimum, maximum } satisfies ParameterField]
      }

      if (value['type'] === 'select' && this.isRecord(value['items'])) {
        const items = Object.fromEntries(Object.entries(value['items']).filter((entry): entry is [string, string] => typeof entry[1] === 'string'))
        const defaultValue = typeof value['default'] === 'string' && Object.hasOwn(items, value['default'])
          ? value['default']
          : Object.keys(items)[0] ?? ''

        return [{ key, label, kind: 'select', defaultValue, items } satisfies ParameterField]
      }

      return []
    })
  }

  private isRecord(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === 'object' && !Array.isArray(value)
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

  setExportBeginFromInput(value: string) {
    this.exportBegin.set(fromDateTimeLocalValue(value))
  }

  setExportEndFromInput(value: string) {
    this.exportEnd.set(fromDateTimeLocalValue(value))
  }

  setExportFilePeriod(value: string) {
    this.exportFilePeriodDraft.set(value)
    const period = parseFilePeriod(value)
    if (period !== null && period % this.samplePeriod() === 0n) this.exportFilePeriod.set(formatPeriod(period))
  }

  normalizeExportFilePeriod() {
    if (!this.exportFilePeriodError()) this.exportFilePeriodDraft.set(formatFilePeriod(parsePeriod(this.exportFilePeriod()) ?? 0n))
  }

  async openLicenseDialog() {
    const catalogId = this.selectedCatalogId()
    if (!catalogId || this.isSelectedFake()) return

    this.isLicenseOpen.set(true)
    this.licenseLoading.set(true)
    this.licenseError.set('')
    this.licenseText.set('')
    try {
      this.licenseText.set(await this.nexus.getCatalogLicense(catalogId))
    } catch (error) {
      this.licenseError.set(this.errorMessage(error))
    } finally {
      this.licenseLoading.set(false)
    }
  }

  async acceptSelectedCatalogLicense() {
    const catalogId = this.selectedCatalogId()
    if (!catalogId || this.licenseAccepting()) return

    this.licenseAccepting.set(true)
    this.licenseError.set('')
    try {
      await this.nexus.acceptCatalogLicense(catalogId)
      this.catalogCacheGeneration++
      this.catalogLoadGeneration++
      this.catalogBundleCache.clear()
      this.catalogBundleRequests.clear()
      this.childRequests.clear()
      this.childMap.set(new Map())
      this.selectedCatalogInfo.set(null)
      this.selectedBundle.set(null)
      await this.loadOverview()
      await this.loadCatalogPathChildren(catalogId)
      await this.loadSelectedCatalog(catalogId, false, this.apiAvailable())
      this.isLicenseOpen.set(false)
    } catch (error) {
      this.licenseError.set(this.errorMessage(error))
    } finally {
      this.licenseAccepting.set(false)
    }
  }

  async uploadCatalogFiles(files: FileList | File[] | null | undefined) {
    const catalogId = this.selectedCatalogId()
    const selectedFiles = files ? Array.from(files) : []
    if (!catalogId || !this.selectedCatalogWritable() || this.catalogFilesBusy() || selectedFiles.length === 0) return

    this.catalogFilesBusy.set(true)
    this.catalogFilesError.set('')
    try {
      for (const file of selectedFiles) await this.nexus.uploadCatalogAttachment(catalogId, file.name, file)
      this.catalogBundleCache.delete(catalogId)
      await this.loadSelectedCatalog(catalogId, this.isSelectedFake(), this.apiAvailable())
    } catch (error) {
      this.catalogFilesError.set(this.errorMessage(error))
    } finally {
      this.catalogFilesBusy.set(false)
      this.catalogFilesDragActive.set(false)
    }
  }

  onCatalogFilesDragOver(event: DragEvent) {
    if (!this.selectedCatalogWritable() || this.catalogFilesBusy()) return
    event.preventDefault()
    this.catalogFilesDragActive.set(true)
  }

  onCatalogFilesDragLeave(event: DragEvent) {
    event.preventDefault()
    this.catalogFilesDragActive.set(false)
  }

  async onCatalogFilesDrop(event: DragEvent) {
    event.preventDefault()
    this.catalogFilesDragActive.set(false)
    await this.uploadCatalogFiles(event.dataTransfer?.files)
  }

  requestDeleteCatalogAttachment(attachmentId: string) {
    if (!this.selectedCatalogWritable() || this.catalogFilesBusy() || this.deletingAttachmentId()) return
    this.pendingDeleteAttachmentId.set(attachmentId)
  }

  cancelDeleteCatalogAttachment() {
    if (!this.deletingAttachmentId()) this.pendingDeleteAttachmentId.set('')
  }

  async confirmDeleteCatalogAttachment() {
    await this.deleteCatalogAttachment(this.pendingDeleteAttachmentId())
  }

  private async deleteCatalogAttachment(attachmentId: string) {
    const catalogId = this.selectedCatalogId()
    if (!attachmentId || !catalogId || !this.selectedCatalogWritable() || this.catalogFilesBusy() || this.deletingAttachmentId()) return

    this.deletingAttachmentId.set(attachmentId)
    this.catalogFilesError.set('')
    try {
      await this.nexus.deleteCatalogAttachment(catalogId, attachmentId)
      this.catalogBundleCache.delete(catalogId)
      await this.loadSelectedCatalog(catalogId, this.isSelectedFake(), this.apiAvailable())
    } catch (error) {
      this.catalogFilesError.set(this.errorMessage(error))
    } finally {
      this.deletingAttachmentId.set('')
      this.pendingDeleteAttachmentId.set('')
    }
  }

  setSelectedWriterType(value: string) {
    const previous = this.selectedWriterType()
    this.configurationByWriter.update((current) => ({ ...current, [previous]: this.exportConfiguration() }))
    this.selectedWriterType.set(value)
    this.exportConfiguration.set(this.configurationByWriter()[value] ?? {})
  }

  setExportPrecision(value: V2.Precision) {
    this.exportPrecision.set(value)
  }

  updateConfig(key: string, value: unknown) {
    this.exportConfiguration.update((current) => {
      const next = { ...current, [key]: value }
      this.configurationByWriter.update((stored) => ({ ...stored, [this.selectedWriterType()]: next }))
      return next
    })
  }

  async createExportJob() {
    if (this.exportBusy()) return
    if (this.exportError()) {
      this.exportStatus.set(this.exportError())
      return
    }

    this.resetCurrentExportJob()
    const controller = new AbortController()
    this.exportController = controller
    const parameters = this.exportPreview()
    this.exportBusy.set(true)
    this.exportStatus.set('')
    this.currentExportJobError.set('')
    try {
      const job = await this.nexus.v2.jobs.export(parameters, controller.signal)
      if (!job.id) throw new Error('The export job did not return an ID.')
      this.currentExportJobId.set(job.id)
      this.upsertJobHistory(job, parameters)
      await this.pollCurrentExportJob(job.id, parameters, controller)
    } catch (error) {
      if (!controller.signal.aborted) this.currentExportJobError.set(this.errorMessage(error))
    } finally {
      if (this.exportController === controller) {
        this.exportBusy.set(false)
        this.exportController = undefined
      }
    }
  }

  openJobs() {
    this.isJobsOpen.set(true)
    void this.refreshJobHistoryStatuses()
  }

  closeExportComposer() {
    this.isExportOpen.set(false)
    this.resetCurrentExportJob()
  }

  async cancelCurrentExportJob() {
    const jobId = this.currentExportJobId()
    if (!jobId) return
    this.exportController?.abort()
    this.exportBusy.set(false)
    this.currentExportJobError.set('Canceling export job...')
    try {
      await this.cancelJobById(jobId)
      const status = await this.nexus.v1.jobs.getJobStatus(jobId)
      this.currentExportJobStatus.set(status)
      this.updateJobHistory(jobId, { status, error: '' })
      this.currentExportJobError.set('The export job has been canceled.')
    } catch (error) {
      this.currentExportJobError.set(this.errorMessage(error))
      this.updateJobHistory(jobId, { error: this.errorMessage(error) })
    }
  }

  async downloadCurrentExportJob() {
    const jobId = this.currentExportJobId()
    const artifactId = this.artifactIdFromStatus(this.currentExportJobStatus() ?? undefined)
    if (!jobId || !artifactId) return

    this.currentExportDownloading.set(true)
    try {
      const entry = this.jobHistory().find(job => job.id === jobId)
      await this.downloadArtifact(artifactId, entry?.downloadName ?? this.exportDownloadName())
    } catch (error) {
      this.currentExportJobError.set(this.errorMessage(error))
    } finally {
      this.currentExportDownloading.set(false)
    }
  }

  async refreshJobStatus(entry: ExportJobHistoryEntry) {
    try {
      const status = await this.nexus.v1.jobs.getJobStatus(entry.id)
      this.updateJobHistory(entry.id, { status, error: '' })
    } catch (error) {
      this.updateJobHistory(entry.id, { error: this.errorMessage(error) })
    }
  }

  async cancelJob(entry: ExportJobHistoryEntry) {
    try {
      await this.cancelJobById(entry.id)
      const status = await this.nexus.v1.jobs.getJobStatus(entry.id)
      this.updateJobHistory(entry.id, { status, error: '' })
      if (this.currentExportJobId() === entry.id) this.currentExportJobStatus.set(status)
    } catch (error) {
      this.updateJobHistory(entry.id, { error: this.errorMessage(error) })
    }
  }

  async downloadJob(entry: ExportJobHistoryEntry) {
    const artifactId = this.artifactIdFromStatus(entry.status)
    if (!artifactId) return

    this.updateJobHistory(entry.id, { downloading: true, error: '' })
    try {
      await this.downloadArtifact(artifactId, entry.downloadName)
    } catch (error) {
      this.updateJobHistory(entry.id, { error: this.errorMessage(error) })
    } finally {
      this.updateJobHistory(entry.id, { downloading: false })
    }
  }

  jobProgress(status?: V1.JobStatus) {
    const progress = status?.status === V1.TaskStatus.RanToCompletion ? 1 : status?.progress
    if (typeof progress !== 'number' || !Number.isFinite(progress)) return 0
    return Math.max(0, Math.min(100, Math.round(progress * 100)))
  }

  jobStatusLabel(entry: ExportJobHistoryEntry) {
    return entry.status ? this.formatJobStatus(entry.status) : 'Status not loaded.'
  }

  jobStartLabel(entry: ExportJobHistoryEntry) {
    const value = entry.status?.start
    if (!value) return 'Start time pending'
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
  }

  jobDetailsJson(entry: ExportJobHistoryEntry) {
    return JSON.stringify({
      owner: entry.job.owner,
      start: entry.status?.start,
      status: entry.status?.status,
      progress: entry.status?.progress,
      exceptionMessage: entry.status?.exceptionMessage,
      result: entry.status?.result,
      parameters: entry.parameters,
    }, null, 2)
  }

  jobCanCancel(entry: ExportJobHistoryEntry) {
    return !this.isTerminalStatus(entry.status?.status)
  }

  jobCanDownload(entry: ExportJobHistoryEntry) {
    return !!this.artifactIdFromStatus(entry.status)
  }

  private async pollCurrentExportJob(jobId: string, parameters: V2.ExportParameters, controller: AbortController) {
    for (;;) {
      await this.delay(1000, controller.signal)
      const status = await this.nexus.v1.jobs.getJobStatus(jobId, controller.signal)
      if (this.exportController !== controller) return
      this.currentExportJobStatus.set(status)
      this.updateJobHistory(jobId, { status, error: '' })

      if (status.status === V1.TaskStatus.RanToCompletion) {
        const artifactId = this.artifactIdFromStatus(status)
        if (!artifactId) throw new Error('The completed export job did not return an artifact ID.')
        await this.downloadArtifact(artifactId, this.exportDownloadName(parameters))
        return
      }
      if (status.status === V1.TaskStatus.Canceled) throw new Error('The export job has been canceled.')
      if (status.status === V1.TaskStatus.Faulted) throw new Error(`The export job failed. Reason: ${status.exceptionMessage ?? 'unknown'}`)
    }
  }

  private resetCurrentExportJob() {
    this.exportController?.abort()
    this.exportController = undefined
    this.exportBusy.set(false)
    this.exportStatus.set('')
    this.currentExportJobId.set('')
    this.currentExportJobStatus.set(null)
    this.currentExportJobError.set('')
    this.currentExportDownloading.set(false)
  }

  private async cancelJobById(jobId: string) {
    await this.nexus.v1.jobs.cancelJob(jobId)
  }

  private async refreshJobHistoryStatuses() {
    await Promise.all(this.jobHistory().map(entry => this.refreshJobStatus(entry)))
  }

  private mergeJobHistory(jobs: (V1.Job | V2.Job)[]) {
    if (!jobs.length) return
    this.jobHistory.update((current) => {
      const entries = new Map(current.map(entry => [entry.id, entry]))
      for (const job of jobs) {
        if (!job.id) continue
        if (!this.isExportJob(job)) continue
        entries.set(job.id, entries.get(job.id) ?? this.createJobHistoryEntry(job))
      }
      return [...entries.values()].slice(-20).reverse()
    })
  }

  private upsertJobHistory(job: V1.Job | V2.Job, parameters?: V2.ExportParameters) {
    if (!job.id) return
    this.jobHistory.update((current) => {
      const existing = current.find(entry => entry.id === job.id)
      const entry = existing
        ? { ...existing, job, parameters: parameters ?? existing.parameters, downloadName: this.exportDownloadName(parameters ?? existing.parameters) }
        : this.createJobHistoryEntry(job, parameters)
      return [entry, ...current.filter(item => item.id !== job.id)].slice(0, 20)
    })
  }

  private updateJobHistory(id: string, patch: Partial<ExportJobHistoryEntry>) {
    this.jobHistory.update(current => current.map(entry => entry.id === id ? { ...entry, ...patch } : entry))
  }

  private createJobHistoryEntry(job: V1.Job | V2.Job, parameters = this.exportParametersFromJob(job)): ExportJobHistoryEntry {
    return {
      id: job.id ?? '',
      job,
      parameters,
      error: '',
      downloading: false,
      downloadName: this.exportDownloadName(parameters),
    }
  }

  private exportParametersFromJob(job: V1.Job | V2.Job): V2.ExportParameters | undefined {
    return isExportParameters(job.parameters) ? job.parameters : undefined
  }

  private isExportJobEntry(entry: ExportJobHistoryEntry) {
    return this.isExportJob(entry.job, entry.parameters)
  }

  private isExportJob(job: V1.Job | V2.Job, parameters = this.exportParametersFromJob(job)) {
    return !!parameters || job.type?.toLowerCase().includes('export') === true
  }

  private formatJobStatus(status: V1.JobStatus) {
    if (status.status === V1.TaskStatus.RanToCompletion) return 'Completed.'
    if (status.status === V1.TaskStatus.Canceled) return 'Canceled.'
    if (status.status === V1.TaskStatus.Faulted) return `Failed: ${status.exceptionMessage ?? 'unknown'}`
    return `${status.status ?? 'Pending'} (${this.jobProgress(status)}%)`
  }

  private isTerminalStatus(status?: V1.TaskStatus) {
    return status === V1.TaskStatus.RanToCompletion || status === V1.TaskStatus.Canceled || status === V1.TaskStatus.Faulted
  }

  private artifactIdFromStatus(status?: V1.JobStatus) {
    return status?.status === V1.TaskStatus.RanToCompletion && typeof status.result === 'string' ? status.result : ''
  }

  private async downloadArtifact(artifactId: string, downloadName: string) {
    const response = await this.nexus.v1.artifacts.download(artifactId)
    if (!response.ok) throw new Error(`Download failed with HTTP ${response.status}.`)
    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const anchor = this.document.createElement('a')
    anchor.href = url
    anchor.download = downloadName
    this.document.body.append(anchor)
    anchor.click()
    anchor.remove()
    URL.revokeObjectURL(url)
  }

  private exportDownloadName(parameters = this.exportPreview()) {
    const begin = formatDateForDownloadName(parameters.begin ?? this.exportBegin())
    const period = (parameters.filePeriod ?? this.exportFilePeriod()).replace(/\s+/g, '_')
    return `Nexus_${begin}_${period}.zip`
  }

  private delay(milliseconds: number, signal: AbortSignal) {
    return new Promise<void>((resolve, reject) => {
      const abort = () => { clearTimeout(timer); reject(signal.reason ?? new DOMException('Aborted', 'AbortError')) }
      const timer = setTimeout(() => { signal.removeEventListener('abort', abort); resolve() }, milliseconds)
      signal.addEventListener('abort', abort, { once: true })
      if (signal.aborted) abort()
    })
  }

  copyCatalogPath() {
    const catalogId = this.selectedCatalogId()
    if (!catalogId || !navigator.clipboard) return

    void navigator.clipboard.writeText(catalogId).then(() => this.messageService.add({ key: 'app-status', severity: 'success', summary: 'Catalog path copied', life: 1800 }))
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

function isExportParameters(value: unknown): value is V2.ExportParameters {
  if (!value || typeof value !== 'object') return false
  const parameters = value as V2.ExportParameters
  return typeof parameters.begin === 'string' && typeof parameters.end === 'string'
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

function getStoredSelectionRequestPaths(selections: StoredSelectionReference[], period: bigint) {
  return selections.flatMap(selection => selection.kinds.map(kind => formatStoredSelectionRequestPath(selection, kind, period)))
}

function formatStoredSelectionRequestPath(selection: StoredSelectionReference, kind: RepresentationKind, period: bigint) {
  const suffix = kind === 'Original' ? '' : `_${kind.replace(/([a-z])([A-Z])/g, '$1_$2').toLowerCase()}`
  const entries = Object.keys(selection.parameters).sort().map((name) => [name, selection.parameters[name]])
  const parameters = entries.length ? `(${entries.map(([name, value]) => `${name}=${value}`).join(',')})` : ''
  const base = selection.basePeriod ? `#base=${selection.basePeriod.replace(/\s+/g, '_')}` : ''
  return `${selection.path}/${formatPeriod(period, '_')}${suffix}${parameters}${base}`
}

function getResourcePathCatalogCandidates(path: string) {
  const segments = path.split('/').filter(Boolean)
  const candidates: string[] = []
  for (let count = segments.length - 1; count >= 0; count -= 1) {
    candidates.push(count === 0 ? '/' : `/${segments.slice(0, count).join('/')}`)
  }
  return candidates
}

function readSetupExportSettings(value: unknown): Partial<StoredExportSettings> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return {}
  const settings = value as NexusUiSetup
  const result: Partial<StoredExportSettings> = {}
  if (typeof settings.begin === 'string') result.begin = settings.begin
  if (typeof settings.end === 'string') result.end = settings.end
  if (typeof settings.filePeriod === 'string') result.exportFilePeriod = settings.filePeriod
  if (typeof settings.type === 'string' && settings.type) result.selectedWriterType = settings.type
  if (settings.precision === V2.Precision.Float64 || settings.precision === V2.Precision.Float32) result.exportPrecision = settings.precision
  if (Array.isArray(settings.resourcePaths)) result.resourcePaths = settings.resourcePaths.filter((path): path is string => typeof path === 'string')
  if (settings.configuration && typeof settings.configuration === 'object' && !Array.isArray(settings.configuration) && result.selectedWriterType) {
    result.configurationByWriter = { [result.selectedWriterType]: { ...(settings.configuration as Record<string, unknown>) } }
  }
  return result
}

function isValidDateString(value: string) {
  return !Number.isNaN(new Date(value).getTime())
}

function isOutsideViewport(event: DragEvent) {
  return event.clientX <= 0 || event.clientY <= 0 || event.clientX >= window.innerWidth || event.clientY >= window.innerHeight
}

function hasSetupFile(dataTransfer: DataTransfer | null) {
  if (!dataTransfer) return false
  if (Array.from(dataTransfer.files).some(isSetupFile)) return true
  return Array.from(dataTransfer.items).some(item => item.kind === 'file' && (item.type === 'application/json' || item.type === ''))
}

function isSetupFile(file: File) {
  const name = file.name.toLowerCase()
  return name.endsWith('.nexus.json') || name.endsWith('.json') || file.type === 'application/json'
}

function getStoredCatalogNodeKeys(storage: BrowserStorageService) {
  const storedKeys = storage.getJson<unknown>(catalogExpansionStorageKey, [])
  return Array.isArray(storedKeys) ? storedKeys.filter((key): key is string => typeof key === 'string') : []
}

function getInitialThemeMode(storage: BrowserStorageService): ThemeMode {
  const value = storage.getJson<string>(themeModeStorageKey, 'system')
  return value === 'dark' || value === 'light' || value === 'system' ? value : 'system'
}

function getStoredExportSettings(storage: BrowserStorageService): StoredExportSettings {
  const stored = storage.getJson<unknown>(exportSettingsStorageKey, null)
  const defaults: StoredExportSettings = {
    selectedWriterType: defaultWriterType,
    exportFilePeriod: 'PT0S',
    exportPrecision: V2.Precision.Float32,
    configurationByWriter: { [defaultWriterType]: { 'row-index-format': 'excel', 'significant-figures': 4 } },
  }
  if (!stored || typeof stored !== 'object') return defaults

  const value = stored as Partial<StoredExportSettings>
  const selectedWriterType = typeof value.selectedWriterType === 'string' && value.selectedWriterType
    ? value.selectedWriterType
    : defaults.selectedWriterType
  return {
    selectedWriterType,
    exportFilePeriod: typeof value.exportFilePeriod === 'string' ? value.exportFilePeriod : defaults.exportFilePeriod,
    exportPrecision: value.exportPrecision === V2.Precision.Float64 ? V2.Precision.Float64 : V2.Precision.Float32,
    configurationByWriter: getStoredWriterConfigurations(value.configurationByWriter, defaults.configurationByWriter),
  }
}

function getStoredWriterConfigurations(value: unknown, fallback: Record<string, Record<string, unknown>>) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return fallback
  const result: Record<string, Record<string, unknown>> = {}
  for (const [writerType, configuration] of Object.entries(value)) {
    if (typeof writerType === 'string' && configuration && typeof configuration === 'object' && !Array.isArray(configuration)) {
      result[writerType] = { ...(configuration as Record<string, unknown>) }
    }
  }
  return Object.keys(result).length ? result : fallback
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
  const position = window.history.state?.nexusCatalogPosition ?? 0
  if (url.searchParams.get('catalog') === catalogId && window.history.state?.nexusCatalogPosition !== undefined) return position
  const nextPosition = replace ? position : position + 1
  url.searchParams.set('catalog', catalogId)
  window.history[replace ? 'replaceState' : 'pushState']({ ...window.history.state, nexusCatalogPosition: nextPosition }, '', `${url.pathname}${url.search}${url.hash}`)
  return nextPosition
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

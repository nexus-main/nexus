import { useDeferredValue, useState, useTransition } from 'react'
import { useMutation, useQueries, useQuery } from '@tanstack/react-query'
import {
  Activity,
  Archive,
  Check,
  ChevronDown,
  ChevronsUpDown,
  CircleAlert,
  Clock3,
  Database,
  Download,
  FileJson,
  Filter,
  Gauge,
  Layers3,
  Loader2,
  Lock,
  Menu,
  PackageCheck,
  PanelLeftClose,
  RadioTower,
  Search,
  ShieldCheck,
  Sparkles,
  SquareArrowOutUpRight,
  X,
} from 'lucide-react'
import {
  buildExportParameters,
  getCatalogBundle,
  getCatalogChildren,
  getSessionOverview,
  hasConfiguredToken,
  mapResources,
  nexusClient,
  nexusEndpoint,
  type CatalogNode,
  type ResourceRow,
  type WriterDescription,
  V1,
  V2,
} from './lib/nexus'
import { cn, compactPath, formatNumber } from './lib/utils'

const quickRanges = [
  { label: 'Last 10 min', begin: '-PT10M', end: 'now' },
  { label: 'Last hour', begin: '-PT1H', end: 'now' },
  { label: 'Campaign day', begin: '2025-01-01T00:00:00Z', end: '2025-01-02T00:00:00Z' },
]

const fallbackCatalogs: CatalogNode[] = [
  {
    id: '/SCADA_OLD',
    title: 'Messdaten der HLB SPS',
    isReadable: true,
    isWritable: false,
    isReleased: true,
    isVisible: true,
    isOwner: true,
    depth: 0,
    parentId: '/',
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
    depth: 0,
    parentId: '/',
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
    depth: 0,
    parentId: '/',
    pipelineInfo: { types: ['Nexus.Sources.Sample'] },
  },
]

const fallbackResources: ResourceRow[] = [
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

const fallbackWriters: WriterDescription[] = [
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

function App() {
  const [selectedCatalogId, setSelectedCatalogId] = useState('/SAMPLE/LOCAL')
  const [expandedCatalogIds, setExpandedCatalogIds] = useState<Set<string>>(() => new Set(['/']))
  const [catalogSearch, setCatalogSearch] = useState('')
  const [resourceSearch, setResourceSearch] = useState('')
  const [selectedResourcePaths, setSelectedResourcePaths] = useState<Set<string>>(() => new Set(['/SAMPLE/LOCAL/T1', '/SAMPLE/LOCAL/V1']))
  const [activeResourcePath, setActiveResourcePath] = useState('/SAMPLE/LOCAL/T1')
  const [isExportOpen, setIsExportOpen] = useState(false)
  const [isMobileCatalogOpen, setIsMobileCatalogOpen] = useState(false)
  const [isPending, startTransition] = useTransition()
  const deferredCatalogSearch = useDeferredValue(catalogSearch)
  const deferredResourceSearch = useDeferredValue(resourceSearch)

  const overviewQuery = useQuery({
    queryKey: ['overview'],
    queryFn: getSessionOverview,
  })

  const rootCatalogs = overviewQuery.data?.roots ?? fallbackCatalogs
  const writerDescriptions = overviewQuery.data?.writers ?? fallbackWriters
  const jobs = overviewQuery.data?.jobs ?? []
  const userName = overviewQuery.data?.me.user?.name ?? 'Prototype user'
  const userId = overviewQuery.data?.me.userId ?? 'not connected'

  const expandedIds = Array.from(expandedCatalogIds).filter((id) => id !== '/')
  const childQueries = useQueries({
    queries: expandedIds.map((catalogId) => ({
      queryKey: ['catalog-children', catalogId],
      queryFn: () => getCatalogChildren(catalogId),
      enabled: hasConfiguredToken,
      staleTime: 5 * 60_000,
    })),
  })

  const childMap = new Map<string, V1.CatalogInfo[]>()
  childQueries.forEach((query, index) => {
    if (query.data) {
      childMap.set(expandedIds[index], query.data)
    }
  })

  const catalogNodes: CatalogNode[] = []
  const appendCatalogNodes = (items: V1.CatalogInfo[], parentId: string, depth: number) => {
    for (const item of items) {
      catalogNodes.push({ ...item, depth, parentId })

      if (item.id && expandedCatalogIds.has(item.id)) {
        appendCatalogNodes(childMap.get(item.id) ?? [], item.id, depth + 1)
      }
    }
  }
  appendCatalogNodes(rootCatalogs, '/', 0)

  const catalogTerm = deferredCatalogSearch.trim().toLowerCase()
  const filteredCatalogNodes = catalogTerm
    ? catalogNodes.filter((node) => `${node.id ?? ''} ${node.title ?? ''} ${node.pipelineInfo?.types?.join(' ') ?? ''}`.toLowerCase().includes(catalogTerm))
    : catalogNodes

  const selectedCatalogQuery = useQuery({
    queryKey: ['catalog', selectedCatalogId],
    queryFn: () => getCatalogBundle(selectedCatalogId),
    enabled: hasConfiguredToken && Boolean(selectedCatalogId),
  })

  const liveRows = selectedCatalogQuery.data?.catalog ? mapResources(selectedCatalogQuery.data.catalog) : []
  const resourceRows = hasConfiguredToken ? liveRows : fallbackResources

  const resourceTerm = deferredResourceSearch.trim().toLowerCase()
  const filteredResources = (resourceTerm
    ? resourceRows.filter((row) => `${row.id} ${row.path} ${row.description} ${row.groups.join(' ')} ${row.unit}`.toLowerCase().includes(resourceTerm))
    : [...resourceRows]
  ).sort((a, b) => a.id.localeCompare(b.id))

  const activeResource = resourceRows.find((resource) => resource.path === activeResourcePath) ?? filteredResources[0]
  const selectedResources = resourceRows.filter((resource) => selectedResourcePaths.has(resource.path))
  const selectedDataTypes = new Set(selectedResources.flatMap((resource) => resource.representations.map((rep) => rep.dataType).filter(Boolean)))
  const groupCount = new Set(resourceRows.flatMap((resource) => resource.groups)).size
  const readableCatalogCount = catalogNodes.filter((node) => node.isReadable).length

  function selectCatalog(catalogId: string) {
    startTransition(() => {
      setSelectedCatalogId(catalogId)
      setIsMobileCatalogOpen(false)
      setSelectedResourcePaths(new Set())
      setActiveResourcePath('')
    })
  }

  function toggleExpanded(catalogId: string) {
    setExpandedCatalogIds((current) => {
      const next = new Set(current)
      if (next.has(catalogId)) {
        next.delete(catalogId)
      } else {
        next.add(catalogId)
      }

      return next
    })
  }

  function toggleResource(resource: ResourceRow) {
    setSelectedResourcePaths((current) => {
      const next = new Set(current)
      if (next.has(resource.path)) {
        next.delete(resource.path)
      } else {
        next.add(resource.path)
      }

      return next
    })
    setActiveResourcePath(resource.path)
  }

  return (
    <div className="min-h-screen text-slate-100">
      <div className="fixed inset-0 -z-10 opacity-80 scanline" />
      <div className="mx-auto flex min-h-screen w-full max-w-[1800px] flex-col gap-4 p-3 sm:p-4 xl:p-5">
        <header className="glass-panel overflow-hidden rounded-[2rem]">
          <div className="flex flex-col gap-5 p-4 sm:p-5 lg:flex-row lg:items-center lg:justify-between">
            <div className="flex items-center gap-4">
              <button
                type="button"
                className="rounded-2xl border border-cyan-300/20 bg-cyan-300/10 p-3 text-cyan-200 lg:hidden"
                onClick={() => setIsMobileCatalogOpen(true)}
                aria-label="Open catalog browser"
              >
                <Menu className="h-5 w-5" />
              </button>
              <div className="relative grid h-13 w-13 place-items-center rounded-2xl border border-cyan-300/30 bg-cyan-300/10 shadow-[0_0_42px_rgba(34,211,238,0.24)]">
                <RadioTower className="h-7 w-7 text-cyan-200" />
                <div className="absolute -right-1 -top-1 h-4 w-4 rounded-full border-2 border-[#080d1a] bg-lime-300" />
              </div>
              <div>
                <div className="flex flex-wrap items-center gap-2 text-xs uppercase tracking-[0.28em] text-cyan-200/80">
                  Nexus Command Surface
                  <span className="rounded-full bg-lime-300/10 px-2 py-0.5 text-[10px] tracking-[0.2em] text-lime-200">live api</span>
                </div>
                <h1 className="mt-1 text-2xl font-semibold tracking-[-0.04em] text-white sm:text-4xl">Catalog intelligence, not catalog clutter.</h1>
              </div>
            </div>

            <div className="grid gap-2 text-sm sm:grid-cols-3 lg:min-w-[560px]">
              <Metric label="Endpoint" value={new URL(nexusEndpoint).host} icon={<ShieldCheck className="h-4 w-4" />} tone="cyan" />
              <Metric label="Identity" value={userName} hint={userId} icon={<Lock className="h-4 w-4" />} tone="violet" />
              <Metric label="Selected" value={`${selectedResourcePaths.size} resources`} hint={`${selectedDataTypes.size} data types`} icon={<Check className="h-4 w-4" />} tone="lime" />
            </div>
          </div>
        </header>

        {!hasConfiguredToken ? <TokenNotice /> : null}
        {overviewQuery.error || selectedCatalogQuery.error ? <ApiErrorNotice error={overviewQuery.error ?? selectedCatalogQuery.error} /> : null}

        <main className="grid min-h-0 flex-1 gap-4 lg:grid-cols-[380px_minmax(0,1fr)] 2xl:grid-cols-[430px_minmax(0,1fr)]">
          <aside className="hidden min-h-[calc(100vh-190px)] lg:block">
            <CatalogBrowser
              nodes={filteredCatalogNodes}
              selectedCatalogId={selectedCatalogId}
              search={catalogSearch}
              isPending={isPending || overviewQuery.isLoading}
              readableCatalogCount={readableCatalogCount}
              onSearch={setCatalogSearch}
              onSelect={selectCatalog}
              onToggleExpanded={toggleExpanded}
              expandedCatalogIds={expandedCatalogIds}
            />
          </aside>

          {isMobileCatalogOpen ? (
            <div className="fixed inset-0 z-50 bg-black/60 p-3 backdrop-blur-sm lg:hidden">
              <div className="h-full overflow-hidden rounded-[1.75rem] border border-cyan-300/20 bg-slate-950 shadow-2xl">
                <div className="flex items-center justify-between border-b border-white/10 p-3">
                  <span className="text-sm font-semibold text-white">Catalog browser</span>
                  <button type="button" className="rounded-xl bg-white/10 p-2" onClick={() => setIsMobileCatalogOpen(false)} aria-label="Close catalog browser">
                    <X className="h-5 w-5" />
                  </button>
                </div>
                <CatalogBrowser
                  nodes={filteredCatalogNodes}
                  selectedCatalogId={selectedCatalogId}
                  search={catalogSearch}
                  isPending={isPending || overviewQuery.isLoading}
                  readableCatalogCount={readableCatalogCount}
                  onSearch={setCatalogSearch}
                  onSelect={selectCatalog}
                  onToggleExpanded={toggleExpanded}
                  expandedCatalogIds={expandedCatalogIds}
                />
              </div>
            </div>
          ) : null}

          <section className="grid min-h-0 gap-4 xl:grid-cols-[minmax(0,1fr)_340px]">
            <div className="grid min-h-0 gap-4">
              <CatalogHero
                catalogId={selectedCatalogId}
                isLoading={selectedCatalogQuery.isFetching}
                resourceCount={resourceRows.length}
                groupCount={groupCount}
                timeRange={selectedCatalogQuery.data?.timeRange}
                metadataKeys={Object.keys(selectedCatalogQuery.data?.metadata ?? {}).length}
                attachments={selectedCatalogQuery.data?.attachments ?? []}
              />

              <ResourceWorkbench
                resources={filteredResources}
                activeResource={activeResource}
                selectedResourcePaths={selectedResourcePaths}
                search={resourceSearch}
                onSearch={setResourceSearch}
                onToggleResource={toggleResource}
                onActivateResource={setActiveResourcePath}
              />

              <TelemetryPreview
                resources={selectedResources.length > 0 ? selectedResources : activeResource ? [activeResource] : []}
                ranges={quickRanges}
              />
            </div>

            <div className="min-h-0">
              <InsightPanel
                catalogId={selectedCatalogId}
                resources={resourceRows}
                activeResource={activeResource}
                jobs={jobs}
                writers={writerDescriptions}
                onOpenExport={() => setIsExportOpen(true)}
              />

            </div>
          </section>
        </main>

        {isExportOpen ? (
          <div className="fixed inset-0 z-50 grid place-items-center bg-black/70 p-3 sm:p-6">
            <div className="max-h-[calc(100vh-2rem)] w-full max-w-3xl overflow-auto rounded-[2rem] border border-cyan-300/25 bg-slate-950 shadow-[0_24px_90px_rgba(0,0,0,0.55)]">
              <ExportPanel
                writers={writerDescriptions}
                selectedResources={selectedResources}
                catalogTimeRange={selectedCatalogQuery.data?.timeRange}
                onClose={() => setIsExportOpen(false)}
              />
            </div>
          </div>
        ) : null}
      </div>
    </div>
  )
}

function Metric({ label, value, hint, icon, tone }: { label: string; value: string; hint?: string; icon: React.ReactNode; tone: 'cyan' | 'violet' | 'lime' }) {
  const toneClass = {
    cyan: 'border-cyan-300/20 bg-cyan-300/10 text-cyan-200',
    violet: 'border-violet-300/20 bg-violet-300/10 text-violet-200',
    lime: 'border-lime-300/20 bg-lime-300/10 text-lime-200',
  }[tone]

  return (
    <div className="rounded-2xl border border-white/10 bg-white/[0.045] p-3">
      <div className="flex items-center gap-2 text-xs uppercase tracking-[0.22em] text-slate-400">
        <span className={cn('grid h-7 w-7 place-items-center rounded-xl border', toneClass)}>{icon}</span>
        {label}
      </div>
      <div className="mt-2 truncate text-sm font-semibold text-white">{value}</div>
      {hint ? <div className="mt-1 truncate text-xs text-slate-500">{hint}</div> : null}
    </div>
  )
}

function TokenNotice() {
  return (
    <div className="rounded-3xl border border-amber-300/25 bg-amber-300/10 p-4 text-sm text-amber-100">
      <div className="flex items-center gap-3">
        <CircleAlert className="h-5 w-5 shrink-0" />
        <span>
          The Vite API proxy is not configured. Start with `./dev-with-local-credentials.sh` to use the live Nexus API, or keep browsing curated sample data.
        </span>
      </div>
    </div>
  )
}

function ApiErrorNotice({ error }: { error: Error | null }) {
  if (!error) {
    return null
  }

  return (
    <div className="rounded-3xl border border-red-300/25 bg-red-300/10 p-4 text-sm text-red-100">
      <div className="flex items-center gap-3">
        <CircleAlert className="h-5 w-5 shrink-0" />
        <span>Live Nexus request failed: {error.message}</span>
      </div>
    </div>
  )
}

function CatalogBrowser({
  nodes,
  selectedCatalogId,
  search,
  isPending,
  readableCatalogCount,
  expandedCatalogIds,
  onSearch,
  onSelect,
  onToggleExpanded,
}: {
  nodes: CatalogNode[]
  selectedCatalogId: string
  search: string
  isPending: boolean
  readableCatalogCount: number
  expandedCatalogIds: Set<string>
  onSearch: (value: string) => void
  onSelect: (catalogId: string) => void
  onToggleExpanded: (catalogId: string) => void
}) {
  return (
    <div className="glass-panel flex h-full min-h-0 flex-col rounded-[2rem]">
      <div className="border-b border-white/10 p-4">
        <div className="flex items-center justify-between gap-3">
          <div>
            <div className="text-xs uppercase tracking-[0.26em] text-cyan-200/80">catalog atlas</div>
            <div className="mt-1 text-xl font-semibold tracking-[-0.03em] text-white">{formatNumber(readableCatalogCount)} readable branches</div>
          </div>
          <div className="grid h-10 w-10 place-items-center rounded-2xl bg-cyan-300/10 text-cyan-200">
            {isPending ? <Loader2 className="h-5 w-5 animate-spin" /> : <Layers3 className="h-5 w-5" />}
          </div>
        </div>
        <label className="mt-4 flex items-center gap-2 rounded-2xl border border-cyan-300/20 bg-slate-950/70 px-3 py-2 text-sm text-slate-300 focus-within:border-cyan-300/60">
          <Search className="h-4 w-4 text-cyan-200" />
          <input
            value={search}
            onChange={(event) => onSearch(event.target.value)}
            className="w-full bg-transparent outline-none placeholder:text-slate-600"
            placeholder="Jump to campaign, source type, owner..."
          />
          <span className="hidden rounded-lg border border-white/10 px-1.5 py-0.5 font-mono text-[10px] text-slate-500 sm:inline">/</span>
        </label>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto p-2">
        {nodes.map((node) => {
          const id = node.id ?? '/'
          const selected = id === selectedCatalogId
          const isExpanded = expandedCatalogIds.has(id)
          return (
            <div key={`${id}-${node.depth}`} className="group relative">
              <div
                className={cn(
                  'grid grid-cols-[32px_minmax(0,1fr)] items-center gap-1 rounded-2xl border px-2 py-2 transition',
                  selected
                    ? 'border-cyan-300/40 bg-cyan-300/12 shadow-[0_0_30px_rgba(34,211,238,0.10)]'
                    : 'border-transparent hover:border-white/10 hover:bg-white/[0.045]',
                )}
                style={{ marginLeft: Math.min(node.depth * 18, 72) }}
              >
                <button
                  type="button"
                  className="grid h-8 w-8 place-items-center rounded-xl text-slate-400 hover:bg-white/10 hover:text-cyan-200"
                  onClick={() => onToggleExpanded(id)}
                  aria-label={`Toggle ${id}`}
                >
                  <ChevronDown className={cn('h-4 w-4 transition', !isExpanded && '-rotate-90')} />
                </button>
                <button type="button" className="min-w-0 text-left" onClick={() => onSelect(id)}>
                  <div className="flex min-w-0 items-center gap-2">
                    <span className={cn('h-2 w-2 shrink-0 rounded-full', node.isOwner ? 'bg-lime-300' : node.isReadable ? 'bg-cyan-300' : 'bg-slate-600')} />
                    <span className="truncate font-mono text-[13px] text-slate-100">{compactPath(id, 4)}</span>
                  </div>
                  <div className="mt-1 flex min-w-0 items-center gap-2 text-xs text-slate-500">
                    <span className="truncate">{node.title ?? node.pipelineInfo?.types?.[0] ?? 'Untitled catalog'}</span>
                    {node.isWritable ? <span className="rounded-full bg-violet-300/10 px-1.5 py-0.5 text-violet-200">write</span> : null}
                  </div>
                </button>
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

function CatalogHero({
  catalogId,
  isLoading,
  resourceCount,
  groupCount,
  timeRange,
  metadataKeys,
  attachments,
}: {
  catalogId: string
  isLoading: boolean
  resourceCount: number
  groupCount: number
  timeRange: V1.CatalogTimeRange | undefined
  metadataKeys: number
  attachments: string[]
}) {
  return (
    <section className="glass-panel overflow-hidden rounded-[2rem]">
      <div className="relative p-5 sm:p-6">
        <div className="absolute right-0 top-0 h-48 w-72 rounded-full bg-cyan-300/10 blur-3xl" />
        <div className="relative flex flex-col gap-5 xl:flex-row xl:items-end xl:justify-between">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2 text-xs uppercase tracking-[0.24em] text-violet-200/80">
              <Database className="h-4 w-4" /> active catalog
              {isLoading ? <span className="rounded-full bg-cyan-300/10 px-2 py-0.5 text-cyan-200">syncing</span> : null}
            </div>
            <h2 className="mt-3 break-words font-mono text-2xl font-semibold tracking-[-0.04em] text-white sm:text-4xl">{catalogId}</h2>
            <div className="mt-3 flex flex-wrap gap-2 text-xs text-slate-400">
              <Pill icon={<Clock3 className="h-3.5 w-3.5" />} label={`${timeRange?.begin ?? 'unknown'} -> ${timeRange?.end ?? 'unknown'}`} />
              <Pill icon={<FileJson className="h-3.5 w-3.5" />} label={`${metadataKeys} metadata keys`} />
              <Pill icon={<Archive className="h-3.5 w-3.5" />} label={`${attachments.length} attachments`} />
            </div>
          </div>

          <div className="grid grid-cols-3 gap-2 sm:min-w-[420px]">
            <Kpi label="Resources" value={resourceCount} />
            <Kpi label="Groups" value={groupCount} />
            <Kpi label="Density" value={resourceCount > 0 ? Math.ceil(resourceCount / Math.max(groupCount, 1)) : 0} />
          </div>
        </div>
      </div>
    </section>
  )
}

function ResourceWorkbench({
  resources,
  activeResource,
  selectedResourcePaths,
  search,
  onSearch,
  onToggleResource,
  onActivateResource,
}: {
  resources: ResourceRow[]
  activeResource: ResourceRow | undefined
  selectedResourcePaths: Set<string>
  search: string
  onSearch: (value: string) => void
  onToggleResource: (resource: ResourceRow) => void
  onActivateResource: (path: string) => void
}) {
  const renderedResources = resources.slice(0, 450)

  return (
    <section className="glass-panel grid min-h-[470px] overflow-hidden rounded-[2rem] xl:grid-cols-[minmax(0,1fr)_360px]">
      <div className="flex min-h-0 flex-col border-b border-white/10 xl:border-b-0 xl:border-r">
        <div className="border-b border-white/10 p-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <div className="text-xs uppercase tracking-[0.24em] text-cyan-200/80">resource matrix</div>
              <div className="mt-1 text-xl font-semibold tracking-[-0.03em] text-white">Select channels without losing context</div>
            </div>
            <div className="flex items-center gap-2 rounded-2xl border border-white/10 bg-white/[0.045] px-3 py-2 text-sm text-slate-300">
              <Filter className="h-4 w-4 text-cyan-200" />
              <input
                value={search}
                onChange={(event) => onSearch(event.target.value)}
                className="w-full min-w-0 bg-transparent outline-none placeholder:text-slate-600 sm:w-64"
                placeholder="Filter resources, groups, units"
              />
            </div>
          </div>
        </div>

        <div className="min-h-0 flex-1 overflow-auto">
          <table className="w-full min-w-[760px] border-separate border-spacing-0 text-left text-sm">
            <thead className="sticky top-0 z-10 bg-slate-950/90 text-xs uppercase tracking-[0.18em] text-slate-500 backdrop-blur">
              <tr>
                <th className="w-12 border-b border-white/10 p-3" />
                <th className="border-b border-white/10 p-3">Resource</th>
                <th className="border-b border-white/10 p-3">Group</th>
                <th className="border-b border-white/10 p-3">Unit</th>
                <th className="border-b border-white/10 p-3">Representation</th>
              </tr>
            </thead>
            <tbody>
              {renderedResources.map((resource) => {
                const selected = selectedResourcePaths.has(resource.path)
                const active = activeResource?.path === resource.path
                const firstRep = resource.representations[0]
                return (
                  <tr
                    key={resource.path}
                    className={cn('transition hover:bg-white/[0.035]', active && 'bg-cyan-300/[0.055]', selected && 'outline outline-1 -outline-offset-1 outline-cyan-300/20')}
                    onClick={() => onActivateResource(resource.path)}
                  >
                    <td className="border-b border-white/5 p-3 align-top">
                      <button
                        type="button"
                        className={cn(
                          'grid h-6 w-6 place-items-center rounded-lg border transition',
                          selected ? 'border-cyan-300 bg-cyan-300 text-slate-950' : 'border-white/15 bg-white/[0.03] text-transparent hover:text-slate-400',
                        )}
                        onClick={(event) => {
                          event.stopPropagation()
                          onToggleResource(resource)
                        }}
                        aria-label={`Select ${resource.path}`}
                      >
                        <Check className="h-4 w-4" />
                      </button>
                    </td>
                    <td className="border-b border-white/5 p-3 align-top">
                      <div className="font-mono text-[13px] font-semibold text-white">{resource.id}</div>
                      <div className="mt-1 max-w-[420px] truncate text-xs text-slate-500">{resource.description}</div>
                    </td>
                    <td className="border-b border-white/5 p-3 align-top">
                      <div className="flex flex-wrap gap-1">
                        {(resource.groups.length > 0 ? resource.groups : ['ungrouped']).slice(0, 3).map((group) => (
                          <span key={group} className="rounded-full border border-violet-300/20 bg-violet-300/10 px-2 py-0.5 text-xs text-violet-100">
                            {group}
                          </span>
                        ))}
                      </div>
                    </td>
                    <td className="border-b border-white/5 p-3 align-top text-slate-300">{resource.unit}</td>
                    <td className="border-b border-white/5 p-3 align-top">
                      <div className="flex flex-wrap gap-1.5">
                        <span className="rounded-lg bg-lime-300/10 px-2 py-1 font-mono text-xs text-lime-200">{firstRep?.dataType ?? 'unknown'}</span>
                        <span className="rounded-lg bg-cyan-300/10 px-2 py-1 font-mono text-xs text-cyan-100">{firstRep?.samplePeriod ?? 'no cadence'}</span>
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
          {resources.length > renderedResources.length ? (
            <div className="border-t border-white/10 bg-slate-950/80 p-3 text-center text-xs text-slate-500">
              Showing the first {renderedResources.length} filtered resources. Narrow the search to reduce rows.
            </div>
          ) : null}
          {resources.length === 0 ? (
            <div className="grid min-h-48 place-items-center p-6 text-center text-sm text-slate-500">
              <div>
                <Database className="mx-auto mb-3 h-8 w-8 text-slate-600" />
                No resources found for this catalog or filter.
              </div>
            </div>
          ) : null}
        </div>
      </div>

      <div className="bg-white/[0.025] p-4">
        <div className="flex items-center justify-between gap-3">
          <div>
            <div className="text-xs uppercase tracking-[0.22em] text-violet-200/80">inspector</div>
            <div className="mt-1 font-mono text-lg font-semibold text-white">{activeResource?.id ?? 'No resource selected'}</div>
          </div>
          <Gauge className="h-6 w-6 text-cyan-200" />
        </div>

        <div className="mt-4 space-y-3 text-sm">
          <InspectorRow label="Full path" value={activeResource?.path ?? '-'} mono />
          <InspectorRow label="Description" value={activeResource?.description ?? '-'} />
          <InspectorRow label="Unit" value={activeResource?.unit ?? '-'} />
          <InspectorRow label="Groups" value={activeResource?.groups.join(', ') || 'ungrouped'} />
        </div>

        <div className="mt-5 rounded-2xl border border-white/10 bg-slate-950/60 p-3">
          <div className="mb-3 text-xs uppercase tracking-[0.2em] text-slate-500">representations</div>
          <div className="space-y-2">
            {(activeResource?.representations ?? []).map((rep, index) => (
              <div key={`${rep.dataType}-${rep.samplePeriod}-${index}`} className="rounded-xl border border-cyan-300/15 bg-cyan-300/5 p-3">
                <div className="flex items-center justify-between gap-2">
                  <span className="font-mono text-sm text-cyan-100">{rep.dataType}</span>
                  <span className="rounded-full bg-white/10 px-2 py-0.5 font-mono text-xs text-slate-300">{rep.samplePeriod}</span>
                </div>
                <div className="mt-2 text-xs text-slate-500">{rep.parameters ? Object.keys(rep.parameters).length : 0} parameter definitions</div>
              </div>
            ))}
            {!activeResource ? <div className="rounded-xl border border-white/10 bg-white/[0.03] p-3 text-sm text-slate-500">Select a resource to inspect representations.</div> : null}
          </div>
        </div>
      </div>
    </section>
  )
}

function TelemetryPreview({ resources, ranges }: { resources: ResourceRow[]; ranges: typeof quickRanges }) {
  const strokes = ['#22d3ee', '#8b5cf6', '#a3e635', '#f59e0b']
  const gridLines = Array.from({ length: 7 }, (_, index) => 15 + index * 12)

  return (
    <section className="glass-panel overflow-hidden rounded-[2rem]">
      <div className="flex flex-col gap-4 border-b border-white/10 p-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <div className="text-xs uppercase tracking-[0.24em] text-lime-200/80">preview canvas</div>
          <div className="mt-1 text-xl font-semibold tracking-[-0.03em] text-white">Placeholder graph wired to selection state</div>
        </div>
        <div className="flex flex-wrap gap-2">
          {ranges.map((range) => (
            <button key={range.label} type="button" className="rounded-xl border border-white/10 bg-white/[0.045] px-3 py-2 text-xs font-medium text-slate-300 hover:border-cyan-300/40 hover:text-cyan-100">
              {range.label}
            </button>
          ))}
        </div>
      </div>
      <div className="grid gap-4 p-4 xl:grid-cols-[minmax(0,1fr)_230px]">
        <div className="relative h-72 overflow-hidden rounded-3xl border border-white/10 bg-[#06101d] p-4">
          <div className="absolute inset-0 bg-[radial-gradient(circle_at_20%_10%,rgba(34,211,238,0.16),transparent_30%),radial-gradient(circle_at_80%_20%,rgba(139,92,246,0.14),transparent_30%)]" />
          <svg className="relative h-full w-full" viewBox="0 0 900 260" preserveAspectRatio="none" role="img" aria-label="Placeholder chart">
            {gridLines.map((line) => (
              <line key={line} x1="0" x2="900" y1={line * 2} y2={line * 2} stroke="rgba(148,163,184,0.11)" />
            ))}
            {resources.slice(0, 4).map((resource, index) => {
              const phase = index * 26
              const points = Array.from({ length: 34 }, (_, itemIndex) => {
                const x = (itemIndex / 33) * 900
                const y = 128 + Math.sin((itemIndex + phase) / 3.2) * (32 + index * 8) + Math.cos(itemIndex / 1.7 + index) * 14
                return `${x},${y}`
              }).join(' ')

              return <polyline key={resource.path} points={points} fill="none" stroke={strokes[index]} strokeWidth="3" strokeLinecap="round" opacity={0.92} />
            })}
          </svg>
          <div className="absolute left-4 top-4 rounded-2xl border border-white/10 bg-slate-950/70 px-3 py-2 text-xs text-slate-300 backdrop-blur">
            Data rendering placeholder. Real WebGPU chart intentionally not ported yet.
          </div>
        </div>
        <div className="space-y-2">
          {resources.slice(0, 5).map((resource, index) => (
            <div key={resource.path} className="rounded-2xl border border-white/10 bg-white/[0.035] p-3">
              <div className="flex items-center gap-2">
                <span className="h-2.5 w-2.5 rounded-full" style={{ backgroundColor: strokes[index % strokes.length] }} />
                <span className="truncate font-mono text-sm text-white">{resource.id}</span>
              </div>
              <div className="mt-1 text-xs text-slate-500">{resource.representations[0]?.dataType ?? 'unknown'} · {resource.representations[0]?.samplePeriod ?? 'no cadence'}</div>
            </div>
          ))}
        </div>
      </div>
    </section>
  )
}

function InsightPanel({
  catalogId,
  resources,
  activeResource,
  jobs,
  writers,
  onOpenExport,
}: {
  catalogId: string
  resources: ResourceRow[]
  activeResource: ResourceRow | undefined
  jobs: V1.Job[]
  writers: WriterDescription[]
  onOpenExport: () => void
}) {
  const topGroups = Object.entries(
    resources.reduce<Record<string, number>>((groups, resource) => {
      for (const group of resource.groups.length ? resource.groups : ['ungrouped']) {
        groups[group] = (groups[group] ?? 0) + 1
      }
      return groups
    }, {}),
  )
    .sort((a, b) => b[1] - a[1])
    .slice(0, 5)

  return (
    <section className="glass-panel rounded-[2rem] p-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <div className="text-xs uppercase tracking-[0.22em] text-cyan-200/80">operations</div>
          <div className="mt-1 text-lg font-semibold text-white">Fast decisions panel</div>
        </div>
        <button type="button" onClick={onOpenExport} className="rounded-2xl bg-cyan-300 px-3 py-2 text-sm font-semibold text-slate-950 hover:bg-cyan-200">
          Export
        </button>
      </div>

      <div className="mt-4 grid grid-cols-2 gap-2">
        <MiniStat label="Writers" value={writers.length} icon={<PackageCheck className="h-4 w-4" />} />
        <MiniStat label="Jobs" value={jobs.length} icon={<Activity className="h-4 w-4" />} />
      </div>

      <div className="mt-4 rounded-2xl border border-white/10 bg-white/[0.035] p-3">
        <div className="mb-3 flex items-center justify-between text-xs uppercase tracking-[0.18em] text-slate-500">
          group pressure
          <ChevronsUpDown className="h-4 w-4" />
        </div>
        <div className="space-y-2">
          {topGroups.map(([group, count]) => (
            <div key={group}>
              <div className="mb-1 flex items-center justify-between gap-3 text-xs">
                <span className="truncate text-slate-300">{group}</span>
                <span className="font-mono text-slate-500">{count}</span>
              </div>
              <div className="h-1.5 rounded-full bg-white/10">
                <div className="h-full rounded-full bg-gradient-to-r from-cyan-300 to-violet-400" style={{ width: `${Math.max(8, (count / Math.max(resources.length, 1)) * 100)}%` }} />
              </div>
            </div>
          ))}
        </div>
      </div>

      <div className="mt-4 rounded-2xl border border-violet-300/15 bg-violet-300/5 p-3">
        <div className="mb-2 flex items-center gap-2 text-sm font-semibold text-violet-100">
          <Sparkles className="h-4 w-4" /> Suggested next action
        </div>
        <p className="text-sm text-slate-400">
          {activeResource ? (
            <>Inspect <span className="font-mono text-slate-200">{activeResource.id}</span>, compare nearby channels, then send {resources.length > 1 ? 'a curated selection' : 'this channel'} to the export queue.</>
          ) : (
            <>Choose a catalog with resources, then pin the most relevant channels before opening the export composer.</>
          )}
        </p>
      </div>

      <a className="mt-4 flex items-center justify-between rounded-2xl border border-white/10 bg-white/[0.035] p-3 text-sm text-slate-300 hover:border-cyan-300/40 hover:text-cyan-100" href={`${nexusEndpoint}/api/v1/catalogs/${encodeURIComponent(catalogId)}`} target="_blank" rel="noreferrer">
        Open raw catalog JSON
        <SquareArrowOutUpRight className="h-4 w-4" />
      </a>
    </section>
  )
}

function ExportPanel({
  writers,
  selectedResources,
  catalogTimeRange,
  onClose,
}: {
  writers: WriterDescription[]
  selectedResources: ResourceRow[]
  catalogTimeRange: V1.CatalogTimeRange | undefined
  onClose: () => void
}) {
  const [writerType, setWriterType] = useState(writers[0]?.type ?? '')
  const [begin, setBegin] = useState(catalogTimeRange?.begin ?? '2025-01-01T00:00:00Z')
  const [end, setEnd] = useState(catalogTimeRange?.end ?? '2025-01-02T00:00:00Z')
  const [filePeriod, setFilePeriod] = useState('PT0S')
  const [precision, setPrecision] = useState<V2.Precision>(V2.Precision.Float32)
  const [configuration, setConfiguration] = useState<Record<string, unknown>>({ 'row-index-format': 'excel', 'significant-figures': 4 })

  const writer = writers.find((item) => item.type === writerType) ?? writers[0]
  const writerOptions = writer?.additionalInformation?.options ?? {}

  const exportMutation = useMutation({
    mutationFn: async () => {
      const parameters = buildExportParameters(
        begin,
        end,
        filePeriod,
        writer,
        selectedResources.map((resource) => resource.path),
        configuration,
        precision,
      )
      return nexusClient.v2.jobs.export(parameters)
    },
  })

  function updateConfiguration(key: string, value: unknown) {
    setConfiguration((current) => ({ ...current, [key]: value }))
  }

  return (
    <section className="glass-panel rounded-[2rem] p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="text-xs uppercase tracking-[0.22em] text-lime-200/80">export composer</div>
          <div className="mt-1 text-lg font-semibold text-white">Package selected resources</div>
          <p className="mt-1 text-sm text-slate-500">Metadata-driven writer options, compact defaults, job response inline.</p>
        </div>
        <button type="button" onClick={onClose} className="rounded-xl bg-white/10 p-2 text-slate-400 hover:text-white" aria-label="Close export panel">
          <PanelLeftClose className="h-5 w-5" />
        </button>
      </div>

      <div className="mt-4 space-y-3">
        <Field label="Writer">
          <select value={writerType} onChange={(event) => setWriterType(event.target.value)} className="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 text-sm text-white outline-none focus:border-cyan-300/60">
            {writers.map((item) => (
              <option key={item.type} value={item.type}>{item.additionalInformation?.label ?? item.type}</option>
            ))}
          </select>
        </Field>

        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-1 2xl:grid-cols-2">
          <Field label="Begin">
            <input value={begin} onChange={(event) => setBegin(event.target.value)} className="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 font-mono text-xs text-white outline-none focus:border-cyan-300/60" />
          </Field>
          <Field label="End">
            <input value={end} onChange={(event) => setEnd(event.target.value)} className="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 font-mono text-xs text-white outline-none focus:border-cyan-300/60" />
          </Field>
        </div>

        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-1 2xl:grid-cols-2">
          <Field label="File period">
            <input value={filePeriod} onChange={(event) => setFilePeriod(event.target.value)} className="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 font-mono text-xs text-white outline-none focus:border-cyan-300/60" />
          </Field>
          <Field label="Precision">
            <select value={precision} onChange={(event) => setPrecision(event.target.value as V2.Precision)} className="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 text-sm text-white outline-none focus:border-cyan-300/60">
              <option value={V2.Precision.Float32}>Float32</option>
              <option value={V2.Precision.Float64}>Float64</option>
            </select>
          </Field>
        </div>

        {Object.entries(writerOptions).map(([key, option]) => (
          <Field key={key} label={option.label ?? key}>
            {option.type === 'select' && option.items ? (
              <select value={String(configuration[key] ?? option.default ?? '')} onChange={(event) => updateConfiguration(key, event.target.value)} className="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 text-sm text-white outline-none focus:border-cyan-300/60">
                {Object.entries(option.items).map(([value, label]) => (
                  <option key={value} value={value}>{label}</option>
                ))}
              </select>
            ) : (
              <input
                type={option.type === 'input-integer' ? 'number' : 'text'}
                min={option.minimum}
                max={option.maximum}
                value={String(configuration[key] ?? option.default ?? '')}
                onChange={(event) => updateConfiguration(key, option.type === 'input-integer' ? Number(event.target.value) : event.target.value)}
                className="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 text-sm text-white outline-none focus:border-cyan-300/60"
              />
            )}
          </Field>
        ))}
      </div>

      <div className="mt-4 rounded-2xl border border-white/10 bg-slate-950/60 p-3">
        <div className="mb-2 text-xs uppercase tracking-[0.18em] text-slate-500">selection payload</div>
        <div className="max-h-32 space-y-1 overflow-auto font-mono text-xs text-slate-400">
          {(selectedResources.length > 0 ? selectedResources : []).map((resource) => (
            <div key={resource.path} className="truncate">{resource.path}</div>
          ))}
          {selectedResources.length === 0 ? <div>No resources selected yet.</div> : null}
        </div>
      </div>

      <button
        type="button"
        disabled={selectedResources.length === 0 || exportMutation.isPending || !hasConfiguredToken}
        onClick={() => exportMutation.mutate()}
        className="mt-4 flex w-full items-center justify-center gap-2 rounded-2xl bg-gradient-to-r from-cyan-300 to-lime-300 px-4 py-3 text-sm font-bold text-slate-950 shadow-[0_0_36px_rgba(34,211,238,0.22)] transition hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-45"
      >
        {exportMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
        Create export job
      </button>

      {exportMutation.data ? (
        <div className="mt-3 rounded-2xl border border-lime-300/20 bg-lime-300/10 p-3 text-sm text-lime-100">
          Job created: <span className="font-mono">{exportMutation.data.id}</span>
        </div>
      ) : null}

      {exportMutation.error ? (
        <div className="mt-3 rounded-2xl border border-red-300/20 bg-red-300/10 p-3 text-sm text-red-100">
          {exportMutation.error.message}
        </div>
      ) : null}
    </section>
  )
}

function Kpi({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-2xl border border-white/10 bg-white/[0.045] p-3">
      <div className="font-mono text-2xl font-semibold text-white">{formatNumber(value)}</div>
      <div className="mt-1 text-xs uppercase tracking-[0.18em] text-slate-500">{label}</div>
    </div>
  )
}

function Pill({ icon, label }: { icon: React.ReactNode; label: string }) {
  return (
    <span className="inline-flex max-w-full items-center gap-1.5 rounded-full border border-white/10 bg-white/[0.045] px-2.5 py-1">
      {icon}
      <span className="truncate">{label}</span>
    </span>
  )
}

function InspectorRow({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <div className="mb-1 text-xs uppercase tracking-[0.18em] text-slate-500">{label}</div>
      <div className={cn('break-words rounded-xl border border-white/10 bg-white/[0.035] px-3 py-2 text-slate-200', mono && 'font-mono text-xs')}>{value}</div>
    </div>
  )
}

function MiniStat({ label, value, icon }: { label: string; value: number; icon: React.ReactNode }) {
  return (
    <div className="rounded-2xl border border-white/10 bg-white/[0.035] p-3">
      <div className="flex items-center justify-between text-slate-400">
        <span className="text-xs uppercase tracking-[0.18em]">{label}</span>
        {icon}
      </div>
      <div className="mt-2 font-mono text-2xl font-semibold text-white">{formatNumber(value)}</div>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-xs uppercase tracking-[0.18em] text-slate-500">{label}</span>
      {children}
    </label>
  )
}

export default App

export type DevUiSettings = {
  fileType?: string | null
  requestConfiguration?: Record<string, unknown> | null
  catalogHidePatterns?: Array<string | null> | null
  chartGpuCacheBudgetMiB?: number
}

export type NexusUiSetup = {
  begin?: unknown
  end?: unknown
  filePeriod?: unknown
  type?: unknown
  resourcePaths?: unknown
  configuration?: unknown
  precision?: unknown
}

export type ParsedNexusUiSetup = {
  source: 'setup' | 'legacy-ui-settings'
  setup: NexusUiSetup
  legacyUiSettings?: DevUiSettings
}

export function parseNexusUiSetupJson(text: string): ParsedNexusUiSetup {
  return parseNexusUiSetup(JSON.parse(text) as unknown)
}

export function parseNexusUiSetup(value: unknown): ParsedNexusUiSetup {
  if (!isRecord(value)) throw new Error('The selected file is not a Nexus setup JSON object.')

  if (hasExportParametersField(value)) {
    return { source: 'setup', setup: { ...value } }
  }

  const legacyUiSettings = readDevUiSettings(value)
  if (legacyUiSettings) {
    return {
      source: 'legacy-ui-settings',
      setup: mapLegacyUiSettingsToExportParameters(legacyUiSettings),
      legacyUiSettings,
    }
  }

  throw new Error('The selected JSON file is neither a Nexus setup export nor legacy UI settings.')
}

function mapLegacyUiSettingsToExportParameters(settings: DevUiSettings) {
  const exportParameters: NexusUiSetup = {}
  if (settings.fileType) exportParameters.type = settings.fileType
  if (settings.requestConfiguration) exportParameters.configuration = settings.requestConfiguration
  return exportParameters
}

function hasExportParametersField(value: Record<string, unknown>) {
  return 'begin' in value || 'end' in value || 'filePeriod' in value || 'type' in value || 'resourcePaths' in value || 'configuration' in value || 'precision' in value
}

function readDevUiSettings(value: unknown): DevUiSettings | null {
  if (!isRecord(value)) return null

  const hasLegacyField = 'fileType' in value || 'requestConfiguration' in value || 'catalogHidePatterns' in value || 'chartGpuCacheBudgetMiB' in value
  if (!hasLegacyField) return null

  const result: DevUiSettings = {}
  if (typeof value['fileType'] === 'string' || value['fileType'] === null) result.fileType = value['fileType']
  if (isRecord(value['requestConfiguration'])) result.requestConfiguration = { ...value['requestConfiguration'] }
  else if (value['requestConfiguration'] === null) result.requestConfiguration = null
  if (Array.isArray(value['catalogHidePatterns'])) result.catalogHidePatterns = value['catalogHidePatterns'].filter((item): item is string | null => typeof item === 'string' || item === null)
  else if (value['catalogHidePatterns'] === null) result.catalogHidePatterns = null
  if (typeof value['chartGpuCacheBudgetMiB'] === 'number' && Number.isFinite(value['chartGpuCacheBudgetMiB'])) result.chartGpuCacheBudgetMiB = value['chartGpuCacheBudgetMiB']
  return result
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value)
}

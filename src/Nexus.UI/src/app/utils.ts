export function formatNumber(value: number | undefined) {
  if (value === undefined || Number.isNaN(value)) return '0'

  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(value)
}

export function lastSegment(path: string | undefined): string {
  if (!path) return '/'
  const segments = path.split('/').filter(Boolean)
  return segments[segments.length - 1] ?? '/'
}

export function compactPath(path: string | undefined, maxSegments = 3) {
  if (!path) return '/'

  const segments = path.split('/').filter(Boolean)
  if (segments.length <= maxSegments) return path

  return `/${segments.slice(0, 1).join('/')}/.../${segments.slice(-maxSegments + 1).join('/')}`
}

export function abbreviateMiddle(value: string, maxLength: number) {
  if (value.length <= maxLength) return value

  const edgeLength = Math.floor((maxLength - 3) / 2)
  const startLength = edgeLength + ((maxLength - 3) % 2)

  return `${value.slice(0, startLength)}...${value.slice(-edgeLength)}`
}

export function getStringProperty(record: Record<string, unknown> | null | undefined, key: string) {
  const value = record?.[key]
  return typeof value === 'string' ? value : undefined
}

export function normalizeMarkdown(markdown: string) {
  return markdown.replace(/\r\n?/g, '\n').replace(/\\n/g, '\n')
}

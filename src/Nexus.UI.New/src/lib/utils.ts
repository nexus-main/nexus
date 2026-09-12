import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

export function formatNumber(value: number | undefined) {
  if (value === undefined || Number.isNaN(value)) {
    return '0'
  }

  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(value)
}

export function compactPath(path: string | undefined, maxSegments = 3) {
  if (!path) {
    return '/'
  }

  const segments = path.split('/').filter(Boolean)

  if (segments.length <= maxSegments) {
    return path
  }

  return `/${segments.slice(0, 1).join('/')}/.../${segments.slice(-maxSegments + 1).join('/')}`
}

import { Injectable } from '@angular/core'

@Injectable({ providedIn: 'root' })
export class BrowserStorageService {
  getJson<T>(key: string, fallback: T): T {
    try {
      const value = window.localStorage.getItem(key)
      return value === null ? fallback : JSON.parse(value) as T
    } catch {
      return fallback
    }
  }

  setJson(key: string, value: unknown) {
    try {
      window.localStorage.setItem(key, JSON.stringify(value))
    } catch {
      // Ignore unavailable storage, quota errors, and private-mode failures.
    }
  }
}

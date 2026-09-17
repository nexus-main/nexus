import type * as Monaco from 'monaco-editor'

export function createYamlMonaco(monaco: typeof Monaco, createYamlWorker: () => Worker) {
  // Call after AMD loading, which replaces MonacoEnvironment during startup.
  const environment = globalThis.MonacoEnvironment
  globalThis.MonacoEnvironment = {
    ...environment,
    getWorker(moduleId, label) {
      if (label === 'yaml') return createYamlWorker()
      if (environment?.getWorker) return environment.getWorker(moduleId, label)
      throw new Error(`No Monaco worker configured for ${label}`)
    },
  }

  // monaco-worker-manager 2 calls the pre-0.55 API. Keep this adapter local:
  // replacing the global editor factory would make the public factory recurse.
  return { ...monaco, editor: { ...monaco.editor, createWebWorker: monaco.createWebWorker } }
}

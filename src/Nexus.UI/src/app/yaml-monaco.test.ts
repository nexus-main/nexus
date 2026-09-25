import assert from 'node:assert/strict'
import { test } from 'node:test'
import type * as Monaco from 'monaco-editor'
import { createYamlMonaco } from './services/yaml-monaco.ts'

test('YAML adapter installs post-load routing and preserves the AMD editor worker', () => {
  const original = globalThis.MonacoEnvironment
  const yamlWorker = {} as Worker
  const editorWorker = {} as Worker
  const calls: string[][] = []
  try {
    globalThis.MonacoEnvironment = { getWorker: (moduleId, label) => {
      calls.push([moduleId, label])
      return editorWorker
    } }
    createYamlMonaco({ editor: {} } as typeof Monaco, () => yamlWorker)
    assert.equal(globalThis.MonacoEnvironment.getWorker!('workerMain.js', 'yaml'), yamlWorker)
    assert.equal(globalThis.MonacoEnvironment.getWorker!('workerMain.js', 'editorWorkerService'), editorWorker)
    assert.deepEqual(calls, [['workerMain.js', 'editorWorkerService']])
  } finally {
    globalThis.MonacoEnvironment = original
  }
})

test('YAML adapter uses the public 0.55 worker factory without replacing the internal factory', () => {
  const original = globalThis.MonacoEnvironment
  const options = { label: 'yaml', moduleId: 'monaco-yaml/yaml.worker', createData: { validate: true } }
  const result = {}
  const internalFactory = () => { throw new Error('Legacy call reached internal worker factory') }
  const publicFactory = (received: unknown) => {
    assert.equal(received, options)
    return result
  }
  const monaco = { createWebWorker: publicFactory, editor: { createWebWorker: internalFactory } } as unknown as typeof Monaco
  try {
    const adapted = createYamlMonaco(monaco, () => ({} as Worker))
    assert.equal(adapted.editor.createWebWorker(options), result)
    assert.equal(monaco.editor.createWebWorker, internalFactory)
    assert.notEqual(adapted.editor, monaco.editor)
  } finally {
    globalThis.MonacoEnvironment = original
  }
})

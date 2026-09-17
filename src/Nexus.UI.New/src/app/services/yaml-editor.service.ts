import { DestroyRef, Injectable, inject } from '@angular/core'
import type * as Monaco from 'monaco-editor'
import { configureMonacoYaml } from 'monaco-yaml'
import type { MonacoYaml, SchemasSettings } from 'monaco-yaml'
import { isJsonObject } from '../json-schema'
import { createYamlMonaco } from './yaml-monaco'

@Injectable({ providedIn: 'root' })
export class YamlEditorService {
  private languageService?: MonacoYaml
  private readonly schemas = new Map<string, SchemasSettings>()

  constructor() {
    inject(DestroyRef).onDestroy(() => this.languageService?.dispose())
  }

  configure(monaco: typeof Monaco, model: Monaco.editor.ITextModel, schema: unknown): void {
    const uri = model.uri.toString()
    if (isJsonObject(schema)) {
      this.schemas.set(uri, { uri: `${uri}.schema.json`, fileMatch: [uri], schema })
    } else {
      this.schemas.delete(uri)
    }
    if (!this.languageService) {
      const yamlMonaco = createYamlMonaco(monaco,
        () => new Worker(new URL('../yaml.worker', import.meta.url), { type: 'module' }))
      // Its current types describe the new internal API, but worker-manager calls the old one.
      this.languageService = configureMonacoYaml(yamlMonaco as unknown as Parameters<typeof configureMonacoYaml>[0], {
        schemas: [...this.schemas.values()],
        enableSchemaRequest: false,
        validate: true,
        hoverSchemaSource: false,
        yamlVersion: '1.2',
        format: { printWidth: 100 },
      })
    } else {
      void this.languageService.update({ schemas: [...this.schemas.values()] })
    }
  }

  release(model: Monaco.editor.ITextModel): void {
    this.schemas.delete(model.uri.toString())
    void this.languageService?.update({ schemas: [...this.schemas.values()] })
  }
}

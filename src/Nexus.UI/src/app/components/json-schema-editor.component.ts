import { Component, DestroyRef, computed, effect, inject, input, linkedSignal, output, signal } from '@angular/core'
import { EditorComponent } from 'ngx-monaco-editor-v2'
import type * as Monaco from 'monaco-editor'
import { ButtonModule } from 'primeng/button'
import { MessageModule } from 'primeng/message'
import { configurationYamlText, createSchemaScaffold, isJsonObject, parseConfigurationText, validateConfiguration } from '../json-schema'
import type { ConfigurationValidation } from '../json-schema'
import { defineNexusMonacoThemes, getNexusMonacoTheme, type ThemeMode } from '../services/nexus-monaco-themes'
import { YamlEditorService } from '../services/yaml-editor.service'

@Component({
  selector: 'app-json-schema-editor',
  standalone: true,
  imports: [EditorComponent, ButtonModule, MessageModule],
  template: `
    <div class="yaml-editor-shell">
      <div class="yaml-editor-toolbar">
        <button pButton type="button" size="small" severity="secondary" [outlined]="true"
          [disabled]="!hasSchema()" (click)="confirmScaffold.set(true)">Generate scaffold</button>
        <button pButton type="button" size="small" severity="secondary" [text]="true"
          [disabled]="!parsed().valid" (click)="formatDocument()">Format YAML</button>
      </div>
      @if (confirmScaffold()) {
        <div class="scaffold-confirmation" role="group" aria-label="Replace draft confirmation">
          <p>Replace the entire YAML draft with a schema scaffold? Rename example_key entries and review placeholder values before saving. You can undo this replacement with Ctrl+Z.</p>
          <div class="yaml-editor-toolbar">
            <button pButton type="button" size="small" [disabled]="!hasSchema()" (click)="generateScaffold()">Replace draft</button>
            <button pButton type="button" size="small" severity="secondary" [text]="true" (click)="confirmScaffold.set(false)">Cancel</button>
          </div>
        </div>
      }
      <ngx-monaco-editor
        style="display:block;flex:1 1 0;min-height:12rem;overflow:hidden;border:1px solid var(--p-content-border-color);border-radius:var(--p-border-radius-md)"
        [options]="editorOptions"
        (onInit)="onEditorInit($event)"></ngx-monaco-editor>
      @if (!validity().valid) {
        <p-message severity="error">
          <ul aria-label="Configuration errors" class="m-0 list-inside list-disc text-sm">
            @for (error of validity().errors; track $index) { <li>{{ error }}</li> }
          </ul>
        </p-message>
      }
      <p class="yaml-editor-help">Ctrl+Space suggests schema fields and values; hover over a field for help. Generate scaffold creates a starting draft, not a ready-to-use configuration. Validation errors below the editor must be resolved before saving.</p>
    </div>
  `,
  styles: [`
    :host { display: flex; flex-direction: column; min-width: 0; flex: 1 1 0; min-height: 0; }
    .yaml-editor-shell { display: flex; flex-direction: column; gap: .75rem; min-width: 0; flex: 1 1 0; min-height: 0; }
    .yaml-editor-toolbar { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; flex: 0 0 auto; }
    .scaffold-confirmation { padding: .75rem; border: 1px solid var(--p-content-border-color); border-radius: var(--p-border-radius-md); flex: 0 0 auto; }
    .scaffold-confirmation p { margin: 0 0 .5rem; font-size: .875rem; }
    .yaml-editor-help { margin: 0; color: var(--p-text-muted-color); font-size: .8125rem; flex: 0 0 auto; }
  `],
})
export class JsonSchemaEditorComponent {
  private readonly destroyRef = inject(DestroyRef)
  private readonly yamlService = inject(YamlEditorService)
  private editor: Monaco.editor.IStandaloneCodeEditor | null = null
  private monaco: typeof Monaco | null = null
  private model: Monaco.editor.ITextModel | null = null
  private suppressChange = false

  readonly editorOptions: Monaco.editor.IStandaloneEditorConstructionOptions = {
    language: 'yaml',
    automaticLayout: true,
    fixedOverflowWidgets: true,
    fontSize: 13,
    minimap: { enabled: false },
    scrollBeyondLastLine: false,
    tabSize: 2,
    insertSpaces: true,
    wordWrap: 'on',
    lineNumbersMinChars: 3,
    quickSuggestions: { other: true, comments: false, strings: true },
    suggestOnTriggerCharacters: true,
  }

  readonly schema = input<unknown>()
  readonly value = input<unknown>()
  readonly rawText = input<string>()
  readonly themeMode = input.required<ThemeMode>()
  readonly valueChange = output<unknown>()
  readonly rawTextChange = output<string>()
  readonly validityChange = output<ConfigurationValidation>()
  readonly saveRequested = output<void>()

  readonly text = linkedSignal(() => this.rawText() ?? configurationYamlText(this.value()))
  readonly parsed = computed(() => parseConfigurationText(this.text()))
  readonly confirmScaffold = signal(false)
  readonly hasSchema = computed(() => isJsonObject(this.schema()))
  readonly validity = computed<ConfigurationValidation>(() => {
    const parsed = this.parsed()
    return parsed.valid ? validateConfiguration(this.schema(), parsed.value) : { valid: false, errors: parsed.errors }
  })

  constructor() {
    effect(() => this.validityChange.emit(this.validity()))
    effect(() => this.updateEditorText(this.text()))
    effect(() => {
      const schema = this.schema()
      if (this.monaco && this.model) this.yamlService.configure(this.monaco, this.model, schema)
    })
    effect(() => this.applyTheme())
    this.destroyRef.onDestroy(() => {
      if (this.model) {
        this.yamlService.release(this.model)
        this.model.dispose()
      }
    })
  }

  onEditorInit(editor: Monaco.editor.IStandaloneCodeEditor): void {
    this.editor = editor
    this.monaco = (window as unknown as { monaco?: typeof Monaco }).monaco ?? null
    this.model = editor.getModel()
    if (this.monaco && this.model) this.yamlService.configure(this.monaco, this.model, this.schema())
    if (this.monaco) defineNexusMonacoThemes(this.monaco)
    this.applyTheme()
    editor.setValue(this.text())
    if (this.monaco) editor.addCommand(this.monaco.KeyMod.CtrlCmd | this.monaco.KeyCode.KeyS, () => this.requestSave())
    editor.onDidChangeModelContent(() => {
      if (!this.editor || this.suppressChange) return
      this.applyText(editor.getValue(), false)
    })
  }

  generateScaffold(): void {
    const editor = this.editor
    const model = this.model
    if (!editor || !model) return
    const text = configurationYamlText(createSchemaScaffold(this.schema()))
    editor.pushUndoStop()
    editor.executeEdits('nexus-scaffold', [{ range: model.getFullModelRange(), text }])
    editor.pushUndoStop()
    this.confirmScaffold.set(false)
    editor.focus()
  }

  formatDocument(): void {
    void this.editor?.getAction('editor.action.formatDocument')?.run()
  }

  private requestSave(): void {
    if (this.editor) this.applyText(this.editor.getValue(), false)
    this.saveRequested.emit()
  }

  private applyText(text: string, updateEditor = true): void {
    this.text.set(text)
    this.rawTextChange.emit(text)
    const parsed = parseConfigurationText(text)
    if (parsed.valid) this.valueChange.emit(parsed.value)
    if (updateEditor) this.updateEditorText(text)
  }

  private updateEditorText(text: string): void {
    if (!this.editor || this.editor.getValue() === text) return
    this.suppressChange = true
    this.editor.setValue(text)
    this.suppressChange = false
  }

  private applyTheme(): void {
    const monaco = this.monaco
    if (!monaco) return
    monaco.editor.setTheme(getNexusMonacoTheme(this.themeMode()))
  }

}

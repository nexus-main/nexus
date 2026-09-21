import { Component, DestroyRef, computed, effect, inject, input, linkedSignal, output, signal } from '@angular/core'
import { EditorComponent } from 'ngx-monaco-editor-v2'
import type * as Monaco from 'monaco-editor'
import { ButtonModule } from 'primeng/button'
import { MessageModule } from 'primeng/message'
import { configurationYamlText, createSchemaScaffold, isJsonObject, parseConfigurationText, validateConfiguration } from '../json-schema'
import type { ConfigurationValidation } from '../json-schema'
import { YamlEditorService } from '../services/yaml-editor.service'

type ThemeMode = 'dark' | 'light'

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
  private themesDefined = false

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
    this.defineThemes()
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
    monaco.editor.setTheme(this.themeMode() === 'dark' ? 'nexus-dark' : 'nexus-light')
  }

  private defineThemes(): void {
    const monaco = this.monaco
    if (!monaco || this.themesDefined) return
    this.themesDefined = true
    monaco.editor.defineTheme('nexus-dark', {
      base: 'vs-dark',
      inherit: true,
      rules: [],
      colors: {
        'editor.background': '#0f172a',
        'editor.foreground': '#cbd5e1',
        'editorLineNumber.foreground': '#475569',
        'editorLineNumber.activeForeground': '#94a3b8',
        'editor.selectionBackground': '#22d3ee33',
        'editor.lineHighlightBackground': '#1e293b',
        'editor.lineHighlightBorder': '#00000000',
        'editorCursor.foreground': '#67e8f9',
        'editorIndentGuide.background1': '#475569',
        'editorIndentGuide.background2': '#475569',
        'editorIndentGuide.background3': '#475569',
        'editorIndentGuide.background4': '#475569',
        'editorIndentGuide.background5': '#475569',
        'editorIndentGuide.background6': '#475569',
        'editorIndentGuide.activeBackground1': '#64748b',
        'editorIndentGuide.activeBackground2': '#64748b',
        'editorIndentGuide.activeBackground3': '#64748b',
        'editorIndentGuide.activeBackground4': '#64748b',
        'editorIndentGuide.activeBackground5': '#64748b',
        'editorIndentGuide.activeBackground6': '#64748b',
        'editorWidget.background': '#1e293b',
        'editorWidget.border': '#334155',
        'editorSuggestWidget.background': '#1e293b',
        'editorSuggestWidget.selectedBackground': '#334155',
        'editorSuggestWidget.highlightForeground': '#67e8f9',
        'editorGutter.background': '#0f172a',
      },
    })
    monaco.editor.defineTheme('nexus-light', {
      base: 'vs',
      inherit: true,
      rules: [],
      colors: {
        'editor.background': '#ffffff',
        'editor.foreground': '#334155',
        'editorLineNumber.foreground': '#cbd5e1',
        'editorLineNumber.activeForeground': '#64748b',
        'editor.selectionBackground': '#22d3ee33',
        'editor.lineHighlightBackground': '#f1f5f9',
        'editor.lineHighlightBorder': '#00000000',
        'editorCursor.foreground': '#0891b2',
        'editorIndentGuide.background1': '#cbd5e1',
        'editorIndentGuide.background2': '#cbd5e1',
        'editorIndentGuide.background3': '#cbd5e1',
        'editorIndentGuide.background4': '#cbd5e1',
        'editorIndentGuide.background5': '#cbd5e1',
        'editorIndentGuide.background6': '#cbd5e1',
        'editorIndentGuide.activeBackground1': '#94a3b8',
        'editorIndentGuide.activeBackground2': '#94a3b8',
        'editorIndentGuide.activeBackground3': '#94a3b8',
        'editorIndentGuide.activeBackground4': '#94a3b8',
        'editorIndentGuide.activeBackground5': '#94a3b8',
        'editorIndentGuide.activeBackground6': '#94a3b8',
        'editorWidget.background': '#ffffff',
        'editorWidget.border': '#e2e8f0',
        'editorSuggestWidget.background': '#ffffff',
        'editorSuggestWidget.selectedBackground': '#f1f5f9',
        'editorSuggestWidget.highlightForeground': '#0891b2',
        'editorGutter.background': '#ffffff',
      },
    })
  }

}

import { Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core'
import { EditorComponent } from 'ngx-monaco-editor-v2'
import type * as Monaco from 'monaco-editor'
import { DialogModule } from 'primeng/dialog'
import { defineNexusMonacoThemes, getNexusMonacoTheme, type ThemeMode } from '../services/nexus-monaco-themes'
import { RestoreFocusDirective } from '../restore-focus.directive'

@Component({
  selector: 'app-properties-dialog',
  standalone: true,
  imports: [DialogModule, EditorComponent, RestoreFocusDirective],
  template: `
    @if (visible()) {
      <p-dialog
        appRestoreFocus
        [header]="header()"
        [visible]="visible()"
        (visibleChange)="visibleChange.emit($event)"
        [modal]="true"
        [blockScroll]="true"
        [dismissableMask]="true"
        [closeOnEscape]="true"
        [draggable]="false"
        [resizable]="false"
        appendTo="body"
        [closeButtonProps]="{ ariaLabel: 'Close properties', severity: 'secondary', text: true, rounded: true }"
        [style]="{ width: 'min(48rem, calc(100vw - 2rem))', maxHeight: '88dvh' }"
        [contentStyle]="{ display: 'flex', flexDirection: 'column', minHeight: '0' }">
        <ngx-monaco-editor
          style="display:block;overflow:hidden"
          [style.height.px]="editorHeight()"
          [options]="editorOptions"
          (onInit)="onEditorInit($event)"></ngx-monaco-editor>
      </p-dialog>
    }
  `,
  styles: [`
    :host { display: contents; }
  `],
})
export class PropertiesDialogComponent {
  readonly visible = input(false)
  readonly header = input('Properties')
  readonly data = input<Record<string, unknown> | null>(null)
  readonly themeMode = input.required<ThemeMode>()
  readonly visibleChange = output<boolean>()

  private monaco: typeof Monaco | null = null
  private editor: Monaco.editor.IStandaloneCodeEditor | null = null
  private readonly destroyRef = inject(DestroyRef)
  readonly editorHeight = signal(84)

  readonly editorOptions: Monaco.editor.IStandaloneEditorConstructionOptions = {
    language: 'json',
    readOnly: true,
    automaticLayout: true,
    minimap: { enabled: false },
    wordWrap: 'on',
    scrollBeyondLastLine: false,
    lineNumbers: 'on',
    folding: true,
    fontSize: 13,
  }

  readonly text = computed(() => {
    const data = this.data()
    if (!data) return '{}'
    try {
      return JSON.stringify(data, null, 2)
    } catch {
      return '{}'
    }
  })

  constructor() {
    effect(() => {
      const themeMode = this.themeMode()
      if (this.monaco) {
        this.monaco.editor.setTheme(getNexusMonacoTheme(themeMode))
      }
    })

    effect(() => {
      const text = this.text()
      if (this.editor) {
        this.editor.setValue(text)
        this.updateHeight()
      }
    })

    this.destroyRef.onDestroy(() => {
      this.editor = null
      this.monaco = null
    })
  }

  onEditorInit(editor: Monaco.editor.IStandaloneCodeEditor): void {
    this.editor = editor
    this.monaco = (window as unknown as { monaco?: typeof Monaco }).monaco ?? null
    if (this.monaco) defineNexusMonacoThemes(this.monaco)
    editor.setValue(this.text())
    if (this.monaco) {
      this.monaco.editor.setTheme(getNexusMonacoTheme(this.themeMode()))
    }
    editor.onDidContentSizeChange(() => this.updateHeight())
    this.updateHeight()
  }

  private updateHeight(): void {
    const editor = this.editor
    if (!editor) return
    const contentHeight = Math.ceil(editor.getContentHeight()) + 16
    const maxHeight = Math.max(84, window.innerHeight * 0.72)
    this.editorHeight.set(Math.min(contentHeight, maxHeight))
    requestAnimationFrame(() => editor.layout())
  }
}

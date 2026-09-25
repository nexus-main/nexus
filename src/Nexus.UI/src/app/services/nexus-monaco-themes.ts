import type * as Monaco from 'monaco-editor'

export type ThemeMode = 'dark' | 'light'

let themesDefined = false

export function defineNexusMonacoThemes(monaco: typeof Monaco): void {
  if (themesDefined) return
  themesDefined = true

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

export function getNexusMonacoTheme(themeMode: ThemeMode): string {
  return themeMode === 'dark' ? 'nexus-dark' : 'nexus-light'
}

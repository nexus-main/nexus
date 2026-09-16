import { Component, computed, effect, input, linkedSignal, output, signal } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { ButtonModule } from 'primeng/button'
import { MessageModule } from 'primeng/message'
import { TextareaModule } from 'primeng/textarea'
import { configurationResetNeedsConfirmation, configurationText, createSchemaValue, getSchemaView, parseJsonSafely, validateConfiguration } from '../json-schema'
import type { ConfigurationValidation } from '../json-schema'
import { SchemaFieldComponent } from './schema-field.component'
import type { SchemaRawChange } from './schema-field.component'

/**
 * Controlled JSON editor. Bind rawText/rawTextChange to retain invalid drafts per registration.
 * A defined rawText is authoritative, including ''. Clear it to undefined to load value instead.
 * valueChange emits only safely parsed JSON (which may still fail schema validation).
 * No output mutates the supplied value, and rendering/mode changes never emit value changes.
 */
@Component({
  selector: 'app-json-schema-editor',
  standalone: true,
  imports: [FormsModule, ButtonModule, MessageModule, TextareaModule, SchemaFieldComponent],
  template: `
    <div class="min-w-0 space-y-3">
      <div class="flex flex-wrap gap-2" role="group" aria-label="Configuration editing mode">
        <button pButton type="button" size="small" severity="secondary" [outlined]="mode() !== 'form'"
          [attr.aria-pressed]="mode() === 'form'" (click)="mode.set('form')">Form</button>
        <button pButton type="button" size="small" severity="secondary" [outlined]="mode() !== 'raw'"
          [attr.aria-pressed]="mode() === 'raw'" (click)="mode.set('raw')">JSON</button>
        <button pButton type="button" size="small" severity="secondary" [text]="true" (click)="requestReset()">Create / reset configuration</button>
      </div>
      @if (confirmingReset()) {
        <div role="alert" class="space-y-2 rounded border p-3" style="border-color: var(--p-content-border-color)">
          <p>Replace the entire configuration and discard its raw draft? This cannot be undone.</p>
          <div class="flex flex-wrap gap-2">
            <button pButton type="button" size="small" severity="danger" (click)="confirmReset()">Replace configuration</button>
            <button pButton type="button" size="small" severity="secondary" (click)="confirmingReset.set(false)">Cancel</button>
          </div>
        </div>
      }
      @if (mode() === 'raw' || (!parsed().valid && !liveNumber())) {
        <textarea pTextarea class="w-full font-mono text-sm" rows="14" aria-label="Configuration JSON"
          spellcheck="false" [ngModel]="text()" (ngModelChange)="editRaw($event)"></textarea>
        @if (mode() === 'form') {
          <p class="text-sm">Resolve the JSON error here before returning to the form. The draft is retained.</p>
        }
      } @else {
        <app-schema-field [schema]="schema()" [value]="formValue()" [present]="true" [required]="true"
          [allowReset]="false" [hideHeader]="true"
          (changed)="editValue($event.value)" (rawChange)="editFieldRaw($event)" />
      }
      @if (!validity().valid) {
        <p-message severity="error">
          <ul aria-label="Configuration errors" class="list-inside list-disc text-sm">
            @for (error of validity().errors; track $index) { <li>{{ error }}</li> }
          </ul>
        </p-message>
      }
      <p class="text-xs">JSON numbers are limited to safe integers and decimal round trips; arbitrary precision is not supported. Keep dates and durations as strings. Unknown schema formats block validation.</p>
    </div>
  `,
})
export class JsonSchemaEditorComponent {
  readonly schema = input<unknown>()
  readonly value = input<unknown>()
  readonly rawText = input<string>()
  readonly valueChange = output<unknown>()
  readonly rawTextChange = output<string>()
  readonly validityChange = output<ConfigurationValidation>()
  readonly mode = signal<'form' | 'raw'>('form')
  readonly text = linkedSignal(() => this.rawText() ?? configurationText(this.value()))
  readonly confirmingReset = linkedSignal(() => {
    this.schema(); this.value(); this.rawText(); this.text()
    return false
  })
  readonly parsed = computed(() => parseJsonSafely(this.text()))
  private readonly numberDraft = signal<{ text: string; schema: unknown; value: unknown } | null>(null)
  readonly liveNumber = computed(() => {
    const draft = this.numberDraft()
    return draft && draft.schema === this.schema() && draft.text === this.text() ? draft : null
  })
  readonly formValue = computed(() => {
    const draft = this.liveNumber()
    if (draft) return draft.value
    const parsed = this.parsed()
    return parsed.valid ? parsed.value : this.value()
  })
  readonly validity = computed<ConfigurationValidation>(() => {
    const parsed = this.parsed()
    return parsed.valid ? validateConfiguration(this.schema(), parsed.value) : { valid: false, errors: parsed.errors }
  })

  constructor() {
    effect(() => this.validityChange.emit(this.validity()))
  }

  editRaw(text: string, editingNumber = false): void {
    if (!editingNumber) this.numberDraft.set(null)
    this.text.set(text)
    this.rawTextChange.emit(text)
    const parsed = parseJsonSafely(text)
    if (!parsed.valid && !editingNumber) this.mode.set('raw')
    if (parsed.valid) this.valueChange.emit(parsed.value)
  }

  editFieldRaw(change: SchemaRawChange): void {
    const value = this.formValue()
    this.numberDraft.set(change.editingNumber ? { text: change.text, schema: this.schema(), value } : null)
    this.editRaw(change.text, change.editingNumber)
  }

  editValue(value: unknown): void {
    // A blur can reveal an invalid draft just before another form control dispatches its event.
    if (!this.parsed().valid) return
    this.editRaw(configurationText(value))
  }

  requestReset(): void {
    if (configurationResetNeedsConfirmation(this.value(), this.rawText()) || this.text() !== '') {
      this.confirmingReset.set(true)
    } else this.resetValue()
  }

  confirmReset(): void {
    if (!this.confirmingReset()) return
    this.confirmingReset.set(false)
    this.resetValue()
  }

  private resetValue(): void {
    this.editRaw(configurationText(createSchemaValue(getSchemaView(this.schema()))))
  }
}

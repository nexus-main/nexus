import { NgTemplateOutlet } from '@angular/common'
import { Component, computed, input, linkedSignal, output, signal } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { ButtonModule } from 'primeng/button'
import { InputTextModule } from 'primeng/inputtext'
import { SelectModule } from 'primeng/select'
import { TextareaModule } from 'primeng/textarea'
import {
  configurationText, configurationWithRawMember, createSchemaValue, getSchemaView,
  isJsonObject, parseJsonSafely, SchemaNumberSession, schemaPresenceOptions, setConfigurationProperty,
} from '../json-schema'

export interface SchemaFieldChange { present: boolean; value: unknown }
export interface SchemaRawChange { text: string; editingNumber?: boolean }

@Component({
  selector: 'app-schema-field',
  standalone: true,
  imports: [NgTemplateOutlet, FormsModule, ButtonModule, InputTextModule, SelectModule, TextareaModule],
  template: `
    <section class="min-w-0 space-y-2 rounded border p-3" style="border-color: var(--p-content-border-color)">
      <div class="flex flex-wrap items-center justify-between gap-2">
        <span class="text-sm font-semibold">{{ view().title || label() }}{{ required() ? ' *' : '' }}</span>
        <p-select [ariaLabel]="label() + ' presence'" [options]="presenceOptions()" optionLabel="label" optionValue="value"
          [ngModel]="presence()" (ngModelChange)="changePresence($event)" appendTo="body" />
      </div>
      @if (view().description) { <p class="text-sm">{{ view().description }}</p> }
      @if (!present()) {
        <p class="text-sm">Not set{{ required() ? ' (required)' : '' }}. Select Value to create explicitly.</p>
      } @else if (value() === null) {
        <p class="text-sm">Null{{ view().nullable ? '' : ' (not allowed by this schema)' }}.</p>
      } @else if (useRaw()) {
        <textarea pTextarea class="w-full font-mono text-sm" rows="5" [attr.aria-label]="label() + ' JSON'"
          [ngModel]="draft()" (ngModelChange)="editJson($event)"></textarea>
        <p class="text-xs">Raw JSON: this schema or value is not supported by the form.</p>
      } @else {
        @switch (view().kind) {
          @case ('enum') {
            <p-select class="w-full" [ariaLabel]="label()" [options]="enumOptions()" optionLabel="label" optionValue="index"
              [ngModel]="enumIndex()" (ngModelChange)="chooseEnum($event)" appendTo="body" />
          }
          @case ('string') {
            <input pInputText type="text" class="w-full" [attr.aria-label]="label()" [ngModel]="value()"
              (ngModelChange)="changed.emit({ present: true, value: $event })" />
          }
          @case ('boolean') {
            <p-select [ariaLabel]="label()" [options]="booleanOptions" optionLabel="label" optionValue="value"
              [ngModel]="value()" (ngModelChange)="changed.emit({ present: true, value: $event })" appendTo="body" />
          }
          @case ('number') { <ng-container [ngTemplateOutlet]="numberInput" /> }
          @case ('integer') { <ng-container [ngTemplateOutlet]="numberInput" /> }
          @case ('object') {
            @for (property of view().properties; track property.key) {
              <app-schema-field [schema]="schema()" [paths]="property.paths" [label]="property.key" [depth]="depth() + 1"
                [value]="objectValue()[property.key]" [present]="hasProperty(property.key)" [required]="property.required"
                (changed)="changeProperty(property.key, $event)" (rawChange)="rawProperty(property.key, $event)" />
            }
            @for (key of additionalKeys(); track key) {
              <div class="space-y-1">
                <app-schema-field [schema]="schema()" [paths]="view().additionalPaths" [label]="key" [depth]="depth() + 1"
                  [value]="objectValue()[key]" [present]="true" [required]="false"
                  (changed)="changeProperty(key, $event)" (rawChange)="rawProperty(key, $event)" />
                @if (!view().allowAdditional) { <p class="text-xs">Unknown field retained; additional properties are not allowed.</p> }
              </div>
            }
            @if (view().allowAdditional) {
              <div class="flex flex-wrap gap-2">
                <input pInputText type="text" [attr.aria-label]="label() + ' new key'" placeholder="New property name"
                  [ngModel]="newKey()" (ngModelChange)="newKey.set($event)" />
                <button pButton type="button" size="small" severity="secondary" [disabled]="hasProperty(newKey())" (click)="addProperty()">Add property</button>
              </div>
            }
          }
          @case ('array') {
            @for (item of arrayValue(); track $index) {
              <div class="space-y-1">
                <app-schema-field [schema]="schema()" [paths]="view().itemPaths" [label]="label() + ' [' + $index + ']'"
                  [depth]="depth() + 1" [value]="item" [present]="true" [required]="true"
                  (changed)="changeItem($index, $event.value)" (rawChange)="rawItem($index, $event)" />
                <button pButton type="button" size="small" severity="secondary" (click)="removeItem($index)">Remove item {{ $index + 1 }}</button>
              </div>
            }
            <button pButton type="button" size="small" severity="secondary" (click)="addItem()">Add item</button>
          }
        }
      }
      @if (localError()) { <p role="alert" class="text-sm text-red-600">{{ localError() }}</p> }
      @if (present() && allowReset()) {
        <button pButton type="button" size="small" severity="secondary" [text]="true" (click)="confirmingReset.set(true)">Reset value</button>
        @if (confirmingReset()) {
          <div role="alert" class="space-y-2">
            <p>Replace {{ label() }} and discard its current value?</p>
            <div class="flex flex-wrap gap-2">
              <button pButton type="button" size="small" severity="danger" (click)="resetValue()">Replace value</button>
              <button pButton type="button" size="small" severity="secondary" (click)="confirmingReset.set(false)">Cancel</button>
            </div>
          </div>
        }
      }
    </section>
    <ng-template #numberInput>
      <input pInputText type="text" inputmode="decimal" class="w-full" [attr.aria-label]="label()"
        [ngModel]="draft()" (ngModelChange)="editNumber($event)" (blur)="finishNumber()" />
    </ng-template>
  `,
})
export class SchemaFieldComponent {
  private readonly numberSession = new SchemaNumberSession()
  readonly schema = input<unknown>()
  readonly paths = input<string[]>(['#'])
  readonly value = input<unknown>()
  readonly present = input(true)
  readonly required = input(true)
  readonly label = input('Configuration')
  readonly depth = input(0)
  readonly allowReset = input(true)
  readonly changed = output<SchemaFieldChange>()
  readonly rawChange = output<SchemaRawChange>()
  readonly view = computed(() => getSchemaView(this.schema(), this.paths()))
  readonly draft = linkedSignal(() => configurationText(this.value()))
  readonly localError = linkedSignal(() => { this.schema(); this.value(); return '' })
  readonly confirmingReset = linkedSignal(() => {
    this.schema(); this.paths(); this.value(); this.present(); this.draft()
    return false
  })
  readonly newKey = signal('')
  readonly booleanOptions = [{ label: 'True', value: true }, { label: 'False', value: false }]
  readonly presenceOptions = computed(() => schemaPresenceOptions(this.required(), this.view().nullable))
  readonly presence = computed(() => !this.present() ? 'unset' : this.value() === null ? 'null' : 'value')
  readonly objectValue = computed(() => isJsonObject(this.value()) ? this.value() as Record<string, unknown> : {})
  readonly arrayValue = computed(() => Array.isArray(this.value()) ? this.value() as unknown[] : [])
  readonly additionalKeys = computed(() => Object.keys(this.objectValue()).filter(key => !this.view().properties.some(property => property.key === key)))
  readonly enumOptions = computed(() => this.view().enumLabels.map((label, index) => ({ label, index })))
  readonly enumIndex = computed(() => this.view().enumValues.findIndex(value => configurationText(value) === configurationText(this.value())))
  readonly useRaw = computed(() => {
    if (this.depth() >= 24) return true
    switch (this.view().kind) {
      case 'object': return !isJsonObject(this.value())
      case 'array': return !Array.isArray(this.value())
      case 'number': case 'integer': return typeof this.value() !== 'number'
      case 'boolean': return typeof this.value() !== 'boolean'
      case 'string': return typeof this.value() !== 'string'
      case 'enum': return this.enumIndex() < 0
      default: return true
    }
  })

  hasProperty(key: string): boolean { return Object.hasOwn(this.objectValue(), key) }

  changePresence(presence: string): void {
    if (presence === this.presence()) return
    this.localError.set('')
    this.changed.emit({ present: presence !== 'unset', value: presence === 'null' ? null : presence === 'unset' ? undefined : createSchemaValue(this.view()) })
  }

  resetValue(): void {
    if (!this.confirmingReset()) return
    this.confirmingReset.set(false)
    this.localError.set('')
    this.changed.emit({ present: true, value: createSchemaValue(this.view()) })
  }

  chooseEnum(index: number): void {
    this.changed.emit({ present: true, value: structuredClone(this.view().enumValues[index]) })
  }

  editJson(text: string): void {
    this.draft.set(text)
    const parsed = parseJsonSafely(text)
    this.localError.set(parsed.valid ? '' : parsed.errors.join(' '))
    this.rawChange.emit({ text })
  }

  editNumber(text: string): void {
    this.numberSession.edit(text)
    this.draft.set(text)
    const parsed = parseJsonSafely(text)
    this.localError.set(!parsed.valid ? parsed.errors.join(' ') : typeof parsed.value !== 'number' ? 'Enter a JSON number.' : '')
    // Emit the token, not Number(text), so incomplete and unsafe drafts survive in the parent.
    this.rawChange.emit({ text, editingNumber: true })
  }

  finishNumber(): void {
    const text = this.numberSession.finish()
    if (text !== null) this.rawChange.emit({ text, editingNumber: false })
  }

  changeProperty(key: string, change: SchemaFieldChange): void {
    this.changed.emit({ present: true, value: setConfigurationProperty(this.objectValue(), key, change.value, change.present) })
  }

  rawProperty(key: string, change: SchemaRawChange): void {
    this.rawChange.emit({ ...change, text: configurationWithRawMember(this.objectValue(), key, change.text) })
  }

  addProperty(): void {
    if (this.hasProperty(this.newKey())) return
    const property = this.view().properties.find(property => property.key === this.newKey())
    this.changeProperty(this.newKey(), { present: true, value: createSchemaValue(getSchemaView(this.schema(), property?.paths ?? this.view().additionalPaths)) })
    this.newKey.set('')
  }

  changeItem(index: number, value: unknown): void {
    this.changed.emit({ present: true, value: this.arrayValue().map((item, i) => i === index ? value : item) })
  }

  rawItem(index: number, change: SchemaRawChange): void {
    this.rawChange.emit({ ...change, text: configurationWithRawMember(this.arrayValue(), index, change.text) })
  }

  removeItem(index: number): void {
    this.changed.emit({ present: true, value: this.arrayValue().filter((_, i) => i !== index) })
  }

  addItem(): void {
    this.changed.emit({ present: true, value: [...this.arrayValue(), createSchemaValue(getSchemaView(this.schema(), this.view().itemPaths))] })
  }
}

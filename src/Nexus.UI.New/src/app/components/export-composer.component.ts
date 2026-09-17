import { CommonModule } from '@angular/common'
import { Component, computed, input, output } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { ButtonModule } from 'primeng/button'
import { DialogModule } from 'primeng/dialog'
import { InputTextModule } from 'primeng/inputtext'
import { MessageModule } from 'primeng/message'
import { SelectModule } from 'primeng/select'
import { V2, WriterDescription, WriterOption } from '../nexus.service'
import { RestoreFocusDirective } from '../restore-focus.directive'

type QuickRange = {
  label: string
  begin: string
  end: string
}

@Component({
  selector: 'app-export-composer',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, DialogModule, InputTextModule, MessageModule, SelectModule, RestoreFocusDirective],
  template: `
    <p-dialog appRestoreFocus [visible]="true" (visibleChange)="!$event && close.emit()" [modal]="true" [dismissableMask]="true" [closeOnEscape]="true" [blockScroll]="true" appendTo="body" [draggable]="false" [resizable]="false" [closeButtonProps]="{ ariaLabel: 'Close export composer', severity: 'secondary', text: true, rounded: true }" [style]="{ width: 'min(48rem, calc(100vw - 2rem))' }">
        <ng-template #header let-ariaLabelledBy="ariaLabelledBy">
          <div>
            <div class="text-xs uppercase tracking-[0.22em]">export composer</div>
            <div [id]="ariaLabelledBy" class="mt-1 text-lg font-semibold">Package selected resources</div>
          </div>
        </ng-template>
        <p class="text-sm">Metadata-driven writer options, compact defaults, job response inline.</p>

        <div class="mt-4 space-y-3">
          <div>
            <label id="export-writer-label" for="export-writer" class="mb-1.5 block text-xs uppercase tracking-[0.18em]">Writer</label>
            <p-select inputId="export-writer" ariaLabelledBy="export-writer-label" class="w-full" appendTo="body" [options]="writerSelectOptions()" optionLabel="label" optionValue="value" [ngModel]="selectedWriterType()" (ngModelChange)="selectedWriterTypeChange.emit($event)" />
          </div>

          <div class="grid gap-3 sm:grid-cols-2">
            <div><label for="export-begin" class="mb-1.5 block text-xs uppercase tracking-[0.18em]">Begin</label><input pInputText id="export-begin" type="text" class="w-full" [ngModel]="exportBegin()" (ngModelChange)="exportBeginChange.emit($event)" /></div>
            <div><label for="export-end" class="mb-1.5 block text-xs uppercase tracking-[0.18em]">End</label><input pInputText id="export-end" type="text" class="w-full" [ngModel]="exportEnd()" (ngModelChange)="exportEndChange.emit($event)" /></div>
            <div><label for="export-file-period" class="mb-1.5 block text-xs uppercase tracking-[0.18em]">File period</label><input pInputText id="export-file-period" type="text" class="w-full" [ngModel]="exportFilePeriod()" (ngModelChange)="exportFilePeriodChange.emit($event)" /></div>
            <div>
              <label id="export-precision-label" for="export-precision" class="mb-1.5 block text-xs uppercase tracking-[0.18em]">Precision</label>
              <p-select inputId="export-precision" ariaLabelledBy="export-precision-label" class="w-full" appendTo="body" [options]="precisionOptions" optionLabel="label" optionValue="value" [ngModel]="exportPrecision()" (ngModelChange)="exportPrecisionChange.emit($event)" />
            </div>
          </div>

          <div class="flex flex-wrap gap-2">
            @for (range of quickRanges(); track range.label) {
              <button pButton type="button" size="small" severity="secondary" (click)="quickRangeApplied.emit(range)">{{ range.label }}</button>
            }
          </div>

          @for (option of writerOptions(); track option[0]) {
            <div>
              <label [id]="'export-option-label-' + option[0]" [for]="'export-option-' + option[0]" class="mb-1.5 block text-xs uppercase tracking-[0.18em]">{{ option[1].label ?? option[0] }}</label>
              @if (option[1].items) {
                <p-select [inputId]="'export-option-' + option[0]" [ariaLabelledBy]="'export-option-label-' + option[0]" class="w-full" appendTo="body" [options]="option[1].items | keyvalue" optionLabel="value" optionValue="key" [ngModel]="exportConfiguration()[option[0]] ?? option[1].default" (ngModelChange)="configChanged.emit({ key: option[0], value: $event })" />
              } @else {
                <input pInputText [id]="'export-option-' + option[0]" type="text" class="w-full" [ngModel]="exportConfiguration()[option[0]] ?? option[1].default" (ngModelChange)="configChanged.emit({ key: option[0], value: $event })" />
              }
            </div>
          }
        </div>

        <div class="mt-4">
          <div class="mb-2 text-xs uppercase tracking-[0.18em]">selection payload</div>
          <div class="max-h-32 space-y-1 overflow-auto font-mono text-xs">
            @for (path of exportPreview().resourcePaths; track path) { <div class="truncate" [title]="path">{{ path }}</div> }
            @if (!exportPreview().resourcePaths?.length) { <div>No resources selected yet.</div> }
          </div>
        </div>
        <pre class="mt-4 max-h-40 overflow-auto text-xs">{{ exportPreview() | json }}</pre>
        @if (exportStatus()) { <p-message severity="info" class="mt-3">{{ exportStatus() }}</p-message> }
        @if (exportError()) { <p-message severity="error" class="mt-3">{{ exportError() }}</p-message> }
        <button pButton type="button" size="small" class="mt-4 w-full" [disabled]="!!exportError() || exportBusy()" (click)="createJob.emit()">{{ exportBusy() ? 'Creating...' : 'Create export job' }}</button>
    </p-dialog>
  `,
})
export class ExportComposerComponent {
  readonly writerDescriptions = input.required<WriterDescription[]>()
  readonly selectedWriterType = input.required<string>()
  readonly exportBegin = input.required<string>()
  readonly exportEnd = input.required<string>()
  readonly exportFilePeriod = input.required<string>()
  readonly exportPrecision = input.required<V2.Precision>()
  readonly writerOptions = input.required<[string, WriterOption][]>()
  readonly exportConfiguration = input.required<Record<string, unknown>>()
  readonly quickRanges = input.required<QuickRange[]>()
  readonly exportError = input.required<string>()
  readonly exportPreview = input.required<V2.ExportParameters>()
  readonly exportStatus = input.required<string>()
  readonly exportBusy = input.required<boolean>()

  readonly writerSelectOptions = computed(() => this.writerDescriptions().map(writer => ({
    label: writer.additionalInformation?.label ?? writer.type ?? '',
    value: writer.type ?? '',
  })))
  readonly precisionOptions = [
    { label: '32-bit', value: V2.Precision.Float32 },
    { label: '64-bit', value: V2.Precision.Float64 },
  ]

  readonly close = output<void>()
  readonly selectedWriterTypeChange = output<string>()
  readonly exportBeginChange = output<string>()
  readonly exportEndChange = output<string>()
  readonly exportFilePeriodChange = output<string>()
  readonly exportPrecisionChange = output<V2.Precision>()
  readonly configChanged = output<{ key: string; value: unknown }>()
  readonly quickRangeApplied = output<QuickRange>()
  readonly createJob = output<void>()
}

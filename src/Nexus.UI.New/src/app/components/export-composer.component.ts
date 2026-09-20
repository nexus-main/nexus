import { CommonModule } from '@angular/common'
import { Component, computed, input, output } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { ButtonModule } from 'primeng/button'
import { DialogModule } from 'primeng/dialog'
import { InputTextModule } from 'primeng/inputtext'
import { MessageModule } from 'primeng/message'
import { ProgressBarModule } from 'primeng/progressbar'
import { SelectModule } from 'primeng/select'
import { TooltipModule } from 'primeng/tooltip'
import { V2, WriterDescription, WriterOption } from '../nexus.service'
import { RestoreFocusDirective } from '../restore-focus.directive'

@Component({
  selector: 'app-export-composer',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, DialogModule, InputTextModule, MessageModule, ProgressBarModule, SelectModule, TooltipModule, RestoreFocusDirective],
  template: `
    <p-dialog appRestoreFocus [visible]="true" (visibleChange)="!$event && close.emit()" [modal]="true" [dismissableMask]="true" [closeOnEscape]="true" [blockScroll]="true" appendTo="body" [draggable]="false" [resizable]="false" [closeButtonProps]="{ ariaLabel: 'Close export composer', severity: 'secondary', text: true, rounded: true }" [style]="{ width: 'min(48rem, calc(100vw - 2rem))' }">
        <ng-template #header let-ariaLabelledBy="ariaLabelledBy">
          <div>
            <div class="text-xs uppercase tracking-[0.22em]">export job</div>
            <div [id]="ariaLabelledBy" class="mt-1 text-lg font-semibold">Export selected resources</div>
          </div>
        </ng-template>

        <div class="mt-4 space-y-3">
          <div class="rounded-sm border border-white/10 bg-white/[0.035] px-3 py-2 text-sm">
            <div class="text-xs uppercase tracking-[0.18em] text-slate-400">Selected range</div>
            <div class="mt-2 grid gap-2 sm:grid-cols-2">
              <div class="min-w-0"><div class="text-[11px] uppercase tracking-[0.16em] text-slate-500">From</div><div class="mt-0.5 truncate font-mono text-xs text-slate-200" [pTooltip]="exportBegin()" tooltipPosition="top">{{ exportBegin() }}</div></div>
              <div class="min-w-0"><div class="text-[11px] uppercase tracking-[0.16em] text-slate-500">To</div><div class="mt-0.5 truncate font-mono text-xs text-slate-200" [pTooltip]="exportEnd()" tooltipPosition="top">{{ exportEnd() }}</div></div>
            </div>
          </div>

          <div class="grid gap-3 sm:grid-cols-2">
            <div><label for="export-file-period" class="mb-1.5 block text-xs uppercase tracking-[0.18em]">File period</label><input pInputText pSize="small" id="export-file-period" type="text" class="w-full" [invalid]="!!exportFilePeriodError()" [attr.aria-invalid]="!!exportFilePeriodError()" [ngModel]="exportFilePeriod()" (ngModelChange)="exportFilePeriodChange.emit($event)" (blur)="exportFilePeriodBlur.emit()" placeholder="Single file" />@if (exportFilePeriodError()) { <p class="mt-1 text-xs text-rose-400" role="alert">{{ exportFilePeriodError() }}</p> }</div>
            <div>
              <label id="export-precision-label" for="export-precision" class="mb-1.5 block text-xs uppercase tracking-[0.18em]">Precision</label>
              <p-select inputId="export-precision" ariaLabelledBy="export-precision-label" class="w-full" appendTo="body" size="small" [options]="precisionOptions" optionLabel="label" optionValue="value" [ngModel]="exportPrecision()" (ngModelChange)="exportPrecisionChange.emit($event)" />
            </div>
          </div>

          <div>
            <label id="export-writer-label" for="export-writer" class="mb-1.5 block text-xs uppercase tracking-[0.18em]">Writer</label>
            <p-select inputId="export-writer" ariaLabelledBy="export-writer-label" class="w-full" appendTo="body" size="small" [options]="writerSelectOptions()" optionLabel="label" optionValue="value" [ngModel]="selectedWriterType()" (ngModelChange)="selectedWriterTypeChange.emit($event)" />
          </div>

          @for (option of writerOptions(); track option[0]) {
            <div>
              <label [id]="'export-option-label-' + option[0]" [for]="'export-option-' + option[0]" class="mb-1.5 block text-xs uppercase tracking-[0.18em]">{{ option[1].label ?? option[0] }}</label>
              @if (option[1].items) {
                <p-select [inputId]="'export-option-' + option[0]" [ariaLabelledBy]="'export-option-label-' + option[0]" class="w-full" appendTo="body" size="small" [options]="option[1].items | keyvalue" optionLabel="value" optionValue="key" [ngModel]="exportConfiguration()[option[0]] ?? option[1].default" (ngModelChange)="configChanged.emit({ key: option[0], value: $event })" />
              } @else {
                <input pInputText pSize="small" [id]="'export-option-' + option[0]" type="text" class="w-full" [ngModel]="exportConfiguration()[option[0]] ?? option[1].default" (ngModelChange)="configChanged.emit({ key: option[0], value: $event })" />
              }
            </div>
          }
        </div>

        @if (exportSize()) {
          <div class="mt-4 rounded-sm border border-white/10 px-3 py-2 text-sm" [pTooltip]="rawDataSizeTooltip" tooltipPosition="top">
            <div class="text-xs uppercase tracking-[0.18em] text-slate-400">Estimated raw data size</div>
            <div class="mt-1 font-mono">{{ exportSize() }}</div>
          </div>
        }
        @if (currentJobStatus() || currentJobError()) {
          <div class="mt-4 rounded-sm border border-white/10 bg-white/[0.035] px-3 py-3 text-sm" aria-live="polite">
            <div class="flex items-start justify-between gap-3">
              <div>
                <div class="text-xs uppercase tracking-[0.18em] text-slate-400">Current job</div>
                @if (currentJobStatus()) { <div class="mt-1 text-slate-200">{{ currentJobStatus() }}</div> }
              </div>
              <div class="shrink-0 font-mono text-xs text-slate-400">{{ currentJobProgress() }}%</div>
            </div>
            @if (!currentJobError()) { <p-progressbar class="nexus-progress-outlined mt-3 block" [value]="currentJobProgress()" ariaLabel="Export job progress" /> }
            @if (currentJobError()) { <p-message severity="error" class="mt-3">{{ currentJobError() }}</p-message> }
            <div class="mt-3 flex flex-wrap justify-end gap-2">
              @if (currentJobCanCancel()) { <button pButton type="button" size="small" severity="secondary" [text]="true" (click)="cancelJob.emit()">Cancel job</button> }
              @if (currentJobCanDownload()) { <button pButton type="button" size="small" [outlined]="true" [disabled]="currentJobDownloading()" (click)="downloadJob.emit()">{{ currentJobDownloading() ? 'Downloading...' : 'Download result' }}</button> }
            </div>
          </div>
        }
        @if (exportStatus()) { <p-message severity="info" class="mt-3">{{ exportStatus() }}</p-message> }
        @if (exportError()) { <p-message severity="error" class="mt-3">{{ exportError() }}</p-message> }
        <span class="mt-4 block w-full" [pTooltip]="exportError()" [tooltipDisabled]="!exportError()" tooltipPosition="top">
          <button pButton type="button" size="small" class="w-full" [outlined]="true" [disabled]="!!exportError() || exportBusy() || currentJobCanCancel()" (click)="createJob.emit()">{{ exportBusy() ? 'Creating...' : 'Create export job' }}</button>
        </span>
    </p-dialog>
  `,
})
export class ExportComposerComponent {
  readonly writerDescriptions = input.required<WriterDescription[]>()
  readonly selectedWriterType = input.required<string>()
  readonly exportBegin = input.required<string>()
  readonly exportEnd = input.required<string>()
  readonly exportFilePeriod = input.required<string>()
  readonly exportFilePeriodError = input.required<string>()
  readonly exportPrecision = input.required<V2.Precision>()
  readonly exportSize = input('')
  readonly writerOptions = input.required<[string, WriterOption][]>()
  readonly exportConfiguration = input.required<Record<string, unknown>>()
  readonly exportError = input.required<string>()
  readonly exportPreview = input.required<V2.ExportParameters>()
  readonly exportStatus = input.required<string>()
  readonly exportBusy = input.required<boolean>()
  readonly currentJobStatus = input('')
  readonly currentJobProgress = input(0)
  readonly currentJobError = input('')
  readonly currentJobCanCancel = input(false)
  readonly currentJobCanDownload = input(false)
  readonly currentJobDownloading = input(false)

  readonly writerSelectOptions = computed(() => this.writerDescriptions().map(writer => ({
    label: writer.additionalInformation?.label ?? writer.type ?? '',
    value: writer.type ?? '',
  })))
  readonly precisionOptions = [
    { label: '32-bit', value: V2.Precision.Float32 },
    { label: '64-bit', value: V2.Precision.Float64 },
  ]
  readonly rawDataSizeTooltip = 'Approximate uncompressed sample payload for the selected time range, resources, and precision. Compression-friendly formats may be smaller; text formats such as CSV can be larger because each value is written as characters.'

  readonly close = output<void>()
  readonly selectedWriterTypeChange = output<string>()
  readonly exportFilePeriodChange = output<string>()
  readonly exportFilePeriodBlur = output<void>()
  readonly exportPrecisionChange = output<V2.Precision>()
  readonly configChanged = output<{ key: string; value: unknown }>()
  readonly createJob = output<void>()
  readonly cancelJob = output<void>()
  readonly downloadJob = output<void>()
}

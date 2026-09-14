import { CommonModule } from '@angular/common'
import { Component, input, output } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { ResourceRow, V2, WriterDescription, WriterOption } from '../nexus.service'

type QuickRange = {
  label: string
  begin: string
  end: string
}

@Component({
  selector: 'app-export-composer',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 grid place-items-center bg-black/70 p-3 sm:p-6" (click)="close.emit()">
      <section class="glass-panel max-h-[calc(100vh-2rem)] w-full max-w-3xl overflow-auto rounded-xl p-4" (click)="$event.stopPropagation()">
        <div class="flex items-start justify-between gap-3">
          <div>
            <div class="text-xs uppercase tracking-[0.22em] text-lime-200/80">export composer</div>
            <div class="mt-1 text-lg font-semibold text-white">Package selected resources</div>
            <p class="mt-1 text-sm text-slate-500">Metadata-driven writer options, compact defaults, job response inline.</p>
          </div>
          <button type="button" class="rounded-xl bg-white/10 px-3 py-2 text-slate-300" (click)="close.emit()">Close</button>
        </div>

        <div class="mt-4 space-y-3">
          <label class="block"><span class="mb-1.5 block text-xs uppercase tracking-[0.18em] text-slate-500">Writer</span>
            <select class="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 text-sm text-white outline-none focus:border-cyan-300/60" [ngModel]="selectedWriterType()" (ngModelChange)="selectedWriterTypeChange.emit($event)">
              @for (writer of writerDescriptions(); track writer.type) {
                <option [value]="writer.type ?? ''">{{ writer.additionalInformation?.label ?? writer.type }}</option>
              }
            </select>
          </label>

          <div class="grid gap-3 sm:grid-cols-2">
            <label class="block"><span class="mb-1.5 block text-xs uppercase tracking-[0.18em] text-slate-500">Begin</span><input class="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 font-mono text-xs text-white outline-none focus:border-cyan-300/60" [ngModel]="exportBegin()" (ngModelChange)="exportBeginChange.emit($event)" /></label>
            <label class="block"><span class="mb-1.5 block text-xs uppercase tracking-[0.18em] text-slate-500">End</span><input class="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 font-mono text-xs text-white outline-none focus:border-cyan-300/60" [ngModel]="exportEnd()" (ngModelChange)="exportEndChange.emit($event)" /></label>
            <label class="block"><span class="mb-1.5 block text-xs uppercase tracking-[0.18em] text-slate-500">File period</span><input class="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 font-mono text-xs text-white outline-none focus:border-cyan-300/60" [ngModel]="exportFilePeriod()" (ngModelChange)="exportFilePeriodChange.emit($event)" /></label>
            <label class="block"><span class="mb-1.5 block text-xs uppercase tracking-[0.18em] text-slate-500">Precision</span>
              <select class="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 text-sm text-white outline-none focus:border-cyan-300/60" [ngModel]="exportPrecision()" (ngModelChange)="exportPrecisionChange.emit($event)">
                <option [value]="'Float32'">32-bit</option>
                <option [value]="'Float64'">64-bit</option>
              </select>
            </label>
          </div>

          <div class="flex flex-wrap gap-2">
            @for (range of quickRanges(); track range.label) {
              <button type="button" class="rounded-xl border border-white/10 bg-white/[0.045] px-3 py-2 text-xs font-medium text-slate-300" (click)="quickRangeApplied.emit(range)">{{ range.label }}</button>
            }
          </div>

          @for (option of writerOptions(); track option[0]) {
            <label class="block"><span class="mb-1.5 block text-xs uppercase tracking-[0.18em] text-slate-500">{{ option[1].label ?? option[0] }}</span>
              @if (option[1].items) {
                <select class="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 text-sm text-white outline-none focus:border-cyan-300/60" [ngModel]="exportConfiguration()[option[0]] ?? option[1].default" (ngModelChange)="configChanged.emit({ key: option[0], value: $event })">
                  @for (item of option[1].items | keyvalue; track item.key) { <option [value]="item.key">{{ item.value }}</option> }
                </select>
              } @else {
                <input class="w-full rounded-xl border border-white/10 bg-slate-950 px-3 py-2 text-sm text-white outline-none focus:border-cyan-300/60" [ngModel]="exportConfiguration()[option[0]] ?? option[1].default" (ngModelChange)="configChanged.emit({ key: option[0], value: $event })" />
              }
            </label>
          }
        </div>

        <div class="mt-4 rounded-2xl border border-white/10 bg-slate-950/60 p-3">
          <div class="mb-2 text-xs uppercase tracking-[0.18em] text-slate-500">selection payload</div>
          <div class="max-h-32 space-y-1 overflow-auto font-mono text-xs text-slate-400">
            @for (resource of selectedResources(); track resource.path) { <div class="truncate">{{ resource.path }}</div> }
            @if (selectedResources().length === 0) { <div>No resources selected yet.</div> }
          </div>
        </div>
        <pre class="mt-4 max-h-40 overflow-auto rounded-2xl border border-white/10 bg-slate-950/70 p-3 text-xs text-slate-300">{{ exportPreview() | json }}</pre>
        @if (exportStatus()) { <div class="mt-3 rounded-2xl border border-lime-300/20 bg-lime-300/10 p-3 text-sm text-lime-100">{{ exportStatus() }}</div> }
        <button type="button" class="mt-4 flex w-full items-center justify-center gap-2 rounded-2xl bg-gradient-to-r from-cyan-300 to-lime-300 px-4 py-3 text-sm font-bold text-slate-950 shadow-[0_0_36px_rgba(34,211,238,0.22)] transition hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-45" [disabled]="selectedResources().length === 0 || exportBusy()" (click)="createJob.emit()">{{ exportBusy() ? 'Creating...' : 'Create export job' }}</button>
      </section>
    </div>
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
  readonly selectedResources = input.required<ResourceRow[]>()
  readonly exportPreview = input.required<V2.ExportParameters>()
  readonly exportStatus = input.required<string>()
  readonly exportBusy = input.required<boolean>()

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

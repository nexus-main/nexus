import { CommonModule } from '@angular/common'
import { Component, computed, input, output, signal } from '@angular/core'
import { ButtonModule } from 'primeng/button'
import { Popover, PopoverModule } from 'primeng/popover'
import { TooltipModule } from 'primeng/tooltip'
import { ResourceSelection, RepresentationKind, representationKinds, kindValid, formatPeriod, requestPath } from '../resource-selection'

@Component({
  selector: 'app-pinned-resource',
  standalone: true,
  imports: [CommonModule, ButtonModule, PopoverModule, TooltipModule],
  host: { class: 'block min-w-0' },
  template: `
    <div class="selected-resource-card mb-1 min-w-0 cursor-pointer rounded-sm border border-cyan-300/10 bg-cyan-300/[0.035] p-1 transition hover:border-cyan-300/25 hover:bg-cyan-300/[0.06] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-cyan-300/70" role="button" tabindex="0"
      [attr.aria-label]="'Select catalog for ' + resourceLabel()" (click)="activated.emit()" (keydown.enter)="activated.emit()" (keydown.space)="$event.preventDefault(); activated.emit()">
      <div class="flex min-w-0 items-center justify-between gap-1">
        <div class="min-w-0">
          <div class="truncate font-mono text-xs font-semibold leading-4 text-slate-100" [pTooltip]="selection().path" tooltipPosition="top">{{ selection().id }}</div>
          @if (parameterSummary()) {
            <div class="truncate font-mono text-[11px] text-slate-400" [pTooltip]="parameterSummary()" tooltipPosition="top">{{ parameterSummary() }}</div>
          }
        </div>
        <button pButton type="button" size="small" severity="secondary" [text]="true"
          class="shrink-0 p-0.5" [disabled]="disabled()" [attr.aria-label]="'Deselect ' + resourceLabel()"
          [pTooltip]="'Deselect ' + resourceLabel()" tooltipPosition="top" (click)="$event.stopPropagation(); removed.emit()">
          <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true" focusable="false"><path d="m6 6 12 12M6 18 18 6" /></svg>
        </button>
      </div>

      <div class="mt-0.5 flex flex-wrap items-center gap-1">
        @for (kind of selection().kinds; track kind) {
          <span class="group relative inline-flex min-w-16 justify-center overflow-hidden rounded-md border text-[11px] font-medium leading-4 transition-colors"
            [ngClass]="methodChipClass(kind)">
            <span class="flex w-full items-center justify-center px-1.5 py-0.5 text-center transition-opacity group-hover:opacity-0 group-focus-within:opacity-0">{{ displayKind(kind) }}</span>
            <span class="pointer-events-none absolute inset-0 grid grid-cols-2 opacity-0 transition-opacity group-hover:pointer-events-auto group-hover:opacity-100 group-focus-within:pointer-events-auto group-focus-within:opacity-100">
              <button type="button" class="flex items-center justify-center border-r border-current/25 bg-current/10 text-current hover:bg-current/20"
                [disabled]="disabled()" [attr.aria-label]="'Copy ' + displayKind(kind) + ' resource path for ' + resourceLabel()"
                (click)="$event.stopPropagation(); copyMethodPath(kind)">
                <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true" focusable="false"><rect x="9" y="9" width="13" height="13" rx="2" /><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" /></svg>
              </button>
              <button type="button" class="flex items-center justify-center bg-current/10 text-current hover:bg-current/20"
                [disabled]="disabled()" [attr.aria-label]="'Remove ' + displayKind(kind) + ' method from ' + resourceLabel()"
                (click)="$event.stopPropagation(); kindToggled.emit(kind)">
                <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true" focusable="false"><path d="m6 6 12 12M6 18 18 6" /></svg>
              </button>
            </span>
          </span>
        }
        <button #methodsOpener pButton type="button" size="small" severity="secondary" [text]="true"
          class="px-1.5 py-0.5" [hidden]="!validKinds().length" [disabled]="disabled()"
          [attr.aria-label]="'Add methods for ' + resourceLabel()" aria-haspopup="dialog"
          [pTooltip]="'Add methods for ' + resourceLabel()" tooltipPosition="top"
          [attr.aria-expanded]="methods.overlayVisible" (click)="$event.stopPropagation(); methods.toggle($event, methodsOpener)"
          (keydown.escape)="methods.overlayVisible && closeMethods($event, methods)">
          <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true" focusable="false"><path d="M12 5v14M5 12h14" /></svg>
        </button>
      </div>
    </div>

    @if (resourcePathCopied()) {
      <div class="fixed bottom-4 right-4 z-50 rounded-sm border border-emerald-300/25 bg-emerald-300/15 px-4 py-2 text-sm font-medium text-emerald-100 shadow-lg shadow-black/30" role="status" aria-live="polite">Resource path copied</div>
    }

    <p-popover #methods appendTo="body" [ariaLabel]="'Methods for ' + resourceLabel()"
      [style]="{ width: 'min(20rem, calc(100vw - 2rem))' }" [focusOnShow]="false"
      (onShow)="methodsContent.focus()" (onHide)="methodsOpener.isConnected && methodsOpener.focus()">
      <div #methodsContent tabindex="-1" class="min-w-0" (keydown.escape)="closeMethods($event, methods)">
        <div class="flex items-center justify-between gap-2">
          <span class="text-xs font-semibold text-slate-200">Methods</span>
          <button pButton type="button" size="small" severity="secondary" [text]="true" class="px-2 py-1 text-xs"
            [attr.aria-label]="'Close methods for ' + resourceLabel()" (click)="methods.hide()">Done</button>
        </div>
        <p class="my-2 text-xs text-slate-400">Select one or more methods for the output period.</p>
        <div class="flex max-h-60 flex-wrap gap-1.5 overflow-y-auto" role="group" [attr.aria-label]="'Valid methods for ' + resourceLabel()">
          @for (kind of validKinds(); track kind) {
            <button pButton type="button" size="small" class="px-2 py-1 text-xs"
              [ngClass]="methodOptionClass(kind)" severity="secondary"
              [outlined]="!methodSelected(kind)" [attr.aria-pressed]="methodSelected(kind)"
              [attr.aria-label]="displayKind(kind) + ' method for ' + resourceLabel()" [disabled]="disabled()"
              (click)="kindToggled.emit(kind)">{{ displayKind(kind) }}</button>
          } @empty {
            <p class="m-0 text-xs text-slate-400">No methods are valid for this output period. Choose a compatible period.</p>
          }
        </div>
      </div>
    </p-popover>
  `,
})
export class PinnedResourceComponent {
  readonly selection = input.required<ResourceSelection>()
  readonly period = input.required<bigint>()
  readonly disabled = input(false)
  readonly kindToggled = output<RepresentationKind>()
  readonly removed = output<void>()
  readonly activated = output<void>()
  readonly resourcePathCopied = signal(false)
  readonly formatPeriod = formatPeriod
  readonly parameterSummary = computed(() => Object.entries(this.selection().parameters)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([key, value]) => `${key}=${value}`)
    .join(', '))
  readonly resourceLabel = computed(() => `${this.selection().path}, native period ${formatPeriod(this.selection().basePeriod)}`)
  readonly validKinds = computed(() => representationKinds.filter(kind => this.valid(kind)))

  valid(kind: RepresentationKind): boolean {
    return kindValid(kind, this.period(), this.selection().basePeriod)
  }

  invalidReason(kind: RepresentationKind): string {
    const base = formatPeriod(this.selection().basePeriod)
    const requirement = kind === 'Original'
      ? `must equal the native period (${base})`
      : kind === 'Resampled'
        ? `must be positive, shorter than and divide the native period (${base}) exactly`
        : `must be a larger integer multiple of the native period (${base})`
    return `${this.displayKind(kind)} is invalid for the selected output period. The output period ${requirement}. Click to remove this method.`
  }

  displayKind(kind: RepresentationKind): string {
    if (kind === 'MeanPolarDeg') return 'Mean polar (deg)'
    if (kind === 'Std') return 'STD'
    if (kind === 'Rms') return 'RMS'
    if (kind === 'MinBitwise') return 'Minimum (bitwise)'
    if (kind === 'MaxBitwise') return 'Maximum (bitwise)'
    return kind
  }

  methodChipClass(kind: RepresentationKind): string {
    if (!this.valid(kind)) return 'method-invalid border-rose-400/55 bg-rose-500/15 text-rose-100'
    if (kind === 'Original') return 'method-original border-cyan-300/45 bg-cyan-300/15 text-cyan-100'
    if (kind === 'Resampled') return 'method-resampled border-lime-300/45 bg-lime-300/15 text-lime-100'
    return 'method-aggregated border-orange-400/60 bg-orange-500/20 text-orange-100'
  }

  methodOptionClass(kind: RepresentationKind): string {
    const selected = this.methodSelected(kind)
    if (kind === 'Original') return selected
      ? 'method-original border-cyan-300/60 bg-cyan-300/25 text-cyan-50'
      : 'method-unselected border-slate-500/45 bg-transparent text-slate-200'
    if (kind === 'Resampled') return selected
      ? 'method-resampled border-lime-300/60 bg-lime-300/25 text-lime-50'
      : 'method-unselected border-slate-500/45 bg-transparent text-slate-200'
    return selected
      ? 'method-aggregated border-orange-400/70 bg-orange-500/30 text-orange-50'
      : 'method-unselected border-slate-500/45 bg-transparent text-slate-200'
  }

  methodSelected(kind: RepresentationKind): boolean {
    return this.selection().kinds.includes(kind)
  }

  methodPath(kind: RepresentationKind): string {
    return requestPath(this.selection(), kind, this.period())
  }

  copyMethodPath(kind: RepresentationKind): void {
    if (!navigator.clipboard) return

    void navigator.clipboard.writeText(this.methodPath(kind)).then(() => {
      this.resourcePathCopied.set(true)
      window.setTimeout(() => this.resourcePathCopied.set(false), 1800)
    })
  }

  closeMethods(event: Event, methods: Popover) {
    // Keep Escape away from the mobile drawer's document listener.
    event.preventDefault()
    event.stopPropagation()
    methods.hide()
  }
}

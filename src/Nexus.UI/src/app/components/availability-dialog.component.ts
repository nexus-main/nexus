import { Component, effect, inject, input, output, signal } from '@angular/core'
import { LucideX } from '@lucide/angular'
import { DialogModule } from 'primeng/dialog'
import { RestoreFocusDirective } from '../restore-focus.directive'
import { NexusService } from '../nexus.service'
import { AvailabilityChartComponent } from '../charts/availability-chart.component'
import { TICKS_PER_DAY } from '../charts/chart-math'
import { toTimeSpan } from '../resource-selection'

type ThemeMode = 'dark' | 'light'
const MAX_AVAILABILITY_STEPS = 1000
const MILLISECONDS_PER_DAY = 86_400_000

@Component({
  selector: 'app-availability-dialog',
  standalone: true,
  imports: [DialogModule, RestoreFocusDirective, AvailabilityChartComponent, LucideX],
  template: `
    @if (visible()) {
      <p-dialog
        appRestoreFocus
        header="Availability"
        [visible]="visible()"
        (visibleChange)="visibleChange.emit($event)"
        [modal]="true"
        [blockScroll]="true"
        [dismissableMask]="true"
        [closeOnEscape]="true"
        [draggable]="false"
        [resizable]="false"
        appendTo="body"
        [closeButtonProps]="{ ariaLabel: 'Close availability', severity: 'secondary', text: true, rounded: true }"
        [style]="{ width: 'min(64rem, calc(100vw - 2rem))', height: 'min(40rem, 80dvh)' }"
        [contentStyle]="{ display: 'flex', flexDirection: 'column', minHeight: '0', padding: '0' }">
        <ng-template #closeicon><svg lucideX class="h-4 w-4" aria-hidden="true"></svg></ng-template>
        @if (loading()) {
          <div class="flex flex-1 items-center justify-center p-6 text-sm text-ink-muted">Loading availability data ...</div>
        } @else if (error()) {
          <div class="flex flex-1 items-center justify-center p-6 text-center text-sm text-rose-accent" role="alert">{{ error() }}</div>
        } @else {
          <nexus-availability-chart
            class="flex-1"
            [data]="data()"
            [begin]="beginDate()"
            [end]="endDate()"
            [themeMode]="themeMode()" />
        }
      </p-dialog>
    }
  `,
  styles: [`:host { display: contents; }`],
})
export class AvailabilityDialogComponent {
  readonly visible = input(false)
  readonly catalogId = input('')
  readonly begin = input('')
  readonly end = input('')
  readonly themeMode = input<ThemeMode>('dark')
  readonly visibleChange = output<boolean>()

  private readonly nexus = inject(NexusService)
  readonly loading = signal(false)
  readonly error = signal('')
  readonly data = signal<number[]>([])
  readonly beginDate = signal('')
  readonly endDate = signal('')

  private controller?: AbortController

  constructor() {
    effect(() => {
      if (!this.visible()) return
      const catalogId = this.catalogId()
      const begin = this.begin()
      const end = this.end()
      this.load(catalogId, begin, end)
    })
  }

  private load(catalogId: string, begin: string, end: string): void {
    this.controller?.abort()
    if (!catalogId || !begin || !end) return

    const beginDate = begin.slice(0, 10) + 'T00:00:00Z'
    const endDate = end.slice(0, 10) + 'T00:00:00Z'

    if (beginDate >= endDate) {
      this.data.set([])
      this.error.set('')
      this.loading.set(false)
      this.beginDate.set(beginDate)
      this.endDate.set(endDate)
      return
    }

    this.controller = new AbortController()
    this.loading.set(true)
    this.error.set('')

    const rangeDays = Math.ceil((Date.parse(endDate) - Date.parse(beginDate)) / MILLISECONDS_PER_DAY)
    const stepDays = Math.max(1, Math.ceil(rangeDays / MAX_AVAILABILITY_STEPS))
    const step = toTimeSpan(TICKS_PER_DAY * BigInt(stepDays))
    this.nexus.v1.catalogs.getAvailability(catalogId, beginDate, endDate, step, this.controller.signal)
      .then(result => {
        this.data.set(result.data ?? [])
        this.beginDate.set(beginDate)
        this.endDate.set(endDate)
      })
      .catch(err => {
        if (err instanceof DOMException && err.name === 'AbortError') return
        this.error.set(err instanceof Error ? err.message : String(err))
      })
      .finally(() => { this.loading.set(false) })
  }
}

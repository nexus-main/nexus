import { Component, computed, effect, inject, input, output, signal } from '@angular/core'
import { LucideExternalLink, LucideInfo, LucideX } from '@lucide/angular'
import { DialogModule } from 'primeng/dialog'
import { RestoreFocusDirective } from '../restore-focus.directive'
import { NexusService, V1 } from '../nexus.service'

interface AboutEntry {
  type: string
  version?: string
  description?: string | null
  infoUrl?: string | null
  projectUrl?: string | null
  repositoryUrl?: string | null
}

@Component({
  selector: 'app-catalog-about-dialog',
  standalone: true,
  imports: [DialogModule, RestoreFocusDirective, LucideExternalLink, LucideInfo, LucideX],
  template: `
    @if (visible()) {
      <p-dialog
        appRestoreFocus
        header="About"
        [visible]="visible()"
        (visibleChange)="visibleChange.emit($event)"
        [modal]="true"
        [blockScroll]="true"
        [dismissableMask]="true"
        [closeOnEscape]="true"
        [draggable]="false"
        [resizable]="false"
        appendTo="body"
        [closeButtonProps]="{ ariaLabel: 'Close about', severity: 'secondary', text: true, rounded: true }"
        [style]="{ width: 'min(36rem, calc(100vw - 2rem))', maxHeight: '88dvh' }">
        <ng-template #closeicon><svg lucideX class="h-4 w-4" aria-hidden="true"></svg></ng-template>
        @if (loading()) {
          <div class="grid min-h-32 place-items-center text-sm text-ink-muted">Loading ...</div>
        } @else if (entries().length) {
          <div class="flex flex-col gap-3 py-2">
            @for (entry of entries(); track entry.type) {
              <h2 class="mt-2 border-b border-surface-border p-1 text-xs font-semibold uppercase tracking-widest text-cyan-accent first:mt-0">{{ entry.type }}</h2>
              <div class="flex flex-col gap-2 p-1 text-sm text-ink-muted">
                @if (entry.version) {
                  <p class="font-mono text-xs text-ink">{{ entry.version }}</p>
                }
                @if (entry.description) {
                  <p class="text-sm">{{ entry.description }}</p>
                }
                @if (entry.infoUrl) {
                  <a class="inline-flex items-center gap-1.5 text-sm text-cyan-accent hover:text-cyan-accent/80" [href]="entry.infoUrl" target="_blank" rel="noopener noreferrer">
                    <svg lucideInfo class="h-4 w-4 shrink-0" aria-hidden="true"></svg>
                    <span>Info Website</span>
                  </a>
                }
                <div class="flex justify-between gap-2">
                  @if (entry.projectUrl) {
                    <a class="inline-flex items-center gap-1.5 text-sm text-cyan-accent hover:text-cyan-accent/80" [href]="entry.projectUrl" target="_blank" rel="noopener noreferrer">
                      <svg lucideExternalLink class="h-4 w-4 shrink-0" aria-hidden="true"></svg>
                      <span>Project Website</span>
                    </a>
                  }
                  @if (entry.repositoryUrl) {
                    <a class="inline-flex items-center gap-1.5 text-sm text-cyan-accent hover:text-cyan-accent/80" [href]="entry.repositoryUrl" target="_blank" rel="noopener noreferrer">
                      <svg lucideExternalLink class="h-4 w-4 shrink-0" aria-hidden="true"></svg>
                      <span>Source Repository</span>
                    </a>
                  }
                </div>
              </div>
            }
          </div>
        } @else {
          <div class="grid min-h-32 place-items-center text-sm text-ink-muted">No data available.</div>
        }
      </p-dialog>
    }
  `,
  styles: [`
    :host { display: contents; }
  `],
})
export class CatalogAboutDialogComponent {
  readonly visible = input(false)
  readonly pipelineInfo = input<V1.PipelineInfo | null | undefined>(null)
  readonly visibleChange = output<boolean>()

  private readonly nexus = inject(NexusService)
  readonly loading = signal(false)
  readonly entries = signal<AboutEntry[]>([])

  constructor() {
    effect(() => {
      if (!this.visible()) return
      const pipelineInfo = this.pipelineInfo()
      if (!pipelineInfo?.types?.length) {
        this.entries.set([])
        return
      }
      this.loading.set(true)
      this.nexus.v1.sources.getDescriptions()
        .then(descriptions => {
          const types = pipelineInfo.types ?? []
          const infoUrls = pipelineInfo.infoUrls ?? []
          const entries: AboutEntry[] = types.map((type, i) => {
            const desc = descriptions.find(d => d.type === type)
            return {
              type,
              version: desc?.version,
              description: desc?.description,
              infoUrl: infoUrls[i] ?? null,
              projectUrl: desc?.projectUrl ?? null,
              repositoryUrl: desc?.repositoryUrl ?? null,
            }
          })
          this.entries.set(entries)
        })
        .catch(() => this.entries.set([]))
        .finally(() => this.loading.set(false))
    })
  }
}

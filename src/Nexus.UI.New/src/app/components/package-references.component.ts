import { Component, ElementRef, afterRenderEffect, computed, inject, output, signal, viewChild } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { NgClass } from '@angular/common'
import { LucidePlus, LucideRefreshCw, LucideTrash2 } from '@lucide/angular'
import { ButtonModule } from 'primeng/button'
import { DialogModule } from 'primeng/dialog'
import { InputTextModule } from 'primeng/inputtext'
import { MessageModule } from 'primeng/message'
import { SelectModule } from 'primeng/select'
import { DialogPassThrough } from 'primeng/types/dialog'
import { NexusService, V1 } from '../nexus.service'
import { RestoreFocusDirective } from '../restore-focus.directive'

type PackageEntry = { id: string; reference: V1.PackageReference }

@Component({
  selector: 'app-package-references',
  standalone: true,
  imports: [NgClass, FormsModule, ButtonModule, DialogModule, InputTextModule, MessageModule, SelectModule, RestoreFocusDirective, LucidePlus, LucideRefreshCw, LucideTrash2],
  template: `
    <p-dialog appRestoreFocus header="Administrator / Package references" [visible]="true" (visibleChange)="!$event && !busy() && close.emit()" [modal]="true" [blockScroll]="true" [dismissableMask]="false" [closeOnEscape]="false" [pt]="dialogPt" [closable]="!busy()" [draggable]="false" [resizable]="false" appendTo="body" styleClass="package-references-dialog" [closeButtonProps]="{ ariaLabel: 'Close package references', severity: 'secondary', text: true, rounded: true }" [style]="{ width: 'min(48rem, calc(100vw - 2rem))' }">
      <div #panel tabindex="-1" class="space-y-4" [attr.aria-busy]="loading() || busy()">
        @if (error()) { <p-message severity="error">{{ error() }}</p-message> }
        @if (status()) { <p-message severity="success">{{ status() }}</p-message> }

        @if (editing()) {
          <h2 class="text-lg font-semibold">{{ editedEntry() ? 'Edit package reference' : 'Add package reference' }}</h2>
          <form class="space-y-3" (ngSubmit)="save()">
            <div>
              <label id="package-provider-label" for="package-provider" class="mb-1 block text-sm">Provider</label>
              <p-select inputId="package-provider" ariaLabelledBy="package-provider-label" name="provider" class="w-full" appendTo="body" [options]="providers" [ngModel]="provider()" (ngModelChange)="setProvider($event)" [disabled]="busy()" size="small" />
            </div>
            <div>
              <label for="package-location" class="mb-1 block text-sm">{{ provider() === 'local' ? 'Path on the Nexus server' : 'Repository URL' }}</label>
              <input pInputText pSize="small" id="package-location" name="location" class="w-full" required [ngModel]="location()" (ngModelChange)="location.set($event)" [disabled]="busy()" />
            </div>
            <div>
              <label for="package-version" class="mb-1 block text-sm">{{ provider() === 'local' ? 'Version folder' : 'Git tag' }}</label>
              <p-select inputId="package-version" ariaLabel="Package version" name="version" class="w-full" appendTo="body" [editable]="true" [options]="versionOptions()" [loading]="versionsLoading()" [ngModel]="version()" (ngModelChange)="version.set($event)" [disabled]="busy() || versionsLoading()" size="small" />
              <div class="mt-1 flex items-center justify-between gap-2 text-xs">
                @if (versionsLoading()) {
                  <span>Loading available versions...</span>
                } @else if (versionError()) {
                  <span>{{ versionError() }}</span>
                } @else if (!versionOptions().length) {
                  <span>Enter a location above and load versions, or type one manually.</span>
                } @else {
                  <span>{{ versionOptions().length }} available versions loaded.</span>
                }
                <button pButton type="button" size="small" severity="secondary" [text]="true" [disabled]="busy() || versionsLoading() || !location().trim()" (click)="loadVersions()">Reload versions</button>
              </div>
            </div>
            <div>
              <label for="package-entrypoint" class="mb-1 block text-sm">Entrypoint</label>
              <input pInputText pSize="small" id="package-entrypoint" name="entrypoint" class="w-full" required aria-describedby="package-entrypoint-help" [ngModel]="entrypoint()" (ngModelChange)="entrypoint.set($event)" [disabled]="busy()" />
              <p id="package-entrypoint-help" class="mt-1 text-xs">Relative path to the extension .csproj file.</p>
            </div>
            <div class="flex justify-between gap-2">
              @if (editedEntry()) {
                @if (confirmingDelete()) {
                  <span class="flex items-center gap-2 text-xs text-rose-300">
                    <svg lucideTrash2 class="h-4 w-4" aria-hidden="true"></svg>
                    Delete this package reference?
                  </span>
                  <div class="flex gap-2 ml-auto">
                    <button pButton type="button" size="small" severity="secondary" [text]="true" [disabled]="busy()" (click)="confirmingDelete.set(false)">No</button>
                    <button pButton type="button" size="small" severity="danger" [disabled]="busy()" (click)="remove()">{{ busy() ? 'Deleting...' : 'Yes, delete' }}</button>
                  </div>
                } @else {
                  <button pButton type="button" size="small" severity="danger" [text]="true" [disabled]="busy()" (click)="confirmingDelete.set(true)" aria-label="Delete package reference"><svg lucideTrash2 class="h-4 w-4" aria-hidden="true"></svg></button>
                  <div class="flex gap-2 ml-auto">
                    <button pButton type="button" size="small" severity="secondary" [text]="true" [disabled]="busy()" (click)="cancel()">Cancel</button>
                    <button pButton type="submit" size="small" [disabled]="!valid() || busy()">{{ busy() ? 'Saving...' : 'Save' }}</button>
                  </div>
                }
              } @else {
                <div class="flex gap-2 ml-auto">
                  <button pButton type="button" size="small" severity="secondary" [text]="true" [disabled]="busy()" (click)="cancel()">Cancel</button>
                  <button pButton type="submit" size="small" [disabled]="!valid() || busy()">{{ busy() ? 'Saving...' : 'Save' }}</button>
                </div>
              }
            </div>
          </form>
        } @else {
          <p class="text-sm text-[var(--p-text-muted-color)]">Manage extension packages available to Nexus. Saving references does not reload running extensions until you refresh the database.</p>
          <div class="flex items-center justify-between gap-3 rounded-lg border border-[var(--p-content-border-color)] p-3" style="background: color-mix(in srgb, var(--p-primary-color) 4%, transparent);">
            <div class="flex items-center gap-3">
              <div class="grid h-9 w-9 place-items-center rounded-md border border-[var(--p-content-border-color)]">
                <svg lucideRefreshCw class="h-4 w-4" [class.animate-spin]="refreshing()" aria-hidden="true"></svg>
              </div>
              <div class="min-w-0">
                <div class="text-sm font-medium">Extension database</div>
                @if (refreshStatus()) {
                  <p class="truncate text-xs text-[var(--p-text-muted-color)]" role="status">{{ refreshStatus() }}</p>
                } @else {
                  <p class="text-xs text-[var(--p-text-muted-color)]">Click to reload installed extensions.</p>
                }
              </div>
            </div>
            <button pButton type="button" size="small" [outlined]="true" [disabled]="loading() || refreshing()" (click)="refreshDatabase()">{{ refreshButtonLabel() }}</button>
          </div>
          @if (loading()) {
            <p class="text-sm" role="status">Loading package references...</p>
          } @else if (!error()) {
            <div class="grid gap-3" style="grid-template-columns: repeat(auto-fill, minmax(18rem, 1fr));">
              @for (entry of entries(); track entry.id) {
                <article class="group relative cursor-pointer overflow-hidden rounded-lg border border-[var(--p-content-border-color)] p-3 transition-all hover:border-[var(--p-primary-color)]" style="background: color-mix(in srgb, var(--p-primary-color) 4%, transparent);" role="button" tabindex="0" [attr.aria-label]="'Edit package reference ' + entry.id" (click)="edit(entry)" (keydown.enter)="edit(entry)" (keydown.space)="edit(entry)">
                  <div class="relative">
                    <h2 class="truncate font-mono text-sm font-semibold">{{ packageName(entry) }}</h2>
                    <div class="mt-2 flex items-center gap-2">
                      <span class="rounded-full border px-2 py-0.5 text-xs" [ngClass]="entry.reference.provider === 'local' ? 'border-violet-300/20 text-violet-100' : 'border-cyan-300/20 text-cyan-100'">{{ entry.reference.provider }}</span>
                      <span class="rounded-sm border border-[var(--p-content-border-color)] px-2 py-0.5 font-mono text-xs text-[var(--p-text-muted-color)]">{{ entry.reference.configuration?.['tag'] ?? entry.reference.configuration?.['version'] }}</span>
                    </div>
                  </div>
                </article>
              } @empty {
                <p class="py-6 text-center text-sm">No package references configured.</p>
              }
              <article class="flex cursor-pointer items-center justify-center gap-2 rounded-lg border border-dashed border-[var(--p-content-border-color)] p-3 text-sm font-semibold text-[var(--p-primary-color)] transition-colors hover:border-[var(--p-primary-color)]" role="button" tabindex="0" [attr.aria-label]="'Add package reference'" (click)="edit()" (keydown.enter)="edit()" (keydown.space)="edit()">
                <svg lucidePlus class="h-4 w-4" aria-hidden="true"></svg>
              </article>
            </div>
          }
        }
      </div>
    </p-dialog>
  `,
})
export class PackageReferencesComponent {
  private readonly nexus = inject(NexusService)
  private readonly api = this.nexus.v1.packageReferences
  private readonly jobs = this.nexus.v1.jobs
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel')
  readonly close = output<void>()
  readonly dialogPt: DialogPassThrough = {
    root: {
      // PrimeNG's document handler can lose Escape after a nested select has closed.
      onkeydown: (event: KeyboardEvent) => {
        if (event.key !== 'Escape') return
        event.stopPropagation()
        if (!this.busy()) this.close.emit()
      },
    },
  }
  readonly providers = ['git-tag', 'local']
  readonly entries = signal<PackageEntry[]>([])
  readonly loading = signal(true)
  readonly busy = signal(false)
  readonly error = signal('')
  readonly status = signal('')
  readonly editing = signal(false)
  readonly editedEntry = signal<PackageEntry | null>(null)
  readonly confirmingDelete = signal(false)
  readonly provider = signal('git-tag')
  readonly location = signal('')
  readonly version = signal('')
  readonly versionOptions = signal<string[]>([])
  readonly versionsLoading = signal(false)
  readonly versionError = signal('')
  readonly refreshing = signal(false)
  readonly refreshStatus = signal('')
  readonly entrypoint = signal('')
  readonly valid = computed(() => this.providers.includes(this.provider()) && !!this.location().trim() && !!this.version().trim() && !!this.entrypoint().trim())
  readonly refreshButtonLabel = computed(() => this.refreshing() ? 'Refreshing database...' : 'Refresh database')

  constructor() {
    afterRenderEffect(() => {
      this.editing()
      // View changes remove the focused action; keep keyboard focus inside the dialog.
      if (!this.loading() && !this.busy()) this.panel()?.nativeElement.focus()
    })
    void this.load()
  }

  async load() {
    this.loading.set(true)
    this.error.set('')
    try {
      this.entries.set(Object.entries(await this.api.get()).map(([id, reference]) => ({ id, reference })))
    } catch (error) {
      this.showError('load package references', error)
    } finally {
      this.loading.set(false)
    }
  }

  edit(entry: PackageEntry | null = null) {
    this.error.set('')
    this.status.set('')
    this.confirmingDelete.set(false)
    this.editedEntry.set(entry)
    this.provider.set(entry?.reference.provider ?? 'git-tag')
    const config = entry?.reference.configuration
    this.location.set(config?.[this.provider() === 'local' ? 'path' : 'repository'] ?? '')
    this.version.set(config?.[this.provider() === 'local' ? 'version' : 'tag'] ?? '')
    this.entrypoint.set(config?.['entrypoint'] ?? '')
    this.versionOptions.set([])
    this.versionError.set('')
    this.editing.set(true)
    void this.loadVersions()
  }

  setProvider(provider: string) {
    this.provider.set(provider)
    this.versionOptions.set([])
    this.versionError.set('')
    if (!this.editedEntry()) this.version.set('')
    if (this.location().trim()) void this.loadVersions()
  }

  async loadVersions() {
    if (this.versionsLoading()) return
    const location = this.location().trim()
    if (!location) return
    this.versionsLoading.set(true)
    this.versionError.set('')
    try {
      const reference: V1.PackageReference = {
        provider: this.provider(),
        configuration: { [this.provider() === 'local' ? 'path' : 'repository']: location },
      }
      const versions = await this.api.getVersions(reference)
      this.versionOptions.set([...versions].sort((a, b) => b.localeCompare(a, undefined, { numeric: true, sensitivity: 'base' })))
    } catch (error) {
      this.versionOptions.set([])
      const detail = error instanceof Error ? error.message : 'Unknown error'
      this.versionError.set(`Could not load available versions. ${detail}`)
    } finally {
      this.versionsLoading.set(false)
    }
  }

  cancel() {
    this.error.set('')
    this.confirmingDelete.set(false)
    this.editing.set(false)
  }

  async save() {
    if (!this.valid() || this.busy()) return
    this.busy.set(true)
    this.error.set('')
    const entry = this.editedEntry()
    const provider = this.provider()
    // Preserve provider-specific options that are not exposed by this form.
    const configuration = provider === entry?.reference.provider ? { ...entry.reference.configuration } : {}
    configuration[provider === 'local' ? 'path' : 'repository'] = this.location().trim()
    configuration[provider === 'local' ? 'version' : 'tag'] = this.version().trim()
    configuration['entrypoint'] = this.entrypoint().trim()
    try {
      const reference: V1.PackageReference = { provider, configuration }
      if (entry) {
        await this.api.update(reference, entry.id)
        this.editing.set(false)
        this.status.set('Package reference updated.')
      } else {
        await this.api.create(reference)
        this.editing.set(false)
      }
      await this.load()
    } catch (error) {
      this.showError('save the package reference', error)
    } finally {
      this.busy.set(false)
    }
  }

  async remove() {
    const entry = this.editedEntry()
    if (!entry || this.busy()) return
    this.busy.set(true)
    this.error.set('')
    try {
      await this.api.delete(entry.id)
      this.confirmingDelete.set(false)
      this.editing.set(false)
      this.status.set('Package reference deleted.')
      await this.load()
    } catch (error) {
      this.showError('delete the package reference', error)
    } finally {
      this.busy.set(false)
    }
  }

  async refreshDatabase() {
    if (this.refreshing()) return
    this.refreshing.set(true)
    this.error.set('')
    this.status.set('')
    this.refreshStatus.set('Starting database refresh...')
    try {
      const job = await this.jobs.refreshDatabase()
      const jobId = job.id ?? ''
      if (!jobId) throw new Error('The refresh job did not return an id.')

      while (this.refreshing()) {
        await delay(1000)
        const jobStatus = await this.jobs.getJobStatus(jobId)
        const progress = jobStatus.progress === undefined ? '' : ` (${Math.round(jobStatus.progress * 100)}%)`
        this.refreshStatus.set(`Refresh database: ${jobStatus.status ?? 'Running'}${progress}`)

        if (jobStatus.status === V1.TaskStatus.RanToCompletion) {
          this.refreshStatus.set('Database refresh completed.')
          await this.load()
          return
        }

        if (jobStatus.status === V1.TaskStatus.Canceled) throw new Error('The refresh job was canceled.')
        if (jobStatus.status === V1.TaskStatus.Faulted) throw new Error(`The refresh job failed. Reason: ${jobStatus.exceptionMessage ?? 'unknown'}`)
      }
    } catch (error) {
      this.showError('refresh the database', error)
    } finally {
      this.refreshing.set(false)
    }
  }

  packageName(entry: PackageEntry): string {
    const config = entry.reference.configuration
    const url = config?.['repository'] ?? config?.['path'] ?? entry.id
    return url.split('/').pop() || url
  }

  private showError(action: string, error: unknown) {
    const detail = error instanceof Error ? error.message : 'Unknown error'
    this.error.set(`Could not ${action}. ${detail}${/\b(401|403)\b/.test(detail) ? ' The current session or token must have administrator permission.' : ''}`)
  }
}

function delay(milliseconds: number) {
  return new Promise(resolve => setTimeout(resolve, milliseconds))
}

import { Component, ElementRef, afterRenderEffect, computed, inject, output, signal, viewChild } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { LucidePencil, LucidePlus, LucideRefreshCw, LucideTrash2 } from '@lucide/angular'
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
  imports: [FormsModule, ButtonModule, DialogModule, InputTextModule, MessageModule, SelectModule, RestoreFocusDirective, LucidePencil, LucidePlus, LucideRefreshCw, LucideTrash2],
  template: `
    <p-dialog appRestoreFocus header="Administrator / Package references" [visible]="true" (visibleChange)="!$event && !busy() && close.emit()" [modal]="true" [blockScroll]="true" [dismissableMask]="false" [closeOnEscape]="false" [pt]="dialogPt" [closable]="!busy()" [draggable]="false" [resizable]="false" appendTo="body" [closeButtonProps]="{ ariaLabel: 'Close package references', severity: 'secondary', text: true, rounded: true }" [style]="{ width: 'min(48rem, calc(100vw - 2rem))' }">
      <div #panel tabindex="-1" class="space-y-4" [attr.aria-busy]="loading() || busy()">
        @if (error()) { <p-message severity="error">{{ error() }}</p-message> }
        @if (status()) { <p-message severity="success">{{ status() }}</p-message> }

        @if (editing()) {
          <h2 class="text-lg font-semibold">{{ editedEntry() ? 'Edit package reference' : 'Add package reference' }}</h2>
          <form class="space-y-3" (ngSubmit)="save()">
            <div>
              <label id="package-provider-label" for="package-provider" class="mb-1 block text-sm">Provider</label>
              <p-select inputId="package-provider" ariaLabelledBy="package-provider-label" name="provider" class="w-full" appendTo="body" [options]="providers" [ngModel]="provider()" (ngModelChange)="setProvider($event)" [disabled]="busy()" />
            </div>
            <div>
              <label for="package-location" class="mb-1 block text-sm">{{ provider() === 'local' ? 'Path on the Nexus server' : 'Repository URL' }}</label>
              <input pInputText id="package-location" name="location" class="w-full" required [ngModel]="location()" (ngModelChange)="location.set($event)" [disabled]="busy()" />
            </div>
            <div>
              <label for="package-version" class="mb-1 block text-sm">{{ provider() === 'local' ? 'Version folder' : 'Git tag' }}</label>
              @if (editedEntry()) {
                <p-select inputId="package-version" ariaLabel="Package version" name="version" class="w-full" appendTo="body" [editable]="true" [options]="versionOptions()" [loading]="versionsLoading()" [ngModel]="version()" (ngModelChange)="version.set($event)" [disabled]="busy() || versionsLoading()" />
              } @else {
                <input pInputText id="package-version" name="version" class="w-full" required [ngModel]="version()" (ngModelChange)="version.set($event)" [disabled]="busy() || versionsLoading()" />
              }
              <div class="mt-1 flex items-center justify-between gap-2 text-xs">
                @if (versionsLoading()) {
                  <span>Loading available versions...</span>
                } @else if (versionError()) {
                  <span>{{ versionError() }}</span>
                } @else if (editedEntry() && !versionOptions().length) {
                  <span>No available versions returned; enter one manually.</span>
                } @else if (versionOptions().length) {
                  <span>{{ versionOptions().length }} available versions loaded.</span>
                } @else {
                  <span>Enter an initial tag or version and save. Available versions will load next.</span>
                }
                @if (editedEntry()) {
                  <button pButton type="button" size="small" severity="secondary" [text]="true" [disabled]="busy() || versionsLoading()" (click)="loadVersions()">Reload versions</button>
                }
              </div>
            </div>
            <div>
              <label for="package-entrypoint" class="mb-1 block text-sm">Entrypoint</label>
              <input pInputText id="package-entrypoint" name="entrypoint" class="w-full" required aria-describedby="package-entrypoint-help" [ngModel]="entrypoint()" (ngModelChange)="entrypoint.set($event)" [disabled]="busy()" />
              <p id="package-entrypoint-help" class="mt-1 text-xs">Relative path to the extension .csproj file.</p>
            </div>
            <div class="flex justify-end gap-2">
              <button pButton type="button" size="small" severity="secondary" [disabled]="busy()" (click)="cancel()">Cancel</button>
              <button pButton type="submit" size="small" [disabled]="!valid() || busy()">{{ busy() ? 'Saving...' : 'Save package reference' }}</button>
            </div>
          </form>
        } @else if (deleting(); as entry) {
          <h2 class="text-lg font-semibold">Delete package reference?</h2>
          <p class="break-all font-mono text-sm">{{ entry.reference.configuration?.['repository'] ?? entry.reference.configuration?.['path'] ?? entry.id }}</p>
          <p class="text-sm">Existing pipelines may depend on this package. This cannot be undone.</p>
          <div class="flex justify-end gap-2">
            <button pButton type="button" size="small" severity="secondary" [disabled]="busy()" (click)="cancel()">Cancel</button>
            <button pButton type="button" size="small" severity="danger" [disabled]="busy()" (click)="remove()">{{ busy() ? 'Deleting...' : 'Delete package reference' }}</button>
          </div>
        } @else {
          <p class="text-sm">Manage extension packages available to Nexus. Saving references does not reload running extensions until you refresh the database.</p>
          <div class="flex flex-wrap justify-end gap-2">
            <button pButton type="button" size="small" severity="secondary" [disabled]="loading() || refreshing()" (click)="load()">Reload list</button>
            <button pButton type="button" size="small" severity="secondary" [disabled]="loading() || refreshing()" (click)="refreshDatabase()"><svg lucideRefreshCw class="h-4 w-4" aria-hidden="true"></svg>{{ refreshButtonLabel() }}</button>
            <button pButton type="button" size="small" [disabled]="loading() || !!error()" (click)="edit()"><svg lucidePlus class="h-4 w-4" aria-hidden="true"></svg>Add package reference</button>
          </div>
          @if (refreshStatus()) {
            <p class="text-sm" role="status">{{ refreshStatus() }}</p>
          }
          @if (loading()) {
            <p class="text-sm" role="status">Loading package references...</p>
          } @else if (!error()) {
            @for (entry of entries(); track entry.id) {
              <article class="rounded-xl border border-white/10 p-3">
                <div class="flex items-start justify-between gap-2">
                  <div class="min-w-0">
                    <h2 class="break-all font-mono text-sm font-semibold">{{ entry.reference.configuration?.['repository'] ?? entry.reference.configuration?.['path'] ?? 'Package reference' }}</h2>
                    <p class="mt-1 text-xs">{{ entry.reference.provider }} / {{ entry.reference.configuration?.['tag'] ?? entry.reference.configuration?.['version'] }}</p>
                  </div>
                  <div class="flex shrink-0 gap-1">
                    <button pButton type="button" size="small" severity="secondary" [text]="true" [disabled]="!providers.includes(entry.reference.provider ?? '')" (click)="edit(entry)" [attr.aria-label]="'Edit package reference ' + entry.id"><svg lucidePencil class="h-4 w-4" aria-hidden="true"></svg></button>
                    <button pButton type="button" size="small" severity="danger" [text]="true" (click)="confirmDelete(entry)" [attr.aria-label]="'Delete package reference ' + entry.id"><svg lucideTrash2 class="h-4 w-4" aria-hidden="true"></svg></button>
                  </div>
                </div>
                <p class="mt-2 break-all font-mono text-xs">{{ entry.reference.configuration?.['entrypoint'] }}</p>
                @if (!providers.includes(entry.reference.provider ?? '')) { <p class="mt-2 text-xs">Editing this provider is not supported.</p> }
              </article>
            } @empty {
              <p class="py-6 text-center text-sm">No package references configured.</p>
            }
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
  readonly deleting = signal<PackageEntry | null>(null)
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
      this.deleting()
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
    this.editedEntry.set(entry)
    this.provider.set(entry?.reference.provider ?? 'git-tag')
    const config = entry?.reference.configuration
    this.location.set(config?.[this.provider() === 'local' ? 'path' : 'repository'] ?? '')
    this.version.set(config?.[this.provider() === 'local' ? 'version' : 'tag'] ?? '')
    this.entrypoint.set(config?.['entrypoint'] ?? '')
    this.versionOptions.set([])
    this.versionError.set('')
    this.editing.set(true)
    if (entry) void this.loadVersions()
  }

  setProvider(provider: string) {
    this.provider.set(provider)
    this.versionOptions.set([])
    this.versionError.set('')
    if (!this.editedEntry()) this.version.set('')
  }

  async loadVersions() {
    const entry = this.editedEntry()
    if (!entry || this.versionsLoading()) return
    this.versionsLoading.set(true)
    this.versionError.set('')
    try {
      const versions = await this.api.getVersions(entry.id)
      this.versionOptions.set([...versions].sort((a, b) => b.localeCompare(a, undefined, { numeric: true, sensitivity: 'base' })))
    } catch (error) {
      this.versionOptions.set([])
      const detail = error instanceof Error ? error.message : 'Unknown error'
      this.versionError.set(`Could not load available versions. ${detail}`)
    } finally {
      this.versionsLoading.set(false)
    }
  }

  confirmDelete(entry: PackageEntry) {
    this.error.set('')
    this.status.set('')
    this.deleting.set(entry)
  }

  cancel() {
    this.error.set('')
    this.editing.set(false)
    this.deleting.set(null)
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
      } else {
        const id = await this.api.create(reference)
        this.edit({ id, reference })
      }
      this.status.set(entry ? 'Package reference updated.' : 'Package reference created.')
      await this.load()
    } catch (error) {
      this.showError('save the package reference', error)
    } finally {
      this.busy.set(false)
    }
  }

  async remove() {
    const entry = this.deleting()
    if (!entry || this.busy()) return
    this.busy.set(true)
    this.error.set('')
    try {
      await this.api.delete(entry.id)
      this.deleting.set(null)
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

  private showError(action: string, error: unknown) {
    const detail = error instanceof Error ? error.message : 'Unknown error'
    this.error.set(`Could not ${action}. ${detail}${/\b(401|403)\b/.test(detail) ? ' The current session or token must have administrator permission.' : ''}`)
  }
}

function delay(milliseconds: number) {
  return new Promise(resolve => setTimeout(resolve, milliseconds))
}

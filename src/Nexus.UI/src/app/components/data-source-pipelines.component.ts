import { CommonModule } from '@angular/common'
import { Component, DestroyRef, ElementRef, afterRenderEffect, computed, inject, input, output, signal, viewChild } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { ButtonModule } from 'primeng/button'
import { ConfirmDialogModule } from 'primeng/confirmdialog'
import { DialogModule } from 'primeng/dialog'
import { InputTextModule } from 'primeng/inputtext'
import { MessageModule } from 'primeng/message'
import { SelectModule } from 'primeng/select'
import { TabsModule } from 'primeng/tabs'
import { ToastModule } from 'primeng/toast'
import { TooltipModule } from 'primeng/tooltip'
import { ConfirmationService, ConfirmEventType, MessageService } from 'primeng/api'
import type { DialogPassThrough } from 'primeng/types/dialog'
import { LucideCircleHelp, LucidePlus } from '@lucide/angular'
import { NexusService, V1 } from '../nexus.service'
import { RestoreFocusDirective } from '../restore-focus.directive'
import { JsonSchemaEditorComponent } from './json-schema-editor.component'
import { acceptPipelineSave, addRegistration, createPipelineDraft, editRegistrationText, moveRegistration, pipelineIsDirty,
  preparePipeline, reconcilePipelineDraft, removeRegistration, resolveUnsavedChoice, sourceSchema, updateRegistration } from '../data-source-pipelines'
import type { PipelineDraft, UnsavedChoice } from '../data-source-pipelines'

type Destination = { kind: 'pipeline'; id: string | null } | { kind: 'close' } | { kind: 'refresh' } | { kind: 'descriptions' } | { kind: 'reload' }
type PipelineEntry = { id: string; pipeline: V1.DataSourcePipeline }
type ThemeMode = 'dark' | 'light'

@Component({
  selector: 'app-data-source-pipelines',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, ConfirmDialogModule, DialogModule, InputTextModule, MessageModule, SelectModule, TabsModule, ToastModule, TooltipModule, RestoreFocusDirective, JsonSchemaEditorComponent, LucideCircleHelp, LucidePlus],
  providers: [ConfirmationService, MessageService],
  templateUrl: './data-source-pipelines.component.html',
  styleUrl: './data-source-pipelines.component.css',
})
export class DataSourcePipelinesComponent {
  private readonly nexus = inject(NexusService)
  private readonly api = this.nexus.v1.sources
  private readonly destroyRef = inject(DestroyRef)
  private readonly confirmationService = inject(ConfirmationService)
  private readonly messageService = inject(MessageService)
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel')
  private readonly lifetime = new AbortController()
  private readController?: AbortController
  private readGeneration = 0
  private destroyed = false
  readonly refreshDatabase = input.required<() => Promise<boolean>>()
  readonly themeMode = input.required<ThemeMode>()
  readonly close = output<void>()
  readonly administrator = computed(() => this.nexus.currentUser()?.claims?.some(claim => claim.type === 'role' && claim.value === 'Administrator') ?? false)
  readonly owner = computed(() => this.nexus.currentUser()?.name ?? this.nexus.currentUser()?.userId ?? 'Current user')
  readonly entries = signal<PipelineEntry[]>([])
  readonly descriptions = signal<V1.ExtensionDescription[]>([])
  readonly draft = signal<PipelineDraft | null>(null)
  readonly selectedKey = signal<number | null>(null)
  readonly pipelineTab = signal<'pipelines' | 'pipeline' | 'registration'>('pipelines')
  readonly mobileView = signal<'list' | 'pipeline' | 'registration'>('list')
  readonly loading = signal(false)
  readonly busy = signal(false)
  readonly refreshing = signal(false)
  readonly loaded = signal(false)
  readonly error = signal('')
  readonly descriptionError = signal('')
  readonly pending = signal<Destination | null>(null)
  readonly confirmingDelete = signal(false)
  readonly removingKey = signal<number | null>(null)
  readonly dirty = computed(() => { const draft = this.draft(); return draft !== null && pipelineIsDirty(draft) })
  readonly locked = computed(() => this.loading() || this.busy() || this.refreshing())
  readonly editingLocked = computed(() => this.locked() || !this.loaded() || !!this.pending() || this.confirmingDelete() || this.removingKey() !== null || !this.administrator())
  readonly prepared = computed(() => { const draft = this.draft(); return draft ? preparePipeline(draft, this.descriptions()) : null })
  readonly selected = computed(() => this.draft()?.registrations.find(registration => registration.key === this.selectedKey()))
  readonly selectedSchema = computed(() => sourceSchema(this.descriptions(), this.selected()?.type ?? ''))
  readonly typeOptions = computed(() => {
    const types = this.descriptions().flatMap(description => description.type ? [description.type] : [])
    const selected = this.selected()?.type
    if (selected && !types.includes(selected)) types.unshift(selected)
    return types
  })
  readonly dialogPt: DialogPassThrough = { root: { onkeydown: (event: KeyboardEvent) => {
    if (event.key !== 'Escape' || event.defaultPrevented) return
    event.stopPropagation()
    if (this.locked()) return
    if (this.pending()) { this.confirmationService.close(); this.pending.set(null) }
    else if (this.confirmingDelete()) this.cancelDelete()
    else if (this.removingKey() !== null) this.removingKey.set(null)
    else this.request({ kind: 'close' })
  } } }

  constructor() {
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if (this.dirty() || this.busy() || this.refreshing()) { event.preventDefault(); event.returnValue = '' }
    }
    globalThis.addEventListener?.('beforeunload', beforeUnload)
    this.destroyRef.onDestroy(() => {
      this.destroyed = true
      this.readGeneration++
      this.readController?.abort()
      this.lifetime.abort()
      globalThis.removeEventListener?.('beforeunload', beforeUnload)
    })
    afterRenderEffect(() => {
      this.mobileView()
      this.selectedKey()
      this.pending()
      this.confirmingDelete()
      this.removingKey()
      if (!this.locked()) this.panel()?.nativeElement.focus()
    })
    void this.load()
  }

  hasIssue(key: number): boolean { return this.prepared()?.issues.some(issue => issue.key === key) ?? false }

  displayLocator(url: string | null | undefined): string {
    return url ? url.replace(/^file:\/\//i, '') : ''
  }

  private async load(descriptionsOnly = false): Promise<boolean> {
    if (!this.administrator() || this.destroyed) return false
    this.readController?.abort()
    const controller = this.readController = new AbortController()
    const generation = ++this.readGeneration
    this.loading.set(true)
    if (!descriptionsOnly) this.loaded.set(false)
    this.error.set('')
    try {
      const [descriptions, pipelines] = await Promise.allSettled([
        this.api.getDescriptions(controller.signal),
        descriptionsOnly ? Promise.resolve(null) : this.api.getPipelines(controller.signal),
      ])
      if (this.destroyed || generation !== this.readGeneration) return false
      this.descriptions.set(descriptions.status === 'fulfilled' ? descriptions.value : [])
      this.descriptionError.set(descriptions.status === 'fulfilled' ? '' :
        `Source descriptions could not be loaded. Raw configuration remains editable; saving is blocked. ${String(descriptions.reason)}`)
      if (pipelines.status === 'rejected') {
        this.showError('load pipelines; your draft has been retained', pipelines.reason)
        return false
      }
      if (pipelines.value !== null) {
        this.entries.set(Object.entries(pipelines.value).map(([id, pipeline]) => ({ id, pipeline })))
        this.loaded.set(true)
        const previous = this.draft()
        // Full reloads are guarded; partial reloads retain dirty buffers but reconcile clean drafts.
        const next = reconcilePipelineDraft(previous, pipelines.value, descriptions.status === 'fulfilled')
        this.draft.set(next)
        if (next === null) { this.selectedKey.set(null); this.pipelineTab.set('pipelines'); this.mobileView.set('list') }
        else if (next.registrations !== previous?.registrations) {
          this.selectedKey.set(next.registrations[0]?.key ?? null)
        }
      }
      return descriptions.status === 'fulfilled'
    } catch (error) {
      if (!this.destroyed && generation === this.readGeneration) this.showError('load pipelines and source descriptions', error)
      return false
    } finally {
      if (!this.destroyed && generation === this.readGeneration) this.loading.set(false)
    }
  }

  request(destination: Destination): void {
    if (this.locked() || this.pending() || this.confirmingDelete() || this.removingKey() !== null) return
    if (destination.kind !== 'close' && (!this.administrator() || (!this.loaded() && destination.kind !== 'reload' && destination.kind !== 'descriptions'))) return
    if (destination.kind === 'pipeline' && destination.id !== null && destination.id === this.draft()?.id) {
      if (this.draft()?.serverDiverged) destination = { kind: 'reload' }
      else { this.pipelineTab.set('pipeline'); this.mobileView.set('pipeline'); return }
    }
    if (this.dirty()) { this.pending.set(destination); this.showUnsavedConfirm(destination) }
    else void this.proceed(destination)
  }

  private showUnsavedConfirm(destination: Destination): void {
    const isDescriptions = destination.kind === 'descriptions'
    const isRefreshOrReload = destination.kind === 'refresh' || destination.kind === 'reload'
    this.confirmationService.confirm({
      key: 'unsavedChanges',
      header: isDescriptions ? 'Retry source descriptions?' : 'Unsaved pipeline changes',
      message: isDescriptions
        ? 'Retry source descriptions and revalidate your draft? All edits and raw JSON buffers will be retained, even if the retry fails.'
        : isRefreshOrReload
          ? 'Save first or reload without saving. Unsaved edits are replaced only after pipelines and descriptions reload successfully; cancellation or failure retains your draft.'
          : 'Save before continuing, discard these edits, or stay here. Saving validates every registration.',
      acceptLabel: isDescriptions ? 'Retry' : 'Save',
      rejectLabel: 'Discard',
      acceptVisible: true,
      rejectVisible: !isDescriptions,
      closeOnEscape: true,
      dismissableMask: true,
      acceptButtonProps: { severity: 'primary', outlined: true, size: 'small' },
      rejectButtonProps: { severity: 'secondary', text: true, size: 'small' },
      accept: () => {
        if (isDescriptions) void this.retryDescriptions()
        else void this.choose('save')
      },
      reject: (type: ConfirmEventType) => {
        if (type === ConfirmEventType.REJECT && !isDescriptions) void this.choose('discard')
        else this.pending.set(null)
      },
    })
  }

  async choose(choice: UnsavedChoice): Promise<void> {
    const destination = this.pending()
    if (!destination || this.locked()) return
    if (choice === 'stay') { this.pending.set(null); return }
    if (!await resolveUnsavedChoice(choice, () => this.save()) || this.destroyed) return
    this.pending.set(null)
    await this.proceed(destination)
  }

  private async proceed(destination: Destination): Promise<void> {
    if (destination.kind === 'close') this.close.emit()
    else if (destination.kind === 'pipeline') this.openPipeline(destination.id)
    else if (destination.kind === 'descriptions') await this.load(true)
    else if (destination.kind === 'reload') await this.load()
    else await this.refresh()
  }

  async retryDescriptions(): Promise<void> {
    if (this.pending()?.kind !== 'descriptions' || this.locked()) return
    this.pending.set(null)
    await this.load(true)
  }

  private openPipeline(id: string | null): void {
    const entry = this.entries().find(entry => entry.id === id)
    if (id !== null && !entry) return
    const draft = createPipelineDraft(id, entry?.pipeline)
    this.draft.set(draft)
    this.selectedKey.set(draft.registrations[0]?.key ?? null)
    this.pipelineTab.set('pipeline')
    this.mobileView.set('pipeline')
    this.confirmingDelete.set(false)
    this.error.set('')
  }

  setPipelineTab(value: string | number | undefined): void {
    if (value === 'pipelines' || value === 'pipeline' || value === 'registration') {
      this.pipelineTab.set(value)
      this.mobileView.set(value === 'pipelines' ? 'list' : value)
    }
  }

  navigate(view: 'list' | 'pipeline' | 'registration', key = this.selectedKey()): void {
    if (this.editingLocked()) return
    this.selectedKey.set(key)
    this.mobileView.set(view)
    if (view === 'list') this.pipelineTab.set('pipelines')
    else if (view === 'pipeline') this.pipelineTab.set('pipeline')
    else this.pipelineTab.set('registration')
  }

  setPattern(field: 'releasePattern' | 'visibilityPattern', value: string | null): void {
    if (!this.editingLocked()) this.draft.update(draft => draft ? { ...draft, [field]: value } : draft)
  }

  editRegistration(key: number, field: 'type' | 'resourceLocator' | 'infoUrl', value: string | null): void {
    if (this.editingLocked()) return
    this.draft.update(draft => draft ? updateRegistration(draft, key, { [field]: value }) : draft)
  }

  editText(key: number, rawText: string): void {
    if (!this.editingLocked()) this.draft.update(draft => draft ? editRegistrationText(draft, key, rawText) : draft)
  }

  addStage(): void {
    const draft = this.draft()
    if (!draft || this.editingLocked()) return
    this.draft.set(addRegistration(draft))
    this.navigate('registration', draft.nextKey)
  }

  removeStage(key: number): void {
    const draft = this.draft()
    if (!draft || this.editingLocked()) return
    if (draft.registrations.some(registration => registration.key === key)) this.removingKey.set(key)
  }

  confirmRemoveStage(): void {
    const draft = this.draft()
    const key = this.removingKey()
    if (!draft || key === null || this.locked() || !this.administrator()) return
    const next = removeRegistration(draft, key)
    this.draft.set(next)
    this.removingKey.set(null)
    if (this.selectedKey() === key) this.navigate('pipeline', next.registrations[0]?.key ?? null)
  }

  moveStage(key: number, direction: -1 | 1): void {
    if (!this.editingLocked()) this.draft.update(draft => draft ? moveRegistration(draft, key, direction) : draft)
  }

  async save(): Promise<boolean> {
    const draft = this.draft()
    if (!draft || this.locked() || !this.loaded() || this.confirmingDelete() || this.removingKey() !== null || !this.administrator()) return false
    const prepared = preparePipeline(draft, this.descriptions())
    if (!prepared.valid) { this.error.set('Saving is blocked. Resolve every registration error below.'); return false }
    this.busy.set(true)
    this.error.set('')
    try {
      const payload = prepared.payload
      let id = draft.id
      if (id !== null) await this.api.updatePipeline(id, payload, this.lifetime.signal)
      else id = await this.api.createPipeline(payload, this.lifetime.signal)
      if (this.destroyed) return false
      if (!id) throw new Error('The server did not return a pipeline ID. Reload before retrying creation.')
      this.entries.update(entries => draft.id === null ? [...entries, { id, pipeline: payload }] : entries.map(entry => entry.id === id ? { id, pipeline: payload } : entry))
      this.draft.set(acceptPipelineSave(draft, id, payload))
      this.messageService.add({ key: 'status', severity: 'success', summary: 'Pipeline saved', detail: 'Refresh the database to apply pipeline changes to catalogs.', life: 5000 })
      return true
    } catch (error) {
      if (!this.destroyed) this.showError('save the pipeline; your draft has been retained', error)
      return false
    } finally { if (!this.destroyed) this.busy.set(false) }
  }

  requestDelete(): void {
    if (!this.draft()?.id || this.locked() || !this.administrator()) return
    this.confirmingDelete.set(true)
    this.messageService.clear('deleteConfirm')
    this.messageService.add({
      key: 'deleteConfirm',
      summary: 'Delete this pipeline?',
      detail: 'This removes the saved pipeline and discards its local edits. Refresh the database afterward to apply the deletion.',
      sticky: true,
      closable: false,
    })
  }

  cancelDelete(): void {
    this.messageService.clear('deleteConfirm')
    this.confirmingDelete.set(false)
  }

  async remove(): Promise<void> {
    const id = this.draft()?.id
    if (!id || !this.confirmingDelete() || this.locked() || !this.administrator()) return
    this.messageService.clear('deleteConfirm')
    this.busy.set(true)
    this.error.set('')
    try {
      await this.api.deletePipeline(id, this.lifetime.signal)
      if (this.destroyed) return
      this.entries.update(entries => entries.filter(entry => entry.id !== id))
      this.draft.set(null)
      this.selectedKey.set(null)
      this.pipelineTab.set('pipelines')
      this.mobileView.set('list')
      this.confirmingDelete.set(false)
      this.messageService.add({ key: 'status', severity: 'success', summary: 'Pipeline deleted', detail: 'Refresh the database to apply pipeline changes to catalogs.', life: 5000 })
    } catch (error) {
      if (!this.destroyed) this.showError('delete the pipeline; your draft has been retained', error)
    } finally { if (!this.destroyed) this.busy.set(false) }
  }

  private async refresh(): Promise<void> {
    if (this.locked() || !this.administrator()) return
    this.refreshing.set(true)
    this.error.set('')
    try {
      // The parent owns its metadata guard, refresh job, and shared cache invalidation.
      const refreshed = await this.refreshDatabase()()
      if (this.destroyed) return
      if (!refreshed) { this.messageService.add({ key: 'status', severity: 'info', summary: 'Refresh canceled', detail: 'Your pipeline draft has been retained.', life: 5000 }); return }
      if (await this.load()) this.messageService.add({ key: 'status', severity: 'success', summary: 'Database refreshed', detail: 'Pipelines and source schemas reloaded, including configuration upgrades.', life: 5000 })
      else if (!this.destroyed) this.messageService.add({ key: 'status', severity: 'warn', summary: 'Database refreshed', detail: 'Reloading was incomplete. Clean drafts follow loaded server data; unsaved edits are retained. Review the warnings before saving.', life: 8000 })
    } catch (error) {
      if (!this.destroyed) this.showError('refresh the database; your pipeline draft has been retained', error)
    } finally { if (!this.destroyed) this.refreshing.set(false) }
  }

  private showError(action: string, error: unknown): void {
    this.error.set(`Could not ${action}. ${error instanceof Error ? error.message : String(error)}`)
  }
}

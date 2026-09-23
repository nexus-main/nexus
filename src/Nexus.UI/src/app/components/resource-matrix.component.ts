import { DOCUMENT } from '@angular/common'
import { CdkVirtualScrollViewport, ScrollingModule } from '@angular/cdk/scrolling'
import { Component, DestroyRef, ElementRef, afterRenderEffect, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { LucideChartNoAxesCombined, LucideChevronDown, LucideChevronUp, LucideDownload, LucidePencil, LucideSlidersHorizontal, LucideTriangleAlert, LucideX } from '@lucide/angular'
import { ButtonModule } from 'primeng/button'
import { CheckboxModule } from 'primeng/checkbox'
import { DialogModule } from 'primeng/dialog'
import { InputTextModule } from 'primeng/inputtext'
import { TextareaModule } from 'primeng/textarea'
import { TooltipModule } from 'primeng/tooltip'
import { groupResourceRows } from '../resource-matrix'
import type { MetadataDrafts, MetadataField } from '../resource-matrix'
import { formatPeriod, resourceAvailableForRange } from '../resource-selection'
import type { RepresentationRow } from '../resource-selection'

@Component({
  selector: 'app-resource-matrix',
  standalone: true,
  imports: [ScrollingModule, FormsModule, ButtonModule, CheckboxModule, DialogModule, InputTextModule, TextareaModule, TooltipModule,
    LucideChartNoAxesCombined, LucideChevronDown, LucideChevronUp, LucideDownload, LucidePencil, LucideSlidersHorizontal, LucideTriangleAlert, LucideX],
  templateUrl: './resource-matrix.component.html',
  styleUrl: './resource-matrix.component.css',
  host: { '[class.narrow]': 'narrow()' },
})
export class ResourceMatrixComponent {
  private static nextId = 0
  readonly groupStripId = `resource-matrix-groups-${ResourceMatrixComponent.nextId++}`
  readonly catalogId = input.required<string>()
  readonly rows = input.required<RepresentationRow[]>()
  readonly selectedKeys = input.required<ReadonlySet<string>>()
  readonly activeKey = input('')
  readonly revealKey = input('')
  readonly revealSequence = input(0)
  readonly writable = input(false)
  readonly loading = input(false)
  readonly selectionLoading = input(false)
  readonly visualizationPanelVisible = input(false)
  readonly visualizationSize = input('')
  readonly visualizationDisabledReason = input('')
  readonly exportDisabledReason = input('')
  readonly catalogProperties = input<Record<string, unknown> | null | undefined>(null)
  readonly selectedBegin = input('')
  readonly selectedEnd = input('')
  readonly saveMetadata = input.required<(catalogId: string, drafts: MetadataDrafts) => Promise<{ warning?: string }>>()
  readonly toggle = output<RepresentationRow>()
  readonly activate = output<RepresentationRow>()
  readonly visualize = output<void>()
  readonly exportRequested = output<void>()

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef)
  private readonly document = inject(DOCUMENT)
  private readonly destroyRef = inject(DestroyRef)
  private readonly viewport = viewChild(CdkVirtualScrollViewport)
  private readonly groupStrip = viewChild<ElementRef<HTMLElement>>('groupStrip')
  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput')
  private readonly rememberedGroups = new Map<string, string>()
  private readonly sourceSnapshot = signal<RepresentationRow[] | null>(null)
  private readonly scrollRequest = signal<{ key: string } | null>(null)
  private pendingNavigation: (() => void) | null = null
  private dialogOpener: HTMLElement | null = null
  private dialogOpenerKey: string | null = null
  private navigationOpener: HTMLElement | null = null
  private dialogDraft: MetadataDrafts[string] | undefined
  private frame: number | null = null
  private requestedIndex: number | null = null
  private previousItemSize = 48

  readonly search = signal('')
  readonly expanded = signal(false)
  readonly showAll = signal(false)
  readonly canScrollGroupsBack = signal(false)
  readonly canScrollGroupsForward = signal(false)
  readonly groupKey = signal('')
  readonly narrow = signal(false)
  readonly editing = signal(false)
  readonly itemSize = computed(() => this.editing() && !this.narrow() ? 56 : 48)
  readonly saving = signal(false)
  readonly drafts = signal<MetadataDrafts>({})
  readonly dirtyCount = computed(() => Object.keys(this.drafts()).length)
  readonly error = signal('')
  readonly status = signal('')
  readonly navigationVisible = signal(false)
  readonly detail = signal<RepresentationRow | null>(null)
  readonly detailVisible = signal(false)
  readonly canEdit = computed(() => this.editing() && this.writable() && !this.saving())
  readonly fields: MetadataField[] = ['unit', 'description', 'warning']
  readonly formatPeriod = formatPeriod
  // Search never sees draft text; an edit session also survives a parent metadata refresh.
  readonly sourceRows = computed(() => {
    const rows = (this.sourceSnapshot() ?? this.rows()).filter(row => row.catalogId === this.catalogId())
    if (this.showAll()) return rows
    const properties = this.catalogProperties()
    const begin = this.selectedBegin()
    const end = this.selectedEnd()
    return rows.filter(row => resourceAvailableForRange(row, properties, begin, end))
  })
  readonly hiddenCount = computed(() => {
    const rows = this.rows().filter(row => row.catalogId === this.catalogId())
    const properties = this.catalogProperties()
    const begin = this.selectedBegin()
    const end = this.selectedEnd()
    return rows.filter(row => !resourceAvailableForRange(row, properties, begin, end)).length
  })
  readonly groups = computed(() => groupResourceRows(this.sourceRows(), this.search()))
  readonly activeGroup = computed(() => this.groups().find(group => group.key === this.groupKey()) ?? this.groups().at(0))
  readonly visibleRows = computed(() => this.activeGroup()?.rows ?? [])
  readonly resultCount = computed(() => new Set(this.groups().flatMap(group => group.rows.map(row => row.key))).size)
  readonly trackRow = (_: number, row: RepresentationRow) => row.key

  constructor() {
    effect(() => {
      const catalog = this.catalogId()
      untracked(() => {
        this.saving.set(false)
        this.cancelEditing()
        this.search.set('')
        this.expanded.set(false)
        this.showAll.set(false)
        this.groupKey.set(this.rememberedGroups.get(catalog) ?? '')
        this.navigationVisible.set(false)
        this.pendingNavigation = null
        this.error.set('')
        this.status.set('')
        this.scrollRequest.set(null)
      })
    })

    effect(() => {
      const groups = this.groups()
      const key = this.groupKey()
      const catalog = this.catalogId()
      if (!groups.length) return
      const next = groups.some(group => group.key === key) ? key : groups[0].key
      this.rememberedGroups.set(catalog, next)
      if (next !== key) this.groupKey.set(next)
    })

    let revealed = ''
    let revealCatalog = ''
    effect(() => {
      const catalog = this.catalogId()
      const key = this.revealKey()
      const rows = this.sourceRows()
      const token = JSON.stringify([catalog, key, this.revealSequence()])
      if (catalog !== revealCatalog) { revealed = ''; revealCatalog = catalog }
      if (!key) { revealed = ''; return }
      if (token === revealed) return
      const row = rows.find(row => row.key === key)
      if (!row) return // Keep the request pending while the catalog is loading.
      untracked(() => {
        const groups = groupResourceRows(rows, '')
        const matches = groups.filter(group => group.rows.some(item => item.key === key))
        const group = matches.find(group => group.key === this.groupKey()) ?? matches[0]
        if (!group) return
        this.search.set('')
        this.groupKey.set(group.key)
        this.scrollRequest.set({ key })
        revealed = token
      })
    })

    let renderedCatalog: string | undefined
    let renderedSearch = ''
    let renderedGroup = ''
    afterRenderEffect(() => {
      const rows = this.visibleRows()
      const catalog = this.catalogId()
      const search = this.search()
      const group = this.groupKey()
      const request = this.scrollRequest()
      const viewport = this.viewport()
      if (!viewport) return
      if (!request && catalog === renderedCatalog && search === renderedSearch && group === renderedGroup) return
      renderedCatalog = catalog
      renderedSearch = search
      renderedGroup = group
      const index = request ? rows.findIndex(row => row.key === request.key) : 0
      this.resizeViewport(Math.max(0, index))
      if (request && index >= 0) untracked(() => this.scrollRequest.set(null))
    })

    afterRenderEffect(() => {
      this.groupKey()
      this.groups()
      this.expanded()
      const strip = this.groupStrip()?.nativeElement
      const selected = strip?.querySelector<HTMLElement>('[aria-pressed="true"]')
      if (!strip || !selected) { this.updateGroupScroll(); return }
      // Scroll only the strip, not its ancestors or the page.
      if (this.expanded()) {
        if (selected.offsetTop < strip.scrollTop) strip.scrollTop = selected.offsetTop
        else if (selected.offsetTop + selected.offsetHeight > strip.scrollTop + strip.clientHeight)
          strip.scrollTop = selected.offsetTop + selected.offsetHeight - strip.clientHeight
      } else {
        const left = selected.offsetLeft
        if (left < strip.scrollLeft) strip.scrollLeft = left
        else if (left + selected.offsetWidth > strip.scrollLeft + strip.clientWidth)
          strip.scrollLeft = left + selected.offsetWidth - strip.clientWidth
      }
      this.updateGroupScroll()
    })

    afterRenderEffect(onCleanup => {
      const strip = this.groupStrip()?.nativeElement
      if (!strip || typeof ResizeObserver === 'undefined') return
      const observer = new ResizeObserver(() => this.updateGroupScroll())
      observer.observe(strip)
      onCleanup(() => observer.disconnect())
    })

    afterRenderEffect(() => {
      const size = this.itemSize()
      const viewport = this.viewport()
      if (!viewport || size === this.previousItemSize) return
      const index = Math.floor(viewport.measureScrollOffset() / this.previousItemSize)
      this.previousItemSize = size
      this.resizeViewport(this.requestedIndex ?? index)
    })

    afterRenderEffect(onCleanup => {
      const viewport = this.viewport()
      if (!viewport || typeof ResizeObserver === 'undefined') return
      const observer = new ResizeObserver(() => {
        const index = Math.floor(viewport.measureScrollOffset() / this.previousItemSize)
        this.narrow.set(window.innerWidth < 640 || this.host.nativeElement.getBoundingClientRect().width < 560)
        this.resizeViewport(this.requestedIndex ?? index)
      })
      observer.observe(this.host.nativeElement)
      observer.observe(viewport.elementRef.nativeElement)
      onCleanup(() => observer.disconnect())
    })
    this.destroyRef.onDestroy(() => {
      if (this.frame !== null) cancelAnimationFrame(this.frame)
    })
  }

  private resizeViewport(index: number): void {
    this.requestedIndex = index
    if (this.frame !== null) cancelAnimationFrame(this.frame)
    this.frame = requestAnimationFrame(() => {
      this.frame = null
      const viewport = this.viewport()
      viewport?.checkViewportSize()
      viewport?.scrollToIndex(this.requestedIndex ?? 0)
      this.requestedIndex = null
    })
  }

  setSearch(value: string): void {
    if (!this.saving()) this.search.set(value)
  }

  selectGroup(key: string): void {
    if (!this.saving()) this.groupKey.set(key)
  }

  updateGroupScroll(): void {
    const strip = this.groupStrip()?.nativeElement
    if (!strip) {
      this.canScrollGroupsBack.set(false)
      this.canScrollGroupsForward.set(false)
      return
    }

    if (this.expanded()) {
      this.canScrollGroupsBack.set(strip.scrollTop > 1)
      this.canScrollGroupsForward.set(strip.scrollTop + strip.clientHeight < strip.scrollHeight - 1)
      return
    }

    this.canScrollGroupsBack.set(strip.scrollLeft > 1)
    this.canScrollGroupsForward.set(strip.scrollLeft + strip.clientWidth < strip.scrollWidth - 1)
  }

  scrollCollapsedGroups(event: WheelEvent): void {
    if (this.expanded() || this.saving()) return
    const strip = this.groupStrip()?.nativeElement
    if (!strip) return
    const delta = Math.abs(event.deltaX) > Math.abs(event.deltaY) ? event.deltaX : event.deltaY
    if (!delta) return
    event.preventDefault()
    strip.scrollBy({ left: delta })
  }

  selectionDisabled(row: RepresentationRow): boolean {
    return this.saving() || this.loading() || this.selectionLoading()
  }

  selectionLabel(row: RepresentationRow): string {
    const label = `${row.id}, ${formatPeriod(row.basePeriod)}`
    if (this.selectedKeys().has(row.key)) return `Deselect ${label}`
    if (this.requiresParameters(row)) return `Add ${label} with parameters`
    return `Select ${label}`
  }

  requiresParameters(row: RepresentationRow): boolean {
    return Object.keys(row.representation.parameters ?? {}).length > 0
  }

  toggleRow(row: RepresentationRow): void {
    if (!this.selectionDisabled(row)) this.toggle.emit(row)
  }

  activateRow(row: RepresentationRow, event: Event): void {
    if (this.saving()) return
    this.activate.emit(row)
    if (this.narrow()) this.openDetail(row, event)
  }

  value(row: RepresentationRow, field: MetadataField): string {
    return this.resourceDraft(row.id)?.[field] ?? this.sourceValue(row, field)
  }

  private resourceDraft(id: string): MetadataDrafts[string] | undefined {
    const drafts = this.drafts()
    return Object.hasOwn(drafts, id) ? drafts[id] : undefined
  }

  private sourceValue(row: RepresentationRow, field: MetadataField): string {
    return row[field] ?? ''
  }

  setField(row: RepresentationRow, field: MetadataField, value: string): void {
    if (!this.canEdit()) return
    let drafts = { ...this.drafts() }
    const draft = { ...this.resourceDraft(row.id), [field]: value }
    if (value === this.sourceValue(row, field)) delete draft[field]
    if (Object.keys(draft).length) drafts = { ...drafts, [row.id]: draft }
    else delete drafts[row.id]
    this.drafts.set(drafts)
    this.error.set('')
  }

  startEditing(): void {
    if (!this.writable() || this.saving() || this.loading()) return
    this.sourceSnapshot.set(this.rows().map(row => ({ ...row, groups: [...row.groups] })))
    this.editing.set(true)
    this.error.set('')
    this.status.set('')
  }

  cancelEditing(): void {
    if (this.saving()) return
    this.detailVisible.set(false)
    this.editing.set(false)
    this.drafts.set({})
    this.sourceSnapshot.set(null)
    this.error.set('')
  }

  openDetail(row: RepresentationRow, event: Event): void {
    if (this.saving()) return
    this.dialogOpener = event.currentTarget as HTMLElement
    this.dialogOpenerKey = row.key
    const draft = this.resourceDraft(row.id)
    this.dialogDraft = draft ? { ...draft } : undefined
    this.detail.set(row)
    this.detailVisible.set(true)
  }

  closeDetail(): void {
    if (this.saving()) return
    const row = this.detail()
    if (row && this.editing()) {
      let drafts = { ...this.drafts() }
      if (this.dialogDraft) drafts = { ...drafts, [row.id]: this.dialogDraft }
      else delete drafts[row.id]
      this.drafts.set(drafts)
    }
    this.detailVisible.set(false)
  }

  keepDetailDraft(): void {
    if (!this.saving()) this.detailVisible.set(false)
  }

  restoreDetailFocus(): void {
    // CDK can reuse the very same DOM button for a different representation.
    const key = this.dialogOpener?.closest<HTMLElement>('[data-row-key]')?.dataset['rowKey']
    this.restoreFocus(key === this.dialogOpenerKey ? this.dialogOpener : null)
    this.dialogOpener = null
  }

  private restoreFocus(opener: HTMLElement | null): void {
    if (this.destroyRef.destroyed) return
    const target = opener?.isConnected && !opener.matches(':disabled') ? opener : this.searchInput()?.nativeElement
    target?.focus({ preventScroll: true })
  }

  hasUnsavedChanges(): boolean {
    return this.dirtyCount() > 0
  }

  requestNavigation(action: () => void): void {
    if (this.saving() || this.navigationVisible()) return
    if (!this.hasUnsavedChanges()) {
      this.cancelEditing()
      action()
      return
    }
    this.navigationOpener = this.document.activeElement instanceof HTMLElement ? this.document.activeElement : null
    this.pendingNavigation = action
    this.navigationVisible.set(true)
  }

  stay(): void {
    if (this.saving()) return
    this.pendingNavigation = null
    this.navigationVisible.set(false)
  }

  restoreNavigationFocus(): void {
    this.restoreFocus(this.navigationOpener)
    this.navigationOpener = null
  }

  discardAndNavigate(): void {
    if (this.saving()) return
    const action = this.pendingNavigation
    this.pendingNavigation = null
    this.navigationVisible.set(false)
    this.cancelEditing()
    action?.()
  }

  async saveChanges(): Promise<void> {
    if (!this.writable() || this.saving() || !this.hasUnsavedChanges()) return
    const catalog = this.catalogId()
    const drafts = Object.fromEntries(Object.entries(this.drafts()).map(([id, fields]) => [id, { ...fields }]))
    this.saving.set(true)
    this.error.set('')
    this.status.set('')
    let action: (() => void) | null = null
    try {
      const result = await this.saveMetadata()(catalog, drafts)
      if (this.destroyRef.destroyed || this.catalogId() !== catalog) return
      this.saving.set(false)
      this.cancelEditing()
      this.status.set(result.warning ? `Changes saved. ${result.warning}` : 'Metadata changes saved.')
      action = this.pendingNavigation
      this.pendingNavigation = null
      this.navigationVisible.set(false)
    } catch (error) {
      if (!this.destroyRef.destroyed && this.catalogId() === catalog)
        this.error.set(error instanceof Error ? error.message : 'Could not save metadata. Your drafts are retained; try again.')
    } finally {
      if (!this.destroyRef.destroyed && this.catalogId() === catalog) this.saving.set(false)
    }
    action?.()
  }
}

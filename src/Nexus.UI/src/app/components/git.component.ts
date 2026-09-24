import { CommonModule } from '@angular/common'
import { Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { DiffEditorComponent, type DiffEditorModel } from 'ngx-monaco-editor-v2'
import type * as Monaco from 'monaco-editor'
import { ButtonModule } from 'primeng/button'
import { DialogModule } from 'primeng/dialog'
import { MessageModule } from 'primeng/message'
import { SelectModule } from 'primeng/select'
import { TabsModule } from 'primeng/tabs'
import { AppTooltipDirective } from '../app-tooltip.directive'
import { NexusService } from '../nexus.service'
import { RestoreFocusDirective } from '../restore-focus.directive'
import { defineNexusMonacoThemes, getNexusMonacoTheme, type ThemeMode } from '../services/nexus-monaco-themes'

type GitPushStatus = 0 | 1 | 2 | 'NotConfigured' | 'Succeeded' | 'Failed'

type GitConfigResponse = {
  branch: string
  commitThrottleSeconds: number
  remoteUrl?: string | null
  username?: string | null
  hasToken: boolean
  hasSshPrivateKey: boolean
  authMode: string
  commitAuthorName: string
  commitAuthorEmail: string
  isRemoteConfigured: boolean
}

type GitStatusResponse = {
  gitAvailable: boolean
  sshAvailable: boolean
  hasUncommittedChanges: boolean
  currentCommitSha?: string | null
  lastPushedCommitSha?: string | null
  lastSuccessfulPushAt?: string | null
  lastPushStatus: GitPushStatus
  lastPushError?: string | null
}

type GitHistoryEntry = {
  sha: string
  shortSha: string
  date: string
  authorName: string
  authorEmail: string
  message: string
}

type GitDiffFile = {
  path: string
  status: string
  originalText?: string | null
  modifiedText?: string | null
}

@Component({
  selector: 'app-git',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, DialogModule, MessageModule, SelectModule, TabsModule, DiffEditorComponent, AppTooltipDirective, RestoreFocusDirective],
  styleUrl: './git.component.css',
  template: `
    <p-dialog appRestoreFocus header="Administrator / Git" [visible]="true" (visibleChange)="!$event && close.emit()" [modal]="true" [blockScroll]="true" [dismissableMask]="false" [closeOnEscape]="true" [draggable]="false" [resizable]="false" appendTo="body" styleClass="git-dialog" [closeButtonProps]="{ ariaLabel: 'Close Git', severity: 'secondary', text: true, rounded: true }" [style]="{ width: 'calc(100vw - 2rem)' }" [contentStyle]="{ display: 'flex', flexDirection: 'column', minHeight: '0', height: '100%', overflow: 'hidden' }">
      <div class="git-panel" [attr.aria-busy]="loading() || busy()">
      <div class="git-workspace hidden min-h-0 flex-1 gap-4 md:grid md:grid-cols-[22rem_minmax(0,1fr)]">
        <p-tabs value="commits" class="git-desktop-tabs flex min-h-0 flex-1 flex-col" [selectOnFocus]="true" [showNavigators]="false">
          <div class="shrink-0 pb-3">
            <p-tablist>
              <p-tab value="sync" class="min-w-0 flex-1 whitespace-normal text-center">Sync</p-tab>
              <p-tab value="commits" class="min-w-0 flex-1 whitespace-normal text-center">Commits</p-tab>
            </p-tablist>
          </div>
          <p-tabpanels class="flex min-h-0 flex-1 flex-col overflow-hidden p-0">
            <p-tabpanel value="sync" class="min-h-0 overflow-auto">
              <div class="flex min-h-0 flex-col gap-4 pr-1">
          @if (error()) { <p-message severity="error">{{ error() }}</p-message> }
          @if (statusMessage()) { <p-message severity="success">{{ statusMessage() }}</p-message> }

          <section class="rounded-lg border border-[var(--p-content-border-color)] bg-overlay/[0.03] p-3">
            <div class="mb-3 flex items-center justify-between gap-2">
              <h2 class="text-sm font-semibold">Status</h2>
              <button pButton type="button" size="small" severity="secondary" [text]="true" [disabled]="loading()" (click)="refresh()">Refresh</button>
            </div>
            @if (status(); as current) {
              <dl class="space-y-2 text-xs">
                <div class="grid grid-cols-[8rem_minmax(0,1fr)] gap-2"><dt class="text-[var(--p-text-muted-color)]">{{ toolAvailabilityLabel() }}</dt><dd class="min-w-0 truncate">{{ toolsAvailable() ? 'available' : 'not available' }}</dd></div>
                <div class="grid grid-cols-[8rem_minmax(0,1fr)] gap-2"><dt class="text-[var(--p-text-muted-color)]">Changes</dt><dd class="min-w-0 truncate">{{ current.hasUncommittedChanges ? 'pending' : 'clean' }}</dd></div>
                <div class="grid grid-cols-[8rem_minmax(0,1fr)] gap-2"><dt class="text-[var(--p-text-muted-color)]">Current commit</dt><dd class="min-w-0 truncate font-mono" [pTooltip]="current.currentCommitSha || undefined" [tooltipDisabled]="!current.currentCommitSha">{{ shortSha(current.currentCommitSha) }}</dd></div>
                <div class="grid grid-cols-[8rem_minmax(0,1fr)] gap-2"><dt class="text-[var(--p-text-muted-color)]">Last pushed</dt><dd class="min-w-0 truncate font-mono" [pTooltip]="current.lastPushedCommitSha || undefined" [tooltipDisabled]="!current.lastPushedCommitSha">{{ shortSha(current.lastPushedCommitSha) }}</dd></div>
                <div class="grid grid-cols-[8rem_minmax(0,1fr)] gap-2"><dt class="text-[var(--p-text-muted-color)]">Last push</dt><dd class="min-w-0 truncate">{{ pushStatusLabel(current.lastPushStatus) }}</dd></div>
                <div class="grid grid-cols-[8rem_minmax(0,1fr)] gap-2"><dt class="text-[var(--p-text-muted-color)]">Last success</dt><dd class="min-w-0 truncate">{{ formatDate(current.lastSuccessfulPushAt) }}</dd></div>
                @if (current.lastPushError) { <div><dt class="text-rose-accent">Last error</dt><dd class="break-words text-rose-accent">{{ current.lastPushError }}</dd></div> }
              </dl>
            } @else {
              <p class="text-sm text-[var(--p-text-muted-color)]">Loading Git status...</p>
            }
          </section>

          <section class="rounded-lg border border-[var(--p-content-border-color)] bg-overlay/[0.03] p-3">
            <h2 class="mb-3 text-sm font-semibold">Configured Git</h2>
            @if (config(); as effective) {
              <dl class="space-y-2 text-xs">
                <div><dt class="text-[var(--p-text-muted-color)]">Remote URL</dt><dd class="break-all font-mono">{{ effective.remoteUrl || 'not configured' }}</dd></div>
                <div class="grid grid-cols-2 gap-2"><dt class="text-[var(--p-text-muted-color)]">Remote ready</dt><dd>{{ effective.isRemoteConfigured ? 'yes' : 'no' }}</dd></div>
                <div class="grid grid-cols-2 gap-2"><dt class="text-[var(--p-text-muted-color)]">Auth</dt><dd>{{ effective.authMode }}</dd></div>
                @if (effective.authMode === 'HttpsToken') { <div class="grid grid-cols-2 gap-2"><dt class="text-[var(--p-text-muted-color)]">HTTPS username</dt><dd>{{ effective.username || 'x-access-token' }}</dd></div> }
                <div class="grid grid-cols-2 gap-2"><dt class="text-[var(--p-text-muted-color)]">Token</dt><dd>{{ effective.hasToken ? 'configured' : 'not configured' }}</dd></div>
                <div class="grid grid-cols-2 gap-2"><dt class="text-[var(--p-text-muted-color)]">SSH key</dt><dd>{{ effective.hasSshPrivateKey ? 'configured' : 'not configured' }}</dd></div>
                <div class="grid grid-cols-2 gap-2"><dt class="text-[var(--p-text-muted-color)]">Throttle</dt><dd>{{ effective.commitThrottleSeconds }} s</dd></div>
                <div><dt class="text-[var(--p-text-muted-color)]">Author</dt><dd class="font-mono">{{ effective.commitAuthorName }} &lt;{{ effective.commitAuthorEmail }}&gt;</dd></div>
              </dl>
            }
          </section>

          <section class="rounded-lg border border-[var(--p-content-border-color)] bg-overlay/[0.03] p-3">
            <h2 class="mb-3 text-sm font-semibold">Actions</h2>
            <div class="flex flex-wrap gap-2">
              <button pButton type="button" size="small" [outlined]="true" [disabled]="syncDisabled()" (click)="sync(false)">Sync now</button>
              <button pButton type="button" size="small" severity="danger" [outlined]="true" [disabled]="syncDisabled()" (click)="confirmForce.set(true)">Force push once</button>
            </div>
            @if (!toolsAvailable()) { <p class="mt-3 text-xs text-[var(--p-text-muted-color)]">{{ toolAvailabilityLabel() }} is not available on this server.</p> }
            @else if (!remoteConfigured()) { <p class="mt-3 text-xs text-[var(--p-text-muted-color)]">Remote Git settings are not configured.</p> }
            @if (confirmForce()) {
              <div class="mt-3 rounded-md border border-rose-core/25 bg-rose-core/10 p-3 text-xs text-rose-accent">
                <p>Force push overwrites the configured remote branch with the local Nexus configuration history.</p>
                <div class="mt-3 flex justify-end gap-2">
                  <button pButton type="button" size="small" severity="secondary" [text]="true" (click)="confirmForce.set(false)">Cancel</button>
                  <button pButton type="button" size="small" severity="danger" [disabled]="syncDisabled()" (click)="sync(true)">Force push once</button>
                </div>
              </div>
            }
          </section>
              </div>
            </p-tabpanel>

            <p-tabpanel value="commits" class="min-h-0 overflow-auto">
              <section class="git-history-panel min-h-0 overflow-hidden rounded-lg border border-[var(--p-content-border-color)] bg-overlay/[0.03]">
                <div class="flex items-center justify-between gap-2 border-b border-[var(--p-content-border-color)] p-3">
                  <h2 class="text-sm font-semibold">Commits</h2>
                  <span class="text-xs text-[var(--p-text-muted-color)]">{{ history().length }} commits</span>
                </div>
                <div class="min-h-0 overflow-auto">
                @for (entry of history(); track entry.sha) {
                  <button type="button" class="block w-full border-b border-[var(--p-content-border-color)] p-3 text-left text-sm transition-colors hover:bg-overlay/[0.04]" [ngClass]="selectedCommit()?.sha === entry.sha ? 'bg-cyan-core/10 text-cyan-accent' : ''" (click)="selectCommit(entry)">
                    <div class="truncate font-medium">{{ entry.message }}</div>
                    <div class="mt-1 flex items-center justify-between gap-2 text-xs text-[var(--p-text-muted-color)]"><span class="font-mono">{{ entry.shortSha }}</span><span>{{ formatDate(entry.date) }}</span></div>
                  </button>
                } @empty {
                  <p class="p-4 text-sm text-[var(--p-text-muted-color)]">No configuration commits yet.</p>
                }
                </div>
              </section>
            </p-tabpanel>
          </p-tabpanels>
        </p-tabs>

        <section class="git-commit-panel flex min-h-0 flex-col overflow-hidden rounded-lg border border-[var(--p-content-border-color)] bg-overlay/[0.03]">
                @if (selectedCommit(); as selected) {
                  <div class="flex flex-wrap items-center justify-between gap-2 border-b border-[var(--p-content-border-color)] p-3">
                    <div class="min-w-0">
                      <div class="truncate text-sm font-semibold">{{ selected.message }}</div>
                      <div class="mt-1 truncate font-mono text-xs text-[var(--p-text-muted-color)]">{{ selected.sha }}</div>
                    </div>
                    <div class="flex gap-2">
                      <p-select class="w-52" appendTo="body" size="small" optionLabel="path" [options]="diffFiles()" [ngModel]="selectedFile()" (ngModelChange)="selectedFile.set($event)" placeholder="Changed file" />
                      <button pButton type="button" size="small" severity="danger" [outlined]="true" [disabled]="restoreDisabled()" (click)="confirmRestore.set(true)">Restore this version</button>
                    </div>
                  </div>
                  @if (confirmRestore()) {
                    <div class="border-b border-rose-core/20 bg-rose-core/10 p-3 text-xs text-rose-accent">
                      <p>Restore overwrites the current configuration folder contents with this version and creates a new Git commit.</p>
                      <div class="mt-3 flex justify-end gap-2">
                        <button pButton type="button" size="small" severity="secondary" [text]="true" (click)="confirmRestore.set(false)">Cancel</button>
                        <button pButton type="button" size="small" severity="danger" [disabled]="restoreDisabled()" (click)="restore(selected.sha)">Restore</button>
                      </div>
                    </div>
                  }
                  <div class="git-editor-host min-h-0 flex-1">
                    @if (selectedFile(); as file) {
                      <ngx-monaco-diff-editor class="git-editor" [options]="diffEditorOptions()" [originalModel]="originalModel()" [modifiedModel]="modifiedModel()" (onInit)="onEditorInit($event)" />
                    } @else {
                      <div class="git-empty-state grid h-full place-items-center p-6 text-sm text-[var(--p-text-muted-color)]">Select a changed file.</div>
                    }
                  </div>
                } @else {
                   <div class="git-empty-state grid h-full place-items-center p-6 text-sm text-[var(--p-text-muted-color)]">Select a commit to inspect changes.</div>
                }
        </section>
      </div>

      <p-tabs [value]="mobileTab()" (valueChange)="setMobileTab($event)" [lazy]="true" class="git-mobile-tabs" [selectOnFocus]="true" [showNavigators]="false">
        <div class="git-mobile-tab-bar">
          <p-tablist>
            <p-tab value="sync" class="min-w-0 flex-1 whitespace-normal text-center">Sync</p-tab>
            <p-tab value="commits" class="min-w-0 flex-1 whitespace-normal text-center">Commits</p-tab>
            <p-tab value="details" class="min-w-0 flex-1 whitespace-normal text-center">Details</p-tab>
          </p-tablist>
        </div>
        <p-tabpanels class="git-mobile-tab-panels">
          <p-tabpanel value="sync" class="git-mobile-panel-scroll">
            <div class="space-y-4 pr-1">
              @if (error()) { <p-message severity="error">{{ error() }}</p-message> }
              @if (statusMessage()) { <p-message severity="success">{{ statusMessage() }}</p-message> }

              <section class="rounded-lg border border-[var(--p-content-border-color)] bg-overlay/[0.03] p-3">
                <div class="mb-3 flex items-center justify-between gap-2">
                  <h2 class="text-sm font-semibold">Status</h2>
                  <button pButton type="button" size="small" severity="secondary" [text]="true" [disabled]="loading()" (click)="refresh()">Refresh</button>
                </div>
                @if (status(); as current) {
                  <dl class="space-y-2 text-xs">
                    <div class="grid grid-cols-[8rem_minmax(0,1fr)] gap-2"><dt class="text-[var(--p-text-muted-color)]">{{ toolAvailabilityLabel() }}</dt><dd class="min-w-0 truncate">{{ toolsAvailable() ? 'available' : 'not available' }}</dd></div>
                    <div class="grid grid-cols-[8rem_minmax(0,1fr)] gap-2"><dt class="text-[var(--p-text-muted-color)]">Changes</dt><dd class="min-w-0 truncate">{{ current.hasUncommittedChanges ? 'pending' : 'clean' }}</dd></div>
                    <div class="grid grid-cols-[8rem_minmax(0,1fr)] gap-2"><dt class="text-[var(--p-text-muted-color)]">Current commit</dt><dd class="min-w-0 truncate font-mono" [pTooltip]="current.currentCommitSha || undefined" [tooltipDisabled]="!current.currentCommitSha">{{ shortSha(current.currentCommitSha) }}</dd></div>
                    <div class="grid grid-cols-[8rem_minmax(0,1fr)] gap-2"><dt class="text-[var(--p-text-muted-color)]">Last pushed</dt><dd class="min-w-0 truncate font-mono" [pTooltip]="current.lastPushedCommitSha || undefined" [tooltipDisabled]="!current.lastPushedCommitSha">{{ shortSha(current.lastPushedCommitSha) }}</dd></div>
                    <div class="grid grid-cols-[8rem_minmax(0,1fr)] gap-2"><dt class="text-[var(--p-text-muted-color)]">Last push</dt><dd class="min-w-0 truncate">{{ pushStatusLabel(current.lastPushStatus) }}</dd></div>
                    <div class="grid grid-cols-[8rem_minmax(0,1fr)] gap-2"><dt class="text-[var(--p-text-muted-color)]">Last success</dt><dd class="min-w-0 truncate">{{ formatDate(current.lastSuccessfulPushAt) }}</dd></div>
                    @if (current.lastPushError) { <div><dt class="text-rose-accent">Last error</dt><dd class="break-words text-rose-accent">{{ current.lastPushError }}</dd></div> }
                  </dl>
                } @else {
                  <p class="text-sm text-[var(--p-text-muted-color)]">Loading Git status...</p>
                }
              </section>

              <section class="rounded-lg border border-[var(--p-content-border-color)] bg-overlay/[0.03] p-3">
                <h2 class="mb-3 text-sm font-semibold">Configured Git</h2>
                @if (config(); as effective) {
                  <dl class="space-y-2 text-xs">
                    <div><dt class="text-[var(--p-text-muted-color)]">Remote URL</dt><dd class="break-all font-mono">{{ effective.remoteUrl || 'not configured' }}</dd></div>
                    <div class="grid grid-cols-2 gap-2"><dt class="text-[var(--p-text-muted-color)]">Remote ready</dt><dd>{{ effective.isRemoteConfigured ? 'yes' : 'no' }}</dd></div>
                    <div class="grid grid-cols-2 gap-2"><dt class="text-[var(--p-text-muted-color)]">Auth</dt><dd>{{ effective.authMode }}</dd></div>
                    @if (effective.authMode === 'HttpsToken') { <div class="grid grid-cols-2 gap-2"><dt class="text-[var(--p-text-muted-color)]">HTTPS username</dt><dd>{{ effective.username || 'x-access-token' }}</dd></div> }
                    <div class="grid grid-cols-2 gap-2"><dt class="text-[var(--p-text-muted-color)]">Token</dt><dd>{{ effective.hasToken ? 'configured' : 'not configured' }}</dd></div>
                    <div class="grid grid-cols-2 gap-2"><dt class="text-[var(--p-text-muted-color)]">SSH key</dt><dd>{{ effective.hasSshPrivateKey ? 'configured' : 'not configured' }}</dd></div>
                    <div class="grid grid-cols-2 gap-2"><dt class="text-[var(--p-text-muted-color)]">Throttle</dt><dd>{{ effective.commitThrottleSeconds }} s</dd></div>
                    <div><dt class="text-[var(--p-text-muted-color)]">Author</dt><dd class="font-mono">{{ effective.commitAuthorName }} &lt;{{ effective.commitAuthorEmail }}&gt;</dd></div>
                  </dl>
                }
              </section>

              <section class="rounded-lg border border-[var(--p-content-border-color)] bg-overlay/[0.03] p-3">
                <h2 class="mb-3 text-sm font-semibold">Actions</h2>
                <div class="flex flex-wrap gap-2">
                  <button pButton type="button" size="small" [outlined]="true" [disabled]="syncDisabled()" (click)="sync(false)">Sync now</button>
                  <button pButton type="button" size="small" severity="danger" [outlined]="true" [disabled]="syncDisabled()" (click)="confirmForce.set(true)">Force push once</button>
                </div>
                @if (!toolsAvailable()) { <p class="mt-3 text-xs text-[var(--p-text-muted-color)]">{{ toolAvailabilityLabel() }} is not available on this server.</p> }
                @else if (!remoteConfigured()) { <p class="mt-3 text-xs text-[var(--p-text-muted-color)]">Remote Git settings are not configured.</p> }
                @if (confirmForce()) {
                  <div class="mt-3 rounded-md border border-rose-core/25 bg-rose-core/10 p-3 text-xs text-rose-accent">
                    <p>Force push overwrites the configured remote branch with the local Nexus configuration history.</p>
                    <div class="mt-3 flex justify-end gap-2">
                      <button pButton type="button" size="small" severity="secondary" [text]="true" (click)="confirmForce.set(false)">Cancel</button>
                      <button pButton type="button" size="small" severity="danger" [disabled]="syncDisabled()" (click)="sync(true)">Force push once</button>
                    </div>
                  </div>
                }
              </section>
            </div>
          </p-tabpanel>

          <p-tabpanel value="commits" class="git-mobile-panel-scroll">
            <div class="rounded-lg border border-[var(--p-content-border-color)] bg-overlay/[0.03]">
              <div class="flex items-center justify-between gap-2 border-b border-[var(--p-content-border-color)] p-3">
                <h2 class="text-sm font-semibold">Commits</h2>
                <span class="text-xs text-[var(--p-text-muted-color)]">{{ history().length }} commits</span>
              </div>
              @for (entry of history(); track entry.sha) {
                <button type="button" class="block w-full border-b border-[var(--p-content-border-color)] p-3 text-left text-sm transition-colors hover:bg-overlay/[0.04]" [ngClass]="selectedCommit()?.sha === entry.sha ? 'bg-cyan-core/10 text-cyan-accent' : ''" (click)="selectCommit(entry, true)">
                  <div class="truncate font-medium">{{ entry.message }}</div>
                  <div class="mt-1 flex items-center justify-between gap-2 text-xs text-[var(--p-text-muted-color)]"><span class="font-mono">{{ entry.shortSha }}</span><span>{{ formatDate(entry.date) }}</span></div>
                </button>
              } @empty {
                <p class="p-4 text-sm text-[var(--p-text-muted-color)]">No configuration commits yet.</p>
              }
            </div>
          </p-tabpanel>

          <p-tabpanel value="details" class="git-mobile-details">
            <div class="flex h-full min-h-0 flex-col overflow-hidden">
            @if (selectedCommit(); as selected) {
              <div class="flex flex-wrap items-center justify-between gap-2 border-b border-[var(--p-content-border-color)] p-3">
                <div class="min-w-0">
                  <div class="truncate text-sm font-semibold">{{ selected.message }}</div>
                  <div class="mt-1 truncate font-mono text-xs text-[var(--p-text-muted-color)]">{{ selected.sha }}</div>
                </div>
                <div class="flex flex-wrap gap-2">
                  <p-select class="w-52" appendTo="body" size="small" optionLabel="path" [options]="diffFiles()" [ngModel]="selectedFile()" (ngModelChange)="selectedFile.set($event)" placeholder="Changed file" />
                  <button pButton type="button" size="small" severity="danger" [outlined]="true" [disabled]="restoreDisabled()" (click)="confirmRestore.set(true)">Restore this version</button>
                </div>
              </div>
              @if (confirmRestore()) {
                <div class="border-b border-rose-core/20 bg-rose-core/10 p-3 text-xs text-rose-accent">
                  <p>Restore overwrites the current configuration folder contents with this version and creates a new Git commit.</p>
                  <div class="mt-3 flex justify-end gap-2">
                    <button pButton type="button" size="small" severity="secondary" [text]="true" (click)="confirmRestore.set(false)">Cancel</button>
                    <button pButton type="button" size="small" severity="danger" [disabled]="restoreDisabled()" (click)="restore(selected.sha)">Restore</button>
                  </div>
                </div>
              }
              <div class="git-editor-host">
                @if (selectedFile(); as file) {
                  <ngx-monaco-diff-editor class="git-editor" [options]="diffEditorOptions()" [originalModel]="originalModel()" [modifiedModel]="modifiedModel()" (onInit)="onEditorInit($event)" />
                } @else {
                  <div class="git-empty-state grid h-full place-items-center p-6 text-sm text-[var(--p-text-muted-color)]">Select a changed file.</div>
                }
              </div>
            } @else {
              <div class="git-empty-state grid h-full place-items-center p-6 text-sm text-[var(--p-text-muted-color)]">Select a commit to inspect changes.</div>
            }
            </div>
          </p-tabpanel>
        </p-tabpanels>
      </p-tabs>
      </div>
    </p-dialog>
  `,
})
export class GitComponent {
  private readonly nexus = inject(NexusService)
  private readonly destroyRef = inject(DestroyRef)
  private monaco: typeof Monaco | null = null
  readonly close = output<void>()
  readonly themeMode = input.required<ThemeMode>()
  readonly config = signal<GitConfigResponse | null>(null)
  readonly status = signal<GitStatusResponse | null>(null)
  readonly history = signal<GitHistoryEntry[]>([])
  readonly diffFiles = signal<GitDiffFile[]>([])
  readonly selectedCommit = signal<GitHistoryEntry | null>(null)
  readonly selectedFile = signal<GitDiffFile | null>(null)
  readonly mobileTab = signal('commits')
  readonly loading = signal(false)
  readonly busy = signal(false)
  readonly error = signal('')
  readonly statusMessage = signal('')
  readonly confirmForce = signal(false)
  readonly confirmRestore = signal(false)
  readonly renderSideBySide = signal(true)
  readonly diffEditorOptions = computed<Monaco.editor.IDiffEditorConstructionOptions>(() => ({ readOnly: true, renderSideBySide: this.renderSideBySide(), useInlineViewWhenSpaceIsLimited: false, minimap: { enabled: false }, automaticLayout: true, scrollBeyondLastLine: false }))
  readonly gitAvailable = computed(() => this.status()?.gitAvailable ?? false)
  readonly sshRequired = computed(() => this.config()?.authMode === 'SshPrivateKey')
  readonly toolsAvailable = computed(() => this.gitAvailable() && (!this.sshRequired() || (this.status()?.sshAvailable ?? false)))
  readonly remoteConfigured = computed(() => this.config()?.isRemoteConfigured ?? false)
  readonly syncDisabled = computed(() => this.busy() || !this.toolsAvailable() || !this.remoteConfigured())
  readonly restoreDisabled = computed(() => this.busy() || !this.gitAvailable() || !this.selectedCommit())
  readonly originalModel = computed<DiffEditorModel>(() => {
    const file = this.selectedFile()
    return { code: file?.originalText ?? '', language: file ? this.detectLanguage(file.path) : 'plaintext' }
  })
  readonly modifiedModel = computed<DiffEditorModel>(() => {
    const file = this.selectedFile()
    return { code: file?.modifiedText ?? '', language: file ? this.detectLanguage(file.path) : 'plaintext' }
  })

  constructor() {
    effect(() => this.applyTheme())

    const mq = window.matchMedia('(min-width: 1280px)')
    this.renderSideBySide.set(mq.matches)
    const onChange = (event: MediaQueryListEvent) => this.renderSideBySide.set(event.matches)
    mq.addEventListener('change', onChange)
    this.destroyRef.onDestroy(() => mq.removeEventListener('change', onChange))

    void this.refresh()
  }

  onEditorInit(_editor: Monaco.editor.IDiffEditor): void {
    this.monaco = (window as unknown as { monaco?: typeof Monaco }).monaco ?? null
    if (this.monaco) defineNexusMonacoThemes(this.monaco)
    this.applyTheme()
  }

  async refresh() {
    this.loading.set(true)
    this.error.set('')
    try {
      const [config, status, history] = await Promise.all([
        this.request<GitConfigResponse>('git/config'),
        this.request<GitStatusResponse>('git/status'),
        this.request<GitHistoryEntry[]>('git/history'),
      ])
      this.config.set(config)
      this.status.set(status)
      this.history.set(history)
      if (!this.selectedCommit() && history.length) await this.selectCommit(history[0])
    } catch (error) {
      this.showError('load Git state', error)
    } finally {
      this.loading.set(false)
    }
  }

  setMobileTab(value: string | number | undefined) {
    if (typeof value === 'string') this.mobileTab.set(value)
  }

  async selectCommit(entry: GitHistoryEntry, openDetails = false) {
    this.selectedCommit.set(entry)
    this.confirmRestore.set(false)
    this.error.set('')
    if (openDetails) this.mobileTab.set('details')
    try {
      const files = await this.request<GitDiffFile[]>(`git/diff/${encodeURIComponent(entry.sha)}`)
      this.diffFiles.set(files)
      this.selectedFile.set(files[0] ?? null)
    } catch (error) {
      this.showError('load commit diff', error)
    }
  }

  async sync(force: boolean) {
    this.busy.set(true)
    this.error.set('')
    this.statusMessage.set('')
    this.confirmForce.set(false)
    try {
      const job = await this.request<{ id: string }>('jobs/git/sync', { method: 'POST', body: JSON.stringify({ force }) })
      this.statusMessage.set(`Started Git sync job ${job.id}.`)
      await this.refresh()
    } catch (error) {
      this.showError('start Git sync', error)
    } finally {
      this.busy.set(false)
    }
  }

  async restore(commitSha: string) {
    this.busy.set(true)
    this.error.set('')
    this.statusMessage.set('')
    this.confirmRestore.set(false)
    try {
      const result = await this.request<{ message: string }>('git/restore', { method: 'POST', body: JSON.stringify({ commitSha }) })
      this.statusMessage.set(result.message)
      this.selectedCommit.set(null)
      this.selectedFile.set(null)
      await this.refresh()
    } catch (error) {
      this.showError('restore configuration', error)
    } finally {
      this.busy.set(false)
    }
  }

  formatDate(value?: string | null) {
    if (!value) return 'never'
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
  }

  shortSha(value?: string | null) {
    return value ? value.slice(0, 8) : 'none'
  }

  toolAvailabilityLabel() {
    return this.sshRequired() ? 'Git + SSH' : 'Git'
  }

  pushStatusLabel(value: GitPushStatus) {
    if (value === 0 || value === 'NotConfigured') return 'not configured'
    if (value === 1 || value === 'Succeeded') return 'succeeded'
    if (value === 2 || value === 'Failed') return 'failed'
    return String(value)
  }

  private applyTheme(): void {
    const monaco = this.monaco
    if (!monaco) return
    monaco.editor.setTheme(getNexusMonacoTheme(this.themeMode()))
  }

  private detectLanguage(path: string): string {
    const ext = path.slice(path.lastIndexOf('.') + 1).toLowerCase()
    switch (ext) {
      case 'json': return 'json'
      case 'yaml':
      case 'yml': return 'yaml'
      case 'md': return 'markdown'
      default: return 'plaintext'
    }
  }

  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const response = await fetch(`${this.nexus.endpoint}/api/v1/${path}`, {
      ...init,
      headers: {
        ...(init.body ? { 'content-type': 'application/json' } : {}),
        ...init.headers,
      },
    })

    if (!response.ok) throw new Error(await response.text() || response.statusText)
    return await response.json() as T
  }

  private showError(action: string, error: unknown) {
    this.error.set(`Unable to ${action}: ${error instanceof Error ? error.message : String(error)}`)
  }
}

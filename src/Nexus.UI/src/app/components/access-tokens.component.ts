import { CommonModule } from '@angular/common'
import { Component, OnInit, computed, inject, output, signal } from '@angular/core'
import { FormsModule } from '@angular/forms'
import { LucideCopy, LucideRefreshCw, LucideTrash2 } from '@lucide/angular'
import { MessageService } from 'primeng/api'
import { ButtonModule } from 'primeng/button'
import { CheckboxModule } from 'primeng/checkbox'
import { DialogModule } from 'primeng/dialog'
import { InputTextModule } from 'primeng/inputtext'
import { MessageModule } from 'primeng/message'
import { ToastModule } from 'primeng/toast'
import { NexusService, V1 } from '../nexus.service'
import { RestoreFocusDirective } from '../restore-focus.directive'

type TokenEntry = {
  id: string
  token: V1.PersonalAccessToken
}

type CatalogClaimDraft = {
  catalogPattern: string
  writeAccess: boolean
}

@Component({
  selector: 'app-access-tokens',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, CheckboxModule, DialogModule, InputTextModule, MessageModule, ToastModule, RestoreFocusDirective, LucideCopy, LucideRefreshCw, LucideTrash2],
  providers: [MessageService],
  template: `
    <p-toast key="access-token-status" position="bottom-center" [style]="{ width: 'min(32rem, calc(100vw - 2rem))' }" appendTo="body" />
    <p-toast key="access-token-delete-confirm" position="bottom-center" [style]="{ width: 'min(32rem, calc(100vw - 2rem))' }" appendTo="body">
      <ng-template let-message pTemplate="message">
        <div class="max-w-sm text-sm leading-5">
          <div class="font-semibold">{{ message.summary }}</div>
          <div class="mt-1 opacity-70">{{ message.detail }}</div>
          <div class="mt-3 flex justify-end gap-2">
            <button pButton type="button" size="small" severity="secondary" [text]="true" [disabled]="busy()" (click)="cancelDelete()">Cancel</button>
            <button pButton type="button" size="small" severity="danger" [disabled]="busy()" (click)="deleteConfirmedToken()">Revoke</button>
          </div>
        </div>
      </ng-template>
    </p-toast>
    <p-dialog appRestoreFocus header="Personal access tokens" [visible]="true" (visibleChange)="setVisible($event)" [modal]="true" [blockScroll]="true" [dismissableMask]="false" [closeOnEscape]="true" [draggable]="false" [resizable]="false" appendTo="body"       [closeButtonProps]="{ ariaLabel: 'Close access tokens', severity: 'secondary', text: true, rounded: true }" [style]="{ width: 'min(46rem, calc(100vw - 2rem))', maxHeight: '88dvh' }">

      <div class="space-y-5 text-sm leading-6 text-ink-muted">
        @if (error()) { <p-message severity="error">{{ error() }}</p-message> }
        @if (createdToken()) {
          <section class="rounded-sm border border-emerald-core/30 bg-emerald-core/[0.08] p-4 text-emerald-accent">
            <div class="text-xs font-semibold uppercase tracking-[0.24em] text-emerald-accent">New token</div>
            <p class="mt-2 text-emerald-accent">Copy this value now. It will not be shown again.</p>
            <div class="mt-3 flex min-w-0 items-center gap-2">
              <code class="min-w-0 flex-1 overflow-hidden text-ellipsis rounded-sm border border-emerald-core/25 bg-surface px-3 py-2 font-mono text-xs text-emerald-accent">{{ createdToken() }}</code>
              <button pButton type="button" size="small" severity="secondary" [text]="true" class="shrink-0" (click)="copyCreatedToken()" aria-label="Copy new token"><svg lucideCopy class="h-4 w-4" aria-hidden="true"></svg></button>
            </div>
          </section>
        }

        <section class="rounded-sm border border-surface-border bg-overlay/[0.035] p-4">
          <div class="mb-3 text-xs font-semibold uppercase tracking-[0.24em] text-ink-muted">Create token</div>
          <div class="grid gap-3 sm:grid-cols-[minmax(0,1fr)_13.25rem]">
            <div>
              <label for="access-token-description" class="mb-1.5 block text-xs uppercase tracking-[0.18em] text-ink-muted">Description</label>
              <input pInputText pSize="small" id="access-token-description" class="w-full" [ngModel]="description()" (ngModelChange)="description.set($event)" [disabled]="busy()" placeholder="Automation script" />
            </div>
            <div>
              <label for="access-token-expires" class="mb-1.5 block text-xs uppercase tracking-[0.18em] text-ink-muted">Expires</label>
              <input pInputText pSize="small" id="access-token-expires" type="datetime-local" step="1" class="w-full" [ngModel]="expiresInput()" (ngModelChange)="expiresInput.set($event)" [disabled]="busy()" />
              @if (!expiresInput().trim()) { <p class="mt-1 text-xs text-ink-muted">Expires never.</p> }
            </div>
          </div>

          @if (isCurrentUserAdmin()) {
            <label class="mt-4 flex items-center gap-2 text-sm text-ink">
              <p-checkbox [binary]="true" [ngModel]="privileged()" (ngModelChange)="privileged.set($event)" [disabled]="busy()" inputId="access-token-privileged" />
              <span>Privileged administrator token</span>
            </label>
          }

          <div class="mt-4 space-y-3">
            <div>
              <div class="text-xs font-semibold uppercase tracking-[0.18em] text-ink-muted">Catalog access</div>
              <p class="mt-1 text-xs text-ink-muted">Add regex patterns for catalogs this token may access.</p>
            </div>

            @for (claim of catalogClaims(); track $index) {
              <div class="grid gap-2 rounded-sm border border-surface-border bg-overlay/[0.04] p-3 sm:grid-cols-[minmax(0,1fr)_auto_auto] sm:items-center">
                <input pInputText pSize="small" class="w-full" [attr.aria-label]="'Catalog pattern ' + ($index + 1)" [ngModel]="claim.catalogPattern" (ngModelChange)="setCatalogPattern($index, $event)" [disabled]="busy()" placeholder="^/MY/CATALOG/PATH" />
                <label class="flex items-center gap-2 text-xs text-ink-muted">
                  <p-checkbox [binary]="true" [ngModel]="claim.writeAccess" (ngModelChange)="setWriteAccess($index, $event)" [disabled]="busy()" />
                  Write access
                </label>
                <button pButton type="button" size="small" severity="danger" [text]="true" [disabled]="busy() || catalogClaims().length === 1" (click)="removeCatalogClaim($index)" [attr.aria-label]="'Remove catalog pattern ' + ($index + 1)"><svg lucideTrash2 class="h-4 w-4" aria-hidden="true"></svg></button>
              </div>
            }

            <button pButton type="button" size="small" severity="secondary" [text]="true" [disabled]="busy()" (click)="addCatalogClaim()">+ Add catalog pattern</button>
          </div>

          <div class="mt-4 flex flex-wrap items-center justify-between gap-2">
            <p class="text-xs text-ink-muted">Leave expiration empty for a token that never expires.</p>
            <button pButton type="button" size="small" [outlined]="true" [disabled]="busy() || !canCreate()" (click)="createToken()">{{ creating() ? 'Creating...' : 'Create token' }}</button>
          </div>
        </section>

        <section class="space-y-3">
          <div class="flex items-center justify-between gap-2">
            <div class="text-xs font-semibold uppercase tracking-[0.24em] text-ink-muted">Existing tokens</div>
            <button pButton type="button" size="small" severity="secondary" [text]="true" [disabled]="loading()" (click)="loadTokens()" aria-label="Refresh tokens"><svg lucideRefreshCw class="h-4 w-4" aria-hidden="true"></svg></button>
          </div>

          @if (loading()) {
            <p role="status" class="text-ink-muted">Loading tokens...</p>
          } @else if (tokens().length === 0) {
            <p class="rounded-sm border border-surface-border bg-overlay/[0.03] p-4 text-ink-muted">No personal access tokens exist for your account.</p>
          } @else {
            <div class="divide-y divide-surface-border overflow-hidden rounded-sm border border-surface-border">
              @for (entry of tokens(); track entry.id) {
                <article class="flex min-w-0 items-center justify-between gap-2 bg-overlay/[0.025] px-3 py-2">
                  <div class="min-w-0">
                    <div class="truncate font-medium text-ink">{{ entry.token.description || 'Untitled token' }}</div>
                    <div class="mt-1 text-xs text-ink-muted">Expires {{ formatDate(entry.token.expires) }}</div>
                  </div>
                  <button pButton type="button" size="small" severity="danger" [text]="true" [disabled]="busy()" (click)="confirmDelete(entry)" [attr.aria-label]="'Revoke token ' + (entry.token.description || entry.id)"><svg lucideTrash2 class="h-4 w-4" aria-hidden="true"></svg></button>
                </article>
              }
            </div>
          }
        </section>
      </div>
    </p-dialog>
  `,
})
export class AccessTokensComponent implements OnInit {
  private readonly nexus = inject(NexusService)
  private readonly messageService = inject(MessageService)
  readonly close = output<void>()
  readonly tokens = signal<TokenEntry[]>([])
  readonly loading = signal(false)
  readonly creating = signal(false)
  readonly deleting = signal(false)
  readonly error = signal('')
  readonly createdToken = signal('')
  readonly confirmingDeleteId = signal('')
  readonly description = signal('')
  readonly expiresInput = signal('')
  readonly privileged = signal(false)
  readonly catalogClaims = signal<CatalogClaimDraft[]>([emptyCatalogClaim()])
  readonly busy = computed(() => this.loading() || this.creating() || this.deleting())
  readonly canCreate = computed(() => this.description().trim().length > 0 && (!this.expiresInput().trim() || isValidLocalDateTime(this.expiresInput())))
  readonly isCurrentUserAdmin = computed(() => this.nexus.currentUser()?.claims?.some(claim => claim.type === 'role' && claim.value === 'Administrator') ?? false)

  ngOnInit() {
    void this.loadTokens()
  }

  setVisible(visible: boolean) {
    if (!visible) this.close.emit()
  }

  async loadTokens() {
    this.loading.set(true)
    this.error.set('')

    try {
      const tokenMap = await this.nexus.getPersonalAccessTokens()
      this.tokens.set(Object.entries(tokenMap)
        .map(([id, token]) => ({ id, token }))
        .sort((a, b) => (a.token.expires ?? '').localeCompare(b.token.expires ?? '')))
    } catch (error) {
      this.error.set(errorMessage(error, 'Could not load personal access tokens.'))
    } finally {
      this.loading.set(false)
    }
  }

  async createToken() {
    if (!this.canCreate()) return
    this.creating.set(true)
    this.error.set('')
    this.createdToken.set('')

    try {
      const tokenValue = await this.nexus.createPersonalAccessToken({
        description: this.description().trim(),
        expires: this.expiresInput().trim() ? new Date(this.expiresInput()).toISOString() : neverExpires,
        claims: this.createClaims(),
        grantClaims: [],
      })
      this.createdToken.set(tokenValue)
      this.description.set('')
      this.expiresInput.set('')
      this.privileged.set(false)
      this.catalogClaims.set([emptyCatalogClaim()])
      await this.loadTokens()
    } catch (error) {
      this.error.set(errorMessage(error, 'Could not create personal access token.'))
    } finally {
      this.creating.set(false)
    }
  }

  async deleteToken(tokenId: string) {
    if (!tokenId) return
    this.deleting.set(true)
    this.error.set('')

    try {
      this.messageService.clear('access-token-delete-confirm')
      await this.nexus.deletePersonalAccessToken(tokenId)
      this.confirmingDeleteId.set('')
      this.createdToken.set('')
      this.messageService.add({ key: 'access-token-status', severity: 'success', summary: 'Token revoked', life: 2500 })
      await this.loadTokens()
    } catch (error) {
      this.error.set(errorMessage(error, 'Could not revoke personal access token.'))
    } finally {
      this.deleting.set(false)
    }
  }

  confirmDelete(entry: TokenEntry) {
    this.confirmingDeleteId.set(entry.id)
    this.messageService.clear('access-token-delete-confirm')
    this.messageService.add({
      key: 'access-token-delete-confirm',
      summary: 'Revoke this token?',
      detail: `This permanently revokes ${entry.token.description || 'this token'}.`,
      sticky: true,
      closable: false,
    })
  }

  cancelDelete() {
    this.messageService.clear('access-token-delete-confirm')
    this.confirmingDeleteId.set('')
  }

  async deleteConfirmedToken() {
    await this.deleteToken(this.confirmingDeleteId())
  }

  copyCreatedToken() {
    const value = this.createdToken()
    if (!value || !navigator.clipboard) return

    void navigator.clipboard.writeText(value).then(() => this.messageService.add({ key: 'access-token-status', severity: 'success', summary: 'Token copied', life: 2500 }))
  }

  formatDate(value: string | undefined) {
    if (!value) return 'unknown'
    if (value.startsWith('9999-12-31')) return 'never'
    const date = new Date(value)
    if (Number.isNaN(date.getTime())) return value
    return date.getUTCFullYear() >= 9999 ? 'never' : date.toLocaleString()
  }

  addCatalogClaim() {
    this.catalogClaims.update(claims => [...claims, emptyCatalogClaim()])
  }

  removeCatalogClaim(index: number) {
    this.catalogClaims.update(claims => claims.length === 1 ? claims : claims.filter((_, claimIndex) => claimIndex !== index))
  }

  setCatalogPattern(index: number, value: string) {
    this.catalogClaims.update(claims => claims.map((claim, claimIndex) => claimIndex === index ? { ...claim, catalogPattern: value } : claim))
  }

  setWriteAccess(index: number, value: boolean) {
    this.catalogClaims.update(claims => claims.map((claim, claimIndex) => claimIndex === index ? { ...claim, writeAccess: value } : claim))
  }

  private createClaims(): V1.TokenClaim[] {
    const claims = this.catalogClaims()
      .map(claim => ({ ...claim, catalogPattern: claim.catalogPattern.trim() }))
      .filter(claim => claim.catalogPattern.length > 0)
      .map(claim => ({
        type: claim.writeAccess ? 'CanWriteCatalog' : 'CanReadCatalog',
        value: claim.catalogPattern,
      }))

    if (this.privileged() && this.isCurrentUserAdmin()) {
      claims.push({ type: 'role', value: 'Administrator' })
    }

    return claims
  }
}

const neverExpires = '9999-12-31T23:59:59.9999999Z'

function emptyCatalogClaim(): CatalogClaimDraft {
  return { catalogPattern: '', writeAccess: false }
}

function isValidLocalDateTime(value: string) {
  return value.trim().length > 0 && !Number.isNaN(new Date(value).getTime())
}

function errorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? `${fallback} ${error.message}` : fallback
}

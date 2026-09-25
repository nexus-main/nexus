import { Component, computed, input, output } from '@angular/core'
import { MenuItem } from 'primeng/api'
import { ButtonModule } from 'primeng/button'
import { MenuModule } from 'primeng/menu'
import { AppIconComponent } from './app-icon.component'

type ThemeMode = 'dark' | 'light' | 'system'
type DevAuthMode = 'admin' | 'user'

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [ButtonModule, MenuModule, AppIconComponent],
  template: `
    <header class="content-panel overflow-hidden rounded-md">
      <div class="relative flex items-center justify-between gap-2 p-2.5 sm:gap-3 sm:p-3 lg:p-4">
        <div class="pointer-events-none order-1 min-w-0 flex-1 text-center">
          <svg class="nexus-logo inline h-6 w-auto opacity-80" viewBox="0 0 34.53199 8.4074602" role="img" aria-label="Nexus" xmlns="http://www.w3.org/2000/svg">
            <defs>
              <linearGradient id="nexusLogoGradient" gradientUnits="userSpaceOnUse" x1="136.16373" y1="135.48936" x2="92.778351" y2="135.62166" gradientTransform="matrix(0.72736381,0,0,0.72736381,45.515429,35.533905)">
                <stop offset="0" stop-color="var(--nexus-logo-from)" />
                <stop offset="1" stop-color="var(--nexus-logo-to)" />
              </linearGradient>
            </defs>
            <g transform="translate(-113.14477,-130.15869)">
              <g style="font-size:9.34481px;line-height:1.25;letter-spacing:0px;word-spacing:0px;fill:url(#nexusLogoGradient);stroke-width:0.233484">
                <path d="m 118.32822,137.79045 h -1.11791 l -3.2214,-6.07778 v 6.07778 h -0.84414 v -6.79415 h 1.40081 l 2.93851,5.54848 v -5.54848 h 0.84413 z" />
                <path d="m 124.61133,137.79045 h -4.4762 v -6.79415 h 4.4762 v 0.80307 h -3.57275 v 1.86166 h 3.57275 v 0.80307 h -3.57275 v 2.52328 h 3.57275 z" />
                <path d="m 141.01494,135.06184 q 0,0.73919 -0.16427,1.2913 -0.1597,0.54755 -0.52929,0.91258 -0.35135,0.34678 -0.82133,0.50648 -0.46997,0.1597 -1.09509,0.1597 -0.63881,0 -1.11335,-0.16883 -0.47454,-0.16882 -0.7985,-0.49735 -0.3696,-0.37416 -0.53386,-0.90346 -0.1597,-0.52929 -0.1597,-1.30042 v -4.06554 h 0.90345 v 4.11117 q 0,0.55211 0.073,0.87151 0.0776,0.3194 0.25552,0.57949 0.20076,0.29659 0.54298,0.44716 0.34678,0.15058 0.83045,0.15058 0.48823,0 0.83045,-0.14601 0.34221,-0.15058 0.54754,-0.45173 0.17796,-0.26009 0.25096,-0.59318 0.0776,-0.33765 0.0776,-0.83501 v -4.13398 h 0.90346 z" />
                <path d="m 147.67676,135.85122 q 0,0.39697 -0.18708,0.78482 -0.18251,0.38784 -0.5156,0.65706 -0.36503,0.29202 -0.85326,0.45628 -0.48367,0.16427 -1.16811,0.16427 -0.73462,0 -1.32324,-0.13689 -0.58405,-0.13688 -1.19091,-0.4061 v -1.13159 h 0.0639 q 0.51561,0.42891 1.19092,0.66162 0.6753,0.2327 1.26848,0.2327 0.83957,0 1.30499,-0.31484 0.46998,-0.31484 0.46998,-0.83957 0,-0.45173 -0.22359,-0.66618 -0.21901,-0.21446 -0.67074,-0.33309 -0.34222,-0.0913 -0.74375,-0.15058 -0.39697,-0.0593 -0.84414,-0.15057 -0.90345,-0.19165 -1.34149,-0.6525 -0.43347,-0.46541 -0.43347,-1.20916 0,-0.85327 0.72093,-1.39625 0.72094,-0.54755 1.82972,-0.54755 0.71638,0 1.31412,0.13689 0.59774,0.13689 1.05859,0.33765 v 1.06772 h -0.0639 q -0.38785,-0.32853 -1.02209,-0.54298 -0.62968,-0.21902 -1.2913,-0.21902 -0.7255,0 -1.1681,0.30115 -0.43804,0.30115 -0.43804,0.77569 0,0.42435 0.21902,0.66618 0.21902,0.24184 0.77113,0.3696 0.29203,0.0639 0.83045,0.15514 0.53842,0.0913 0.91258,0.18707 0.75744,0.20077 1.14072,0.60687 0.38328,0.4061 0.38328,1.13616 z" />
              </g>
              <path d="m 126.35243,130.15869 -0.60564,0.60564 3.31266,3.31239 h 0.60536 v -0.60565 z m 7.19646,0 -3.31267,3.31267 v 0.60536 h 0.60565 l 3.31265,-3.31239 z m -4.48972,4.48944 -3.31238,3.31238 0.60564,0.60564 3.31238,-3.31238 v -0.60564 z m 1.17705,0 v 0.60536 l 3.31267,3.31266 0.60563,-0.60564 -3.31238,-3.31238 z" style="fill:var(--nexus-logo-to)" />
            </g>
          </svg>
        </div>

        <button pButton type="button" size="small" [outlined]="true" class="h-9 shrink-0 px-2.5 transition-colors lg:hidden" (click)="openCatalog.emit()" aria-label="Open catalog browser">
          <span class="text-xl leading-none">☰</span>
        </button>

        <div class="hidden min-w-0 flex-col leading-tight sm:flex">
          <span class="text-[0.65rem] font-semibold uppercase tracking-[0.18em] text-ink-muted">Instance</span>
          <span class="max-w-56 truncate text-sm font-semibold text-ink">{{ applicationName() ?? endpointHost() }}</span>
        </div>

        <p-menu #adminMenu styleClass="header-menu" [model]="adminMenuItems()" [popup]="true" appendTo="body">
          <ng-template pTemplate="item" let-item>
            @if (item.url) {
              <a class="flex cursor-pointer items-center gap-2 px-3 py-2" [href]="item.url" [target]="item.target ?? '_self'" [attr.rel]="item.target === '_blank' ? 'noopener' : null">
                @switch (item.icon) {
                  @case ('api') { <app-icon name="api" class="h-4 w-4 shrink-0" /> }
                  @case ('download') { <app-icon name="download" class="h-4 w-4 shrink-0" /> }
                  @case ('git') { <app-icon name="git" class="h-4 w-4 shrink-0" /> }
                  @case ('package') { <app-icon name="package" class="h-4 w-4 shrink-0" /> }
                  @case ('upload') { <app-icon name="upload" class="h-4 w-4 shrink-0" /> }
                  @case ('waypoints') { <app-icon name="waypoints" class="h-4 w-4 shrink-0" /> }
                  @case ('help') { <app-icon name="help" class="h-4 w-4 shrink-0" /> }
                  @case ('info') { <app-icon name="info" class="h-4 w-4 shrink-0" /> }
                }
                <span>{{ item.label }}</span>
              </a>
            } @else {
              <div class="flex cursor-pointer items-center gap-2 px-3 py-2" (click)="$event.stopPropagation(); item.command?.($event)">
                @switch (item.icon) {
                  @case ('api') { <app-icon name="api" class="h-4 w-4 shrink-0" /> }
                  @case ('download') { <app-icon name="download" class="h-4 w-4 shrink-0" /> }
                  @case ('git') { <app-icon name="git" class="h-4 w-4 shrink-0" /> }
                  @case ('package') { <app-icon name="package" class="h-4 w-4 shrink-0" /> }
                  @case ('upload') { <app-icon name="upload" class="h-4 w-4 shrink-0" /> }
                  @case ('waypoints') { <app-icon name="waypoints" class="h-4 w-4 shrink-0" /> }
                  @case ('help') { <app-icon name="help" class="h-4 w-4 shrink-0" /> }
                  @case ('info') { <app-icon name="info" class="h-4 w-4 shrink-0" /> }
                }
                <span>{{ item.label }}</span>
              </div>
            }
          </ng-template>
        </p-menu>
        <p-menu #userMenu styleClass="header-menu" [model]="userMenuItems()" [popup]="true" appendTo="body">
          <ng-template pTemplate="item" let-item>
            @if (item.url) {
              <a class="flex cursor-pointer items-center gap-2 px-3 py-2" [href]="item.url">
                @if (item.icon === 'log-out') { <app-icon name="log-out" class="h-4 w-4 shrink-0" /> }
                <span>{{ item.label }}</span>
              </a>
            } @else {
              <div class="flex cursor-pointer items-center gap-2 px-3 py-2" (click)="$event.stopPropagation(); item.command?.($event)">
                @if (item.icon === 'key') { <app-icon name="key" class="h-4 w-4 shrink-0" /> }
                @if (item.icon === 'shield') { <app-icon name="shield" class="h-4 w-4 shrink-0" /> }
                @if (item.icon === 'user') { <app-icon name="user" class="h-4 w-4 shrink-0" /> }
                <span>{{ item.label }}</span>
              </div>
            }
          </ng-template>
        </p-menu>
        <div class="order-2 ml-auto flex shrink-0 items-center gap-1 sm:gap-2">
          <button pButton type="button" size="small" [outlined]="true" severity="secondary" class="h-9 gap-2 px-2.5 transition-colors" (click)="toggleTheme.emit()" aria-label="Toggle theme">
            @if (themeMode() === 'dark') {
              <app-icon name="moon" class="h-4 w-4" />
            } @else if (themeMode() === 'system') {
              <app-icon name="sun-moon" class="h-4 w-4" />
            } @else {
              <app-icon name="sun" class="h-4 w-4" />
            }
            <span class="hidden lg:inline">{{ themeMode() === 'dark' ? 'Dark' : themeMode() === 'light' ? 'Light' : 'System' }}</span>
          </button>
          <button pButton type="button" size="small" [outlined]="true" severity="secondary" class="hidden h-9 gap-2 px-2.5 transition-colors sm:flex" (click)="openJobs.emit()" aria-label="Open jobs menu">
            <app-icon name="jobs" class="h-4 w-4" />
            <span class="tabular-nums">{{ jobCount() }}</span>
            <span class="hidden lg:inline">Jobs</span>
          </button>
          <button pButton type="button" size="small" [outlined]="true" [severity]="adminMenu.visible ? 'primary' : 'secondary'" class="h-9 gap-2 px-2.5 transition-colors" (click)="adminMenu.toggle($event)" aria-label="Settings" aria-haspopup="menu" [attr.aria-expanded]="adminMenu.visible" [attr.aria-controls]="adminMenu.id">
            <app-icon name="settings" class="h-4 w-4" />
            <span class="hidden lg:inline">Settings</span>
          </button>
          <button type="button" class="grid h-10 w-10 place-items-center rounded-full border border-violet-core/25 bg-violet-core/15 font-mono text-xs font-semibold text-violet-accent transition-colors hover:border-violet-core/50 hover:bg-violet-core/25 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-violet-core" (click)="userMenu.toggle($event)" aria-label="Open user menu" aria-haspopup="menu" [attr.aria-expanded]="userMenu.visible" [attr.aria-controls]="userMenu.id">
            {{ userInitials() }}
          </button>
        </div>
      </div>
    </header>
  `,
})
export class AppHeaderComponent {
  readonly endpointHost = input.required<string>()
  readonly applicationName = input<string | null | undefined>(null)
  readonly jobCount = input.required<number>()
  readonly userInitials = input.required<string>()
  readonly themeMode = input.required<ThemeMode>()
  readonly isAdministrator = input(false)
  readonly isDevelopmentMode = input(false)
  readonly devAuthMode = input<DevAuthMode>('admin')
  readonly helpLink = input<string | null | undefined>(null)
  readonly logoutUrl = input<string | null | undefined>(null)
  readonly openPackageReferences = output<void>()
  readonly openDataSourcePipelines = output<void>()
  readonly openGit = output<void>()
  readonly openSetupImport = output<void>()
  readonly openSetupExport = output<void>()
  readonly openJobs = output<void>()
  readonly openAccessTokens = output<void>()
  readonly openAbout = output<void>()
  readonly changeDevAuthMode = output<DevAuthMode>()
  readonly adminMenuItems = computed<MenuItem[]>(() => {
    const items: MenuItem[] = []
    if (this.isAdministrator()) {
      items.push({ label: 'Package references', icon: 'package', command: () => this.openPackageReferences.emit() })
      items.push({ label: 'Data source pipelines', icon: 'waypoints', command: () => this.openDataSourcePipelines.emit() })
      items.push({ label: 'Git', icon: 'git', command: () => this.openGit.emit() })
      items.push({ separator: true })
    }
    items.push({ label: 'Import settings...', icon: 'upload', command: () => this.openSetupImport.emit() })
    items.push({ label: 'Export settings...', icon: 'download', command: () => this.openSetupExport.emit() })
    items.push({ separator: true })
    items.push({ label: 'API', icon: 'api', url: '/api', target: '_blank' })
    if (this.helpLink()) items.push({ label: 'Help', icon: 'help', url: this.helpLink()!, target: '_blank' })
    items.push({ label: 'About', icon: 'info', command: () => this.openAbout.emit() })
    return items
  })
  readonly userMenuItems = computed<MenuItem[]>(() => {
    const items: MenuItem[] = [{ label: 'Access tokens', icon: 'key', command: () => this.openAccessTokens.emit() }]

    if (this.isDevelopmentMode()) {
      const mode = this.devAuthMode()
      items.push(
        { separator: true },
        { label: mode === 'admin' ? 'Admin mode (current)' : 'Admin mode', icon: 'shield', command: () => this.changeDevAuthMode.emit('admin') },
        { label: mode === 'user' ? 'User mode (current)' : 'User mode', icon: 'user', command: () => this.changeDevAuthMode.emit('user') },
      )
    }

    if (this.logoutUrl()) items.push({ separator: true }, { label: 'Logout', icon: 'log-out', url: this.logoutUrl()! })
    return items
  })
  readonly openCatalog = output<void>()
  readonly toggleTheme = output<void>()
}

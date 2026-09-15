import { Component, input, output } from '@angular/core'

type ThemeMode = 'dark' | 'light'

@Component({
  selector: 'app-header',
  standalone: true,
  template: `
    <header class="glass-panel overflow-hidden rounded-xl">
      <div class="relative flex items-center justify-between gap-3 p-2.5 sm:p-3 lg:p-4">
        <div class="pointer-events-none absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2">
          <span class="bg-gradient-to-r from-[#3dd9ef] to-cyan-400 bg-clip-text font-mono text-xl font-light uppercase leading-none tracking-[0.12em] text-transparent opacity-80 sm:text-3xl sm:tracking-[0.2em] lg:text-4xl">Nexus</span>
        </div>

        <button type="button" class="grid h-10 w-10 shrink-0 place-items-center rounded-2xl border border-cyan-300/20 bg-cyan-300/10 text-cyan-200 lg:hidden" (click)="openCatalog.emit()" aria-label="Open catalog browser">
          <span class="text-xl leading-none">☰</span>
        </button>

        <div class="hidden items-center gap-2 rounded-2xl border border-cyan-300/20 bg-cyan-300/10 px-3 py-2 text-xs text-cyan-100 sm:flex">
          <span>◇</span>
          <span class="max-w-48 truncate font-mono">{{ endpointHost() }}</span>
        </div>

        <div class="ml-auto flex shrink-0 items-center gap-2">
          <button type="button" class="inline-flex h-8 items-center gap-1.5 rounded-2xl border border-white/10 bg-white/[0.04] px-3 text-white transition hover:border-cyan-300/35 hover:text-white" (click)="toggleTheme.emit()" [attr.aria-label]="themeMode() === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'" [attr.title]="themeMode() === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'">
            <span>{{ themeMode() === 'dark' ? '☾' : '☀' }}</span>
            <span class="hidden sm:inline">{{ themeMode() === 'dark' ? 'Dark' : 'Light' }}</span>
          </button>
          <button type="button" class="hidden h-8 items-center gap-1.5 rounded-2xl border border-white/10 bg-white/[0.04] px-3 text-white transition hover:border-cyan-300/35 hover:text-white sm:flex" aria-label="Open jobs menu" title="Open jobs menu">
            <span>{{ jobCount() }}</span>
            <span>Jobs</span>
          </button>
          <div class="grid h-10 w-10 place-items-center rounded-full border border-violet-300/25 bg-violet-300/15 font-mono text-xs font-semibold text-violet-100" aria-label="Signed-in user initials">
            {{ userInitials() }}
          </div>
        </div>
      </div>
    </header>
  `,
})
export class AppHeaderComponent {
  readonly endpointHost = input.required<string>()
  readonly jobCount = input.required<number>()
  readonly userInitials = input.required<string>()
  readonly themeMode = input.required<ThemeMode>()
  readonly openCatalog = output<void>()
  readonly toggleTheme = output<void>()
}

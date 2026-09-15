import { provideZonelessChangeDetection } from '@angular/core'
import { bootstrapApplication } from '@angular/platform-browser'
import { provideRouter } from '@angular/router'
import { providePrimeNG } from 'primeng/config'
import { AppComponent } from './app/app.component'
import { nexusPreset } from './app/theme'

bootstrapApplication(AppComponent, {
  providers: [
    provideZonelessChangeDetection(),
    provideRouter([]),
    providePrimeNG({
      theme: {
        preset: nexusPreset,
        options: {
          darkModeSelector: '[data-theme="dark"]',
          cssLayer: { name: 'primeng', order: 'theme, base, primeng, components, utilities' },
        },
      },
    }),
  ],
}).catch((error: unknown) => console.error(error))

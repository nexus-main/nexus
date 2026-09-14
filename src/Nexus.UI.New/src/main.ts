import { provideZonelessChangeDetection } from '@angular/core'
import { bootstrapApplication } from '@angular/platform-browser'
import { provideRouter } from '@angular/router'
import { AppComponent } from './app/app.component'

bootstrapApplication(AppComponent, {
  providers: [provideZonelessChangeDetection(), provideRouter([])],
}).catch((error: unknown) => console.error(error))

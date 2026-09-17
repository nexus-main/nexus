import { Component, input } from '@angular/core'
import { LucideMapPin, LucideMoon, LucidePackage, LucideSettings, LucideSun, LucideWorkflow } from '@lucide/angular'

export type AppIconName = 'map-pin' | 'moon' | 'package' | 'settings' | 'sun' | 'workflow'

@Component({
  selector: 'app-icon',
  standalone: true,
  imports: [LucideMapPin, LucideMoon, LucidePackage, LucideSettings, LucideSun, LucideWorkflow],
  template: `
    @switch (name()) {
      @case ('map-pin') { <svg lucideMapPin aria-hidden="true"></svg> }
      @case ('moon') { <svg lucideMoon aria-hidden="true"></svg> }
      @case ('package') { <svg lucidePackage aria-hidden="true"></svg> }
      @case ('settings') { <svg lucideSettings aria-hidden="true"></svg> }
      @case ('sun') { <svg lucideSun aria-hidden="true"></svg> }
      @case ('workflow') { <svg lucideWorkflow aria-hidden="true"></svg> }
    }
  `,
  styles: [`
    :host { display: inline-flex; line-height: 0; }
    svg { width: 100%; height: 100%; }
  `],
})
export class AppIconComponent {
  readonly name = input.required<AppIconName>()
}

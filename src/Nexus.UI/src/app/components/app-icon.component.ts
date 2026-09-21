import { Component, input } from '@angular/core'
import { LucideBraces, LucideMapPin, LucideMoon, LucidePackage, LucideSettings, LucideSun, LucideWaypoints } from '@lucide/angular'

export type AppIconName = 'braces' | 'map-pin' | 'moon' | 'package' | 'settings' | 'sun' | 'waypoints'

@Component({
  selector: 'app-icon',
  standalone: true,
  imports: [LucideBraces, LucideMapPin, LucideMoon, LucidePackage, LucideSettings, LucideSun, LucideWaypoints],
  template: `
    @switch (name()) {
      @case ('braces') { <svg lucideBraces aria-hidden="true"></svg> }
      @case ('map-pin') { <svg lucideMapPin aria-hidden="true"></svg> }
      @case ('moon') { <svg lucideMoon aria-hidden="true"></svg> }
      @case ('package') { <svg lucidePackage aria-hidden="true"></svg> }
      @case ('settings') { <svg lucideSettings aria-hidden="true"></svg> }
      @case ('sun') { <svg lucideSun aria-hidden="true"></svg> }
      @case ('waypoints') { <svg lucideWaypoints aria-hidden="true"></svg> }
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

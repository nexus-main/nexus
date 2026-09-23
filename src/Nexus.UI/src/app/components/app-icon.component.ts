import { Component, input } from '@angular/core'
import { LucideCircleHelp, LucideCodeXml, LucideDownload, LucideGitBranch, LucideInfo, LucideKeyRound, LucideListChecks, LucideLogOut, LucideMapPin, LucideMonitor, LucideMoon, LucidePackage, LucideSettings, LucideSun, LucideSunMoon, LucideUpload, LucideWaypoints } from '@lucide/angular'

export type AppIconName = 'api' | 'download' | 'git' | 'help' | 'info' | 'key' | 'jobs' | 'log-out' | 'map-pin' | 'monitor' | 'moon' | 'package' | 'settings' | 'sun' | 'sun-moon' | 'upload' | 'waypoints'

@Component({
  selector: 'app-icon',
  standalone: true,
  imports: [LucideCircleHelp, LucideCodeXml, LucideDownload, LucideGitBranch, LucideInfo, LucideKeyRound, LucideListChecks, LucideLogOut, LucideMapPin, LucideMonitor, LucideMoon, LucidePackage, LucideSettings, LucideSun, LucideSunMoon, LucideUpload, LucideWaypoints],
  template: `
    @switch (name()) {
      @case ('api') { <svg lucideCodeXml aria-hidden="true"></svg> }
      @case ('download') { <svg lucideDownload aria-hidden="true"></svg> }
      @case ('git') { <svg lucideGitBranch aria-hidden="true"></svg> }
      @case ('help') { <svg lucideCircleHelp aria-hidden="true"></svg> }
      @case ('info') { <svg lucideInfo aria-hidden="true"></svg> }
      @case ('key') { <svg lucideKeyRound aria-hidden="true"></svg> }
      @case ('jobs') { <svg lucideListChecks aria-hidden="true"></svg> }
      @case ('log-out') { <svg lucideLogOut aria-hidden="true"></svg> }
      @case ('map-pin') { <svg lucideMapPin aria-hidden="true"></svg> }
      @case ('monitor') { <svg lucideMonitor aria-hidden="true"></svg> }
      @case ('moon') { <svg lucideMoon aria-hidden="true"></svg> }
      @case ('package') { <svg lucidePackage aria-hidden="true"></svg> }
      @case ('settings') { <svg lucideSettings aria-hidden="true"></svg> }
      @case ('sun') { <svg lucideSun aria-hidden="true"></svg> }
      @case ('sun-moon') { <svg lucideSunMoon aria-hidden="true"></svg> }
      @case ('upload') { <svg lucideUpload aria-hidden="true"></svg> }
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

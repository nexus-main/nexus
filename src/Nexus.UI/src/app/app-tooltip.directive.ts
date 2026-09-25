import { Directive, NgZone, ViewContainerRef, inject } from '@angular/core'
import { Tooltip, TooltipStyle } from 'primeng/tooltip'

@Directive({
  selector: '[pTooltip]',
  standalone: true,
  providers: [TooltipStyle],
})
export class AppTooltipDirective extends Tooltip {
  constructor() {
    super(inject(NgZone), inject(ViewContainerRef))
    this.setOption({ showDelay: 500 })
  }
}

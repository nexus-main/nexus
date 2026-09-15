import { DOCUMENT } from '@angular/common'
import { DestroyRef, Directive, afterRenderEffect, booleanAttribute, inject, input } from '@angular/core'
import { Dialog } from 'primeng/dialog'
import { Drawer } from 'primeng/drawer'

@Directive({ selector: '[appRestoreFocus]', standalone: true })
export class RestoreFocusDirective {
  readonly appRestoreFocus = input(true, { transform: booleanAttribute })

  constructor() {
    const document = inject(DOCUMENT)
    const overlay = inject(Dialog, { optional: true }) ?? inject(Drawer)
    let opener: HTMLElement | null = null
    const restore = () => {
      if (opener?.isConnected) opener.focus()
      opener = null
    }
    const show = overlay.onShow.subscribe(() => {
      opener = document.activeElement instanceof HTMLElement ? document.activeElement : null
    })
    const hide = overlay.onHide.subscribe(restore)
    // Drawer does not emit onHide when its visibility is changed by application state.
    afterRenderEffect(() => {
      if (!this.appRestoreFocus()) restore()
    })
    inject(DestroyRef).onDestroy(() => {
      show.unsubscribe()
      hide.unsubscribe()
      restore()
    })
  }
}

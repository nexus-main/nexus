import { AfterViewInit, Directive, ElementRef, Input, OnChanges, OnDestroy, inject } from '@angular/core';
import { fitLegendName } from './legend-text';

@Directive({
  selector: '[legendName]',
  standalone: true,
  host: { '[attr.aria-label]': 'legendName' },
})
export class LegendNameDirective implements AfterViewInit, OnChanges, OnDestroy {
  @Input() legendName = '';
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly context = this.element.ownerDocument.createElement('canvas').getContext('2d');
  private observer?: ResizeObserver;
  private readonly fit = (): void => {
    if (!this.context) return;
    const style = getComputedStyle(this.element);
    this.context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
    this.element.textContent = fitLegendName(this.legendName, this.element.clientWidth, text => this.context!.measureText(text).width);
  };

  ngAfterViewInit(): void {
    this.observer = new ResizeObserver(this.fit);
    this.observer.observe(this.element);
    this.element.ownerDocument.fonts.addEventListener('loadingdone', this.fit);
    this.fit();
  }

  ngOnChanges(): void { this.fit(); }

  ngOnDestroy(): void {
    this.observer?.disconnect();
    this.element.ownerDocument.fonts.removeEventListener('loadingdone', this.fit);
  }
}

import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, OnDestroy, effect, input, viewChild } from '@angular/core'
import { UNIX_EPOCH_TICKS, formatTime, getTimeTicks, isSlowTickRequired, roundAway } from './chart-math'

type ThemeMode = 'dark' | 'light'

@Component({
  selector: 'nexus-availability-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<canvas #canvas class="block h-full w-full"></canvas>`,
  styles: [`:host { display: block; min-height: 0; }`],
})
export class AvailabilityChartComponent implements AfterViewInit, OnDestroy {
  readonly data = input<number[]>([])
  readonly begin = input('') // ISO string
  readonly end = input('') // ISO string
  readonly themeMode = input<ThemeMode>('dark')

  private readonly canvasRef = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas')
  private frame = 0
  private disposed = false
  private resizeObserver?: ResizeObserver
  private dprQuery?: MediaQueryList

  private readonly onResize = (): void => this.scheduleDraw()
  private readonly onDprChange = (): void => { this.watchDpr(); this.scheduleDraw() }

  constructor() {
    effect(() => { this.data(); this.begin(); this.end(); this.themeMode(); this.scheduleDraw() })
  }

  ngAfterViewInit(): void {
    this.resizeObserver = new ResizeObserver(this.onResize)
    this.resizeObserver.observe(this.canvasRef().nativeElement)
    window.addEventListener('resize', this.onResize)
    this.watchDpr()
    void this.loadFont()
  }

  ngOnDestroy(): void {
    this.disposed = true
    this.resizeObserver?.disconnect()
    window.removeEventListener('resize', this.onResize)
    this.dprQuery?.removeEventListener('change', this.onDprChange)
  }

  private async loadFont(): Promise<void> {
    try {
      await document.fonts.load('bold 12px "Nexus Chart"')
      if (!this.disposed) this.scheduleDraw()
    } catch {
      /* fallback to Courier New */
    }
  }

  private watchDpr(): void {
    this.dprQuery?.removeEventListener('change', this.onDprChange)
    this.dprQuery = matchMedia(`(resolution: ${window.devicePixelRatio || 1}dppx)`)
    this.dprQuery.addEventListener('change', this.onDprChange)
  }

  private scheduleDraw(): void {
    if (this.frame) return
    this.frame = requestAnimationFrame(() => { this.frame = 0; this.draw() })
  }


  private draw(): void {
    const canvas = this.canvasRef().nativeElement
    const width = canvas.clientWidth
    const height = canvas.clientHeight
    if (width <= 0 || height <= 0) return
    const dpr = window.devicePixelRatio || 1
    canvas.width = Math.max(1, Math.round(width * dpr))
    canvas.height = Math.max(1, Math.round(height * dpr))
    const context = canvas.getContext('2d')
    if (!context) return
    context.setTransform(1, 0, 0, 1, 0, 0)
    context.scale(dpr, dpr)
    context.clearRect(0, 0, width, height)

    const lightTheme = this.themeMode() === 'light'
    const labelColor = lightTheme ? '#555555' : '#94a3b8'
    const tickColor = lightTheme ? '#dddddd' : 'rgba(148, 163, 184, 0.25)'
    const barColor = '#f97316' // (249, 115, 22)
    const barFillAlpha = 0x19 / 0xff // ~10%

    context.font = 'bold 12px "Nexus Chart", "Courier New", monospace'
    context.fillStyle = labelColor
    context.strokeStyle = tickColor
    context.textAlign = 'start'
    context.textBaseline = 'alphabetic'

    /* sizes — match dev branch AvailabilityChart.razor.cs, with an inset margin for the dialog canvas */
    const LINE_HEIGHT = 7
    const CHART_MARGIN = 16
    const yMin = CHART_MARGIN + LINE_HEIGHT * 2
    const yMax = Math.max(yMin + 54, height - CHART_MARGIN)
    const yRange = Math.max(1, yMax - (yMin + 54))
    const barBottom = yMin + yRange
    const xMax = Math.max(CHART_MARGIN + 1, width - CHART_MARGIN)

    /* y-axis title (rotated 270°) */
    let xMin = CHART_MARGIN + 20
    context.save()
    context.translate(xMin, yMin + yRange / 2)
    context.rotate(-Math.PI / 2)
    context.textAlign = 'center'
    context.textBaseline = 'alphabetic'
    context.font = '17px "Nexus Chart", "Courier New", monospace'
    context.fillText('Availability / %', 0, 0)
    context.restore()

    xMin += 10

    /* y-axis labels + grid lines (0-100% in 11 steps) */
    context.font = 'bold 12px "Nexus Chart", "Courier New", monospace'
    context.textAlign = 'start'
    context.textBaseline = 'middle'
    const characterWidth = context.measureText(' ').width
    const desiredYLabelCount = 11
    const maxYLabelCount = yRange / 50
    const ySkip = Math.ceil(desiredYLabelCount / Math.max(1, maxYLabelCount))

    for (let i = 0; i < desiredYLabelCount; i++) {
      if ((i + ySkip) % ySkip !== 0) continue
      const relative = i / 10
      const y = yMin + (1 - relative) * yRange
      const label = String(Math.round(relative * 100)).padStart(3, ' ')
      context.fillText(label, xMin, y)
      const lineOffset = characterWidth * 3
      context.beginPath()
      context.moveTo(xMin + lineOffset, y)
      context.lineTo(xMax, y)
      context.stroke()
    }

    xMin += characterWidth * 4

    /* bars */
    const data = this.data()
    const count = data.length
    const xRange = xMax - xMin
    const valueWidth = count > 0 ? xRange / count : 0

    if (count > 0) {
      const barGap = valueWidth > 1 ? Math.min(valueWidth - 1, Math.max(valueWidth * 0.2, 1)) : 0
      const barWidth = valueWidth - barGap
      const barOffset = barGap / 2

      context.lineWidth = 1
      context.strokeStyle = barColor
      context.fillStyle = barColor
      context.globalAlpha = barFillAlpha
      context.textBaseline = 'alphabetic'

      for (let i = 0; i < count; i++) {
        const availability = data[i]
        if (!Number.isFinite(availability) || availability <= 0) continue
        const x = xMin + i * valueWidth + barOffset
        const w = barWidth
        const h = yRange * availability
        const y = barBottom - h
        context.fillRect(x, y, w, h)
      }

      context.globalAlpha = 1
      for (let i = 0; i < count; i++) {
        const availability = data[i]
        if (!Number.isFinite(availability) || availability <= 0) continue
        const x = xMin + i * valueWidth + barOffset
        const w = barWidth
        const h = yRange * availability
        const y = barBottom - h
        /* 3-sided outline: left, top, right (no bottom — matches dev) */
        context.beginPath()
        context.moveTo(x, barBottom)
        context.lineTo(x, y)
        context.lineTo(x + w, y)
        context.lineTo(x + w, barBottom)
        context.stroke()
      }
    }

    /* x-axis date labels */
    const beginIso = this.begin()
    const endIso = this.end()
    if (beginIso && endIso) {
      const beginTicks = toTicks(beginIso)
      const endTicks = toTicks(endIso)
      if (beginTicks !== null && endTicks !== null && endTicks > beginTicks) {
        const maxTicks = Math.max(1, roundAway(xRange / 200))
        const { config, ticks } = getTimeTicks(beginTicks, endTicks, maxTicks)
        let previous = 0n
        context.fillStyle = labelColor
        context.strokeStyle = tickColor
        context.textAlign = 'center'
        context.textBaseline = 'alphabetic'
        for (const tick of ticks) {
          const x = xMin + Number(tick - beginTicks) / Number(endTicks - beginTicks) * xRange
          context.fillText(formatTime(tick, config.fast), x, yMax - 26)
          if (isSlowTickRequired(previous, tick, config.trigger)) {
            if (config.slow1) context.fillText(formatTime(tick, config.slow1), x, yMax - 8)
            if (config.slow2) context.fillText(formatTime(tick, config.slow2), x, yMax - 8)
          }
          previous = tick
        }
      }
    }
  }
}

function toTicks(iso: string): bigint | null {
  const ms = Date.parse(iso)
  if (!Number.isFinite(ms)) return null
  return BigInt(Math.round(ms)) * 10_000n + UNIX_EPOCH_TICKS
}

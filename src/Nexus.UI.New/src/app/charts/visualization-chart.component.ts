import { AfterViewInit, ChangeDetectionStrategy, ChangeDetectorRef, Component, ElementRef, Input, OnChanges, OnDestroy, ViewChild, inject, output } from '@angular/core';
import type { VisualizationData, VisualizationSeries } from './visualization-data.ts';
import { getChartInterop } from './chart-interop';
import type { ChartCallbacks, ChartCallbackAdapter, ChartInterop, GpuRange, SeriesPayload } from './chart-interop';
import { CHUNK_LENGTH, FULL_VIEWPORT, SERIES_COLORS, applyZoom, clamp, createAxis, detailWindow, formatDuration, formatRange, formatTime, getTimeTicks, getYTicks, isSlowTickRequired, roundAway, scaleTicks, setViewport, toEngineering, toTime } from './chart-math';
import type { Axis, Viewport } from './chart-math';
import { provideSeriesChunk, uploadSeries } from './chart-upload';
import { LegendNameDirective } from './legend-name.directive';
import { formatLegendValue } from './legend-text';

let nextChartId = 0;
type ThemeMode = 'dark' | 'light';

interface SeriesState {
  source: VisualizationSeries;
  version: number;
  range?: GpuRange;
  task?: Promise<void>;
}

@Component({
  selector: 'nexus-visualization-chart',
  standalone: true,
  imports: [LegendNameDirective],
  templateUrl: './visualization-chart.component.html',
  styleUrl: './visualization-chart.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VisualizationChartComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input() data: VisualizationData | null = null;
  @Input() beginAtZero = false;
  @Input() cacheBudgetBytes = 2048 * 1024 * 1024;
  @Input() themeMode: ThemeMode = 'dark';
  readonly gpuFailed = output<string>();
  @ViewChild('axisCanvas') private axisCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('chartElement') private chartElement!: ElementRef<HTMLElement>;

  chartId = `angular-${++nextChartId}`;
  viewport: Viewport = { ...FULL_VIEWPORT };
  errorTitle: string | null = null;
  errorMessage = '';
  readonly hidden = new Set<string>();
  private readonly changeDetector = inject(ChangeDetectorRef);
  private api?: ChartInterop;
  private ready = false;
  private disposed = false;
  private activeData: VisualizationData | null = null;
  private controller = new AbortController();
  private states = new Map<string, SeriesState>();
  private axes = new Map<string, Axis>();
  private frame = 0;
  private pollTimer: ReturnType<typeof setTimeout> | undefined;
  private resizeObserver?: ResizeObserver;
  private dprQuery?: MediaQueryList;
  private readonly onResize = (): void => this.scheduleDraw();
  private readonly onDprChange = (): void => { this.watchDpr(); this.scheduleDraw(); };

  get series(): VisualizationSeries[] { return this.data?.series ?? []; }
  get detail(): ReturnType<typeof detailWindow> { return detailWindow(this.viewport.left, this.viewport.right); }
  get minimumHorizontalZoom(): number { return 1 / Math.max(1, Number(this.duration)); }
  private get duration(): bigint { return this.data ? this.data.end - this.data.begin : 0n; }
  get zoomedBegin(): bigint { return this.data ? toTime(this.data.begin, this.data.end, this.viewport.left) : 0n; }
  get zoomedEnd(): bigint { return this.data ? toTime(this.data.begin, this.data.end, this.viewport.right) : 0n; }
  get durationLabel(): string { return formatDuration(this.zoomedEnd - this.zoomedBegin); }
  get rangeLabel(): string { return formatRange(this.zoomedBegin, this.zoomedEnd); }
  get detailRangeLabel(): string {
    return this.data ? formatRange(toTime(this.data.begin, this.data.end, this.detail.left), toTime(this.data.begin, this.data.end, this.detail.right)) : '';
  }
  get beginIso(): string { return this.data ? formatTime(this.data.begin, 'yyyy-MM-ddTHH:mm:ss.fffffff') : ''; }
  get endIso(): string { return this.data ? formatTime(this.data.end, 'yyyy-MM-ddTHH:mm:ss.fffffff') : ''; }
  color(index: number): string { return `rgb(${SERIES_COLORS[index % SERIES_COLORS.length].join(', ')})`; }

  ngAfterViewInit(): void {
    this.ready = true;
    this.resizeObserver = new ResizeObserver(this.onResize);
    this.resizeObserver.observe(this.chartElement.nativeElement);
    window.addEventListener('resize', this.onResize);
    this.watchDpr();
    // Defer initialization until Angular has committed all callback target elements.
    this.scheduleDraw();
    void this.loadFont();
  }

  ngOnChanges(): void {
    if (this.disposed) return;
    if (this.data !== this.activeData) {
      this.cancelSession();
      this.api?.chart.clearAuxiliary(this.chartId);
      this.api?.chart.dispose(this.chartId);
      this.api?.chartWebGpu.dispose(this.chartId);
      this.api = undefined;
      this.chartId = `angular-${++nextChartId}`;
      this.activeData = this.data;
      this.states.clear();
      this.hidden.clear();
      this.viewport = { ...FULL_VIEWPORT };
      this.errorTitle = null;
    }
    // A same-reference input update is also used for settings and stream progress.
    // Neither changes the source identity or invalidates GPU-resident overviews.
    this.rebuildAxes();
    this.scheduleDraw();
  }

  private async loadFont(): Promise<void> {
    try {
      await document.fonts.load('bold 12px "Nexus Chart"');
      if (!this.disposed) this.scheduleDraw();
    } catch (error) {
      if (!this.disposed) console.warn('[chart] Chart font unavailable; using Courier New Bold.', error);
    }
  }

  private watchDpr(): void {
    this.dprQuery?.removeEventListener('change', this.onDprChange);
    this.dprQuery = window.matchMedia(`(resolution: ${window.devicePixelRatio || 1}dppx)`);
    this.dprQuery.addEventListener('change', this.onDprChange);
  }

  private initialize(): void {
    if (this.api) return;
    const api = getChartInterop();
    const id = this.chartId;
    const adapter: ChartCallbackAdapter = {
      invokeMethodAsync: async <K extends keyof ChartCallbacks>(method: K, ...args: ChartCallbacks[K]): Promise<void> => {
        if (this.disposed || this.chartId !== id) return;
        switch (method) {
          case 'PointerMoved': { const [x, y] = args as ChartCallbacks['PointerMoved']; this.drawAuxiliary(x, y); break; }
          case 'WheelZoom': { const [x, y, delta, shift] = args as ChartCallbacks['WheelZoom']; this.wheelZoom(x, y, delta, shift); break; }
          case 'DragZoom': { const [left, top, right, bottom] = args as ChartCallbacks['DragZoom']; this.commitViewport(applyZoom(this.viewport, { left, top, right, bottom }, this.duration)); break; }
          case 'NavigatorZoom': { const [left, right] = args as ChartCallbacks['NavigatorZoom']; this.commitViewport(setViewport({ ...this.viewport, left, right }, this.duration)); break; }
          case 'SetViewport': { const [left, top, right, bottom] = args as ChartCallbacks['SetViewport']; this.commitViewport(setViewport({ left, top, right, bottom }, this.duration)); break; }
          case 'WebGpuFailed': { const [title, message] = args as ChartCallbacks['WebGpuFailed']; this.fail(title, message); break; }
          case 'ProvideSeriesChunk': {
            const [seriesId, offset, count, requestId] = args as ChartCallbacks['ProvideSeriesChunk'];
            const source = this.states.get(seriesId)?.source;
            if (!source) throw Object.assign(new Error('Raw series request is no longer active.'), { webGpuCancelled: true });
            await provideSeriesChunk(api.chartWebGpu, id, source, offset, count, requestId, this.controller.signal);
            break;
          }
        }
      },
    };
    api.chartWebGpu.setCacheBudget(id, this.cacheBudgetBytes);
    this.api = api;
    api.chartWebGpu.initialize(id, adapter);
    api.chart.initInteractions(id, adapter);
  }

  private cancelSession(): void {
    this.controller.abort(Object.assign(new Error('Chart data or GPU session was superseded.'), { webGpuCancelled: true }));
    this.controller = new AbortController();
    if (this.pollTimer !== undefined) clearTimeout(this.pollTimer);
    this.pollTimer = undefined;
  }

  private fail(title: string, message: string): void {
    if (this.disposed) return;
    this.cancelSession();
    this.errorTitle = title;
    this.errorMessage = message;
    this.api?.chart.clearAuxiliary(this.chartId);
    this.changeDetector.markForCheck();
    this.gpuFailed.emit(`${title}: ${message}`);
  }

  private ensureUploads(): void {
    if (!this.api || !this.data || this.errorTitle) return;
    const api = this.api.chartWebGpu;
    const id = this.chartId;
    const signal = this.controller.signal;
    api.synchronizeSeries(id, this.series.map(series => series.id));
    for (const series of this.series) {
      // Version is a publication counter, not a new dataset identity. Freeze the GPU
      // key for this append-only source so each CPU chunk is transmitted only once.
      if (this.states.has(series.id)) continue;
      const state: SeriesState = { source: series, version: series.version };
      this.states.set(series.id, state);
      state.task = uploadSeries(api, id, series, state.version, signal).then(range => {
        if (signal.aborted || this.disposed || id !== this.chartId) return;
        state.range = range;
        this.rebuildAxes();
        this.scheduleDraw();
      }).catch((error: unknown) => {
        if (!signal.aborted && !this.disposed && id === this.chartId) this.fail('WebGPU upload failed', String(error));
      });
    }
    this.rebuildAxes();
    // Parent input identity need not change when chunks are published. Upload waits
    // poll independently; this timer also discovers appended series and completion.
    if (this.pollTimer === undefined && this.series.some(series => !series.complete)) {
      this.pollTimer = setTimeout(() => {
        this.pollTimer = undefined;
        if (!signal.aborted) { this.changeDetector.markForCheck(); this.scheduleDraw(); }
      }, 100);
    }
  }

  private rebuildAxes(): void {
    this.axes.clear();
    for (const series of this.series) {
      if (this.axes.has(series.unit)) continue;
      const ranges = this.series.filter(item => item.unit === series.unit).map(item => this.states.get(item.id)?.range).filter((range): range is GpuRange => !!range?.hasValue);
      this.axes.set(series.unit, createAxis(series.unit, ranges.length ? Math.min(...ranges.map(range => range.minimum)) : 0, ranges.length ? Math.max(...ranges.map(range => range.maximum)) : 0, this.beginAtZero, this.viewport));
    }
  }

  toggleSeries(series: VisualizationSeries): void {
    if (!this.hidden.delete(series.id)) this.hidden.add(series.id);
    this.api?.chart.clearAuxiliary(this.chartId);
    this.scheduleDraw();
  }

  resetZoom(event?: MouseEvent): void {
    this.commitViewport({ ...FULL_VIEWPORT });
    if (event && this.api) {
      const point = this.api.chart.toRelative(this.chartId, event.clientX, event.clientY);
      this.drawAuxiliary(point.x, point.y);
    }
  }

  private wheelZoom(x: number, y: number, delta: number, shift: boolean): void {
    const factor = delta < 0 ? 0.15 : -0.15;
    this.commitViewport(applyZoom(this.viewport, { left: shift ? 0 : x * factor, right: shift ? 1 : 1 - (1 - x) * factor, top: shift ? y * factor : 0, bottom: shift ? 1 - (1 - y) * factor : 1 }, this.duration));
    this.drawAuxiliary(x, y);
  }

  private commitViewport(next: Viewport | null): void {
    if (!next || this.errorTitle || !this.data) return;
    this.viewport = next;
    this.rebuildAxes();
    // Keep the readout visible until the post-zoom pointer refresh replaces it.
    this.changeDetector.markForCheck();
    this.scheduleDraw();
  }

  private drawAuxiliary(x: number, y: number): void {
    if (!this.api || !this.data || this.errorTitle || !Number.isFinite(x) || !Number.isFinite(y)) return;
    const time = this.zoomedBegin + scaleTicks(this.zoomedEnd - this.zoomedBegin, x);
    const updates = this.series.map(series => {
      const axis = this.axes.get(series.unit)!;
      const step = Number(series.samplePeriod) / Number(this.duration);
      const width = this.viewport.right - this.viewport.left;
      const index = roundAway((this.viewport.left + x * width) / step);
      const value = index >= 0 && index < series.availableLength ? series.chunks[Math.floor(index / CHUNK_LENGTH)]?.[index % CHUNK_LENGTH] : undefined;
      const pointX = (index * step - this.viewport.left) / width;
      const pointY = value === undefined ? NaN : (value - axis.min) / (axis.max - axis.min);
      const visible = !this.hidden.has(series.id) && Number.isFinite(pointX) && pointX >= 0 && pointX <= 1 && Number.isFinite(pointY) && pointY >= 0 && pointY <= 1;
      const digits = clamp(-roundAway(Math.log10(axis.max - axis.min)) + 2, 0, 100);
      const text = visible ? formatLegendValue(value!, digits) : '--';
      return { id: series.id, visible, x: pointX, y: 1 - pointY, text };
    });
    this.api.chart.updateAuxiliary(this.chartId, x, y, formatTime(time), updates);
  }

  private scheduleDraw(): void {
    if (!this.ready || this.disposed || this.frame) return;
    this.frame = requestAnimationFrame(() => {
      this.frame = 0;
      if (this.disposed) return;
      try {
        this.changeDetector.detectChanges();
        this.initialize();
        this.api!.chartWebGpu.setCacheBudget(this.chartId, this.cacheBudgetBytes);
        this.ensureUploads();
        this.draw();
      } catch (error) { this.fail('Chart rendering failed', String(error)); }
    });
  }

  private draw(): void {
    const canvas = this.axisCanvas.nativeElement;
    const width = canvas.clientWidth;
    const height = canvas.clientHeight;
    if (width <= 0 || height <= 0) return;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.max(1, Math.round(width * dpr));
    canvas.height = Math.max(1, Math.round(height * dpr));
    const context = canvas.getContext('2d');
    if (!context) throw new Error('The browser could not create the chart axis canvas.');
    context.scale(dpr, dpr);
    context.font = 'bold 12px "Nexus Chart", "Courier New", monospace';
    const lightTheme = this.themeMode === 'light';
    context.fillStyle = lightTheme ? '#334155' : '#94a3b8';
    const yMin = 20;
    const yMax = Math.max(51, height - 55);
    const plotTop = 50;
    const maxYTicks = Math.max(1, roundAway((yMax - yMin) / 50));
    const characterWidth = context.measureText(' ').width;
    let xMin = 10;
    const line = (x1: number, y1: number, x2: number, y2: number): void => { context.beginPath(); context.moveTo(x1, y1); context.lineTo(x2, y2); context.stroke(); };
    for (const axis of this.axes.values()) {
      const ticks = getYTicks(axis.min, axis.max, maxYTicks);
      const labels = ticks.map(toEngineering);
      const maxChars = Math.max(axis.unit.length, ...labels.map(label => label.length));
      const textWidth = characterWidth * maxChars;
      if (this.series.some(series => series.unit === axis.unit && !this.hidden.has(series.id))) {
        context.fillText(axis.unit, xMin + (maxChars - axis.unit.length) * characterWidth, yMin);
        context.strokeStyle = lightTheme ? '#dddddd' : 'rgba(148, 163, 184, 0.25)';
        ticks.forEach((tick, index) => {
          if (tick < axis.min || tick > axis.max) return;
          const y = yMax - (tick - axis.min) * (yMax - plotTop) / (axis.max - axis.min);
          context.fillText(labels[index], xMin + (maxChars - labels[index].length) * characterWidth, y + 3.5);
          line(xMin + textWidth + 5, y, xMin + textWidth + 15, y);
        });
      }
      // Hidden unit groups retain their space, as in the original chart.
      xMin += textWidth + 20;
    }
    xMin = clamp(xMin - 5, 0, Math.max(0, width - 1));
    const xMax = Math.max(xMin + 1, width - 8);
    if (this.data && this.duration > 0n) {
      const begin = this.zoomedBegin;
      const end = this.zoomedEnd;
      const { config, ticks } = getTimeTicks(begin, end, Math.max(1, roundAway((xMax - xMin) / 130)));
      let previous = 0n;
      context.textAlign = 'center';
      context.strokeStyle = lightTheme ? '#d3d3d3' : 'rgba(148, 163, 184, 0.25)';
      for (const tick of ticks) {
        const x = xMin + Number(tick - begin) / Number(end - begin) * (xMax - xMin);
        line(x, plotTop, x, yMax + 10);
        context.fillText(formatTime(tick, config.fast), x, yMax + 25);
        if (isSlowTickRequired(previous, tick, config.trigger)) {
          if (config.slow1) context.fillText(formatTime(tick, config.slow1), x, yMax + 40);
          if (config.slow2) context.fillText(formatTime(tick, config.slow2), x, yMax + 55);
        }
        previous = tick;
      }
    }
    const plot = { left: xMin / width, top: Math.min(1, plotTop / height), right: Math.min(1, xMax / width), bottom: Math.min(1, yMax / height) };
    this.api!.chart.resize(this.chartId, 'overlay', plot.left, plot.top, plot.right, plot.bottom);
    if (this.errorTitle) return;
    const series: SeriesPayload[] = this.series.flatMap((source, index) => {
      if (this.hidden.has(source.id)) return [];
      const state = this.states.get(source.id);
      const axis = this.axes.get(source.unit)!;
      const [red, green, blue] = SERIES_COLORS[index % SERIES_COLORS.length];
      return [{ id: source.id, show: true, color: { red, green, blue, alpha: 255 }, axisMin: axis.min, axisMax: axis.max, overviewAxisMin: axis.originalMin, overviewAxisMax: axis.originalMax, dataVersion: state?.version ?? source.version, length: source.length, sampleStep: this.duration > 0n ? Number(source.samplePeriod) / Number(this.duration) : 0 }];
    });
    const gpu = this.api!.chartWebGpu;
    gpu.renderSeries(this.chartId, { plot, zoom: this.viewport, lineWidth: 0.7, fillOpacity: 0.10, series });
    gpu.renderSeries(this.chartId, { target: 'navigator-overview-series', preview: true, plot: FULL_VIEWPORT, zoom: { left: 0, right: 1 }, lineWidth: 0.65, fillOpacity: 0.08, series });
    if (this.detail.visible) gpu.renderSeries(this.chartId, { target: 'navigator-detail-series', preview: true, plot: FULL_VIEWPORT, zoom: this.detail, lineWidth: 0.65, fillOpacity: 0.08, series });
    else gpu.releaseTarget(this.chartId, 'navigator-detail-series');
  }

  ngOnDestroy(): void {
    this.disposed = true;
    this.cancelSession();
    cancelAnimationFrame(this.frame);
    this.resizeObserver?.disconnect();
    window.removeEventListener('resize', this.onResize);
    this.dprQuery?.removeEventListener('change', this.onDprChange);
    this.api?.chart.dispose(this.chartId);
    this.api?.chartWebGpu.dispose(this.chartId);
    this.states.clear();
    this.axes.clear();
    this.activeData = null;
    this.data = null;
  }
}

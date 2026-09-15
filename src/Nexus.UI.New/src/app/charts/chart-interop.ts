export interface ChartCallbacks {
  PointerMoved: [x: number, y: number];
  WheelZoom: [x: number, y: number, deltaY: number, shiftKey: boolean];
  DragZoom: [left: number, top: number, right: number, bottom: number];
  NavigatorZoom: [left: number, right: number];
  SetViewport: [left: number, top: number, right: number, bottom: number];
  ProvideSeriesChunk: [seriesId: string, offset: number, count: number, requestId: number];
  WebGpuFailed: [title: string, message: string];
}

export interface ChartCallbackAdapter {
  invokeMethodAsync<K extends keyof ChartCallbacks>(method: K, ...args: ChartCallbacks[K]): Promise<void>;
}

export interface GpuRange { hasValue: boolean; minimum: number; maximum: number }
export interface AuxiliaryUpdate { id: string; visible: boolean; x: number; y: number; text: string }
export interface SeriesPayload {
  id: string;
  show: boolean;
  color: { red: number; green: number; blue: number; alpha: number };
  axisMin: number;
  axisMax: number;
  overviewAxisMin: number;
  overviewAxisMax: number;
  dataVersion: number;
  length: number;
  sampleStep: number;
}
export interface RenderPayload {
  target?: string;
  preview?: boolean;
  plot: { left: number; top: number; right: number; bottom: number };
  zoom: { left: number; right: number };
  lineWidth: number;
  fillOpacity: number;
  series: SeriesPayload[];
}
export interface ChartInterop {
  chart: {
    initInteractions(id: string, helper: ChartCallbackAdapter): void;
    dispose(id: string): void;
    resize(id: string, element: string, left: number, top: number, right: number, bottom: number): void;
    toRelative(id: string, clientX: number, clientY: number): { x: number; y: number };
    updateAuxiliary(id: string, x: number, y: number, time: string, updates: AuxiliaryUpdate[]): void;
    clearAuxiliary(id: string): void;
  };
  chartWebGpu: {
    initialize(id: string, helper: ChartCallbackAdapter): void;
    setCacheBudget(id: string, bytes: number): boolean;
    synchronizeSeries(id: string, activeIds: string[]): void;
    beginChunkedSeries(id: string, seriesId: string, version: number, length: number): Promise<number>;
    appendChunkedSeries(id: string, token: number, offset: number, data: Float32Array, sampleCount: number): void;
    processChunkedSeriesUpload(id: string, token: number, offset: number, count: number): Promise<void>;
    completeChunkedSeries(id: string, token: number): Promise<GpuRange>;
    abortChunkedSeries(id: string, token: number): void;
    appendSeriesChunk(id: string, requestId: number, offset: number, data: Float32Array, sampleCount: number): void;
    renderSeries(id: string, payload: RenderPayload): void;
    releaseTarget(id: string, target: string): void;
    retry(id: string): Promise<boolean>;
    dispose(id: string): void;
  };
}

export function getChartInterop(): ChartInterop {
  const nexus = (window as Window & { nexus?: ChartInterop }).nexus;
  if (!nexus?.chart || !nexus.chartWebGpu) throw new Error('The Nexus chart scripts have not been loaded.');
  return nexus;
}

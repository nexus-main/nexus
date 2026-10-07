import { AsyncByteStream, RecordBatchReader } from "apache-arrow";
import type { Schema } from "apache-arrow";
import { formatTime } from "./chart-math.ts";
import type { GpuRange } from "./chart-interop.ts";
import { dateTicks } from "../resource-selection.ts";
import { ExactCursor, type CursorTransport } from "./exact-cursor.ts";

export interface VisualizationView {
  id: string;
  begin: string;
  end: string;
  maxPoints: number;
}

export function visualizationPointBudget(width: number, resourceCount: number, dpr = 1): number {
  // Four buckets per physical pixel allow twofold LOD rounding while retaining
  // two buckets per pixel. Reserve five slots per bucket plus boundary capacity.
  // Server per-view and three-view aggregate limits still take precedence.
  return Math.max(
    16,
    Math.min(32768, 5 * (Math.ceil(width * dpr * 4) + 1), Math.floor(262144 / (3 * resourceCount))),
  );
}

export interface VisualizationRequest {
  begin: string;
  end: string;
  resourcePaths: string[];
  views: VisualizationView[];
}

export interface VisualizationPoints {
  resourceIndex: number;
  viewIndex: number;
  indices: BigInt64Array;
  values: Float32Array;
}

export type VisualizationFrame =
  | ({ kind: "data"; complete: boolean } & VisualizationPoints)
  | { kind: "progress"; progress: number; resourceIndex: number; range?: GpuRange };

const columns = [
  ["kind", "Int32"],
  ["resourceIndex", "Int32"],
  ["viewIndex", "Int32"],
  ["offset", "Int64"],
  ["indices", "List<Int64>"],
  ["values", "List<Float32>"],
  ["progress", "Float64"],
  ["minimum", "Float32"],
  ["maximum", "Float32"],
  ["message", "Utf8"],
];

function validateSchema(schema: Schema): void {
  if (
    schema.fields.length !== columns.length ||
    columns.some(([name, type], i) => {
      const field = schema.fields[i];

      return field.name !== name || field.type.toString() !== type;
    }) ||
    (schema.metadata.has("visualizationVersion") &&
      schema.metadata.get("visualizationVersion") !== "1")
  ) {
    throw new Error("Invalid visualization Arrow schema.");
  }
}

export async function decodeVisualization(
  response: Response,
  request: VisualizationRequest,
  sampleCount: bigint,
  signal: AbortSignal,
  onFrame: (frame: VisualizationFrame) => void,
): Promise<void> {
  const stream = response.body?.getReader();

  if (!stream) {
    throw new Error("The visualization response has no body.");
  }

  let reader: RecordBatchReader | undefined;
  let complete = false;
  const completedViews = new Set<string>();
  const cancel = () => {
    void stream.cancel(signal.reason).catch(() => {});
  };

  signal.addEventListener("abort", cancel, { once: true });

  try {
    const domainBegin = dateTicks(request.begin);
    const domainEnd = dateTicks(request.end);

    if (
      domainBegin === null ||
      domainEnd === null ||
      domainEnd <= domainBegin ||
      sampleCount <= 0n ||
      request.views.some(
        (view) => !Number.isInteger(view.maxPoints) || view.maxPoints < 5 || view.maxPoints > 32768,
      )
    ) {
      throw new Error("Invalid visualization request bounds.");
    }

    const period = (domainEnd - domainBegin) / sampleCount;
    const bounds = request.views.map((view) => {
      const begin = dateTicks(view.begin);
      const end = dateTicks(view.end);

      if (
        period <= 0n ||
        begin === null ||
        end === null ||
        begin < domainBegin ||
        end > domainEnd ||
        begin >= end
      ) {
        throw new Error("Invalid visualization view bounds.");
      }

      return {
        first: (begin - domainBegin) / period,
        end: (end - domainBegin + period - 1n) / period,
      };
    });

    async function* bytes(): AsyncGenerator<Uint8Array> {
      while (true) {
        signal.throwIfAborted();
        const result = await stream!.read();

        signal.throwIfAborted();

        if (result.done) {
          return;
        }

        yield result.value;
      }
    }

    reader = await RecordBatchReader.from(new AsyncByteStream(bytes()));
    await reader.open({ autoDestroy: false });
    signal.throwIfAborted();
    validateSchema(reader.schema);

    for await (const batch of reader) {
      validateSchema(batch.schema);

      for (let row = 0; row < batch.numRows; row++) {
        signal.throwIfAborted();

        if (complete) {
          throw new Error("Visualization records follow completion.");
        }

        const get = (column: number) => batch.getChildAt(column)!.get(row);
        const kind = get(0);
        const resourceIndex = get(1);
        const viewIndex = get(2);

        if (
          !Number.isInteger(resourceIndex) ||
          resourceIndex < -1 ||
          resourceIndex >= request.resourcePaths.length ||
          !Number.isInteger(viewIndex) ||
          viewIndex < -1 ||
          viewIndex >= request.views.length
        ) {
          throw new Error("Invalid visualization resource/view index.");
        }

        if (kind === 3) {
          throw new Error(get(9) || "Server visualization failed.");
        }

        if (kind === 2) {
          complete = true;
        } else if (kind === 1) {
          const progress = get(6);
          const minimum = get(7);
          const maximum = get(8);

          if (!Number.isFinite(progress) || progress < 0 || progress > 1) {
            throw new Error("Invalid visualization progress.");
          }

          const range =
            Number.isFinite(minimum) && Number.isFinite(maximum) && minimum <= maximum
              ? { hasValue: true, minimum, maximum }
              : undefined;

          onFrame({ kind: "progress", progress, resourceIndex, range });
        } else if (kind === 0) {
          const indices = get(4);
          const values = get(5);
          const message = get(9);
          const key = `${resourceIndex}:${viewIndex}`;

          if (
            resourceIndex < 0 ||
            viewIndex < 0 ||
            typeof get(3) !== "bigint" ||
            get(3) < 0n ||
            get(3) > sampleCount ||
            !indices ||
            !values ||
            indices.nullCount ||
            values.nullCount ||
            indices.length !== values.length ||
            indices.length > request.views[viewIndex].maxPoints ||
            (message !== "partial" && message !== "complete") ||
            completedViews.has(key)
          ) {
            throw new Error("Invalid visualization replacement points.");
          }

          const xs = new BigInt64Array(indices.length);
          const ys = new Float32Array(values.length);
          let previous = -1n;

          for (let i = 0; i < indices.length; i++) {
            const index = indices.get(i);

            if (
              typeof index !== "bigint" ||
              index <= previous ||
              index < bounds[viewIndex].first ||
              index >= bounds[viewIndex].end ||
              index >= sampleCount
            ) {
              throw new Error("Invalid visualization sample coordinate.");
            }

            previous = index;
            xs[i] = index;
            ys[i] = values.get(i);
          }

          if (message === "complete") {
            completedViews.add(key);
          }

          onFrame({
            kind: "data",
            complete: message === "complete",
            resourceIndex,
            viewIndex,
            indices: xs,
            values: ys,
          });
        } else {
          throw new Error("Unknown visualization record kind.");
        }
      }
    }

    signal.throwIfAborted();

    if (!complete || completedViews.size !== request.resourcePaths.length * request.views.length) {
      throw new Error("Visualization stream ended before explicit completion of all views.");
    }
  } finally {
    signal.removeEventListener("abort", cancel);
    cancel();

    try {
      await reader?.cancel();
    } finally {
      stream.releaseLock();
    }
  }
}

export type VisualizationTransport = (
  request: VisualizationRequest,
  signal: AbortSignal,
  onFrame: (frame: VisualizationFrame) => void,
) => Promise<void>;

interface CachedView {
  begin: bigint;
  end: bigint;
  budget: number;
  points: VisualizationPoints[];
  prefetched?: boolean;
}

interface RequestedView {
  id: string;
  begin: bigint;
  end: bigint;
  maxPoints: number;
}

export function positionVisualizationPoints(
  points: VisualizationPoints,
  domainBegin: bigint,
  samplePeriod: bigint,
  begin: bigint,
  end: bigint,
): Float32Array {
  const result = new Float32Array(points.values.length * 2);

  for (let i = 0; i < points.values.length; i++) {
    // Subtract integer ticks BEFORE Float32 conversion, including for deep zoom.
    result[i * 2] =
      Number(domainBegin + points.indices[i] * samplePeriod - begin) / Number(end - begin);

    result[i * 2 + 1] = points.values[i];
  }

  return result;
}

export class RemoteVisualization {
  readonly cursor?: ExactCursor;
  loading = false;
  progress = 0;
  error = "";
  readonly ranges = new Map<number, GpuRange>();
  private readonly listeners = new Set<() => void>();
  private readonly cache = new Map<string, CachedView>();
  private readonly previews = new Map<number, CachedView>();
  private readonly positioned = new Map<
    string,
    { source: VisualizationPoints; begin: bigint; end: bigint; points: Float32Array }
  >();
  private controller?: AbortController;
  private timer?: ReturnType<typeof setTimeout>;
  private generation = 0;
  private viewportKey = "";
  private mainView?: RequestedView;
  private requestedViews: RequestedView[] = [];
  private zoomAnchor = 500000n;
  private readonly protectedViews = new Set<CachedView>();
  readonly begin: bigint;
  readonly end: bigint;
  readonly samplePeriod: bigint;
  readonly resourcePaths: string[];
  private readonly transport: VisualizationTransport;

  constructor(
    begin: bigint,
    end: bigint,
    samplePeriod: bigint,
    resourcePaths: string[],
    transport: VisualizationTransport,
    cursorTransport?: CursorTransport,
  ) {
    this.begin = begin;
    this.end = end;
    this.samplePeriod = samplePeriod;
    this.resourcePaths = resourcePaths;
    this.transport = transport;

    if (cursorTransport) {
      this.cursor = new ExactCursor(begin, end, samplePeriod, resourcePaths, cursorTransport, () =>
        this.notify(),
      );
    }
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener);

    return () => {
      this.listeners.delete(listener);
    };
  }

  private notify(): void {
    for (const listener of this.listeners) {
      listener();
    }
  }

  cancel(): void {
    this.generation++;
    clearTimeout(this.timer);
    this.controller?.abort();
    this.controller = undefined;
    this.previews.clear();
    this.protectedViews.clear();
    this.requestedViews = [];
    this.loading = false;
    this.viewportKey = "";
    this.notify();
  }

  dispose(): void {
    this.cursor?.dispose();
    this.cancel();
    this.listeners.clear();
    this.cache.clear();
    this.positioned.clear();
  }

  requestViews(views: RequestedView[], immediate = false): void {
    const keyOf = (view: (typeof views)[number]) => `${view.begin}:${view.end}:${view.maxPoints}`;
    const key = views.map(keyOf).join("|");

    if (key === this.viewportKey) {
      return;
    }

    this.cancel();
    this.viewportKey = key;
    this.requestedViews = views;
    this.error = "";
    const main = views.find((view) => view.id === "main");

    if (main && this.mainView) {
      const difference = this.mainView.end - this.mainView.begin - (main.end - main.begin);

      // Infer the stationary zoom anchor using integer ticks, not absolute floats.
      this.zoomAnchor =
        difference === 0n ? 500000n : ((main.begin - this.mainView.begin) * 1000000n) / difference;

      this.zoomAnchor =
        this.zoomAnchor < 0n ? 0n : this.zoomAnchor > 1000000n ? 1000000n : this.zoomAnchor;
    }

    this.mainView = main;
    this.protectedViews.clear();
    const missing = views.filter((view) => {
      const cached = this.cachedView(view);

      // Protect current fallback coverage too while speculative entries are added.
      const fallback =
        cached ??
        [...this.cache.values()]
          .filter((entry) => entry.begin <= view.begin && entry.end >= view.end)
          .sort((a, b) =>
            Number(
              this.strideOf(a.begin, a.end, a.budget) - this.strideOf(b.begin, b.end, b.budget),
            ),
          )[0];

      if (fallback) {
        this.protectedViews.add(fallback);
      }

      if (cached) {
        cached.prefetched = false;
        const cacheKey = `${cached.begin}:${cached.end}:${cached.budget}`;

        this.cache.delete(cacheKey);
        this.cache.set(cacheKey, cached);
      }

      return !cached;
    });
    const generation = this.generation;

    if (!missing.length) {
      this.notify();
      this.schedulePrefetch(main, generation);

      return;
    }

    this.loading = true;
    this.progress = 0;

    this.notify();

    this.timer = setTimeout(
      () => {
        void this.load(missing, generation).then((success) => {
          if (success) {
            this.schedulePrefetch(main, generation);
          }
        });
      },
      immediate ? 0 : 150,
    );
  }

  private strideOf(begin: bigint, end: bigint, budget: number): bigint {
    // Compare actual canonical LOD, not nominal points per tick: rounding can
    // put two similarly budgeted views on opposite sides of a stride boundary.
    const from = begin / this.samplePeriod;
    const to = end / this.samplePeriod;
    let stride = 1n;

    if (to - from > BigInt(budget)) {
      const capacity = BigInt(Math.floor(budget / 5));

      while (
        (capacity >= 2n
          ? (to - from - 2n) / stride + 2n
          : (to - 1n) / stride - from / stride + 1n) > capacity
      ) {
        stride *= 2n;
      }
    }

    return stride;
  }

  private cachedView(view: RequestedView): CachedView | undefined {
    const stride = this.strideOf(view.begin, view.end, view.maxPoints);

    return [...this.cache.values()].find((entry) => {
      const cachedStride = this.strideOf(entry.begin, entry.end, entry.budget);

      return cachedStride <= stride && this.coversBoundaries(entry, view.begin, view.end);
    });
  }

  private coversBoundaries(entry: CachedView, begin: bigint, end: bigint): boolean {
    const stride = this.strideOf(entry.begin, entry.end, entry.budget);

    // Clipped reduced buckets require fresh boundary summaries, especially
    // when a containing multi-gap bucket hid otherwise finite samples.
    return (
      entry.begin <= begin &&
      entry.end >= end &&
      (entry.begin === begin || (begin / this.samplePeriod) % stride === 0n) &&
      (entry.end === end || (end / this.samplePeriod) % stride === 0n)
    );
  }

  private schedulePrefetch(main: RequestedView | undefined, generation: number): void {
    if (!main || generation !== this.generation) {
      return;
    }

    const period = this.samplePeriod;
    const length = (this.end - this.begin) / period;
    const span = (main.end - main.begin) / period;

    // Keep speculative queries above base-summary resolution; only the small
    // boundary fragments may require raw reads, never a whole fine-detail view.
    if (span <= (BigInt(main.maxPoints) * 256n) / 5n) {
      return;
    }

    const anchor = (main.begin - this.begin) / period + (span * this.zoomAnchor) / 1000000n;
    let remainingPoints = Math.floor(262144 / this.resourcePaths.length);
    const predictions: RequestedView[] = [];

    // Two zoom levels in each direction, plus 10% coverage on each side to
    // absorb rounding and slightly different pointer positions.
    for (const [numerator, denominator] of [
      [1n, 2n],
      [1n, 4n],
      [2n, 1n],
      [4n, 1n],
    ]) {
      const width = (span * numerator) / denominator;
      const padding = (width + 9n) / 10n + 1n;
      let first = anchor - (width * this.zoomAnchor) / 1000000n - padding;
      let last = first + width + 2n * padding;

      if (first < 0n) {
        last -= first;
        first = 0n;
      }

      if (last > length) {
        first = first > last - length ? first - (last - length) : 0n;
        last = length;
      }

      const targetStride = this.strideOf(0n, width * period, main.maxPoints);
      const budget = Math.max(main.maxPoints, 5 * Number((last - first - 2n) / targetStride + 2n));

      // Preserve the intended resolution despite padding. Skip rather than
      // fetching an unusably coarse prediction when request/cycle caps bind.
      if (
        budget > 32768 ||
        budget > remainingPoints ||
        (first === 0n &&
          last === length &&
          [...this.cache.values()].some(
            (entry) => entry.begin === this.begin && entry.end === this.end,
          ))
      ) {
        continue;
      }

      const view = {
        id: "prefetch",
        begin: this.begin + first * period,
        end: this.begin + last * period,
        maxPoints: budget,
      };

      if (
        last - first <= (BigInt(budget) * 256n) / 5n ||
        this.cachedView(view) ||
        predictions.some((other) => other.begin === view.begin && other.end === view.end)
      ) {
        continue;
      }

      predictions.push(view);
      remainingPoints -= budget;
    }

    this.timer = setTimeout(() => {
      void (async () => {
        for (const view of predictions) {
          if (generation !== this.generation) {
            break;
          }

          if (!this.cachedView(view)) {
            if (!(await this.load([view], generation, true)) || !this.cachedView(view)) {
              break;
            }
          }
        }
      })();
    }, 250);
  }

  private async load(
    views: RequestedView[],
    generation: number,
    prefetch = false,
  ): Promise<boolean> {
    const controller = new AbortController();

    this.controller = controller;
    const staged = new Map<string, VisualizationPoints>();
    const published = new Map<string, VisualizationPoints>();
    const iso = (ticks: bigint) => formatTime(ticks, "yyyy-MM-ddTHH:mm:ss.fffffff") + "Z";

    try {
      await this.transport(
        {
          begin: iso(this.begin),
          end: iso(this.end),
          resourcePaths: this.resourcePaths,
          views: views.map((view) => ({ ...view, begin: iso(view.begin), end: iso(view.end) })),
        },
        controller.signal,
        (frame) => {
          if (generation !== this.generation || controller.signal.aborted) {
            return;
          }

          if (frame.kind === "data") {
            published.set(`${frame.viewIndex}:${frame.resourceIndex}`, frame);

            if (frame.complete) {
              staged.set(`${frame.viewIndex}:${frame.resourceIndex}`, frame);
            }

            const view = views[frame.viewIndex];
            const points = this.resourcePaths.map((_, resourceIndex) =>
              published.get(`${frame.viewIndex}:${resourceIndex}`)!,
            );

            // Stream previews require coverage for every series. Cache publication
            // remains transactional until explicit stream completion.
            if (!prefetch && points.every(Boolean)) {
              this.previews.set(frame.viewIndex, { ...view, budget: view.maxPoints, points });
              this.notify();
            }
          } else if (!prefetch) {
            this.progress = Math.max(this.progress, frame.progress);

            if (frame.resourceIndex >= 0 && frame.range) {
              this.ranges.set(frame.resourceIndex, frame.range);
            }

            this.notify();
          }
        },
      );

      if (generation !== this.generation || controller.signal.aborted) {
        return false;
      }

      const completed = views.map((view, viewIndex) => {
        const points = this.resourcePaths.map((_, resourceIndex) =>
          staged.get(`${viewIndex}:${resourceIndex}`)!,
        );

        if (points.some((point) => !point)) {
          throw new Error("Missing completed visualization view.");
        }

        return { ...view, budget: view.maxPoints, points, prefetched: prefetch };
      });

      for (const view of completed) {
        if (view.begin === this.begin && view.end === this.end) {
          view.points.forEach((point, resourceIndex) => {
            if (this.ranges.has(resourceIndex)) {
              return;
            }

            let minimum = Infinity;
            let maximum = -Infinity;

            for (const value of point.values) {
              if (Number.isFinite(value)) {
                minimum = Math.min(minimum, value);
                maximum = Math.max(maximum, value);
              }
            }

            if (minimum <= maximum) {
              this.ranges.set(resourceIndex, { hasValue: true, minimum, maximum });
            }
          });
        }

        if (view.begin === this.begin && view.end === this.end) {
          for (const [key, old] of this.cache) {
            if (old.begin === this.begin && old.end === this.end) {
              this.cache.delete(key);
            }
          }
        }

        this.cache.set(`${view.begin}:${view.end}:${view.budget}`, view);

        if (!prefetch) {
          this.protectedViews.add(view);
        }
      }

      // Keep the full-domain fallback; bound recent detail by count and bytes.
      let bytes = [...this.cache.values()].reduce(
        (sum, view) =>
          sum +
          view.points.reduce(
            (n, point) => n + point.indices.byteLength + point.values.byteLength,
            0,
          ),
        0,
      );

      // Speculation must not evict the visible view or displace useful history.
      const priority = (view: CachedView) => (view.prefetched ? 0 : 1);
      const evictionOrder = [...this.cache].sort((a, b) => priority(a[1]) - priority(b[1]));

      for (const [key, view] of evictionOrder) {
        if (this.cache.size <= 8 && bytes <= 32 * 1024 * 1024) {
          break;
        }

        if ((view.begin === this.begin && view.end === this.end) || this.protectedViews.has(view)) {
          continue;
        }

        this.cache.delete(key);

        bytes -= view.points.reduce(
          (n, point) => n + point.indices.byteLength + point.values.byteLength,
          0,
        );
      }

      if (!prefetch) {
        this.progress = 1;
      }

      return true;
    } catch (error) {
      if (!prefetch && generation === this.generation && !controller.signal.aborted) {
        this.error = String(error);
        this.viewportKey = "";
      }

      return false;
    } finally {
      if (generation === this.generation) {
        this.previews.clear();
        this.loading = false;
        this.controller = undefined;
        this.notify();
      }
    }
  }

  pointsFor(
    target: string,
    resourceIndex: number,
    begin: bigint,
    end: bigint,
  ): Float32Array | undefined {
    // Use requested sample-aligned halo bounds when selecting completed coverage,
    // not the fractional viewport bounds used only for positioning the points.
    const requested = this.requestedViews.find(
      (view) => view.id === target && view.begin <= begin && view.end >= end,
    );
    const compatible = (view: CachedView) =>
      this.coversBoundaries(view, requested?.begin ?? begin, requested?.end ?? end);
    const candidates = [...this.previews.values(), ...this.cache.values()]
      .filter((view) => view.begin <= begin && view.end >= end)
      .sort(
        (a, b) =>
          Number(compatible(b)) - Number(compatible(a)) ||
          Number(
            this.strideOf(a.begin, a.end, a.budget) - this.strideOf(b.begin, b.end, b.budget),
          ) ||
          Number(a.end - a.begin - (b.end - b.begin)),
      );
    const source = candidates[0]?.points[resourceIndex];

    if (!source) {
      return undefined;
    }

    const key = `${target}:${resourceIndex}`;
    const cached = this.positioned.get(key);

    if (cached?.source === source && cached.begin === begin && cached.end === end) {
      return cached.points;
    }

    const points = positionVisualizationPoints(source, this.begin, this.samplePeriod, begin, end);

    this.positioned.set(key, { source, begin, end, points });

    return points;
  }
}

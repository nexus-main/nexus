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

export function visualizationPointBudget(width: number, resourceCount: number): number {
  // Reserve all three views within the server's default aggregate limit.
  return Math.max(
    16,
    Math.min(32768, Math.ceil(width * 4), Math.floor(262144 / (3 * resourceCount))),
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

  requestViews(
    views: { id: string; begin: bigint; end: bigint; maxPoints: number }[],
    immediate = false,
  ): void {
    const keyOf = (view: (typeof views)[number]) => `${view.begin}:${view.end}:${view.maxPoints}`;
    const key = views.map(keyOf).join("|");

    if (key === this.viewportKey) {
      return;
    }

    this.cancel();
    this.viewportKey = key;
    this.error = "";
    const missing = views.filter((view) => !this.cache.has(keyOf(view)));

    if (!missing.length) {
      this.notify();

      return;
    }

    this.loading = true;
    this.progress = 0;
    const generation = this.generation;

    this.notify();

    this.timer = setTimeout(
      () => {
        void this.load(missing, generation);
      },
      immediate ? 0 : 150,
    );
  }

  private async load(
    views: { id: string; begin: bigint; end: bigint; maxPoints: number }[],
    generation: number,
  ): Promise<void> {
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
            if (points.every(Boolean)) {
              this.previews.set(frame.viewIndex, { ...view, budget: view.maxPoints, points });
              this.notify();
            }
          } else {
            this.progress = Math.max(this.progress, frame.progress);

            if (frame.resourceIndex >= 0 && frame.range) {
              this.ranges.set(frame.resourceIndex, frame.range);
            }

            this.notify();
          }
        },
      );

      if (generation !== this.generation || controller.signal.aborted) {
        return;
      }

      const completed = views.map((view, viewIndex) => {
        const points = this.resourcePaths.map((_, resourceIndex) =>
          staged.get(`${viewIndex}:${resourceIndex}`)!,
        );

        if (points.some((point) => !point)) {
          throw new Error("Missing completed visualization view.");
        }

        return { ...view, budget: view.maxPoints, points };
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

      for (const [key, view] of this.cache) {
        if (this.cache.size <= 8 && bytes <= 32 * 1024 * 1024) {
          break;
        }

        if (view.begin === this.begin && view.end === this.end) {
          continue;
        }

        this.cache.delete(key);

        bytes -= view.points.reduce(
          (n, point) => n + point.indices.byteLength + point.values.byteLength,
          0,
        );
      }

      this.progress = 1;
    } catch (error) {
      if (generation === this.generation && !controller.signal.aborted) {
        this.error = String(error);
        this.viewportKey = "";
      }
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
    const candidates = [...this.previews.values(), ...this.cache.values()]
      .filter((view) => view.begin <= begin && view.end >= end)
      .sort((a, b) => Number(a.end - a.begin) / a.budget - Number(b.end - b.begin) / b.budget);
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

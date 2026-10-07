import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { setTimeout as delay } from "node:timers/promises";
import { describe, it } from "node:test";
import {
  decodeVisualization,
  positionVisualizationPoints,
  RemoteVisualization,
  visualizationPointBudget,
  type VisualizationFrame,
  type VisualizationRequest,
  type VisualizationTransport,
} from "../../../../src/Nexus.UI/src/app/charts/remote-visualization.ts";
import { requestError } from "../../../../src/Nexus.UI/src/app/request-error.ts";

it("keeps all views within the default server aggregate point budget", () => {
  for (let resources = 1; resources <= 100; resources++) {
    for (const width of [320, 1920, 3840, 8192]) {
      const budget = visualizationPointBudget(width, resources);

      assert.ok(budget >= 5 && budget <= 32768);
      assert.ok(budget * resources * 3 <= 262144);
    }
  }
});

it("shows the backend validation explanation instead of only HTTP 422", async () => {
  const response = new Response(
    "The visualization domain exceeds the configured summary-memory limit.",
    {
      status: 422,
      statusText: "Unprocessable Entity",
    },
  );
  const error = await requestError(response);

  assert.match(error.message, /422 Unprocessable Entity/);
  assert.match(error.message, /summary-memory limit/);
});

it("reads problem details and preserves status for empty error bodies", async () => {
  const error = await requestError(
    new Response(JSON.stringify({ title: "Invalid", detail: "Bad range" }), {
      status: 422,
      headers: { "Content-Type": "application/problem+json" },
    }),
  );

  assert.match(error.message, /Bad range/);
  assert.match((await requestError(new Response(null, { status: 503 }))).message, /503/);
});

const require = createRequire(new URL("../../../../src/Nexus.UI/package.json", import.meta.url));
const arrow: typeof import("../../../../src/Nexus.UI/node_modules/apache-arrow/Arrow.node") = require("apache-arrow");
const request: VisualizationRequest = {
  begin: "0001-01-01T00:00:00.0000000Z",
  end: "0001-01-01T00:00:00.0000100Z",
  resourcePaths: ["/a"],
  views: [
    {
      id: "main",
      begin: "0001-01-01T00:00:00.0000000Z",
      end: "0001-01-01T00:00:00.0000100Z",
      maxPoints: 16,
    },
  ],
};

interface Row {
  kind: number;
  resourceIndex?: number;
  viewIndex?: number;
  indices?: (bigint | null)[];
  values?: (number | null)[];
  progress?: number;
  message?: string;
  offset?: bigint;
}

function ipc(rows: Row[], invalidType = false): Uint8Array {
  const vector = arrow.vectorFromArray;
  const table = new arrow.Table({
    kind: vector(
      rows.map((r) => r.kind),
      invalidType ? new arrow.Float32() : new arrow.Int32(),
    ),
    resourceIndex: vector(
      rows.map((r) => r.resourceIndex ?? -1),
      new arrow.Int32(),
    ),
    viewIndex: vector(
      rows.map((r) => r.viewIndex ?? -1),
      new arrow.Int32(),
    ),
    offset: vector(
      rows.map((r) => r.offset ?? 0n),
      new arrow.Int64(),
    ),
    indices: vector(
      rows.map((r) => r.indices ?? []),
      new arrow.List(new arrow.Field("item", new arrow.Int64())),
    ),
    values: vector(
      rows.map((r) => r.values ?? []),
      new arrow.List(new arrow.Field("item", new arrow.Float32())),
    ),
    progress: vector(
      rows.map((r) => r.progress ?? 0),
      new arrow.Float64(),
    ),
    minimum: vector(
      rows.map(() => -2),
      new arrow.Float32(),
    ),
    maximum: vector(
      rows.map(() => 8),
      new arrow.Float32(),
    ),
    message: vector(
      rows.map((r) => r.message ?? "complete"),
      new arrow.Utf8(),
    ),
  });

  return arrow.tableToIPC(table);
}

const dataRow: Row = {
  kind: 0,
  resourceIndex: 0,
  viewIndex: 0,
  indices: [0n, 20n, 99n],
  values: [-2, NaN, 8],
};

function response(bytes: Uint8Array, fragment = 19): Response {
  let offset = 0;

  return new Response(
    new ReadableStream({
      pull(controller) {
        if (offset >= bytes.length) {
          controller.close();
        } else {
          controller.enqueue(bytes.slice(offset, offset + fragment));
          offset += fragment;
        }
      },
    }),
  );
}

describe("visualization Arrow protocol", () => {
  it("decodes fragmented streams, progress/ranges, gaps and explicit completion", async () => {
    const frames: VisualizationFrame[] = [];

    await decodeVisualization(
      response(ipc([{ kind: 1, progress: 0.2, resourceIndex: 0 }, dataRow, { kind: 2 }])),
      request,
      100n,
      new AbortController().signal,
      (frame) => frames.push(frame),
    );

    assert.deepEqual(frames[0], {
      kind: "progress",
      progress: 0.2,
      resourceIndex: 0,
      range: { hasValue: true, minimum: -2, maximum: 8 },
    });

    assert.equal(frames[1].kind, "data");

    if (frames[1].kind === "data") {
      assert.deepEqual([...frames[1].indices], [0n, 20n, 99n]);
      assert.ok(Number.isNaN(frames[1].values[1]));
    }
  });

  it("accepts nonzero view offsets without adding them to domain-relative indices", async () => {
    const frames: VisualizationFrame[] = [];

    await decodeVisualization(
      response(
        ipc([{ ...dataRow, offset: 20n, indices: [20n, 99n], values: [1, 2] }, { kind: 2 }]),
      ),
      request,
      100n,
      new AbortController().signal,
      (frame) => frames.push(frame),
    );

    assert.equal(frames[0].kind === "data" && frames[0].indices[0], 20n);
  });

  it("rejects bad schema, counts, nulls, unsorted/out-of-range coordinates, errors and truncation", async () => {
    const malformed = [
      ipc([dataRow, { kind: 2 }], true),
      ipc([{ ...dataRow, values: [1] }, { kind: 2 }]),
      ipc([{ ...dataRow, indices: [0n, null, 99n] }, { kind: 2 }]),
      ipc([{ ...dataRow, values: [1, null, 2] }, { kind: 2 }]),
      ipc([{ ...dataRow, indices: [0n, 0n, 99n] }, { kind: 2 }]),
      ipc([{ ...dataRow, indices: [0n, 20n, 100n] }, { kind: 2 }]),
      ipc([{ ...dataRow, resourceIndex: 1 }, { kind: 2 }]),
      ipc([
        {
          ...dataRow,
          values: Array(17).fill(1),
          indices: Array.from({ length: 17 }, (_, i) => BigInt(i)),
        },
        { kind: 2 },
      ]),
      ipc([{ kind: 1, progress: Infinity }]),
      ipc([{ kind: 3, message: "read failed" }]),
      ipc([dataRow]),
      ipc([{ kind: 2 }]),
      ipc([dataRow, { kind: 2 }, { kind: 1 }]),
      ipc([dataRow, { kind: 2 }]).slice(0, 100),
    ];

    for (const bytes of malformed) {
      await assert.rejects(
        decodeVisualization(response(bytes), request, 100n, new AbortController().signal, () => {}),
      );
    }
  });

  it("cancels a stalled body and releases the reader lock", async () => {
    let cancelled = false;
    const body = new ReadableStream<Uint8Array>({
      cancel() {
        cancelled = true;
      },
    });
    const controller = new AbortController();
    const task = decodeVisualization(
      new Response(body),
      request,
      100n,
      controller.signal,
      () => {},
    );

    await delay(5);
    controller.abort();
    await assert.rejects(task);
    assert.equal(cancelled, true);
    assert.equal(body.locked, false);
  });
});

describe("remote viewport provider", () => {
  it("shows streamed partial replacements and restores completed coverage on stream failure", async () => {
    let emit!: (frame: VisualizationFrame) => void;
    let fail!: (error: Error) => void;
    let first = true;
    const provider = new RemoteVisualization(
      0n,
      100n,
      1n,
      ["/a"],
      async (_request, _signal, onFrame) => {
        if (first) {
          first = false;

          onFrame({
            kind: "data",
            complete: true,
            resourceIndex: 0,
            viewIndex: 0,
            indices: new BigInt64Array([0n, 99n]),
            values: new Float32Array([1, 2]),
          });

          return;
        }

        emit = onFrame;

        await new Promise<void>((_resolve, reject) => {
          fail = reject;
        });
      },
    );

    provider.requestViews([{ id: "overview", begin: 0n, end: 100n, maxPoints: 16 }], true);
    await delay(5);
    provider.requestViews([{ id: "main", begin: 10n, end: 20n, maxPoints: 16 }], true);
    await delay(5);
    emit({ kind: "progress", progress: 0.25, resourceIndex: -1 });

    emit({
      kind: "data",
      complete: false,
      resourceIndex: 0,
      viewIndex: 0,
      indices: new BigInt64Array([10n, 19n]),
      values: new Float32Array([8, 9]),
    });

    assert.equal(provider.loading, true);
    assert.equal(provider.progress, 0.25);
    assert.equal(provider.pointsFor("main", 0, 10n, 20n)?.[1], 8);
    fail(new Error("truncated"));
    await delay(5);
    assert.equal(provider.pointsFor("main", 0, 10n, 20n)?.[1], 1);
    provider.dispose();
  });

  it("subtracts integer origins before converting huge indices to Float32", () => {
    const origin = 9_007_199_254_740_990n;
    const points = positionVisualizationPoints(
      {
        resourceIndex: 0,
        viewIndex: 0,
        indices: new BigInt64Array([origin, origin + 1n, origin + 2n]),
        values: new Float32Array([1, 2, 3]),
      },
      0n,
      1n,
      origin,
      origin + 2n,
    );

    assert.deepEqual([...points], [0, 1, 0.5, 2, 1, 3]);
  });

  it("coalesces gestures, aborts stale requests, retains fallback on failure and reuses cached views", async () => {
    const calls: {
      request: VisualizationRequest;
      signal: AbortSignal;
      emit: (frame: VisualizationFrame) => void;
      resolve: () => void;
      reject: (error: Error) => void;
    }[] = [];
    const transport: VisualizationTransport = (request, signal, emit) =>
      new Promise<void>((resolve, reject) => {
        calls.push({ request, signal, emit, resolve, reject });
      });
    const provider = new RemoteVisualization(0n, 100n, 1n, ["/a"], transport);
    const view = (begin = 0n, end = 100n) => [{ id: "main", begin, end, maxPoints: 16 }];
    const finish = (index: number, value: number) => {
      calls[index].emit({
        kind: "data",
        complete: true,
        resourceIndex: 0,
        viewIndex: 0,
        indices: new BigInt64Array([0n, 99n]),
        values: new Float32Array([value, value]),
      });

      calls[index].resolve();
    };

    provider.requestViews(view(), true);
    await delay(5);
    finish(0, 1);
    await delay(5);
    const fallback = provider.pointsFor("main", 0, 0n, 100n);

    assert.equal(fallback?.[1], 1);
    provider.requestViews(view(10n, 30n));
    provider.requestViews(view(20n, 40n));
    await delay(180);
    assert.equal(calls.length, 2);
    provider.requestViews(view(40n, 60n), true);
    assert.equal(calls[1].signal.aborted, true);
    await delay(5);
    finish(1, 99);
    calls[2].reject(new Error("network failed"));
    await delay(5);
    assert.match(provider.error, /network failed/);
    assert.equal(provider.pointsFor("main", 0, 0n, 100n), fallback);
    provider.requestViews(view(), true);
    await delay(5);
    assert.equal(calls.length, 3);
    provider.dispose();
  });

  it("bounds recent cache and keeps the coarse domain when cancelling refinements", async () => {
    let calls = 0;
    const provider = new RemoteVisualization(
      0n,
      1000n,
      1n,
      ["/a"],
      async (_request, _signal, emit) => {
        calls++;

        emit({
          kind: "data",
          complete: true,
          resourceIndex: 0,
          viewIndex: 0,
          indices: new BigInt64Array([0n, 999n]),
          values: new Float32Array([1, 2]),
        });
      },
    );
    const views = (begin: bigint, end: bigint) => [{ id: "main", begin, end, maxPoints: 16 }];

    provider.requestViews(views(0n, 1000n), true);
    await delay(5);

    for (let i = 1n; i < 12n; i++) {
      provider.requestViews(views(i, i + 10n), true);
      await delay(5);
    }

    provider.requestViews(views(1n, 11n), true);
    await delay(5);
    assert.equal(calls, 13);
    provider.requestViews(views(100n, 120n));
    provider.cancel();
    await delay(170);
    assert.equal(calls, 13);
    assert.ok(provider.pointsFor("overview", 0, 0n, 1000n));
    provider.dispose();
  });
});

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
  type VisualizationLoadTrace,
  type VisualizationRequest,
  type VisualizationTransport,
} from "../../../../src/Nexus.UI/src/app/charts/remote-visualization.ts";
import { requestError } from "../../../../src/Nexus.UI/src/app/request-error.ts";
import { dateTicks } from "../../../../src/Nexus.UI/src/app/resource-selection.ts";

it("keeps all views within the default server aggregate point budget", () => {
  for (let resources = 1; resources <= 100; resources++) {
    for (const width of [320, 1920, 3840, 8192]) {
      for (const dpr of [1, 1.25, 2, 3]) {
        const budget = visualizationPointBudget(width, resources, dpr);

        assert.ok(budget >= 5 && budget <= 32768);
        assert.ok(budget * resources * 3 <= 262144);
      }
    }
  }
});

it("budgets extrema buckets rather than points per physical pixel", () => {
  for (const dpr of [1, 1.25, 2, 3]) {
    assert.equal(visualizationPointBudget(320, 1, dpr), 5 * (Math.ceil(320 * dpr * 4) + 1));
  }

  assert.equal(visualizationPointBudget(1000, 1), 20005);
  assert.equal(visualizationPointBudget(1000, 1, 2), 32768);
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
  it("reports bounded decoder milestones and delivered byte totals", async () => {
    const bytes = ipc([{ kind: 1 }, dataRow, { kind: 2 }]);
    const events: { event: string; details?: object }[] = [];
    const body = response(bytes);

    await decodeVisualization(body, request, 100n, new AbortController().signal, () => {}, {
      requestId: "test",
      event: (event, details) => events.push({ event, details }),
    });

    assert.deepEqual(
      events.map((entry) => entry.event),
      [
        "decoder-first-bytes",
        "decoder-open",
        "decoder-first-batch",
        "decoder-first-data",
        "decoder-end",
      ],
    );

    const end = events.at(-1)!.details as {
      bytes: number;
      chunks: number;
      batches: number;
      outcome: string;
      durationMs: number;
    };

    assert.equal(end.bytes, bytes.length);
    assert.equal(end.chunks, Math.ceil(bytes.length / 19));
    assert.equal(end.batches, 1);
    assert.equal(end.outcome, "complete");
    assert.ok(end.durationMs >= 0);
    assert.equal(body.body!.locked, false);
  });

  it("reports decoder failure without changing protocol rejection or reader cleanup", async () => {
    const outcomes: string[] = [];

    for (const body of [response(ipc([dataRow])), new Response(null)]) {
      await assert.rejects(
        decodeVisualization(body, request, 100n, new AbortController().signal, () => {}, {
          requestId: "test",
          event: (event, details) => {
            if (event === "decoder-end") {
              outcomes.push((details as { outcome: string }).outcome);
            }
          },
        }),
      );

      assert.ok(!body.body?.locked);
    }

    assert.deepEqual(outcomes, ["error", "missing-body"]);
  });

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
    const outcomes: string[] = [];
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
      {
        requestId: "test",
        event: (event, details) => {
          if (event === "decoder-end") {
            outcomes.push((details as { outcome: string }).outcome);
          }
        },
      },
    );

    await delay(5);
    controller.abort();
    await assert.rejects(task);
    assert.equal(cancelled, true);
    assert.equal(body.locked, false);
    assert.deepEqual(outcomes, ["cancelled"]);
  });
});

describe("remote viewport provider", () => {
  function prefetchFixture(resources = 1, origin = 0n, period = 1n) {
    const calls: {
      request: VisualizationRequest;
      signal: AbortSignal;
      emit: (frame: VisualizationFrame) => void;
      resolve: () => void;
      reject: (error: Error) => void;
      trace?: VisualizationLoadTrace;
    }[] = [];
    const provider = new RemoteVisualization(
      origin,
      origin + 10000000n * period,
      period,
      Array.from({ length: resources }, (_, i) => `/r${i}`),
      (request, signal, emit, trace) =>
        new Promise<void>((resolve, reject) => {
          calls.push({ request, signal, emit, resolve, reject, trace });
        }),
    );
    const finish = (index: number, value = index + 1) => {
      const call = calls[index];

      call.request.views.forEach((view, viewIndex) => {
        for (let resourceIndex = 0; resourceIndex < resources; resourceIndex++) {
          call.emit({
            kind: "data",
            complete: true,
            resourceIndex,
            viewIndex,
            indices: new BigInt64Array([
              (dateTicks(view.begin)! - origin) / period,
              (dateTicks(view.end)! - origin) / period - 1n,
            ]),
            values: new Float32Array([value, value]),
          });
        }
      });

      call.resolve();
    };
    const view = (begin = 4000000n, end = 5000000n, maxPoints = 100) => [
      { id: "main", begin: origin + begin * period, end: origin + end * period, maxPoints },
    ];

    return { provider, calls, finish, view };
  }

  it("passes unique correlated load traces only while diagnostics are enabled", (t) => {
    t.mock.timers.enable({ apis: ["setTimeout"] });
    const storage = Object.getOwnPropertyDescriptor(globalThis, "localStorage");
    let enabled = false;

    Object.defineProperty(globalThis, "localStorage", {
      configurable: true,
      value: { getItem: () => (enabled ? "true" : null) },
    });

    const debug = t.mock.method(console, "debug", () => {});
    const { provider, calls, view } = prefetchFixture();

    try {
      provider.requestViews(view(0n, 100n), true);
      t.mock.timers.tick(0);
      assert.equal(calls[0].trace, undefined);
      assert.equal(debug.mock.calls.length, 0);
      enabled = true;
      provider.requestViews(view(200n, 300n), true);
      t.mock.timers.tick(0);
      provider.requestViews(view(400n, 500n), true);
      t.mock.timers.tick(0);
      const first = calls[1].trace!;
      const second = calls[2].trace!;

      assert.match(
        first.requestId,
        /^[\da-f]{8}-[\da-f]{4}-4[\da-f]{3}-[89ab][\da-f]{3}-[\da-f]{12}$/,
      );

      assert.notEqual(first.requestId, second.requestId);
      first.event("test-phase", { status: 200 });
      const details = debug.mock.calls.at(-1)!.arguments[1] as {
        requestId: string;
        loadId: number;
        kind: string;
        status: number;
        loadElapsedMs: number;
      };

      assert.equal(details.requestId, first.requestId);
      assert.equal(details.loadId, 2);
      assert.equal(details.kind, "foreground");
      assert.equal(details.status, 200);
      assert.ok(typeof details.loadElapsedMs === "number" && details.loadElapsedMs >= 0);
    } finally {
      provider.dispose();

      if (storage) {
        Object.defineProperty(globalThis, "localStorage", storage);
      } else {
        Reflect.deleteProperty(globalThis, "localStorage");
      }
    }
  });

  it("prefetches four bounded aligned zoom levels sequentially only after foreground completion", async () => {
    const origin = 90071992547409930n;
    const period = 10n;
    const { provider, calls, finish, view } = prefetchFixture(100, origin, period);

    try {
      provider.requestViews(view(), true);
      await delay(280);
      assert.equal(calls.length, 1);
      finish(0);
      await delay(10);
      assert.equal(calls.length, 1);
      await delay(270);

      for (let i = 1; i <= 4; i++) {
        assert.equal(calls.length, i + 1);
        assert.equal(provider.loading, false);
        assert.equal(provider.progress, 1);
        const prediction = calls[i].request.views[0];
        const begin = dateTicks(prediction.begin)!;
        const end = dateTicks(prediction.end)!;

        assert.equal(calls[i].request.views.length, 1);
        assert.equal((begin - origin) % period, 0n);
        assert.equal((end - origin) % period, 0n);
        assert.ok(begin >= origin && end <= provider.end && end > begin);
        assert.ok(prediction.maxPoints <= 32768);
        finish(i);
        await delay(5);
      }

      assert.ok(
        calls.slice(1).reduce((sum, call) => sum + call.request.views[0].maxPoints * 100, 0) <=
          262144,
      );

      assert.equal(calls.length, 5);
      // Exact prefetched bounds are reusable without another foreground request.
      const prediction = calls[1].request.views[0];

      provider.requestViews(
        [
          {
            ...prediction,
            id: "main",
            begin: dateTicks(prediction.begin)!,
            end: dateTicks(prediction.end)!,
          },
        ],
        true,
      );

      await delay(5);
      assert.equal(calls.length, 5);
      assert.equal(provider.loading, false);
    } finally {
      provider.dispose();
    }
  });

  it("starts foreground for the latest view without waiting for a gesture to stop", async (t) => {
    t.mock.timers.enable({ apis: ["setTimeout"] });
    const { provider, calls, finish, view } = prefetchFixture();

    try {
      // Fine views cannot be speculatively prefetched.
      provider.requestViews(view(0n, 100n));
      t.mock.timers.tick(20);
      provider.requestViews(view(1n, 99n));
      t.mock.timers.tick(20);
      provider.requestViews(view(2n, 98n));
      assert.equal(calls.length, 0);
      t.mock.timers.tick(10);
      assert.equal(calls.length, 1);
      assert.equal(calls[0].request.views[0].id, "main");
      assert.equal(dateTicks(calls[0].request.views[0].begin), 2n);
      provider.requestViews(view(3n, 97n));
      assert.equal(calls[0].signal.aborted, false);
      finish(0, 8);
      await Promise.resolve();
      await Promise.resolve();
      assert.equal(provider.loading, false);
      assert.equal(provider.progress, 1);
      assert.equal(provider.pointsFor("main", 0, 3n, 97n)?.[1], 8);
      t.mock.timers.tick(300);
      assert.equal(calls.length, 1);
    } finally {
      provider.dispose();
    }
  });

  it("retains nearby reduced foreground work and then refines the latest boundaries", async (t) => {
    t.mock.timers.enable({ apis: ["setTimeout"] });
    const { provider, calls, finish, view } = prefetchFixture();

    try {
      provider.requestViews(view(0n, 10000n), true);
      t.mock.timers.tick(1);
      provider.requestViews(view(100n, 9900n));
      assert.equal(calls[0].signal.aborted, false);
      // No speculative request may compete with the retained foreground load.
      t.mock.timers.tick(300);
      assert.equal(calls.length, 1);
      const states: { loading: boolean; progress: number }[] = [];

      provider.subscribe(() =>
        states.push({ loading: provider.loading, progress: provider.progress }),
      );

      finish(0);
      await Promise.resolve();
      await Promise.resolve();
      assert.equal(provider.loading, true);
      assert.ok(states.every((state) => state.loading && state.progress === 0));
      assert.equal(provider.pointsFor("main", 0, 100n, 9900n)?.[1], 1);
      t.mock.timers.tick(50);
      assert.equal(calls.length, 2);
      assert.equal(dateTicks(calls[1].request.views[0].begin), 100n);
      finish(1, 7);
      await Promise.resolve();
      await Promise.resolve();
      assert.equal(provider.loading, false);
      assert.equal(provider.pointsFor("main", 0, 100n, 9900n)?.[1], 7);
    } finally {
      provider.dispose();
    }
  });

  it("replaces foreground work that no longer covers the view or is too coarse", (t) => {
    t.mock.timers.enable({ apis: ["setTimeout"] });
    const { provider, calls, view } = prefetchFixture();

    try {
      provider.requestViews(view(0n, 10000n), true);
      t.mock.timers.tick(1);
      provider.requestViews(view(4000n, 4100n));
      assert.equal(calls[0].signal.aborted, true);
      t.mock.timers.tick(50);
      provider.requestViews(view(3900n, 4200n));
      assert.equal(calls[1].signal.aborted, true);
      t.mock.timers.tick(50);
      assert.equal(calls.length, 3);
      provider.cancel();
      assert.equal(calls[2].signal.aborted, true);
      t.mock.timers.tick(500);
      assert.equal(calls.length, 3);
    } finally {
      provider.dispose();
    }
  });

  it("retains completed superseded previews without caching them or suppressing refinement", async (t) => {
    t.mock.timers.enable({ apis: ["setTimeout"] });
    const { provider, calls, finish, view } = prefetchFixture(2);

    try {
      provider.requestViews(view(0n, 10000n), true);
      t.mock.timers.tick(1);
      finish(0, 1);
      await Promise.resolve();
      await Promise.resolve();
      provider.requestViews(view(100n, 9900n, 1000), true);
      t.mock.timers.tick(1);

      for (let resourceIndex = 0; resourceIndex < 2; resourceIndex++) {
        calls[1].emit({
          kind: "data",
          complete: true,
          resourceIndex,
          viewIndex: 0,
          indices: new BigInt64Array([100n, 9899n]),
          values: new Float32Array([8, 9]),
        });
      }

      assert.equal(provider.pointsFor("main", 0, 100n, 9900n)?.[1], 8);
      provider.requestViews(view(2000n, 3000n, 1000));
      assert.equal(calls[1].signal.aborted, true);
      calls[1].reject(new Error("aborted"));
      await Promise.resolve();
      await Promise.resolve();

      for (let resourceIndex = 0; resourceIndex < 2; resourceIndex++) {
        assert.equal(provider.pointsFor("main", resourceIndex, 2000n, 3000n)?.[1], 8);
      }

      // Even exact bounds of the retained view require a fresh transport.
      provider.requestViews(view(100n, 9900n, 1000));
      assert.equal(provider.loading, true);
      t.mock.timers.tick(50);
      assert.equal(calls.length, 3);
      finish(2, 7);
      await Promise.resolve();
      await Promise.resolve();
      assert.equal(provider.loading, false);
      assert.equal(provider.pointsFor("main", 0, 100n, 9900n)?.[1], 7);
    } finally {
      provider.dispose();
    }
  });

  for (const delivery of ["missing-resource", "partial-resource", "complete"] as const) {
    it(`only retains all-resource complete previews on supersession (${delivery})`, async (t) => {
      t.mock.timers.enable({ apis: ["setTimeout"] });
      const { provider, calls, view } = prefetchFixture(2);

      try {
        provider.requestViews(view(0n, 10000n, 1000), true);
        t.mock.timers.tick(1);

        for (
          let resourceIndex = 0;
          resourceIndex < (delivery === "missing-resource" ? 1 : 2);
          resourceIndex++
        ) {
          calls[0].emit({
            kind: "data",
            complete: delivery !== "partial-resource" || resourceIndex === 0,
            resourceIndex,
            viewIndex: 0,
            indices: new BigInt64Array([0n, 9999n]),
            values: new Float32Array([8, 9]),
          });
        }

        provider.requestViews(view(2000n, 3000n, 1000));
        assert.equal(calls[0].signal.aborted, true);

        assert.equal(
          provider.pointsFor("main", 0, 2000n, 3000n)?.[1],
          delivery === "complete" ? 8 : undefined,
        );

        t.mock.timers.tick(50);
        calls[1].reject(new Error("truncated"));
        await Promise.resolve();
        await Promise.resolve();
        assert.equal(provider.pointsFor("main", 0, 2000n, 3000n), undefined);
      } finally {
        provider.dispose();
      }
    });
  }

  it("replaces rather than accumulates retained views across repeated supersessions", (t) => {
    t.mock.timers.enable({ apis: ["setTimeout"] });
    const { provider, calls, view } = prefetchFixture();

    try {
      provider.requestViews(view(0n, 10000n, 1000), true);
      t.mock.timers.tick(1);

      calls[0].emit({
        kind: "data",
        complete: true,
        resourceIndex: 0,
        viewIndex: 0,
        indices: new BigInt64Array([0n, 9999n]),
        values: new Float32Array([8, 9]),
      });

      provider.requestViews(view(2000n, 3000n, 1000));
      t.mock.timers.tick(50);
      // A second supersession with no data must not lose the previous fallback.
      provider.requestViews(view(1900n, 3100n, 1000));
      assert.equal(provider.pointsFor("main", 0, 1900n, 3100n)?.[1], 8);
      t.mock.timers.tick(50);

      calls[2].emit({
        kind: "data",
        complete: true,
        resourceIndex: 0,
        viewIndex: 0,
        indices: new BigInt64Array([1900n, 3099n]),
        values: new Float32Array([7, 7]),
      });

      provider.requestViews(view(2100n, 2200n, 1000));
      assert.equal(provider.pointsFor("main", 0, 2100n, 2200n)?.[1], 7);
      assert.equal(provider.pointsFor("main", 0, 0n, 10000n), undefined);

      // Late data from an aborted request cannot replace retained coverage.
      calls[0].emit({
        kind: "data",
        complete: true,
        resourceIndex: 0,
        viewIndex: 0,
        indices: new BigInt64Array([0n, 9999n]),
        values: new Float32Array([99, 99]),
      });

      assert.equal(provider.pointsFor("main", 0, 2100n, 2200n)?.[1], 7);
    } finally {
      provider.dispose();
    }
  });

  it("bounds retained view payload to 262144 resource-points", (t) => {
    t.mock.timers.enable({ apis: ["setTimeout"] });
    const { provider, calls, view } = prefetchFixture(9);

    try {
      provider.requestViews(view(0n, 1000000n, 32768), true);
      t.mock.timers.tick(1);

      for (let resourceIndex = 0; resourceIndex < 9; resourceIndex++) {
        calls[0].emit({
          kind: "data",
          complete: true,
          resourceIndex,
          viewIndex: 0,
          indices: new BigInt64Array(32768),
          values: new Float32Array(32768),
        });
      }

      provider.requestViews(view(100n, 200n, 32768));
      assert.equal(calls[0].signal.aborted, true);
      assert.equal(provider.pointsFor("main", 0, 100n, 200n), undefined);
    } finally {
      provider.dispose();
    }
  });

  for (const cleanup of ["cancel", "dispose", "uncovered"] as const) {
    it(`clears retained previews on ${cleanup}`, (t) => {
      t.mock.timers.enable({ apis: ["setTimeout"] });
      const { provider, calls, view } = prefetchFixture();

      try {
        provider.requestViews(view(0n, 10000n, 1000), true);
        t.mock.timers.tick(1);

        calls[0].emit({
          kind: "data",
          complete: true,
          resourceIndex: 0,
          viewIndex: 0,
          indices: new BigInt64Array([0n, 9999n]),
          values: new Float32Array([8, 9]),
        });

        provider.requestViews(view(2000n, 3000n, 1000));
        assert.equal(provider.pointsFor("main", 0, 2000n, 3000n)?.[1], 8);

        if (cleanup === "uncovered") {
          provider.requestViews(view(20000n, 21000n, 1000));
        } else {
          provider[cleanup]();
        }

        assert.equal(provider.pointsFor("main", 0, 2000n, 3000n), undefined);
      } finally {
        provider.dispose();
      }
    });
  }

  it("notifies subscribers when completed prefetch coverage becomes available", async () => {
    const { provider, calls, finish, view } = prefetchFixture();

    try {
      provider.requestViews(view(), true);
      await delay(5);
      finish(0);
      await delay(280);
      let notifications = 0;

      provider.subscribe(() => notifications++);
      finish(1);
      await delay(5);
      assert.equal(notifications, 1);
      assert.equal(provider.loading, false);
      assert.equal(provider.progress, 1);
      assert.equal(calls.length, 3);
    } finally {
      provider.dispose();
    }
  });

  it("retries only the latest changed viewport after retained foreground failure", async (t) => {
    t.mock.timers.enable({ apis: ["setTimeout"] });
    const { provider, calls, view } = prefetchFixture();

    try {
      provider.requestViews(view(0n, 10000n), true);
      t.mock.timers.tick(1);
      provider.requestViews(view(1n, 9999n));
      calls[0].reject(new Error("old view failed"));
      await Promise.resolve();
      await Promise.resolve();
      assert.equal(provider.error, "");
      assert.equal(provider.loading, true);
      assert.equal(provider.progress, 0);
      t.mock.timers.tick(50);
      assert.equal(calls.length, 2);
      assert.equal(dateTicks(calls[1].request.views[0].begin), 1n);
      calls[1].reject(new Error("latest view failed"));
      await Promise.resolve();
      await Promise.resolve();
      assert.match(provider.error, /latest view failed/);
      assert.equal(provider.loading, false);
      t.mock.timers.tick(1000);
      assert.equal(calls.length, 2);
    } finally {
      provider.dispose();
    }
  });

  it("retains one-level-coarser main coverage and follows up missing detail", async (t) => {
    t.mock.timers.enable({ apis: ["setTimeout"] });
    const { provider, calls, finish, view } = prefetchFixture();

    try {
      // Canonical strides: 512 for the running view, 256 for the latest main.
      provider.requestViews(view(0n, 9000n), true);
      t.mock.timers.tick(1);

      provider.requestViews([
        ...view(2000n, 6000n),
        { id: "detail", begin: 1000n, end: 7000n, maxPoints: 100 },
      ]);

      assert.equal(calls[0].signal.aborted, false);
      finish(0);
      await Promise.resolve();
      await Promise.resolve();
      t.mock.timers.tick(50);

      assert.deepEqual(
        calls[1].request.views.map((view) => view.id),
        ["main", "detail"],
      );

      finish(1);
      await Promise.resolve();
      await Promise.resolve();
      assert.equal(provider.loading, false);
    } finally {
      provider.dispose();
    }
  });

  it("clears pending refinement on cancel, disposal, or navigation to cached coverage", async (t) => {
    t.mock.timers.enable({ apis: ["setTimeout"] });

    for (const action of ["cancel", "dispose", "cached"]) {
      const { provider, calls, finish, view } = prefetchFixture();

      try {
        provider.requestViews(view(0n, 1000n), true);
        t.mock.timers.tick(1);
        provider.requestViews(view(1n, 999n));
        finish(0);
        await Promise.resolve();
        await Promise.resolve();
        assert.equal(provider.loading, true);

        if (action === "cached") {
          provider.requestViews(view(0n, 1000n));
        } else if (action === "cancel") {
          provider.cancel();
        } else {
          provider.dispose();
        }

        t.mock.timers.tick(1000);
        assert.equal(calls.length, 1);
        assert.equal(provider.loading, false);
      } finally {
        provider.dispose();
      }
    }
  });

  it("cancels speculative work for foreground gestures and ignores late frames and errors", async () => {
    const { provider, calls, finish, view } = prefetchFixture();

    try {
      provider.requestViews(view(), true);
      await delay(5);
      finish(0);
      await delay(280);
      assert.equal(calls.length, 2);
      calls[1].emit({ kind: "progress", progress: 0.1, resourceIndex: -1 });
      assert.equal(provider.progress, 1);
      provider.requestViews(view(7000000n, 7100000n), true);
      assert.equal(calls[1].signal.aborted, true);
      await delay(5);
      assert.equal(calls.length, 3);
      finish(1, 99);
      await delay(5);
      assert.equal(provider.loading, true);
      finish(2);
      await delay(5);
      assert.equal(provider.error, "");
      assert.equal(provider.pointsFor("main", 0, 7000000n, 7100000n)?.[1], 3);
      provider.cancel();
      await delay(280);
      assert.equal(calls.length, 3);
    } finally {
      provider.dispose();
    }
  });

  it("infers edge zoom anchors and clamps all speculative coverage to the domain", async () => {
    for (const rightEdge of [false, true]) {
      const { provider, calls, finish, view } = prefetchFixture();

      try {
        provider.requestViews(rightEdge ? view(8000000n, 10000000n) : view(0n, 2000000n), true);
        await delay(5);
        finish(0);
        await delay(5);
        provider.requestViews(rightEdge ? view(9000000n, 10000000n) : view(0n, 1000000n), true);
        await delay(5);
        finish(1);
        await delay(280);
        const prediction = calls[2].request.views[0];

        assert.equal(
          dateTicks(rightEdge ? prediction.end : prediction.begin),
          rightEdge ? 10000000n : 0n,
        );

        assert.ok(dateTicks(prediction.begin)! >= provider.begin);
        assert.ok(dateTicks(prediction.end)! <= provider.end);
        provider.dispose();
        assert.equal(calls[2].signal.aborted, true);
        finish(2);
        await delay(5);
        assert.equal(calls.length, 3);
      } finally {
        provider.dispose();
      }
    }
  });

  it("provides immediate prefetched coverage while refining changed boundary buckets", async () => {
    const { provider, calls, finish, view } = prefetchFixture();

    try {
      provider.requestViews(view(), true);
      await delay(5);
      finish(0);
      await delay(280);
      finish(1, 8);
      await delay(5);
      const prediction = calls[1].request.views[0];
      const begin = dateTicks(prediction.begin)! + 1n;
      const end = dateTicks(prediction.end)! - 1n;

      provider.requestViews([{ id: "main", begin, end, maxPoints: prediction.maxPoints }], true);
      assert.equal(provider.pointsFor("main", 0, begin, end)?.[1], 8);
      assert.equal(provider.loading, true);
      assert.equal(calls[2].signal.aborted, true);
      await delay(5);
      finish(3, 9);
      await delay(5);
      assert.equal(provider.pointsFor("main", 0, begin, end)?.[1], 9);
    } finally {
      provider.dispose();
    }
  });

  it("keeps failed speculative replacements out of the cache and out of foreground status", async () => {
    const { provider, calls, finish, view } = prefetchFixture();

    try {
      provider.requestViews(view(), true);
      await delay(5);
      finish(0);
      await delay(280);

      calls[1].emit({
        kind: "data",
        complete: true,
        resourceIndex: 0,
        viewIndex: 0,
        indices: new BigInt64Array([4200000n]),
        values: new Float32Array([99]),
      });

      calls[1].reject(new Error("prefetch failed"));
      await delay(10);
      assert.equal(provider.error, "");
      assert.equal(provider.loading, false);
      assert.equal(provider.progress, 1);
      assert.equal(provider.pointsFor("main", 0, 4200000n, 4800000n)?.[1], 1);
      assert.equal(calls.length, 2);
    } finally {
      provider.dispose();
    }
  });

  it("does not prefetch fine raw views or sacrifice visited history for speculation", async () => {
    const { provider, calls, finish, view } = prefetchFixture();

    try {
      provider.requestViews(view(0n, 100n), true);
      await delay(5);
      finish(0);
      await delay(280);
      assert.equal(calls.length, 1);

      for (let i = 1; i < 8; i++) {
        provider.requestViews(view(BigInt(i) * 1000000n, BigInt(i) * 1000000n + 500000n), true);
        await delay(5);
        finish(i);
        await delay(5);
      }

      await delay(280);
      assert.equal(calls.length, 9);
      finish(8);
      await delay(10);
      // Full visited cache rejects speculation and stops the remaining queue.
      assert.equal(calls.length, 9);
      provider.requestViews(view(0n, 100n), true);
      await delay(5);
      assert.equal(calls.length, 9);
    } finally {
      provider.dispose();
    }
  });

  it("refines clipped boundary buckets and renders the finest canonical cached LOD", async () => {
    const { provider, calls, finish, view } = prefetchFixture();

    try {
      provider.requestViews(view(0n, 100n, 24), true);
      await delay(5);
      finish(0, 1);
      await delay(5);
      provider.requestViews(view(5n, 95n, 20), true);
      await delay(5);
      finish(1, 2);
      await delay(5);
      assert.equal(provider.pointsFor("main", 0, 5n, 95n)?.[1], 2);
      provider.requestViews(view(10n, 90n, 20), true);
      await delay(5);
      assert.equal(calls.length, 3);
      assert.equal(provider.loading, true);
      finish(2, 3);
    } finally {
      provider.dispose();
    }
  });

  it("prefers boundary-correct halo results over finer incompatible cached points", async () => {
    const { provider, calls, finish, view } = prefetchFixture();

    try {
      provider.requestViews(view(0n, 100n, 24), true);
      await delay(5);

      calls[0].emit({
        kind: "data",
        complete: true,
        resourceIndex: 0,
        viewIndex: 0,
        indices: new BigInt64Array([0n, 99n]),
        values: new Float32Array([NaN, 1]),
      });

      calls[0].resolve();
      await delay(5);
      provider.requestViews(view(10n, 90n, 10), true);
      await delay(5);
      finish(1, 8);
      await delay(5);
      assert.equal(provider.pointsFor("main", 0, 10n, 90n)?.[1], 8);
      // The drawing viewport excludes its request's one-sample halo.
      assert.equal(provider.pointsFor("main", 0, 11n, 89n)?.[1], 8);
    } finally {
      provider.dispose();
    }
  });

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

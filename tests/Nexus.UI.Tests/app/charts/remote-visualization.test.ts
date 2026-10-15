import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { setImmediate as settle } from "node:timers/promises";
import { beforeEach, describe, it, type TestContext } from "node:test";
import {
  decodeVisualization,
  positionVisualizationPoints,
  RemoteVisualization,
  visualizationPointBudget,
  type VisualizationFrame,
  type VisualizationRequest,
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
  minimum?: number;
  maximum?: number;
}

function ipc(rows: Row[], invalidType = false, version?: string, multiBatch = false): Uint8Array {
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
      rows.map((r) => r.minimum ?? -2),
      new arrow.Float32(),
    ),
    maximum: vector(
      rows.map((r) => r.maximum ?? 8),
      new arrow.Float32(),
    ),
    message: vector(
      rows.map((r) => r.message ?? "complete"),
      new arrow.Utf8(),
    ),
  });

  if (version !== undefined) {
    table.schema.metadata.set("visualizationVersion", version);
  }

  return arrow.tableToIPC(
    multiBatch
      ? new arrow.Table(
          table.schema,
          rows.flatMap((_, i) => table.slice(i, i + 1).batches),
        )
      : table,
  );
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
  it("rejects invalid streams without leaking reader locks", async () => {
    for (const [body, error] of [
      [response(ipc([dataRow])), /before explicit completion of all views/],
      [new Response(null), /response has no body/],
    ] as const) {
      await assert.rejects(
        decodeVisualization(body, request, 100n, new AbortController().signal, () => {}),
        error,
      );

      assert.ok(!body.body?.locked);
    }
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

    assert.deepEqual(frames.slice(1), [
      {
        kind: "data",
        complete: true,
        resourceIndex: 0,
        viewIndex: 0,
        indices: new BigInt64Array([0n, 20n, 99n]),
        values: new Float32Array([-2, NaN, 8]),
      },
    ]);
  });

  it("accepts nonzero view offsets without adding them to domain-relative indices", async () => {
    const frames: VisualizationFrame[] = [];

    await decodeVisualization(
      response(
        ipc([{ ...dataRow, offset: 20n, indices: [20n, 99n], values: [1, 2] }, { kind: 2 }]),
      ),
      { ...request, views: [{ ...request.views[0], begin: "0001-01-01T00:00:00.0000020Z" }] },
      100n,
      new AbortController().signal,
      (frame) => frames.push(frame),
    );

    assert.equal(frames[0].kind === "data" && frames[0].indices[0], 20n);
  });

  it("rejects bad schema, counts, nulls, unsorted/out-of-range coordinates, errors and truncation", async () => {
    const malformed: [Uint8Array, RegExp][] = [
      [ipc([dataRow, { kind: 2 }], true), /Invalid visualization Arrow schema/],
      [ipc([{ ...dataRow, values: [1] }, { kind: 2 }]), /Invalid visualization replacement points/],
      [
        ipc([{ ...dataRow, indices: [0n, null, 99n] }, { kind: 2 }]),
        /Invalid visualization replacement points/,
      ],
      [
        ipc([{ ...dataRow, values: [1, null, 2] }, { kind: 2 }]),
        /Invalid visualization replacement points/,
      ],
      [
        ipc([{ ...dataRow, indices: [0n, 0n, 99n] }, { kind: 2 }]),
        /Invalid visualization sample coordinate/,
      ],
      [
        ipc([{ ...dataRow, indices: [0n, 20n, 100n] }, { kind: 2 }]),
        /Invalid visualization sample coordinate/,
      ],
      [
        ipc([{ ...dataRow, resourceIndex: 1 }, { kind: 2 }]),
        /Invalid visualization resource\/view index/,
      ],
      [
        ipc([{ ...dataRow, viewIndex: 1 }, { kind: 2 }]),
        /Invalid visualization resource\/view index/,
      ],
      [
        ipc([
          {
            ...dataRow,
            values: Array(17).fill(1),
            indices: Array.from({ length: 17 }, (_, i) => BigInt(i)),
          },
          { kind: 2 },
        ]),
        /Invalid visualization replacement points/,
      ],
      ...[Infinity, -0.1, 1.1].map((progress): [Uint8Array, RegExp] => [
        ipc([{ kind: 1, progress }, dataRow, { kind: 2 }]),
        /Invalid visualization progress/,
      ]),
      [ipc([{ kind: 3, message: "read failed" }]), /^Error: read failed$/],
      [ipc([{ kind: 4 }, dataRow, { kind: 2 }]), /Unknown visualization record kind/],
      [ipc([dataRow]), /before explicit completion of all views/],
      [ipc([{ kind: 2 }]), /before explicit completion of all views/],
      [ipc([dataRow, { kind: 2 }, { kind: 1 }]), /records follow completion/],
      [
        ipc([{ ...dataRow, message: "unexpected" }, { kind: 2 }]),
        /Invalid visualization replacement points/,
      ],
      [ipc([{ ...dataRow, offset: -1n }, { kind: 2 }]), /Invalid visualization replacement points/],
      [
        ipc([{ ...dataRow, offset: 101n }, { kind: 2 }]),
        /Invalid visualization replacement points/,
      ],
    ];

    for (const [bytes, error] of malformed) {
      await assert.rejects(
        decodeVisualization(response(bytes), request, 100n, new AbortController().signal, () => {}),
        error,
      );
    }

    // Arrow's own truncation error is version-dependent.
    await assert.rejects(
      decodeVisualization(
        response(ipc([dataRow, { kind: 2 }]).slice(0, 100)),
        request,
        100n,
        new AbortController().signal,
        () => {},
      ),
    );
  });

  it("checks view coordinates independently of the containing domain", async () => {
    const narrow = {
      ...request,
      views: [
        {
          ...request.views[0],
          begin: "0001-01-01T00:00:00.0000020Z",
          end: "0001-01-01T00:00:00.0000080Z",
        },
      ],
    };

    for (const indices of [
      [19n, 79n],
      [20n, 80n],
    ]) {
      await assert.rejects(
        decodeVisualization(
          response(ipc([{ ...dataRow, offset: 20n, indices, values: [1, 2] }, { kind: 2 }])),
          narrow,
          100n,
          new AbortController().signal,
          () => {},
        ),
        /Invalid visualization sample coordinate/,
      );
    }
  });

  it("accepts supported or absent version metadata and rejects unsupported versions", async () => {
    for (const version of [undefined, "1", "2"]) {
      const task = decodeVisualization(
        response(ipc([dataRow, { kind: 2 }], false, version)),
        request,
        100n,
        new AbortController().signal,
        () => {},
      );

      if (version === "2") {
        await assert.rejects(task, /Invalid visualization Arrow schema/);
      } else {
        await task;
      }
    }
  });

  it("routes resource-major multi-view batches through the decoder and provider transactionally", async (t) => {
    const multiRequest = {
      ...request,
      resourcePaths: ["/a", "/b"],
      views: [
        request.views[0],
        {
          ...request.views[0],
          id: "detail",
          begin: "0001-01-01T00:00:00.0000020Z",
          end: "0001-01-01T00:00:00.0000080Z",
        },
      ],
    };
    const rows: Row[] = [];

    for (let resourceIndex = 0; resourceIndex < 2; resourceIndex++) {
      rows.push({ kind: 1, resourceIndex, progress: (resourceIndex + 1) / 2 });

      for (let viewIndex = 0; viewIndex < 2; viewIndex++) {
        rows.push({
          ...dataRow,
          resourceIndex,
          viewIndex,
          offset: viewIndex ? 20n : 0n,
          indices: viewIndex ? [20n, 79n] : [0n, 99n],
          values: [resourceIndex + viewIndex + 1, 8],
        });
      }
    }

    rows.push({ kind: 2, offset: 100n });
    t.mock.timers.enable({ apis: ["setTimeout"] });

    for (const missing of [false, true]) {
      const frames: VisualizationFrame[] = [];
      let complete!: () => void;
      const ended = new Promise<void>((resolve) => {
        complete = resolve;
      });
      const provider = new RemoteVisualization(
        0n,
        100n,
        1n,
        multiRequest.resourcePaths,
        async (actual, signal, emit) => {
          try {
            await decodeVisualization(
              response(
                ipc(
                  missing
                    ? rows.filter(
                        (row) =>
                          !(row.kind === 0 && row.resourceIndex === 1 && row.viewIndex === 1),
                      )
                    : rows,
                  false,
                  "1",
                  true,
                ),
              ),
              actual,
              100n,
              signal,
              (frame) => {
                frames.push(frame);
                emit(frame);

                if (frame.kind === "data" && frame.resourceIndex === 0) {
                  assert.equal(provider.pointsFor("main", 0, 0n, 100n), undefined);
                }
              },
            );
          } finally {
            complete();
          }
        },
        4096,
      );

      try {
        provider.requestViews(
          multiRequest.views.map((view) => ({
            ...view,
            begin: dateTicks(view.begin)!,
            end: dateTicks(view.end)!,
          })),
          true,
        );

        t.mock.timers.tick(0);
        await ended;
        await settle();

        if (missing) {
          assert.match(provider.error, /before explicit completion of all views/);
          assert.equal(provider.pointsFor("main", 0, 0n, 100n), undefined);
        } else {
          assert.equal(provider.error, "");
          assert.equal(frames.filter((frame) => frame.kind === "data" && frame.complete).length, 4);
          assert.equal(provider.pointsFor("main", 1, 0n, 100n)?.[1], 2);
          assert.equal(provider.pointsFor("detail", 1, 20n, 80n)?.[1], 3);
          assert.equal(provider.loading, false);
        }
      } finally {
        provider.dispose();
      }
    }
  });

  it("emits replacement completeness and rejects missing or repeated pair completion across batches", async () => {
    const partial = { ...dataRow, message: "partial", values: [1, 2, 3] };
    const frames: VisualizationFrame[] = [];

    await decodeVisualization(
      response(ipc([partial, dataRow, { kind: 2 }], false, "1", true)),
      request,
      100n,
      new AbortController().signal,
      (frame) => frames.push(frame),
    );

    assert.deepEqual(frames, [
      {
        kind: "data",
        complete: false,
        resourceIndex: 0,
        viewIndex: 0,
        indices: new BigInt64Array([0n, 20n, 99n]),
        values: new Float32Array([1, 2, 3]),
      },
      {
        kind: "data",
        complete: true,
        resourceIndex: 0,
        viewIndex: 0,
        indices: new BigInt64Array([0n, 20n, 99n]),
        values: new Float32Array([-2, NaN, 8]),
      },
    ]);

    for (const [rows, error] of [
      [[partial, { kind: 2 }], /before explicit completion of all views/],
      [[dataRow, dataRow, { kind: 2 }], /Invalid visualization replacement points/],
      [[dataRow, partial, { kind: 2 }], /Invalid visualization replacement points/],
    ] as [Row[], RegExp][]) {
      await assert.rejects(
        decodeVisualization(
          response(ipc(rows, false, "1", true)),
          request,
          100n,
          new AbortController().signal,
          () => {},
        ),
        error,
      );
    }
  });

  it("omits invalid ranges without losing valid progress", async () => {
    const frames: VisualizationFrame[] = [];
    const rows = [
      { kind: 1, progress: 0, minimum: NaN, maximum: NaN },
      { kind: 1, progress: 0.5, minimum: 8, maximum: -2 },
      { kind: 1, progress: 1, minimum: -Infinity, maximum: Infinity },
      dataRow,
      { kind: 2 },
    ];

    await decodeVisualization(
      response(ipc(rows)),
      request,
      100n,
      new AbortController().signal,
      (frame) => frames.push(frame),
    );

    assert.deepEqual(
      frames.slice(0, 3),
      [0, 0.5, 1].map((progress) => ({
        kind: "progress",
        progress,
        resourceIndex: -1,
        range: undefined,
      })),
    );
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

    await settle();
    controller.abort();
    await assert.rejects(task, { name: "AbortError" });
    assert.equal(cancelled, true);
    assert.equal(body.locked, false);
  });
});

describe("remote viewport provider", () => {
  let clock: TestContext["mock"]["timers"];

  beforeEach((t) => {
    clock = (t as TestContext).mock.timers;
    clock.enable({ apis: ["setTimeout"] });
  });

  async function delay(milliseconds: number) {
    await settle();
    clock.tick(milliseconds);
    // Drain promise continuations before advancing another scheduling deadline.
    await settle();
  }

  function prefetchFixture(
    resources = 1,
    origin = 0n,
    period = 1n,
    cacheBudgetBytes = 32 * 1024 * 1024,
    length = 10000000n,
  ) {
    const calls: {
      request: VisualizationRequest;
      signal: AbortSignal;
      emit: (frame: VisualizationFrame) => void;
      resolve: () => void;
      reject: (error: Error) => void;
    }[] = [];
    const provider = new RemoteVisualization(
      origin,
      origin + length * period,
      period,
      Array.from({ length: resources }, (_, i) => `/r${i}`),
      (request, signal, emit) =>
        new Promise<void>((resolve, reject) => {
          calls.push({ request, signal, emit, resolve, reject });
        }),
      cacheBudgetBytes,
    );
    const finish = (index: number, value = index + 1, pointCount?: number) => {
      const call = calls[index];

      call.request.views.forEach((view, viewIndex) => {
        const first = (dateTicks(view.begin)! - origin) / period;
        const end = (dateTicks(view.end)! - origin) / period;
        const count =
          pointCount ?? (end - first <= BigInt(view.maxPoints) ? Number(end - first) : 2);
        const indices = Array.from(
          { length: count },
          (_, i) => first + (BigInt(i) * (end - first - 1n)) / BigInt(Math.max(1, count - 1)),
        );

        for (let resourceIndex = 0; resourceIndex < resources; resourceIndex++) {
          call.emit({
            kind: "data",
            complete: true,
            resourceIndex,
            viewIndex,
            indices: new BigInt64Array(indices),
            values: new Float32Array(indices.length).fill(value),
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
      await settle();
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
      await settle();
      assert.equal(provider.loading, true);
      assert.ok(states.every((state) => state.loading && state.progress === 0));
      assert.equal(provider.pointsFor("main", 0, 100n, 9900n)?.[1], 1);
      t.mock.timers.tick(50);
      assert.equal(calls.length, 2);
      assert.equal(dateTicks(calls[1].request.views[0].begin), 100n);
      finish(1, 7);
      await settle();
      assert.equal(provider.loading, false);
      assert.equal(provider.pointsFor("main", 0, 100n, 9900n)?.[1], 7);
    } finally {
      provider.dispose();
    }
  });

  it("replaces foreground work that no longer covers the view or is too coarse", (t) => {
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
    const { provider, calls, finish, view } = prefetchFixture(2);

    try {
      provider.requestViews(view(0n, 10000n), true);
      t.mock.timers.tick(1);
      finish(0, 1);
      await settle();
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
      await settle();

      for (let resourceIndex = 0; resourceIndex < 2; resourceIndex++) {
        assert.equal(provider.pointsFor("main", resourceIndex, 2000n, 3000n)?.[1], 8);
      }

      // Even exact bounds of the retained view require a fresh transport.
      provider.requestViews(view(100n, 9900n, 1000));
      assert.equal(provider.loading, true);
      t.mock.timers.tick(50);
      assert.equal(calls.length, 3);
      finish(2, 7);
      await settle();
      assert.equal(provider.loading, false);
      assert.equal(provider.pointsFor("main", 0, 100n, 9900n)?.[1], 7);
    } finally {
      provider.dispose();
    }
  });

  for (const delivery of ["missing-resource", "partial-resource", "complete"] as const) {
    it(`only retains all-resource complete previews on supersession (${delivery})`, async (t) => {
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
        await settle();
        assert.equal(provider.pointsFor("main", 0, 2000n, 3000n), undefined);
      } finally {
        provider.dispose();
      }
    });
  }

  it("replaces rather than accumulates retained views across repeated supersessions", (t) => {
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
    const { provider, calls, view } = prefetchFixture(9);

    try {
      // Deliberately exceed the server aggregate limit to exercise the provider's
      // independent retained-preview guard at its transport boundary.
      provider.requestViews(view(0n, 1000000n, 32768), true);
      t.mock.timers.tick(1);

      for (let resourceIndex = 0; resourceIndex < 9; resourceIndex++) {
        calls[0].emit({
          kind: "data",
          complete: true,
          resourceIndex,
          viewIndex: 0,
          indices: BigInt64Array.from({ length: 32768 }, (_, i) => BigInt(i)),
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
    const { provider, calls, view } = prefetchFixture();

    try {
      provider.requestViews(view(0n, 10000n), true);
      t.mock.timers.tick(1);
      provider.requestViews(view(1n, 9999n));
      calls[0].reject(new Error("old view failed"));
      await settle();
      assert.equal(provider.error, "");
      assert.equal(provider.loading, true);
      assert.equal(provider.progress, 0);
      t.mock.timers.tick(50);
      assert.equal(calls.length, 2);
      assert.equal(dateTicks(calls[1].request.views[0].begin), 1n);
      calls[1].reject(new Error("latest view failed"));
      await settle();
      assert.match(provider.error, /latest view failed/);
      assert.equal(provider.loading, false);
      t.mock.timers.tick(1000);
      assert.equal(calls.length, 2);
    } finally {
      provider.dispose();
    }
  });

  it("retains one-level-coarser main coverage and follows up missing detail", async (t) => {
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
      await settle();
      t.mock.timers.tick(50);

      assert.deepEqual(
        calls[1].request.views.map((view) => view.id),
        ["main", "detail"],
      );

      finish(1);
      await settle();
      assert.equal(provider.loading, false);
    } finally {
      provider.dispose();
    }
  });

  it("clears pending refinement on cancel, disposal, or navigation to cached coverage", async (t) => {
    for (const action of ["cancel", "dispose", "cached"]) {
      const { provider, calls, finish, view } = prefetchFixture();

      try {
        provider.requestViews(view(0n, 1000n), true);
        t.mock.timers.tick(1);
        provider.requestViews(view(1n, 999n));
        finish(0);
        await settle();
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

  for (const outcome of ["success", "failure"] as const) {
    it(`cancels speculative work for foreground gestures and ignores late ${outcome}`, async () => {
      const { provider, calls, finish, view } = prefetchFixture();

      try {
        provider.requestViews(view(), true);
        await delay(5);
        finish(0);
        await delay(280);
        assert.equal(calls.length, 2);

        calls[1].emit({
          kind: "progress",
          progress: 0.1,
          resourceIndex: 0,
          range: { hasValue: true, minimum: -99, maximum: 99 },
        });

        assert.equal(provider.progress, 1);
        assert.equal(provider.ranges.size, 0);
        provider.requestViews(view(7000000n, 7100000n), true);
        assert.equal(calls[1].signal.aborted, true);
        await delay(5);
        assert.equal(calls.length, 3);

        if (outcome === "success") {
          finish(1, 99);
        } else {
          calls[1].reject(new Error("stale speculative failure"));
        }

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
  }

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
    // 100 raw points plus seven two-point views fill the payload budget exactly.
    const { provider, calls, finish, view } = prefetchFixture(1, 0n, 1n, (100 + 7 * 2) * 12);

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
      // Full visited cache suppresses speculation before any network request.
      assert.equal(calls.length, 8);
      provider.requestViews(view(0n, 100n), true);
      await delay(5);
      assert.equal(calls.length, 8);
    } finally {
      provider.dispose();
    }
  });

  it("replaces unvisited speculative entries without evicting a full visited history", async () => {
    const { provider, calls, finish, view } = prefetchFixture(1, 0n, 1n, (7 * 2 + 100) * 12);

    try {
      for (let i = 0; i < 7; i++) {
        provider.requestViews(view(BigInt(i) * 1000000n, BigInt(i) * 1000000n + 500000n), true);
        await delay(5);
        finish(i);
        await delay(5);
      }

      await delay(280);

      // Seven two-point views leave 100 points. The two small zoom-in results fit,
      // then each full-budget zoom-out result must replace speculative coverage.
      for (let i = 7; i < 11; i++) {
        assert.equal(calls.length, i + 1);
        assert.equal(calls[i].request.views[0].maxPoints, 100);
        finish(i, i + 1, i < 9 ? 2 : 100);
        await delay(5);
      }

      const last = calls[10].request.views[0];

      assert.equal(
        provider.pointsFor("main", 0, dateTicks(last.begin)!, dateTicks(last.end)!)?.[1],
        11,
      );

      const first = calls[7].request.views[0];

      // The last, broader prediction is the only speculative payload retained.
      assert.notEqual(
        provider.pointsFor("main", 0, dateTicks(first.begin)!, dateTicks(first.end)!)?.[1],
        8,
      );

      for (let i = 0; i < 7; i++) {
        provider.requestViews(view(BigInt(i) * 1000000n, BigInt(i) * 1000000n + 500000n), true);
        assert.equal(provider.loading, false);
        await delay(5);
        assert.equal(calls.length, 11);
      }
    } finally {
      provider.dispose();
    }
  });

  it("refines clipped boundary buckets", async () => {
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
      await delay(0);
      assert.equal(provider.pointsFor("main", 0, 10n, 90n)?.[1], 3);
    } finally {
      provider.dispose();
    }
  });

  it("renders the finest compatible canonical LOD even when coarser cached coverage was requested", async () => {
    const { provider, calls, finish, view } = prefetchFixture();

    try {
      // Both bounds are compatible; budgets cross a canonical stride boundary.
      provider.requestViews(view(0n, 100n, 16), true);
      await delay(0);
      finish(0, 1);
      await delay(0);
      provider.requestViews(view(0n, 100n, 100), true);
      await delay(0);
      finish(1, 2);
      await delay(0);
      provider.requestViews(view(0n, 100n, 16), true);
      assert.equal(calls.length, 2);
      assert.equal(provider.pointsFor("main", 0, 0n, 100n)?.[1], 2);
      assert.equal(provider.pointsFor("main", 0, 0n, 100n)?.length, 200);
    } finally {
      provider.dispose();
    }
  });

  it("force reload invalidates every covering resolution but preserves unrelated history", async () => {
    const { provider, calls, finish, view } = prefetchFixture();

    try {
      for (const requested of [view(0n, 100n, 16), view(0n, 100n, 100), view(200n, 300n, 100)]) {
        provider.requestViews(requested, true);
        await delay(0);
        finish(calls.length - 1);
        await delay(0);
      }

      provider.requestViews(view(0n, 100n, 16), true);
      assert.equal(provider.pointsFor("main", 0, 0n, 100n)?.[1], 2);
      provider.requestViews(view(0n, 100n, 16), true, true);
      assert.equal(provider.loading, true);
      assert.equal(provider.pointsFor("main", 0, 0n, 100n), undefined);
      await delay(0);
      assert.equal(calls.length, 4);
      finish(3, 9);
      await delay(0);
      assert.equal(provider.pointsFor("main", 0, 0n, 100n)?.[1], 9);
      provider.requestViews(view(200n, 300n, 100), true);
      assert.equal(provider.loading, false);
      assert.equal(calls.length, 4);
      assert.equal(provider.pointsFor("main", 0, 200n, 300n)?.[1], 3);
    } finally {
      provider.dispose();
    }
  });

  for (const outcome of ["success", "failure"] as const) {
    it(`force reload aborts useful foreground work and ignores its late ${outcome}`, async () => {
      const { provider, calls, finish, view } = prefetchFixture();

      try {
        provider.requestViews(view(0n, 100n), true);
        await delay(0);
        finish(0, 1);
        // The complete preview exists, but transport completion has not settled.
        assert.equal(provider.pointsFor("main", 0, 0n, 100n)?.[1], 1);
        provider.requestViews(view(0n, 100n), true, true);
        assert.equal(calls[0].signal.aborted, true);
        assert.equal(provider.pointsFor("main", 0, 0n, 100n), undefined);
        await delay(0);
        assert.equal(calls.length, 2);

        // Supersede a second useful request before it completes.
        provider.requestViews(view(0n, 100n), true, true);
        assert.equal(calls[1].signal.aborted, true);
        await delay(0);
        assert.equal(calls.length, 3);

        calls[1].emit({
          kind: "progress",
          progress: 0.9,
          resourceIndex: 0,
          range: { hasValue: true, minimum: -99, maximum: 99 },
        });

        if (outcome === "success") {
          finish(1, 99);
        } else {
          calls[1].reject(new Error("stale failure"));
        }

        await delay(0);
        assert.equal(provider.loading, true);
        assert.equal(provider.progress, 0);
        assert.equal(provider.error, "");
        assert.equal(provider.ranges.size, 0);
        assert.equal(provider.pointsFor("main", 0, 0n, 100n), undefined);
        finish(2, 7);
        await delay(0);
        assert.equal(provider.loading, false);
        assert.equal(provider.pointsFor("main", 0, 0n, 100n)?.[1], 7);
      } finally {
        provider.dispose();
      }
    });
  }

  it("keeps progress monotonic and publishes per-resource ranges and all-resource previews", async () => {
    const { provider, calls, view } = prefetchFixture(2, 0n, 1n, 4096, 100n);
    const authoritative = { hasValue: true, minimum: -10, maximum: 10 };

    try {
      provider.requestViews(view(0n, 100n, 16), true);
      await delay(0);
      calls[0].emit({ kind: "progress", progress: 0.7, resourceIndex: 0, range: authoritative });
      calls[0].emit({ kind: "progress", progress: 0.2, resourceIndex: 1 });

      calls[0].emit({
        kind: "progress",
        progress: 0.3,
        resourceIndex: -1,
        range: { hasValue: true, minimum: -99, maximum: 99 },
      });

      assert.equal(provider.progress, 0.7);
      assert.deepEqual([...provider.ranges], [[0, authoritative]]);

      const point = (resourceIndex: number, complete: boolean): VisualizationFrame => ({
        kind: "data",
        complete,
        resourceIndex,
        viewIndex: 0,
        indices: new BigInt64Array([0n, 50n, 99n]),
        values: new Float32Array([2, NaN, 4]),
      });

      calls[0].emit(point(0, true));
      assert.equal(provider.pointsFor("main", 0, 0n, 100n), undefined);
      calls[0].emit(point(1, false));
      assert.equal(provider.pointsFor("main", 0, 0n, 100n)?.[1], 2);
      assert.equal(provider.pointsFor("main", 1, 0n, 100n)?.[1], 2);
      assert.equal(provider.loading, true);
      calls[0].emit(point(1, true));
      calls[0].resolve();
      await delay(0);
      assert.equal(provider.progress, 1);
      assert.equal(provider.loading, false);

      // Finite full-domain points fill only the missing range, not server extrema.
      assert.deepEqual(
        [...provider.ranges],
        [
          [0, authoritative],
          [1, { hasValue: true, minimum: 2, maximum: 4 }],
        ],
      );
    } finally {
      provider.dispose();
    }
  });

  it("does not invent a full-domain range from all-nonfinite drawing points", async () => {
    const { provider, calls, view } = prefetchFixture(1, 0n, 1n, 4096, 100n);

    try {
      provider.requestViews(view(0n, 100n, 16), true);
      await delay(0);

      calls[0].emit({
        kind: "data",
        complete: true,
        resourceIndex: 0,
        viewIndex: 0,
        indices: new BigInt64Array([0n]),
        values: new Float32Array([NaN]),
      });

      calls[0].resolve();
      await delay(0);
      assert.equal(provider.loading, false);
      assert.equal(provider.ranges.size, 0);
    } finally {
      provider.dispose();
    }
  });

  it("keeps published global ranges on failure and cancel but ignores aborted progress", async () => {
    const { provider, calls, view } = prefetchFixture();
    const range = { hasValue: true, minimum: -4, maximum: 8 };

    try {
      provider.requestViews(view(0n, 100n), true);
      await delay(0);
      calls[0].emit({ kind: "progress", progress: 0.5, resourceIndex: 0, range });
      calls[0].reject(new Error("failed after range"));
      await delay(0);
      assert.deepEqual(provider.ranges.get(0), range);
      provider.requestViews(view(0n, 100n), true);
      await delay(0);
      assert.equal(calls.length, 2);
      assert.equal(provider.progress, 0);
      provider.cancel();

      calls[1].emit({
        kind: "progress",
        progress: 1,
        resourceIndex: 0,
        range: { hasValue: true, minimum: -99, maximum: 99 },
      });

      assert.equal(provider.progress, 0);
      assert.deepEqual(provider.ranges.get(0), range);
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
    const { provider, calls, finish } = prefetchFixture(1, 0n, 1n, 4096, 100n);

    try {
      provider.requestViews([{ id: "overview", begin: 0n, end: 100n, maxPoints: 16 }], true);
      await delay(5);
      finish(0, 1);
      await delay(0);
      provider.requestViews([{ id: "main", begin: 10n, end: 20n, maxPoints: 16 }], true);
      await delay(5);
      calls[1].emit({ kind: "progress", progress: 0.25, resourceIndex: -1 });

      calls[1].emit({
        kind: "data",
        complete: false,
        resourceIndex: 0,
        viewIndex: 0,
        indices: BigInt64Array.from({ length: 10 }, (_, i) => 10n + BigInt(i)),
        values: new Float32Array(10).fill(8),
      });

      assert.equal(provider.loading, true);
      assert.equal(provider.progress, 0.25);
      assert.equal(provider.pointsFor("main", 0, 10n, 20n)?.[1], 8);
      calls[1].reject(new Error("truncated"));
      await delay(5);
      assert.equal(provider.pointsFor("main", 0, 10n, 20n)?.[1], 1);
    } finally {
      provider.dispose();
    }
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
    const { provider, calls, finish } = prefetchFixture(1, 0n, 1n, 4096, 100n);
    const view = (begin = 0n, end = 100n) => [{ id: "main", begin, end, maxPoints: 16 }];

    try {
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
    } finally {
      provider.dispose();
    }
  });

  it("evicts narrow history under byte pressure but protects the domain and current view", async () => {
    // Space for the two-point domain plus one twenty-sample raw view.
    const { provider, calls, finish, view } = prefetchFixture(1, 0n, 1n, 22 * 12, 1000n);

    try {
      for (const [begin, end] of [
        [0n, 1000n],
        [100n, 120n],
        [200n, 210n],
      ]) {
        provider.requestViews(view(begin, end), true);
        await delay(0);
        finish(calls.length - 1);
        await delay(0);
      }

      // The protected new view remains even though it is narrower than history.
      provider.requestViews(view(200n, 210n), true);
      assert.equal(provider.loading, false);
      assert.equal(calls.length, 3);
      provider.requestViews(view(100n, 120n), true);
      await delay(0);
      assert.equal(calls.length, 4);
      finish(3);
      await delay(0);
      provider.requestViews(view(0n, 1000n), true);
      assert.equal(provider.loading, false);
      assert.equal(calls.length, 4);
      provider.requestViews(view(300n, 310n));
      provider.cancel();
      await delay(1000);
      assert.equal(calls.length, 4);
      assert.equal(provider.pointsFor("overview", 0, 0n, 1000n)?.[1], 1);
    } finally {
      provider.dispose();
    }
  });

  it("bounds tiny cached views by entry count even with ample byte capacity", async () => {
    const { provider, calls, finish, view } = prefetchFixture();

    try {
      for (let i = 0; i < 1025; i++) {
        provider.requestViews(view(BigInt(i * 2), BigInt(i * 2 + 1)), true);
        await delay(0);
        finish(i);
        await delay(0);
      }

      provider.requestViews(view(2048n, 2049n), true);
      assert.equal(provider.loading, false);
      assert.equal(calls.length, 1025);
      provider.requestViews(view(0n, 1n), true);
      await delay(0);
      assert.equal(calls.length, 1026);
    } finally {
      provider.dispose();
    }
  });
});

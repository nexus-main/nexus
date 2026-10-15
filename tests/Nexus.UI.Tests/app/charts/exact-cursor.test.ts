import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { setImmediate as settle } from "node:timers/promises";
import { it, type TestContext } from "node:test";
import { decodeCursor, ExactCursor } from "../../../../src/Nexus.UI/src/app/charts/exact-cursor.ts";

const require = createRequire(new URL("../../../../src/Nexus.UI/package.json", import.meta.url));
const arrow: typeof import("../../../../src/Nexus.UI/node_modules/apache-arrow/Arrow.node") = require("apache-arrow");

interface Row {
  resource: number | null;
  offset: bigint;
  values: (number | null)[] | null;
}

function ipc(rows: Row[], invalidType = false): Uint8Array {
  const table = new arrow.Table({
    resourceIndex: arrow.vectorFromArray(
      rows.map((row) => row.resource),
      invalidType ? new arrow.Float32() : new arrow.Int32(),
    ),
    offset: arrow.vectorFromArray(
      rows.map((row) => row.offset),
      new arrow.Int64(),
    ),
    values: arrow.vectorFromArray(
      rows.map((row) => row.values),
      new arrow.List(new arrow.Field("item", new arrow.Float32())),
    ),
  });

  // Each row is a separate batch, so resource offsets must survive batch boundaries.
  return arrow.tableToIPC(
    new arrow.Table(
      table.schema,
      rows.flatMap((_, i) => table.slice(i, i + 1).batches),
    ),
  );
}

function response(bytes: Uint8Array, fragment = 7): Response {
  let position = 0;

  return new Response(
    new ReadableStream({
      pull(controller) {
        if (position >= bytes.length) {
          controller.close();
        } else {
          controller.enqueue(bytes.slice(position, position + fragment));
          position += fragment;
        }
      },
    }),
  );
}

it("decodes interleaved resources across fragmented Arrow batches with independent offsets", async () => {
  const body = response(
    ipc([
      { resource: 1, offset: 0n, values: [10] },
      { resource: 0, offset: 0n, values: [1, NaN] },
      { resource: 1, offset: 1n, values: [20, 30] },
      { resource: 0, offset: 2n, values: [3] },
    ]),
  );
  const values = await decodeCursor(body, 2, 3, new AbortController().signal);

  assert.deepEqual(values, [new Float32Array([1, NaN, 3]), new Float32Array([10, 20, 30])]);
  assert.equal(body.body!.locked, false);
});

const invalidRows: { name: string; rows: Row[]; message: string }[] = [
  {
    name: "truncated resource",
    rows: [
      { resource: 0, offset: 0n, values: [1] },
      { resource: 1, offset: 0n, values: [10, 20, 30] },
    ],
    message: "Truncated exact cursor response.",
  },
  {
    name: "missing resource",
    rows: [{ resource: 0, offset: 0n, values: [1, 2, 3] }],
    message: "Truncated exact cursor response.",
  },
  {
    name: "offset gap",
    rows: [{ resource: 0, offset: 1n, values: [1, 2, 3] }],
    message: "Invalid exact cursor samples or offsets.",
  },
  {
    name: "overlapping batch",
    rows: [
      { resource: 0, offset: 0n, values: [1, 2] },
      { resource: 0, offset: 1n, values: [3] },
    ],
    message: "Invalid exact cursor samples or offsets.",
  },
  {
    name: "null sample",
    rows: [{ resource: 0, offset: 0n, values: [1, null, 3] }],
    message: "Invalid exact cursor samples or offsets.",
  },
  {
    name: "null list",
    rows: [{ resource: 0, offset: 0n, values: null }],
    message: "Invalid exact cursor samples or offsets.",
  },
  {
    name: "negative resource",
    rows: [{ resource: -1, offset: 0n, values: [1] }],
    message: "Invalid exact cursor samples or offsets.",
  },
  {
    name: "out-of-range resource",
    rows: [{ resource: 2, offset: 0n, values: [1] }],
    message: "Invalid exact cursor samples or offsets.",
  },
  {
    name: "null resource",
    rows: [{ resource: null, offset: 0n, values: [1] }],
    message: "Invalid exact cursor samples or offsets.",
  },
  {
    name: "cumulative sample overflow",
    rows: [
      { resource: 0, offset: 0n, values: [1, 2] },
      { resource: 0, offset: 2n, values: [3, 4] },
    ],
    message: "Invalid exact cursor samples or offsets.",
  },
];

for (const { name, rows, message } of invalidRows) {
  it(`rejects ${name} and unlocks the exact Arrow stream`, async () => {
    const body = response(ipc(rows));

    await assert.rejects(decodeCursor(body, 2, 3, new AbortController().signal), { message });
    assert.equal(body.body!.locked, false);
  });
}

it("rejects a wrong exact Arrow schema and releases the reader", async () => {
  const body = response(ipc([{ resource: 0, offset: 0n, values: [1] }], true));

  await assert.rejects(decodeCursor(body, 1, 1, new AbortController().signal), {
    message: "Invalid exact cursor Arrow schema.",
  });

  assert.equal(body.body!.locked, false);
});

it("enforces neighborhood budgets before locking the body", async () => {
  for (const [resources, count] of [
    [0, 1],
    [101, 1],
    [1, 0],
    [1, 33],
    [1, 1.5],
  ]) {
    const body = response(new Uint8Array());

    await assert.rejects(decodeCursor(body, resources, count, new AbortController().signal), {
      message: "Cursor read exceeds the neighborhood budget.",
    });

    assert.equal(body.body!.locked, false);
  }

  await assert.rejects(decodeCursor(new Response(null), 1, 1, new AbortController().signal), {
    message: "Missing cursor response body.",
  });
});

it("accepts the maximum resource and sample counts", async () => {
  const body = response(
    ipc(
      Array.from({ length: 100 }, (_, resource) => ({
        resource,
        offset: 0n,
        values: Array.from({ length: 32 }, (_, i) => resource * 100 + i),
      })),
    ),
    4096,
  );
  const values = await decodeCursor(body, 100, 32, new AbortController().signal);

  assert.deepEqual(
    values,
    Array.from({ length: 100 }, (_, resource) =>
      Float32Array.from({ length: 32 }, (_, i) => resource * 100 + i),
    ),
  );

  assert.equal(body.body!.locked, false);
});

it("rejects an oversized byte chunk before Arrow parsing and unlocks the stream", async () => {
  const bytes = new Uint8Array(1024 * 1024 + 1);
  const body = response(bytes, bytes.length);

  await assert.rejects(decodeCursor(body, 1, 1, new AbortController().signal), {
    message: "Cursor response exceeds its byte budget.",
  });

  assert.equal(body.body!.locked, false);
});

it("aborts a stalled exact Arrow read with AbortError and unlocks the cancelled body", async () => {
  const controller = new AbortController();
  let reason: unknown;
  const body = new ReadableStream<Uint8Array>({
    cancel(value) {
      reason = value;
    },
  });
  const task = decodeCursor(new Response(body), 1, 1, controller.signal);

  try {
    await settle();
    assert.equal(body.locked, true);
    controller.abort();
    await assert.rejects(task, { name: "AbortError" });
    assert.equal(reason, controller.signal.reason);
    assert.equal(body.locked, false);
  } finally {
    controller.abort();
    await task.catch(() => {});
  }
});

function fixture(t: TestContext, length = 100n) {
  t.mock.timers.enable({ apis: ["setTimeout"] });

  const calls: {
    begin: string;
    end: string;
    paths: string[];
    count: number;
    signal: AbortSignal;
    resolve: (values: Float32Array[]) => void;
    reject: (error: Error) => void;
  }[] = [];
  const notify = t.mock.fn();
  const origin = 621355968001234567n; // 1970-01-01T00:00:00.1234567Z
  const period = 2500000n;
  const cursor = new ExactCursor(
    origin,
    origin + length * period,
    period,
    ["/b", "/a"],
    (begin, end, paths, count, signal) =>
      new Promise((resolve, reject) => {
        calls.push({ begin, end, paths: [...paths], count, signal, resolve, reject });
      }),
    notify,
  );
  const finish = (call: number) =>
    calls[call].resolve([
      Float32Array.from({ length: calls[call].count }, (_, i) => i + 10),
      Float32Array.from({ length: calls[call].count }, (_, i) => i + 100),
    ]);

  return { cursor, calls, notify, finish };
}

it("debounces and deduplicates 32-sample neighborhoods with exact time bounds and resource mapping", async (t) => {
  const { cursor, calls, notify, finish } = fixture(t);

  try {
    cursor.request(2n);
    t.mock.timers.tick(60);
    cursor.request(40n);
    t.mock.timers.tick(119);
    assert.equal(calls.length, 0);
    cursor.request(63n);
    t.mock.timers.tick(1);
    assert.equal(calls.length, 1);

    assert.equal(calls[0].begin, "1970-01-01T00:00:08.1234567Z");
    assert.equal(calls[0].end, "1970-01-01T00:00:16.1234567Z");
    assert.deepEqual(calls[0].paths, ["/b", "/a"]);
    assert.equal(calls[0].count, 32);

    cursor.request(32n);
    t.mock.timers.tick(120);
    assert.equal(calls.length, 1);
    assert.equal(calls[0].signal.aborted, false);
    assert.equal(cursor.value(0, 40n), undefined);
    finish(0);
    await settle();
    assert.equal(notify.mock.callCount(), 1);
    assert.equal(cursor.value(0, 32n), 10);
    assert.equal(cursor.value(0, 63n), 41);
    assert.equal(cursor.value(1, 40n), 108);
    assert.equal(cursor.value(0, 64n), undefined);

    cursor.request(99n);
    t.mock.timers.tick(120);
    assert.equal(calls.length, 2);
    assert.equal(calls[1].begin, "1970-01-01T00:00:24.1234567Z");
    assert.equal(calls[1].end, "1970-01-01T00:00:25.1234567Z");
    assert.equal(calls[1].count, 4);
    assert.deepEqual(calls[1].paths, ["/b", "/a"]);
    finish(1);
    await settle();
    assert.equal(cursor.value(0, 96n), 10);
    assert.equal(cursor.value(1, 99n), 103);
    assert.equal(cursor.value(0, 100n), undefined);
    assert.equal(notify.mock.callCount(), 2);

    cursor.request(1n);
    cursor.request(40n);
    t.mock.timers.tick(120);
    assert.equal(calls.length, 2);
    assert.equal(cursor.value(1, 40n), 108);
    assert.equal(notify.mock.callCount(), 2);
  } finally {
    cursor.dispose();
  }
});

for (const index of [-1n, 100n]) {
  it(`cancels pending and active requests for out-of-range index ${index}`, async (t) => {
    const { cursor, calls, notify, finish } = fixture(t);

    try {
      cursor.request(0n);
      cursor.request(index);
      t.mock.timers.tick(120);
      assert.equal(calls.length, 0);
      cursor.request(0n);
      t.mock.timers.tick(120);
      assert.equal(calls.length, 1);
      cursor.request(index);
      assert.equal(calls[0].signal.aborted, true);
      finish(0);
      await settle();
      t.mock.timers.tick(120);
      assert.equal(calls.length, 1);
      assert.equal(cursor.value(0, 0n), undefined);
      assert.equal(cursor.error, "");
      assert.equal(notify.mock.callCount(), 0);
    } finally {
      cursor.dispose();
    }
  });
}

it("supersedes active neighborhoods without letting stale completion clear the new controller", async (t) => {
  const { cursor, calls, notify, finish } = fixture(t);

  try {
    cursor.request(0n);
    t.mock.timers.tick(120);
    cursor.request(32n);
    assert.equal(calls[0].signal.aborted, true);
    t.mock.timers.tick(120);
    assert.equal(calls.length, 2);
    finish(0);
    await settle();
    assert.equal(cursor.value(0, 0n), undefined);
    assert.equal(notify.mock.callCount(), 0);
    assert.equal(calls[1].signal.aborted, false);
    cursor.request(64n);
    assert.equal(calls[1].signal.aborted, true);
    finish(1);
    await settle();
    assert.equal(cursor.value(0, 32n), undefined);
    assert.equal(notify.mock.callCount(), 0);
    t.mock.timers.tick(120);
    assert.equal(calls.length, 3);
    finish(2);
    await settle();
    assert.equal(cursor.value(0, 64n), 10);
    assert.equal(notify.mock.callCount(), 1);
  } finally {
    cursor.dispose();
  }
});

for (const outcome of ["resolve", "reject"] as const) {
  it(`ignores stale ${outcome} after cancellation and a newer successful request`, async (t) => {
    const { cursor, calls, notify, finish } = fixture(t);

    try {
      cursor.request(0n);
      t.mock.timers.tick(120);
      cursor.cancel();
      assert.equal(calls[0].signal.aborted, true);
      cursor.request(0n);
      t.mock.timers.tick(120);
      assert.equal(calls.length, 2);
      finish(1);
      await settle();

      if (outcome === "resolve") {
        calls[0].resolve([new Float32Array(32).fill(-1), new Float32Array(32).fill(-2)]);
      } else {
        calls[0].reject(new Error("stale failure"));
      }

      await settle();
      assert.equal(cursor.value(0, 0n), 10);
      assert.equal(cursor.value(1, 0n), 100);
      assert.equal(cursor.error, "");
      assert.equal(notify.mock.callCount(), 1);
    } finally {
      cursor.dispose();
    }
  });
}

it("evicts the least recently used of eight neighborhoods, preserving a touched cache entry", async (t) => {
  const { cursor, calls, finish } = fixture(t, 320n);

  try {
    for (let i = 0; i < 8; i++) {
      cursor.request(BigInt(i * 32));
      t.mock.timers.tick(120);
      finish(i);
      await settle();
    }

    cursor.request(0n);
    t.mock.timers.tick(120);
    assert.equal(calls.length, 8);
    cursor.request(256n);
    t.mock.timers.tick(120);
    finish(8);
    await settle();
    assert.equal(cursor.value(0, 0n), 10);
    assert.equal(cursor.value(0, 32n), undefined);
    assert.equal(cursor.value(0, 64n), 10);
    cursor.request(32n);
    t.mock.timers.tick(120);
    assert.equal(calls.length, 10);
    finish(9);
    await settle();
    assert.equal(cursor.value(0, 32n), 10);
  } finally {
    cursor.dispose();
  }
});

for (const failure of ["transport", "resource count", "sample count"] as const) {
  it(`reports ${failure} errors without caching partial data and recovers on retry`, async (t) => {
    const { cursor, calls, notify, finish } = fixture(t);

    try {
      cursor.request(0n);
      t.mock.timers.tick(120);

      if (failure === "transport") {
        calls[0].reject(new Error("network failure"));
      } else {
        calls[0].resolve(
          failure === "resource count"
            ? [new Float32Array(32)]
            : [new Float32Array(32), new Float32Array(31)],
        );
      }

      await settle();

      assert.equal(
        cursor.error,
        failure === "transport"
          ? "Error: network failure"
          : "Error: Incomplete exact cursor neighborhood.",
      );

      assert.equal(notify.mock.callCount(), 1);
      assert.equal(cursor.value(0, 0n), undefined);
      assert.equal(cursor.value(1, 0n), undefined);
      cursor.cancel();
      cursor.request(0n);
      assert.equal(cursor.error, "");
      t.mock.timers.tick(120);
      assert.equal(calls.length, 2);
      finish(1);
      await settle();
      assert.equal(cursor.error, "");
      assert.equal(cursor.value(1, 0n), 100);
      assert.equal(notify.mock.callCount(), 2);
    } finally {
      cursor.dispose();
    }
  });
}

for (const active of [false, true]) {
  it(`disposes ${active ? "active" : "pending"} work and clears cached values without notifying`, async (t) => {
    const { cursor, calls, notify, finish } = fixture(t);

    try {
      cursor.request(0n);
      t.mock.timers.tick(120);
      finish(0);
      await settle();
      assert.equal(cursor.value(0, 0n), 10);
      cursor.request(32n);

      if (active) {
        t.mock.timers.tick(120);
      }

      cursor.dispose();

      if (active) {
        assert.equal(calls[1].signal.aborted, true);
        finish(1);
      }

      await settle();
      t.mock.timers.tick(1000);
      assert.equal(calls.length, active ? 2 : 1);
      assert.equal(cursor.value(0, 0n), undefined);
      assert.equal(cursor.value(1, 0n), undefined);
      assert.equal(cursor.value(0, 32n), undefined);
      assert.equal(cursor.error, "");
      assert.equal(notify.mock.callCount(), 1);
    } finally {
      cursor.dispose();
    }
  });
}

import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { setImmediate as settle } from "node:timers/promises";
import { it } from "node:test";
import { decodeRawStream } from "../../../../src/Nexus.UI/src/app/charts/raw-stream-decoder.ts";

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
  const values = await decodeRawStream(body, 2, 3, new AbortController().signal);

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
    message: "Truncated raw stream response.",
  },
  {
    name: "missing resource",
    rows: [{ resource: 0, offset: 0n, values: [1, 2, 3] }],
    message: "Truncated raw stream response.",
  },
  {
    name: "offset gap",
    rows: [{ resource: 0, offset: 1n, values: [1, 2, 3] }],
    message: "Invalid raw stream samples or offsets.",
  },
  {
    name: "overlapping batch",
    rows: [
      { resource: 0, offset: 0n, values: [1, 2] },
      { resource: 0, offset: 1n, values: [3] },
    ],
    message: "Invalid raw stream samples or offsets.",
  },
  {
    name: "null sample",
    rows: [{ resource: 0, offset: 0n, values: [1, null, 3] }],
    message: "Invalid raw stream samples or offsets.",
  },
  {
    name: "null list",
    rows: [{ resource: 0, offset: 0n, values: null }],
    message: "Invalid raw stream samples or offsets.",
  },
  {
    name: "negative resource",
    rows: [{ resource: -1, offset: 0n, values: [1] }],
    message: "Invalid raw stream samples or offsets.",
  },
  {
    name: "out-of-range resource",
    rows: [{ resource: 2, offset: 0n, values: [1] }],
    message: "Invalid raw stream samples or offsets.",
  },
  {
    name: "null resource",
    rows: [{ resource: null, offset: 0n, values: [1] }],
    message: "Invalid raw stream samples or offsets.",
  },
  {
    name: "cumulative sample overflow",
    rows: [
      { resource: 0, offset: 0n, values: [1, 2] },
      { resource: 0, offset: 2n, values: [3, 4] },
    ],
    message: "Invalid raw stream samples or offsets.",
  },
];

for (const { name, rows, message } of invalidRows) {
  it(`rejects ${name} and unlocks the raw Arrow stream`, async () => {
    const body = response(ipc(rows));

    await assert.rejects(decodeRawStream(body, 2, 3, new AbortController().signal), { message });
    assert.equal(body.body!.locked, false);
  });
}

it("rejects a wrong raw Arrow schema and releases the reader", async () => {
  const body = response(ipc([{ resource: 0, offset: 0n, values: [1] }], true));

  await assert.rejects(decodeRawStream(body, 1, 1, new AbortController().signal), {
    message: "Invalid raw stream Arrow schema.",
  });

  assert.equal(body.body!.locked, false);
});

it("enforces raw stream budgets before locking the body", async () => {
  for (const [resources, count] of [
    [0, 1],
    [101, 1],
    [1, 0],
    [1, 33],
    [1, 1.5],
  ]) {
    const body = response(new Uint8Array());

    await assert.rejects(decodeRawStream(body, resources, count, new AbortController().signal), {
      message: "Raw stream read exceeds the sample budget.",
    });

    assert.equal(body.body!.locked, false);
  }

  await assert.rejects(decodeRawStream(new Response(null), 1, 1, new AbortController().signal), {
    message: "Missing raw stream response body.",
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
  const values = await decodeRawStream(body, 100, 32, new AbortController().signal);

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

  await assert.rejects(decodeRawStream(body, 1, 1, new AbortController().signal), {
    message: "Raw stream response exceeds its byte budget.",
  });

  assert.equal(body.body!.locked, false);
});

it("aborts a stalled raw Arrow read with AbortError and unlocks the cancelled body", async () => {
  const controller = new AbortController();
  let reason: unknown;
  const body = new ReadableStream<Uint8Array>({
    cancel(value) {
      reason = value;
    },
  });
  const task = decodeRawStream(new Response(body), 1, 1, controller.signal);

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

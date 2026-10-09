import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { setTimeout as delay } from "node:timers/promises";
import { it } from "node:test";
import { decodeCursor, ExactCursor } from "../../../../src/Nexus.UI/src/app/charts/exact-cursor.ts";
import { readFileSync } from "node:fs";

it("wires exact cursor cancellation and mode-switch navigation preservation into the chart", () => {
  const chart = readFileSync(
    new URL(
      "../../../../src/Nexus.UI/src/app/charts/visualization-chart.component.ts",
      import.meta.url,
    ),
    "utf8",
  );
  const template = readFileSync(
    new URL(
      "../../../../src/Nexus.UI/src/app/charts/visualization-chart.component.html",
      import.meta.url,
    ),
    "utf8",
  );
  const app = readFileSync(
    new URL("../../../../src/Nexus.UI/src/app/app.component.ts", import.meta.url),
    "utf8",
  );

  assert.match(chart, /cursor\.request\(/);
  assert.match(chart, /cursor\.value\(/);
  assert.match(chart, /this\.data\?\.navigation\?\.viewport/);
  assert.match(app, /data\.navigation = existing\.navigation/);
  assert.match(template, /\(mouseleave\)="cursorLeave\(\)"/);
});

const require = createRequire(new URL("../../../../src/Nexus.UI/package.json", import.meta.url));
const arrow: typeof import("../../../../src/Nexus.UI/node_modules/apache-arrow/Arrow.node") = require("apache-arrow");

it("decodes exact raw neighborhoods and rejects truncation/null/offset errors", async () => {
  for (const [offset, values, valid] of [
    [0n, [1, NaN, 3], true],
    [1n, [1, 2, 3], false],
    [0n, [1], false],
    [0n, [1, null, 3], false],
  ] as const) {
    const table = new arrow.Table({
      resourceIndex: arrow.vectorFromArray([0], new arrow.Int32()),
      offset: arrow.vectorFromArray([offset], new arrow.Int64()),
      values: arrow.vectorFromArray(
        [values],
        new arrow.List(new arrow.Field("item", new arrow.Float32())),
      ),
    });
    const bytes = arrow.tableToIPC(table);
    let position = 0;
    const response = new Response(
      new ReadableStream({
        pull(controller) {
          if (position === bytes.length) {
            controller.close();
          } else {
            const end = Math.min(position + 7, bytes.length);

            controller.enqueue(bytes.slice(position, end));
            position = end;
          }
        },
      }),
    );
    const task = decodeCursor(response, 1, 3, new AbortController().signal);

    if (valid) {
      assert.deepEqual([...(await task)[0]], [1, NaN, 3]);
    } else {
      await assert.rejects(task);
    }
  }
});

it("debounces exact cursor reads, bounds neighborhoods, reuses cache and ignores cancelled replies", async () => {
  const calls: { count: number; signal: AbortSignal; resolve: (values: Float32Array[]) => void }[] =
    [];
  let updates = 0;
  const cursor = new ExactCursor(
    0n,
    100n,
    1n,
    ["/a"],
    async (_begin, _end, _paths, count, signal) =>
      new Promise((resolve) => {
        calls.push({ count, signal, resolve });
      }),
    () => updates++,
  );

  cursor.request(2n);
  cursor.request(40n);
  await delay(140);
  assert.equal(calls.length, 1);
  cursor.request(99n);
  assert.equal(calls[0].signal.aborted, true);
  calls[0].resolve([new Float32Array(32).fill(8)]);
  await delay(140);
  assert.equal(calls[1].count, 4);
  calls[1].resolve([new Float32Array([1, 2, 3, 4])]);
  await delay(5);
  assert.equal(updates, 1);
  assert.equal(cursor.value(0, 99n), 4);
  assert.equal(cursor.value(0, 40n), undefined);
  cursor.request(1n);
  cursor.request(99n);
  await delay(140);
  assert.equal(calls.length, 2);
  cursor.dispose();
});

it("aborts stalled exact Arrow streams", async () => {
  const controller = new AbortController();
  const body = new ReadableStream<Uint8Array>();
  const task = decodeCursor(new Response(body), 1, 1, controller.signal);

  controller.abort();
  await assert.rejects(task);
  assert.equal(body.locked, false);
});

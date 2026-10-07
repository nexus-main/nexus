import { AsyncByteStream, RecordBatchReader } from "apache-arrow";
import type { Schema } from "apache-arrow";
import { formatTime } from "./chart-math.ts";

export type CursorTransport = (
  begin: string,
  end: string,
  paths: string[],
  count: number,
  signal: AbortSignal,
) => Promise<Float32Array[]>;

export async function decodeCursor(
  response: Response,
  resources: number,
  count: number,
  signal: AbortSignal,
): Promise<Float32Array[]> {
  if (!Number.isInteger(count) || count < 1 || count > 32 || resources < 1 || resources > 100) {
    throw new Error("Cursor read exceeds the neighborhood budget.");
  }

  const body = response.body?.getReader();

  if (!body) {
    throw new Error("Missing cursor response body.");
  }

  const values = Array.from({ length: resources }, () => new Float32Array(count));
  const offsets = new Uint32Array(resources);
  let reader: RecordBatchReader | undefined;
  const cancel = () => {
    void body.cancel(signal.reason).catch(() => {});
  };

  signal.addEventListener("abort", cancel, { once: true });

  try {
    async function* bytes() {
      let received = 0;

      while (true) {
        signal.throwIfAborted();
        const result = await body!.read();

        signal.throwIfAborted();

        if (result.done) {
          return;
        }

        received += result.value.byteLength;

        if (received > 1024 * 1024) {
          throw new Error("Cursor response exceeds its byte budget.");
        }

        yield result.value;
      }
    }

    reader = await RecordBatchReader.from(new AsyncByteStream(bytes()));
    await reader.open({ autoDestroy: false });

    const validate = (schema: Schema) => {
      const expected = [
        ["resourceIndex", "Int32"],
        ["offset", "Int64"],
        ["values", "List<Float32>"],
      ];

      if (
        schema.fields.length !== 3 ||
        expected.some(
          ([name, type], i) =>
            schema.fields[i].name !== name || schema.fields[i].type.toString() !== type,
        )
      ) {
        throw new Error("Invalid exact cursor Arrow schema.");
      }
    };

    validate(reader.schema);

    for await (const batch of reader) {
      validate(batch.schema);

      for (let row = 0; row < batch.numRows; row++) {
        signal.throwIfAborted();
        const resource = batch.getChildAt(0)!.get(row);
        const offset = batch.getChildAt(1)!.get(row);
        const samples = batch.getChildAt(2)!.get(row);

        if (
          !Number.isInteger(resource) ||
          resource < 0 ||
          resource >= resources ||
          offset !== BigInt(offsets[resource]) ||
          !samples ||
          samples.nullCount ||
          samples.length > count - offsets[resource]
        ) {
          throw new Error("Invalid exact cursor samples or offsets.");
        }

        for (const value of samples) {
          values[resource][offsets[resource]++] = value;
        }
      }
    }

    signal.throwIfAborted();

    if (offsets.some((offset) => offset !== count)) {
      throw new Error("Truncated exact cursor response.");
    }

    return values;
  } finally {
    signal.removeEventListener("abort", cancel);
    cancel();

    try {
      await reader?.cancel();
    } finally {
      body.releaseLock();
    }
  }
}

export class ExactCursor {
  error = "";
  private timer?: ReturnType<typeof setTimeout>;
  private controller?: AbortController;
  private generation = 0;
  private requested?: bigint;
  private readonly cache = new Map<bigint, Float32Array[]>();
  private readonly begin: bigint;
  private readonly period: bigint;
  private readonly length: bigint;
  private readonly paths: string[];
  private readonly transport: CursorTransport;
  private readonly notify: () => void;

  constructor(
    begin: bigint,
    end: bigint,
    period: bigint,
    paths: string[],
    transport: CursorTransport,
    notify: () => void,
  ) {
    this.begin = begin;
    this.period = period;
    this.length = (end - begin) / period;
    this.paths = paths;
    this.transport = transport;
    this.notify = notify;
  }

  value(resource: number, index: bigint): number | undefined {
    return this.cache.get((index / 32n) * 32n)?.[resource]?.[Number(index % 32n)];
  }

  request(index: bigint): void {
    if (index < 0n || index >= this.length) {
      this.cancel();

      return;
    }

    const start = (index / 32n) * 32n;

    if (this.requested === start) {
      return;
    }

    this.cancel();
    this.requested = start;
    this.error = "";
    const cached = this.cache.get(start);

    if (cached) {
      this.cache.delete(start);
      this.cache.set(start, cached);

      return;
    }

    const generation = this.generation;

    this.timer = setTimeout(() => {
      void this.load(start, generation);
    }, 120);
  }

  private async load(start: bigint, generation: number): Promise<void> {
    const controller = new AbortController();

    this.controller = controller;
    const count = Number(this.length - start < 32n ? this.length - start : 32n);
    const iso = (index: bigint) =>
      formatTime(this.begin + index * this.period, "yyyy-MM-ddTHH:mm:ss.fffffff") + "Z";

    try {
      const values = await this.transport(
        iso(start),
        iso(start + BigInt(count)),
        this.paths,
        count,
        controller.signal,
      );

      if (generation !== this.generation || controller.signal.aborted) {
        return;
      }

      if (values.length !== this.paths.length || values.some((value) => value.length !== count)) {
        throw new Error("Incomplete exact cursor neighborhood.");
      }

      this.cache.set(start, values);

      while (this.cache.size > 8) {
        this.cache.delete(this.cache.keys().next().value!);
      }
    } catch (error) {
      if (generation === this.generation && !controller.signal.aborted) {
        this.error = String(error);
      }
    } finally {
      if (generation === this.generation) {
        this.controller = undefined;
        this.notify();
      }
    }
  }

  cancel(): void {
    this.generation++;
    clearTimeout(this.timer);
    this.controller?.abort();
    this.controller = undefined;
    this.requested = undefined;
  }

  dispose(): void {
    this.cancel();
    this.cache.clear();
  }
}

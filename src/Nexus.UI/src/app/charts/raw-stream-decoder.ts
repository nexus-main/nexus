import { AsyncByteStream, RecordBatchReader } from "apache-arrow";
import type { Schema } from "apache-arrow";

export async function decodeRawStream(
  response: Response,
  resources: number,
  count: number,
  signal: AbortSignal,
): Promise<Float32Array[]> {
  if (!Number.isInteger(count) || count < 1 || count > 32 || resources < 1 || resources > 100) {
    throw new Error("Raw stream read exceeds the sample budget.");
  }

  const body = response.body?.getReader();

  if (!body) {
    throw new Error("Missing raw stream response body.");
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
          throw new Error("Raw stream response exceeds its byte budget.");
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
        throw new Error("Invalid raw stream Arrow schema.");
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
          throw new Error("Invalid raw stream samples or offsets.");
        }

        for (const value of samples) {
          values[resource][offsets[resource]++] = value;
        }
      }
    }

    signal.throwIfAborted();

    if (offsets.some((offset) => offset !== count)) {
      throw new Error("Truncated raw stream response.");
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

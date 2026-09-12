import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { NexusClient, NexusException, V2 } from "nexus-api";
import {
    makeData,
    Table,
    Schema,
    Field,
    Int32,
    Int64,
    Float32,
    List,
    RecordBatch,
    Struct,
    tableToIPC,
} from "apache-arrow";

const { Precision } = V2;
const NEXUS_CONFIG_HEADER = "Nexus-Configuration";

type ArrowRow = [number, number, number[]];

function arrowStream(...rows: ArrowRow[]): Uint8Array {
    const listType = new List(new Field("item", new Float32(), true));
    const schema = new Schema(
        [
            new Field("resourceIndex", new Int32(), false),
            new Field("offset", new Int64(), false),
            new Field("values", listType, false),
        ],
        null,
    );

    const allValues: number[] = [];
    const listOffsets: number[] = [0];
    for (const row of rows) {
        allValues.push(...row[2]);
        listOffsets.push(allValues.length);
    }

    const riData = makeData({ type: new Int32(), data: new Int32Array(rows.map((r) => r[0])) });
    const offData = makeData({ type: new Int64(), data: new BigInt64Array(rows.map((r) => BigInt(r[1]))) });
    const childData = makeData({ type: new Float32(), data: new Float32Array(allValues) });
    const listData = makeData({
        type: listType,
        valueOffsets: new Int32Array(listOffsets),
        child: childData,
        length: rows.length,
        nullCount: 0,
    });

    const structType = new Struct(schema.fields);
    const structData = makeData({
        type: structType,
        children: [riData, offData, listData],
        length: rows.length,
        nullCount: 0,
    });

    const table = new Table(schema, new RecordBatch(schema, structData));
    return tableToIPC(table, "stream");
}

function invalidSchemaArrowStream(): Uint8Array {
    const schema = new Schema([new Field("unexpected", new Int32(), false)], null);
    const data = makeData({ type: new Int32(), data: new Int32Array([1]) });
    const structType = new Struct(schema.fields);
    const structData = makeData({ type: structType, children: [data], length: 1, nullCount: 0 });
    const table = new Table(schema, new RecordBatch(schema, structData));
    return tableToIPC(table, "stream");
}

function catalogItemMap(paths: string[]): Record<string, unknown> {
    const result: Record<string, unknown> = {};
    for (const path of paths) {
        result[path] = {
            catalog: { id: "my-catalog" },
            resource: { id: path.split("/").pop() },
            representation: { dataType: "Float64", samplePeriod: "PT1S" },
        };
    }
    return result;
}

function jsonResponse(data: unknown): Response {
    return new Response(JSON.stringify(data), {
        status: 200,
        headers: { "Content-Type": "application/json" },
    });
}

function arrowResponse(data: Uint8Array): Response {
    const body = new ArrayBuffer(data.byteLength);
    new Uint8Array(body).set(data);

    return new Response(body, {
        status: 200,
        headers: { "Content-Type": "application/vnd.apache.arrow.stream" },
    });
}

function mockFetch(handler: (request: Request) => Response | Promise<Response>) {
    return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const request = new Request(input, init);
        return handler(request);
    });
}

describe("TypeScript Client Tests", () => {
    let originalFetch: typeof globalThis.fetch;

    beforeEach(() => {
        originalFetch = globalThis.fetch;
    });

    afterEach(() => {
        globalThis.fetch = originalFetch;
    });

    it("can add configuration", async () => {
        const catalogId = "my-catalog-id";
        const capturedHeaders: (string | null)[] = [];

        globalThis.fetch = mockFetch((request) => {
            capturedHeaders.push(request.headers.get(NEXUS_CONFIG_HEADER));
            return jsonResponse({ id: catalogId });
        });

        const client = new NexusClient("http://localhost");

        await client.v1.catalogs.get(catalogId);

        const configuration = { foo1: "bar1", foo2: "bar2" };
        const disposable = client.attachConfiguration(configuration);
        await client.v1.catalogs.get(catalogId);
        disposable.dispose();

        await client.v1.catalogs.get(catalogId);

        expect(capturedHeaders).toHaveLength(3);
        expect(capturedHeaders[0]).toBeNull();
        expect(capturedHeaders[1]).toBe(btoa(JSON.stringify(configuration)));
        expect(capturedHeaders[2]).toBeNull();
    });

    it("can load interleaved Arrow rows", async () => {
        const paths = ["/A/B/C", "/A/B/D"];
        const content = arrowStream([1, 0, [3, 4]], [0, 0, [1, 2]]);

        globalThis.fetch = mockFetch((request) => {
            if (request.url.includes("/api/v1/catalogs/search-items")) {
                return jsonResponse(catalogItemMap(paths));
            }
            return arrowResponse(content);
        });

        const client = new NexusClient("http://localhost");
        const result = await client.load(
            "1970-01-01T00:00:00.000Z",
            "1970-01-01T00:00:02.000Z",
            paths,
            Precision.Float32,
        );

        expect(Array.from(result[paths[0]].values)).toEqual([1, 2]);
        expect(Array.from(result[paths[1]].values)).toEqual([3, 4]);
    });

    it("rejects invalid Arrow resource index", async () => {
        const response = arrowResponse(arrowStream([1, 0, [1]]));
        const client = new NexusClient("http://localhost");

        await expect(
            (client as any)._readBatch(response, [4], Precision.Float32),
        ).rejects.toThrow("invalid resource index");
    });

    it("rejects invalid Arrow stream", async () => {
        const response = new Response(new Uint8Array([1, 2, 3]), {
            status: 200,
            headers: { "Content-Type": "application/vnd.apache.arrow.stream" },
        });
        const client = new NexusClient("http://localhost");

        await expect(
            (client as any)._readBatch(response, [4], Precision.Float32),
        ).rejects.toThrow();
    });

    it("rejects invalid Arrow schema", async () => {
        const response = arrowResponse(invalidSchemaArrowStream());
        const client = new NexusClient("http://localhost");

        await expect(
            (client as any)._readBatch(response, [4], Precision.Float32),
        ).rejects.toThrow();
    });

    it("rejects incomplete Arrow stream", async () => {
        const content = arrowStream([0, 0, [1]]);
        const response = arrowResponse(content);
        const client = new NexusClient("http://localhost");

        await expect(
            (client as any)._readBatch(response, [8], Precision.Float32),
        ).rejects.toThrow("before all data");
    });

    it("rejects out-of-order Arrow stream", async () => {
        const response = arrowResponse(arrowStream([0, 1, [1]]));
        const client = new NexusClient("http://localhost");

        await expect(
            (client as any)._readBatch(response, [4], Precision.Float32),
        ).rejects.toThrow("out-of-order");
    });

    it("includes response body in exception for unsuccessful response", async () => {
        globalThis.fetch = mockFetch(() => {
            return new Response("stream failed", { status: 500 });
        });

        const client = new NexusClient("http://localhost");

        await expect(
            client.v2.data.getStream({
                begin: "1970-01-01T00:00:00.000Z",
                end: "1970-01-01T00:00:01.000Z",
                resourcePaths: ["/A/B/C"],
                precision: Precision.Float32,
            }),
        ).rejects.toThrow("stream failed");
    });

    it("rejects invalid Arrow stream with truncated data", async () => {
        const content = arrowStream([0, 0, [1]]).slice(0, -4);
        const response = arrowResponse(content);
        const client = new NexusClient("http://localhost");

        await expect(
            (client as any)._readBatch(response, [8], Precision.Float32),
        ).rejects.toThrow();
    });

    it("load reports progress correctly", async () => {
        const path = "/A/B/C";
        const content = arrowStream([0, 0, [1, 2]]);

        globalThis.fetch = mockFetch((request) => {
            if (request.url.includes("/api/v1/catalogs/search-items")) {
                return jsonResponse(catalogItemMap([path]));
            }
            return arrowResponse(content);
        });

        const client = new NexusClient("http://localhost");
        const progressValues: number[] = [];

        const result = await client.load(
            "1970-01-01T00:00:00.000Z",
            "1970-01-01T00:00:02.000Z",
            [path],
            Precision.Float32,
            (progress) => progressValues.push(progress),
        );

        expect(Array.from(result[path].values)).toEqual([1, 2]);
        expect(progressValues.length).toBeGreaterThan(0);
        expect(progressValues[progressValues.length - 1]).toBe(1);
    });
});

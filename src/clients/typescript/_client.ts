import { AsyncByteStream, DataType, Precision as ArrowPrecision, RecordBatchReader } from "apache-arrow";
import type { Float32, Float64, Int32, Int64, List, Schema } from "apache-arrow";
import { HttpRequestHandler, NexusException } from "./_shared";
import { V1, IV1 } from "./V1";
import { CatalogItem, TaskStatus } from "./V1";
import { V2, IV2 } from "./V2";
import { BatchStreamRequest, ExportParameters, Precision } from "./V2";


type StreamSchema = { resourceIndex: Int32; offset: Int64; values: List<Float32> | List<Float64> };

/**
 * A client for the Nexus system.
 */
export interface INexusClient {
    v1: IV1;
    v2: IV2;


    /**
     * Signs in the user.
     * @param accessToken The access token.
     */
    signIn(accessToken: string): void;

    /**
     * Gets a value which indicates if the user is authenticated.
     */
    readonly isAuthenticated: boolean;

    /**
     * Attaches configuration data to subsequent API requests.
     * @param configuration The configuration data.
     */
    attachConfiguration(configuration: unknown): { dispose(): void };

    /**
     * Clears configuration data for all subsequent API requests.
     */
    clearConfiguration(): void;

    /**
     * This high-level method simplifies loading multiple resources at once.
     * @param begin Start date/time.
     * @param end End date/time.
     * @param resourcePaths The resource paths.
     * @param precision The floating point precision requested from the server.
     * @param onProgress A callback which accepts the current progress.
     * @param signal The signal to cancel the current operation.
     */
    load(
        begin: string,
        end: string,
        resourcePaths: string[],
        precision: Precision,
        onProgress?: ((progress: number) => void) | undefined,
        signal?: AbortSignal): Promise<{ [resourcePath: string]: DataResponse }>;

    /**
     * This high-level method simplifies loading multiple resources at once into caller-provided buffers.
     * @param begin Start date/time.
     * @param end End date/time.
     * @param resourcePaths The resource paths.
     * @param precision The floating point precision requested from the server.
     * @param bufferProvider A callback which provides a writable buffer for each resource path, chunk element count, and remaining resource element count.
     * @param onProgress A callback which accepts the current progress.
     * @param signal The signal to cancel the current operation.
     */
    load(
        begin: string,
        end: string,
        resourcePaths: string[],
        precision: Precision,
        bufferProvider: BufferProvider,
        onProgress?: ((progress: number) => void) | undefined,
        signal?: AbortSignal): Promise<{ [resourcePath: string]: ResourceInfo }>;

    /**
     * This high-level method simplifies exporting multiple resources at once.
     * @param begin The begin date/time.
     * @param end The end date/time.
     * @param filePeriod The file period. Use "PT0S" to get a single file.
     * @param fileFormat The target file format. If null, data will be read (and possibly cached) but not returned.
     * @param resourcePaths The resource paths to export.
     * @param configuration The configuration.
     * @param targetFolder The target folder for the files to extract.
     * @param precision The floating point precision used for exported sample values.
     * @param onProgress A callback which accepts the current progress and the progress message.
     * @param signal The signal to cancel the current operation.
     */
    export(
        begin: string,
        end: string,
        filePeriod: string,
        fileFormat: string | undefined,
        resourcePaths: string[],
        configuration: { [key: string]: unknown } | undefined,
        targetFolder: string,
        precision: Precision,
        onProgress?: ((progress: number, message: string) => void) | undefined,
        signal?: AbortSignal): Promise<void>;
}

/**
 * A client for the Nexus system.
 */
export class NexusClient implements INexusClient {
    private readonly _configurationHeaderKey = "Nexus-Configuration";
    private readonly _authorizationHeaderKey = "Authorization";

    private _token: string | null = null;
    private _baseUrl: string;

    public v1: V1;
    public v2: V2;


    /**
     * Initializes a new instance of the NexusClient.
     * @param baseUrl The base URL to connect to.
     */
    constructor(baseUrl: string) {
        if (!baseUrl)
            throw new Error("The base URL must be set.");

        this._baseUrl = baseUrl.endsWith("/") ? baseUrl.slice(0, -1) : baseUrl;

        this.v1 = new V1(this.invoke.bind(this));
        this.v2 = new V2(this.invoke.bind(this));

    }

    get isAuthenticated(): boolean {
        return this._token !== null;
    }

    signIn(accessToken: string): void {
        this._token = accessToken;
    }

    attachConfiguration(configuration: unknown): { dispose(): void } {
        const encodedJson = btoa(JSON.stringify(configuration));
        this._configurationValue = encodedJson;
        return { dispose: () => this.clearConfiguration() };
    }

    private _configurationValue: string | null = null;

    clearConfiguration(): void {
        this._configurationValue = null;
    }

    async invoke<T>(
        method: string,
        relativeUrl: string,
        acceptHeaderValue?: string,
        contentTypeValue?: string,
        content?: BodyInit,
        signal?: AbortSignal): Promise<T>
    {
        const request = this._buildRequestMessage(
            method,
            relativeUrl,
            content,
            contentTypeValue,
            acceptHeaderValue,
            signal);

        const response = await fetch(request);

        if (!response.ok) {
            const message = await response.text();
            const statusCode = `N00.${response.status}`;

            if (!message)
                throw new NexusException(statusCode, `The HTTP request failed with status code ${response.status}.`);
            else
                throw new NexusException(statusCode, `The HTTP request failed with status code ${response.status}. The response message is: ${message}`);
        }

        if (acceptHeaderValue === undefined) {
            return undefined as T;
        }

        if (acceptHeaderValue === "application/octet-stream" || acceptHeaderValue === "application/vnd.apache.arrow.stream") {
            return response as T;
        }

        const text = await response.text();

        try {
            return JSON.parse(text) as T;
        } catch (ex) {
            throw new NexusException("N01", "Response data could not be deserialized.");
        }
    }

    private _buildRequestMessage(
        method: string,
        relativeUrl: string,
        content: BodyInit | undefined,
        contentTypeHeaderValue: string | undefined,
        acceptHeaderValue: string | undefined,
        signal: AbortSignal | undefined): Request
    {
        const url = this._baseUrl + relativeUrl;

        const headers = new Headers();

        if (contentTypeHeaderValue !== undefined && content !== undefined)
            headers.set("Content-Type", contentTypeHeaderValue);

        if (acceptHeaderValue !== undefined)
            headers.set("Accept", acceptHeaderValue);

        if (this._token !== null)
            headers.set(this._authorizationHeaderKey, `Bearer ${this._token}`);

        if (this._configurationValue !== null)
            headers.set(this._configurationHeaderKey, this._configurationValue);

        return new Request(url, {
            method,
            headers,
            body: content,
            signal
        });
    }

    load(
        begin: string,
        end: string,
        resourcePaths: string[],
        precision: Precision,
        onProgress?: ((progress: number) => void) | undefined,
        signal?: AbortSignal): Promise<{ [resourcePath: string]: DataResponse }>;

    load(
        begin: string,
        end: string,
        resourcePaths: string[],
        precision: Precision,
        bufferProvider: BufferProvider,
        onProgress?: ((progress: number) => void) | undefined,
        signal?: AbortSignal): Promise<{ [resourcePath: string]: ResourceInfo }>;

    async load(
        begin: string,
        end: string,
        resourcePaths: string[],
        precision: Precision,
        bufferProviderOrOnProgress?: BufferProvider | ((progress: number) => void) | undefined,
        onProgressOrSignal?: ((progress: number) => void) | AbortSignal | undefined,
        signal?: AbortSignal): Promise<{ [resourcePath: string]: DataResponse } | { [resourcePath: string]: ResourceInfo }>
    {
        const bufferProvider = typeof bufferProviderOrOnProgress === "function" && bufferProviderOrOnProgress.length >= 3
            ? bufferProviderOrOnProgress as BufferProvider
            : undefined;
        const onProgress = bufferProvider
            ? onProgressOrSignal as ((progress: number) => void) | undefined
            : bufferProviderOrOnProgress as ((progress: number) => void) | undefined;
        const actualSignal = bufferProvider
            ? signal
            : onProgressOrSignal as AbortSignal | undefined;

        if (resourcePaths.length === 0)
            return {};

        const catalogItemMap = await this.v1.catalogs.searchCatalogItems(resourcePaths, actualSignal);
        const response = await this.v2.data.getStream({ begin, end, resourcePaths, precision }, actualSignal);
        const expectedLengths = this._getExpectedLengths(begin, end, resourcePaths, catalogItemMap, precision);

        const totalLength = expectedLengths.reduce((a, b) => a + b, 0);
        let consumed = 0;

        const reportProgress = (bytesRead: number) => {
            consumed += bytesRead;
            if (totalLength > 0 && onProgress)
                onProgress(Math.min(1, consumed / totalLength));
        };

        const values = await this._readBatch(response, resourcePaths, expectedLengths, precision, bufferProvider, reportProgress, actualSignal);

        if (onProgress)
            onProgress(1);

        const resourceInfoMap: { [resourcePath: string]: ResourceInfo } = {};

        for (const resourcePath of resourcePaths)
            resourceInfoMap[resourcePath] = this._createResourceInfo(catalogItemMap[resourcePath], resourcePath);

        if (bufferProvider)
            return resourceInfoMap;

        const result: { [resourcePath: string]: DataResponse } = {};

        for (let i = 0; i < resourcePaths.length; i++) {
            const resourcePath = resourcePaths[i];
            result[resourcePath] = {
                info: resourceInfoMap[resourcePath],
                values: values[i]
            };
        }

        return result;
    }

    private _getExpectedLengths(
        begin: string,
        end: string,
        resourcePaths: string[],
        catalogItemMap: { [resourcePath: string]: CatalogItem },
        precision: Precision): number[] {
        const precisionSize = precision === Precision.Float32 ? 4 : 8;

        return resourcePaths.map(path => {
            const catalogItem = catalogItemMap[path];
            const samplePeriodStr = catalogItem?.representation?.samplePeriod;
            if (!samplePeriodStr)
                throw new Error(`The catalog item for resource path "${path}" is missing or has no sample period.`);
            const sampleCount = (this._parseDateTimeToTicks(end) - this._parseDateTimeToTicks(begin)) / this._parseDurationToTicks(samplePeriodStr);
            const length = sampleCount * BigInt(precisionSize);
            if (sampleCount < 0n || length > BigInt(Number.MAX_SAFE_INTEGER))
                throw new Error(`The requested data for resource path "${path}" is too large.`);
            return Number(length);
        });
    }

    private _createResourceInfo(catalogItem: CatalogItem | undefined, resourcePath: string): ResourceInfo {
        const resource = catalogItem?.resource;

        if (!catalogItem || !resource)
            throw new Error(`The catalog item for resource path "${resourcePath}" is missing or has no resource.`);

        let unit: string | undefined = undefined;
        let description: string | undefined = undefined;

        if (resource.properties) {
            if (typeof resource.properties["unit"] === "string")
                unit = resource.properties["unit"] as string;
            if (typeof resource.properties["description"] === "string")
                description = resource.properties["description"] as string;
        }

        return {
            catalogItem,
            name: resource.id ?? "",
            unit,
            description,
            samplePeriod: catalogItem.representation?.samplePeriod ?? ""
        };
    }

    private async _readBatch(
        response: Response,
        resourcePaths: string[],
        expectedLengths: number[],
        precision: Precision,
        bufferProvider?: BufferProvider | undefined,
        reportProgress?: ((bytesRead: number) => void) | undefined,
        signal?: AbortSignal): Promise<TypedDataArray[]>
    {
        const precisionSize = precision === Precision.Float32 ? 4 : 8;
        const arrayType = precision === Precision.Float32 ? Float32Array : Float64Array;
        const maxChunkLength = Math.max(1, Math.floor(16 * 1024 * 1024 / precisionSize));

        const values = new Array<TypedDataArray>(expectedLengths.length);
        const chunks = new Array<TypedDataArray>(expectedLengths.length);
        const chunkOffsets = new Array<number>(expectedLengths.length).fill(0);
        const chunkLengths = new Array<number>(expectedLengths.length).fill(0);
        const offsets = new Array(expectedLengths.length).fill(0);
        const body = response.body?.getReader();
        let reader: RecordBatchReader<StreamSchema> | undefined;

        for (let index = 0; index < expectedLengths.length; index++) {
            if (expectedLengths[index] % precisionSize !== 0)
                throw new Error("The expected resource length is not aligned to the requested precision.");

            const requiredLength = expectedLengths[index] / precisionSize;

            if (!bufferProvider) {
                if (requiredLength > 0x7fffffff)
                    throw new Error(`The resource '${resourcePaths[index]}' is too large for a single contiguous buffer. Provide a chunk-aware buffer provider.`);

                const value = new arrayType(requiredLength);
                values[index] = value;
                chunks[index] = value;
                chunkLengths[index] = expectedLengths[index];
            } else {
                values[index] = new arrayType(0);

                if (requiredLength > 0)
                    rentNextChunk(index, requiredLength);
            }
        }

        if (!body)
            throw new Error("The Arrow response has no body.");

        const stream = body;
        const cancelBody = () => { void stream.cancel(signal?.reason).catch(() => {}); };
        signal?.addEventListener("abort", cancelBody, { once: true });

        try {
            signal?.throwIfAborted();

            async function* bytes(): AsyncGenerator<Uint8Array> {
                while (true) {
                    signal?.throwIfAborted();
                    const result = await stream.read();
                    signal?.throwIfAborted();

                    if (result.done)
                        return;

                    yield result.value;
                }
            }

            reader = await RecordBatchReader.from<StreamSchema>(new AsyncByteStream(bytes()));
            signal?.throwIfAborted();
            await reader.open({ autoDestroy: false });
            signal?.throwIfAborted();
            this._validateBatchSchema(reader.schema, precision);

            for await (const recordBatch of reader) {
                signal?.throwIfAborted();
                this._validateBatchSchema(recordBatch.schema, precision);
                const resourceIndexArray = recordBatch.getChildAt(0)!;
                const offsetArray = recordBatch.getChildAt(1)!;
                const valuesArray = recordBatch.getChildAt(2)!;

                if (resourceIndexArray.nullCount || offsetArray.nullCount || valuesArray.nullCount)
                    throw new Error("The Arrow stream contains nulls.");

                for (let rowIndex = 0; rowIndex < recordBatch.numRows; rowIndex++) {
                    signal?.throwIfAborted();
                    const resourceIndex = resourceIndexArray.get(rowIndex);
                    const offset = offsetArray.get(rowIndex);

                    if (resourceIndex === null)
                        throw new Error("The Arrow stream contains a null resource index.");

                    if (offset === null)
                        throw new Error("The Arrow stream contains a null offset.");

                    const idx = resourceIndex as number;

                    if (idx < 0 || idx >= values.length)
                        throw new Error("The Arrow stream contains an invalid resource index.");

                    const off = Number(offset);

                    if (off < 0)
                        throw new Error("The Arrow stream contains an invalid offset.");

                    if (off !== offsets[idx] / precisionSize)
                        throw new Error("The Arrow stream contains out-of-order data.");

                    const rowValues = valuesArray.get(rowIndex);

                    if (!rowValues || rowValues.nullCount)
                        throw new Error("The Arrow stream contains null values.");

                    const payloadLength = rowValues.length * precisionSize;

                    if (offsets[idx] > expectedLengths[idx] - payloadLength)
                        throw new Error("The Arrow stream contains more data than expected.");

                    for (const part of rowValues.data) {
                        const source = part.values as Float32Array | Float64Array;

                        if (!(source instanceof arrayType) || source.length < part.offset + part.length)
                            throw new Error("The Arrow stream values column is invalid.");

                        let sourceOffset = part.offset;
                        let remainingLength = part.length;

                        while (remainingLength > 0) {
                            if (chunkOffsets[idx] === chunkLengths[idx]) {
                                const remainingResourceLength = (expectedLengths[idx] - offsets[idx]) / precisionSize;
                                rentNextChunk(idx, remainingResourceLength);
                            }

                            const count = Math.min(remainingLength, (chunkLengths[idx] - chunkOffsets[idx]) / precisionSize);
                            const target = chunks[idx].subarray(chunkOffsets[idx] / precisionSize, chunkOffsets[idx] / precisionSize + count);
                            target.set(source.subarray(sourceOffset, sourceOffset + count) as ArrayLike<number>);

                            const bytesCopied = count * precisionSize;
                            chunkOffsets[idx] += bytesCopied;
                            offsets[idx] += bytesCopied;
                            sourceOffset += count;
                            remainingLength -= count;

                            if (reportProgress)
                                reportProgress(bytesCopied);
                        }
                    }
                }
            }

            signal?.throwIfAborted();
        } finally {
            signal?.removeEventListener("abort", cancelBody);
            cancelBody();
            try { await reader?.cancel(); } finally { stream.releaseLock(); }
        }

        for (let i = 0; i < expectedLengths.length; i++) {
            if (offsets[i] !== expectedLengths[i])
                throw new Error("The Arrow stream ended before all data was received.");
        }

        return values;

        function rentNextChunk(index: number, remainingLength: number): void {
            if (!bufferProvider)
                throw new Error("The Arrow stream contains more chunk data than expected.");

            const chunkLength = Math.min(maxChunkLength, remainingLength);
            const buffer = bufferProvider(resourcePaths[index], chunkLength, remainingLength);

            if (!(buffer instanceof arrayType))
                throw new Error(`The buffer provided for resource path '${resourcePaths[index]}' does not match the requested precision.`);

            if (buffer.length < chunkLength)
                throw new Error(`The buffer provided for resource path '${resourcePaths[index]}' is too small. Required length: ${chunkLength}. Provided length: ${buffer.length}.`);

            chunks[index] = buffer.subarray(0, chunkLength);
            chunkOffsets[index] = 0;
            chunkLengths[index] = chunkLength * precisionSize;
        }
    }

    private _validateBatchSchema(schema: Schema | undefined, precision: Precision): void {
        const fields = schema?.fields;
        const valuePrecision = precision === Precision.Float32 ? ArrowPrecision.SINGLE : ArrowPrecision.DOUBLE;

        if (!fields || fields.length !== 3
            || fields[0].name !== "resourceIndex" || !DataType.isInt(fields[0].type) || !fields[0].type.isSigned || fields[0].type.bitWidth !== 32
            || fields[1].name !== "offset" || !DataType.isInt(fields[1].type) || !fields[1].type.isSigned || fields[1].type.bitWidth !== 64
            || fields[2].name !== "values" || !DataType.isList(fields[2].type) || fields[2].type.children.length !== 1
            || !DataType.isFloat(fields[2].type.valueType) || fields[2].type.valueType.precision !== valuePrecision)
            throw new Error("The Arrow stream schema is invalid.");
    }

    private _parseDateTimeToTicks(value: string): bigint {
        const match = value.match(/^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})?$/);
        if (!match)
            throw new Error(`Invalid date/time: ${value}`);

        const date = new Date(`${match[1]}${match[3] ?? "Z"}`);
        if (Number.isNaN(date.getTime()))
            throw new Error(`Invalid date/time: ${value}`);

        const fractionTicks = BigInt((match[2] ?? "").padEnd(7, "0"));
        return 621355968000000000n + BigInt(date.getTime()) * 10000n + fractionTicks;
    }

    private _parseDurationToTicks(duration: string): bigint {
        const timeSpanMatch = duration.match(/^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?$/);
        if (timeSpanMatch) {
            return BigInt(timeSpanMatch[1] || "0") * 864000000000n
                + BigInt(timeSpanMatch[2]) * 36000000000n
                + BigInt(timeSpanMatch[3]) * 600000000n
                + BigInt(timeSpanMatch[4]) * 10000000n
                + BigInt((timeSpanMatch[5] ?? "").padEnd(7, "0"));
        }

        const isoMatch = duration.match(/^P?T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+(?:\.\d+)?)S)?$/);
        if (!isoMatch)
            throw new Error(`Invalid duration: ${duration}`);

        return BigInt(isoMatch[1] || "0") * 36000000000n
            + BigInt(isoMatch[2] || "0") * 600000000n
            + this._parseSecondsToTicks(isoMatch[3] || "0");
    }

    private _parseSecondsToTicks(value: string): bigint {
        const [seconds, fraction = ""] = value.split(".");
        return BigInt(seconds) * 10000000n + BigInt(fraction.padEnd(7, "0").slice(0, 7));
    }

    async export(
        begin: string,
        end: string,
        filePeriod: string,
        fileFormat: string | undefined,
        resourcePaths: string[],
        configuration: { [key: string]: unknown } | undefined,
        targetFolder: string,
        precision: Precision,
        onProgress?: ((progress: number, message: string) => void) | undefined,
        signal?: AbortSignal): Promise<void>
    {
        const exportParameters: ExportParameters = {
            begin,
            end,
            filePeriod,
            type: fileFormat,
            resourcePaths,
            configuration,
            precision
        };

        const job = await this.v2.jobs.export(exportParameters, signal);

        let artifactId: string | null = null;

        while (true) {
            await new Promise(resolve => setTimeout(resolve, 1000));

            const jobStatus = await this.v1.jobs.getJobStatus(job.id ?? "", signal);

            if (jobStatus.status === TaskStatus.Canceled)
                throw new Error("The job has been cancelled.");
            else if (jobStatus.status === TaskStatus.Faulted)
                throw new Error(`The job has failed. Reason: ${jobStatus.exceptionMessage ?? "unknown"}`);
            else if (jobStatus.status === TaskStatus.RanToCompletion) {
                if (jobStatus.result !== null && typeof jobStatus.result === "string") {
                    artifactId = jobStatus.result;
                    break;
                }
            }

            if (jobStatus.progress !== undefined && jobStatus.progress < 1 && onProgress)
                onProgress(jobStatus.progress, "export");
        }

        if (onProgress)
            onProgress(1, "export");

        if (artifactId === null)
            throw new Error("The job result is invalid.");

        if (fileFormat === undefined)
            return;

        // Download zip file
        const response = await this.v1.artifacts.download(artifactId, signal);

        // Note: In a browser environment, zip extraction would need a library like JSZip.
        // In Node.js, the targetFolder extraction is not directly supported.
        // This is a placeholder - the actual extraction depends on the runtime.
        throw new Error("Zip extraction is not yet supported in the TypeScript client.");
    }
}

export type TypedDataArray = Float32Array | Float64Array;

/**
 * Provides writable data buffers for chunk-aware loading.
 */
export type BufferProvider = (resourcePath: string, chunkLength: number, remainingLength: number) => TypedDataArray;

/**
 * Metadata for a data resource.
 */
export interface ResourceInfo {
    /** The catalog item. */
    catalogItem: CatalogItem;
    /** The resource name. */
    name: string;
    /** The optional resource unit. */
    unit: string | undefined;
    /** The optional resource description. */
    description: string | undefined;
    /** The sample period. */
    samplePeriod: string;
}

/**
 * Result of a data request with a certain resource path.
 */
export interface DataResponse {
    /** The resource metadata. */
    info: ResourceInfo;
    /** The data. */
    values: TypedDataArray;
}

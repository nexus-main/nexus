import { tableFromIPC } from "apache-arrow";
import { HttpRequestHandler, NexusException } from "./_shared";
import { V1, IV1 } from "./V1";
import { CatalogItem, TaskStatus } from "./V1";
import { V2, IV2 } from "./V2";
import { BatchStreamRequest, ExportParameters, Precision } from "./V2";


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

    async load(
        begin: string,
        end: string,
        resourcePaths: string[],
        precision: Precision,
        onProgress?: ((progress: number) => void) | undefined,
        signal?: AbortSignal): Promise<{ [resourcePath: string]: DataResponse }>
    {
        if (resourcePaths.length === 0)
            return {};

        const catalogItemMap = await this.v1.catalogs.searchCatalogItems(resourcePaths, signal);
        const response = await this.v2.data.getStream({ begin, end, resourcePaths, precision }, signal);

        const precisionSize = precision === Precision.Float32 ? 4 : 8;
        const expectedLengths = resourcePaths.map(path => {
            const catalogItem = catalogItemMap[path];
            const samplePeriodStr = catalogItem?.representation?.samplePeriod;
            if (!samplePeriodStr)
                throw new Error(`The catalog item for resource path "${path}" is missing or has no sample period.`);
            const beginDate = new Date(begin);
            const endDate = new Date(end);
            const samplePeriodMs = this._parseDurationToMs(samplePeriodStr);
            const totalMs = endDate.getTime() - beginDate.getTime();
            return Math.floor(totalMs / samplePeriodMs) * precisionSize;
        });

        const totalLength = expectedLengths.reduce((a, b) => a + b, 0);
        let consumed = 0;

        const reportProgress = (bytesRead: number) => {
            consumed += bytesRead;
            if (totalLength > 0 && onProgress)
                onProgress(Math.min(1, consumed / totalLength));
        };

        const values = await this._readBatch(response, expectedLengths, precision, reportProgress, signal);

        const result: { [resourcePath: string]: DataResponse } = {};

        for (let i = 0; i < resourcePaths.length; i++) {
            const resourcePath = resourcePaths[i];
            const catalogItem = catalogItemMap[resourcePath];
            const resource = catalogItem?.resource;

            if (!resource)
                throw new Error(`The catalog item for resource path "${resourcePath}" has no resource.`);

            let unit: string | undefined = undefined;
            let description: string | undefined = undefined;

            if (resource.properties) {
                if (typeof resource.properties["unit"] === "string")
                    unit = resource.properties["unit"] as string;
                if (typeof resource.properties["description"] === "string")
                    description = resource.properties["description"] as string;
            }

            const samplePeriod = catalogItem?.representation?.samplePeriod ?? "";

            result[resourcePath] = {
                info: {
                    catalogItem: catalogItem!,
                    name: resource.id ?? "",
                    unit,
                    description,
                    samplePeriod
                },
                values: values[i]
            };
        }

        if (onProgress)
            onProgress(1);

        return result;
    }

    private async _readBatch(
        response: Response,
        expectedLengths: number[],
        precision: Precision,
        reportProgress?: ((bytesRead: number) => void) | undefined,
        signal?: AbortSignal): Promise<(Float32Array | Float64Array)[]>
    {
        const precisionSize = precision === Precision.Float32 ? 4 : 8;
        const arrayType = precision === Precision.Float32 ? Float32Array : Float64Array;

        const buffers = expectedLengths.map(length => new ArrayBuffer(length));
        const dataViews = buffers.map(buf => new DataView(buf));
        const typedArrays = buffers.map(buf => new arrayType(buf));
        const offsets = new Array(expectedLengths.length).fill(0);

        const table = tableFromIPC(await response.arrayBuffer());

        for (const recordBatch of table.batches) {
            const resourceIndexArray = recordBatch.getChild("resourceIndex")!;
            const offsetArray = recordBatch.getChild("offset")!;
            const valuesArray = recordBatch.getChild("values")!;

            for (let rowIndex = 0; rowIndex < recordBatch.numRows; rowIndex++) {
                const resourceIndex = resourceIndexArray.get(rowIndex);
                const offset = offsetArray.get(rowIndex);

                if (resourceIndex === null)
                    throw new Error("The Arrow stream contains a null resource index.");

                if (offset === null)
                    throw new Error("The Arrow stream contains a null offset.");

                const idx = resourceIndex as number;

                if (idx < 0 || idx >= dataViews.length)
                    throw new Error("The Arrow stream contains an invalid resource index.");

                const off = Number(offset);

                if (off < 0)
                    throw new Error("The Arrow stream contains an invalid offset.");

                if (off !== offsets[idx] / precisionSize)
                    throw new Error("The Arrow stream contains out-of-order data.");

                const rowValues = valuesArray.get(rowIndex);
                const valueData = rowValues.data[0];
                const valueArray = valueData.values as Float32Array | Float64Array;
                const payloadLength = rowValues.length * precisionSize;

                if (offsets[idx] > expectedLengths[idx] - payloadLength)
                    throw new Error("The Arrow stream contains more data than expected.");

                const payload = new Uint8Array(valueArray.buffer, valueArray.byteOffset, payloadLength);
                const target = new Uint8Array(buffers[idx], offsets[idx], payloadLength);
                target.set(payload);

                offsets[idx] += payloadLength;

                if (reportProgress)
                    reportProgress(payloadLength);
            }
        }

        for (let i = 0; i < expectedLengths.length; i++) {
            if (offsets[i] !== expectedLengths[i])
                throw new Error("The Arrow stream ended before all data was received.");
        }

        return typedArrays;
    }

    private _parseDurationToMs(duration: string): number {
        const match = duration.match(/^(?:P?T)?(?:(\d+)H)?(?:(\d+)M)?(?:(\d+(?:\.\d+)?)S)?$/);
        if (!match)
            throw new Error(`Invalid duration: ${duration}`);

        const hours = parseInt(match[1] || "0", 10);
        const minutes = parseInt(match[2] || "0", 10);
        const seconds = parseFloat(match[3] || "0");

        return (hours * 3600 + minutes * 60 + seconds) * 1000;
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
    values: Float32Array | Float64Array;
}

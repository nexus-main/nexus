import { HttpRequestHandler } from "./_shared";

/**
 * Provides access to the V2 API.
 */
export interface IV2 {
    data: IDataClient;
    jobs: IJobsClient;

}

/**
 * Provides access to the V2 API.
 */
export class V2 implements IV2 {
    public data: DataClient;
    public jobs: JobsClient;


    constructor(invoke: HttpRequestHandler) {
        this.data = new DataClient(invoke);
        this.jobs = new JobsClient(invoke);

    }

}

/**
 * Provides methods to interact with data.
 */
export interface IDataClient {
    /**
     * Streams multiple resources in an Apache Arrow IPC response.
     * @param request The batch stream request.
     * @param signal The signal to cancel the current operation.
     */
    getStream(request: BatchStreamRequest, signal?: AbortSignal): Promise<Response>;

}

/**
 * Provides methods to interact with data.
 */
export class DataClient implements IDataClient {
    private _invoke: HttpRequestHandler;

    constructor(invoke: HttpRequestHandler) {
        this._invoke = invoke;
    }

    /**
     * Streams multiple resources in an Apache Arrow IPC response.
     * @param request The batch stream request.
     * @param signal The signal to cancel the current operation.
     */
    public async getStream(request: BatchStreamRequest, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v2/data";

        return this._invoke<Response>("POST", __url, "application/vnd.apache.arrow.stream", "application/json", JSON.stringify(request), signal);
    }

}

/**
 * Provides methods to interact with jobs.
 */
export interface IJobsClient {
    /**
     * Creates a new export job.
     * @param parameters Export parameters.
     * @param signal The signal to cancel the current operation.
     */
    export(parameters: ExportParameters, signal?: AbortSignal): Promise<Job>;

}

/**
 * Provides methods to interact with jobs.
 */
export class JobsClient implements IJobsClient {
    private _invoke: HttpRequestHandler;

    constructor(invoke: HttpRequestHandler) {
        this._invoke = invoke;
    }

    /**
     * Creates a new export job.
     * @param parameters Export parameters.
     * @param signal The signal to cancel the current operation.
     */
    public async export(parameters: ExportParameters, signal?: AbortSignal): Promise<Job> {
        let __url = "/api/v2/jobs/export";

        return this._invoke<Job>("POST", __url, "application/json", "application/json", JSON.stringify(parameters), signal);
    }

}


/**
 * A request to stream multiple resources.
 */
export interface BatchStreamRequest {
    /** The start date/time. */
    begin?: string | undefined;
    /** The end date/time. */
    end?: string | undefined;
    /** The resource paths to stream. */
    resourcePaths?: string[] | undefined;
    /** The floating point precision used for streamed sample values. */
    precision?: Precision | undefined;
}


/**
 * Specifies floating point precision for API output values.
 */
export enum Precision {
    Float32 = "Float32",
    Float64 = "Float64"
}


/**
 * Description of a job.
 */
export interface Job {
    /** The global unique identifier. */
    id?: string | undefined;
    /** The job type. */
    type?: string | undefined;
    /** The owner of the job. */
    owner?: string | undefined;
    /** The job parameters. */
    parameters?: unknown | null;
}


/**
 * A structure for export parameters.
 */
export interface ExportParameters {
    /** The start date/time. */
    begin?: string | undefined;
    /** The end date/time. */
    end?: string | undefined;
    /** The file period. */
    filePeriod?: string | undefined;
    /** The writer type. If null, data will be read (and possibly cached) but not returned. This is useful for data pre-aggregation. */
    type?: string | null;
    /** The resource paths to export. */
    resourcePaths?: string[] | undefined;
    /** The configuration. */
    configuration?: Record<string, unknown> | null;
    /** The floating point precision used for exported sample values. */
    precision?: Precision | undefined;
}

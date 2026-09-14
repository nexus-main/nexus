import { HttpRequestHandler } from "./_shared";

/**
 * Provides access to the V1 API.
 */
export interface IV1 {
    artifacts: IArtifactsClient;
    catalogs: ICatalogsClient;
    data: IDataClient;
    jobs: IJobsClient;
    packageReferences: IPackageReferencesClient;
    sources: ISourcesClient;
    system: ISystemClient;
    users: IUsersClient;
    writers: IWritersClient;

}

/**
 * Provides access to the V1 API.
 */
export class V1 implements IV1 {
    public artifacts: ArtifactsClient;
    public catalogs: CatalogsClient;
    public data: DataClient;
    public jobs: JobsClient;
    public packageReferences: PackageReferencesClient;
    public sources: SourcesClient;
    public system: SystemClient;
    public users: UsersClient;
    public writers: WritersClient;


    constructor(invoke: HttpRequestHandler) {
        this.artifacts = new ArtifactsClient(invoke);
        this.catalogs = new CatalogsClient(invoke);
        this.data = new DataClient(invoke);
        this.jobs = new JobsClient(invoke);
        this.packageReferences = new PackageReferencesClient(invoke);
        this.sources = new SourcesClient(invoke);
        this.system = new SystemClient(invoke);
        this.users = new UsersClient(invoke);
        this.writers = new WritersClient(invoke);

    }

}

/**
 * Provides methods to interact with artifacts.
 */
export interface IArtifactsClient {
    /**
     * Gets the specified artifact.
     * @param artifactId The artifact identifier.
     * @param signal The signal to cancel the current operation.
     */
    download(artifactId: string, signal?: AbortSignal): Promise<Response>;

}

/**
 * Provides methods to interact with artifacts.
 */
export class ArtifactsClient implements IArtifactsClient {
    private _invoke: HttpRequestHandler;

    constructor(invoke: HttpRequestHandler) {
        this._invoke = invoke;
    }

    /**
     * Gets the specified artifact.
     * @param artifactId The artifact identifier.
     * @param signal The signal to cancel the current operation.
     */
    public async download(artifactId: string, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/artifacts/{artifactId}";
        __url = __url.replace("{artifactId}", encodeURIComponent(String(artifactId)));

        return this._invoke<Response>("GET", __url, "application/octet-stream", undefined, undefined, signal);
    }

}

/**
 * Provides methods to interact with catalogs.
 */
export interface ICatalogsClient {
    /**
     * Searches for the given resource paths and returns the corresponding catalog items.
     * @param resourcePaths The list of resource paths.
     * @param signal The signal to cancel the current operation.
     */
    searchCatalogItems(resourcePaths: string[], signal?: AbortSignal): Promise<Record<string, CatalogItem>>;

    /**
     * Gets the specified catalog.
     * @param catalogId The catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    get(catalogId: string, signal?: AbortSignal): Promise<ResourceCatalog>;

    /**
     * Gets a list of child catalog info for the provided parent catalog identifier.
     * @param catalogId The parent catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    getChildCatalogInfos(catalogId: string, signal?: AbortSignal): Promise<CatalogInfo[]>;

    /**
     * Gets the specified catalog's time range.
     * @param catalogId The catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    getTimeRange(catalogId: string, signal?: AbortSignal): Promise<CatalogTimeRange>;

    /**
     * Gets the specified catalog's availability.
     * @param catalogId The catalog identifier.
     * @param begin Start date/time.
     * @param end End date/time.
     * @param step Step period.
     * @param signal The signal to cancel the current operation.
     */
    getAvailability(catalogId: string, begin: string, end: string, step: string, signal?: AbortSignal): Promise<CatalogAvailability>;

    /**
     * Gets the license of the catalog if available.
     * @param catalogId The catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    getLicense(catalogId: string, signal?: AbortSignal): Promise<string | null>;

    /**
     * Gets all attachments for the specified catalog.
     * @param catalogId The catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    getAttachments(catalogId: string, signal?: AbortSignal): Promise<string[]>;

    /**
     * Uploads the specified attachment.
     * @param catalogId The catalog identifier.
     * @param attachmentId The attachment identifier.
     * @param content The binary file content.
     * @param signal The signal to cancel the current operation.
     */
    uploadAttachment(catalogId: string, attachmentId: string, content: BodyInit, signal?: AbortSignal): Promise<Response>;

    /**
     * Deletes the specified attachment.
     * @param catalogId The catalog identifier.
     * @param attachmentId The attachment identifier.
     * @param signal The signal to cancel the current operation.
     */
    deleteAttachment(catalogId: string, attachmentId: string, signal?: AbortSignal): Promise<Response>;

    /**
     * Gets the specified attachment.
     * @param catalogId The catalog identifier.
     * @param attachmentId The attachment identifier.
     * @param signal The signal to cancel the current operation.
     */
    getAttachmentStream(catalogId: string, attachmentId: string, signal?: AbortSignal): Promise<Response>;

    /**
     * Gets the catalog metadata.
     * @param catalogId The catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    getMetadata(catalogId: string, signal?: AbortSignal): Promise<CatalogMetadata>;

    /**
     * Puts the catalog metadata.
     * @param catalogId The catalog identifier.
     * @param metadata The catalog metadata to set.
     * @param signal The signal to cancel the current operation.
     */
    setMetadata(catalogId: string, metadata: CatalogMetadata, signal?: AbortSignal): Promise<Response>;

}

/**
 * Provides methods to interact with catalogs.
 */
export class CatalogsClient implements ICatalogsClient {
    private _invoke: HttpRequestHandler;

    constructor(invoke: HttpRequestHandler) {
        this._invoke = invoke;
    }

    /**
     * Searches for the given resource paths and returns the corresponding catalog items.
     * @param resourcePaths The list of resource paths.
     * @param signal The signal to cancel the current operation.
     */
    public async searchCatalogItems(resourcePaths: string[], signal?: AbortSignal): Promise<Record<string, CatalogItem>> {
        let __url = "/api/v1/catalogs/search-items";

        return this._invoke<Record<string, CatalogItem>>("POST", __url, "application/json", "application/json", JSON.stringify(resourcePaths), signal);
    }

    /**
     * Gets the specified catalog.
     * @param catalogId The catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    public async get(catalogId: string, signal?: AbortSignal): Promise<ResourceCatalog> {
        let __url = "/api/v1/catalogs/{catalogId}";
        __url = __url.replace("{catalogId}", encodeURIComponent(String(catalogId)));

        return this._invoke<ResourceCatalog>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Gets a list of child catalog info for the provided parent catalog identifier.
     * @param catalogId The parent catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    public async getChildCatalogInfos(catalogId: string, signal?: AbortSignal): Promise<CatalogInfo[]> {
        let __url = "/api/v1/catalogs/{catalogId}/child-catalog-infos";
        __url = __url.replace("{catalogId}", encodeURIComponent(String(catalogId)));

        return this._invoke<CatalogInfo[]>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Gets the specified catalog's time range.
     * @param catalogId The catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    public async getTimeRange(catalogId: string, signal?: AbortSignal): Promise<CatalogTimeRange> {
        let __url = "/api/v1/catalogs/{catalogId}/timerange";
        __url = __url.replace("{catalogId}", encodeURIComponent(String(catalogId)));

        return this._invoke<CatalogTimeRange>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Gets the specified catalog's availability.
     * @param catalogId The catalog identifier.
     * @param begin Start date/time.
     * @param end End date/time.
     * @param step Step period.
     * @param signal The signal to cancel the current operation.
     */
    public async getAvailability(catalogId: string, begin: string, end: string, step: string, signal?: AbortSignal): Promise<CatalogAvailability> {
        let __url = "/api/v1/catalogs/{catalogId}/availability";
        __url = __url.replace("{catalogId}", encodeURIComponent(String(catalogId)));

        const __searchParams = new URLSearchParams();
        __searchParams.set("begin", String(begin));
        __searchParams.set("end", String(end));
        __searchParams.set("step", String(step));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<CatalogAvailability>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Gets the license of the catalog if available.
     * @param catalogId The catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    public async getLicense(catalogId: string, signal?: AbortSignal): Promise<string | null> {
        let __url = "/api/v1/catalogs/{catalogId}/license";
        __url = __url.replace("{catalogId}", encodeURIComponent(String(catalogId)));

        return this._invoke<string | null>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Gets all attachments for the specified catalog.
     * @param catalogId The catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    public async getAttachments(catalogId: string, signal?: AbortSignal): Promise<string[]> {
        let __url = "/api/v1/catalogs/{catalogId}/attachments";
        __url = __url.replace("{catalogId}", encodeURIComponent(String(catalogId)));

        return this._invoke<string[]>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Uploads the specified attachment.
     * @param catalogId The catalog identifier.
     * @param attachmentId The attachment identifier.
     * @param content The binary file content.
     * @param signal The signal to cancel the current operation.
     */
    public async uploadAttachment(catalogId: string, attachmentId: string, content: BodyInit, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}";
        __url = __url.replace("{catalogId}", encodeURIComponent(String(catalogId)));
        __url = __url.replace("{attachmentId}", encodeURIComponent(String(attachmentId)));

        return this._invoke<Response>("PUT", __url, "application/octet-stream", "application/octet-stream", content, signal);
    }

    /**
     * Deletes the specified attachment.
     * @param catalogId The catalog identifier.
     * @param attachmentId The attachment identifier.
     * @param signal The signal to cancel the current operation.
     */
    public async deleteAttachment(catalogId: string, attachmentId: string, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}";
        __url = __url.replace("{catalogId}", encodeURIComponent(String(catalogId)));
        __url = __url.replace("{attachmentId}", encodeURIComponent(String(attachmentId)));

        return this._invoke<Response>("DELETE", __url, "application/octet-stream", undefined, undefined, signal);
    }

    /**
     * Gets the specified attachment.
     * @param catalogId The catalog identifier.
     * @param attachmentId The attachment identifier.
     * @param signal The signal to cancel the current operation.
     */
    public async getAttachmentStream(catalogId: string, attachmentId: string, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}/content";
        __url = __url.replace("{catalogId}", encodeURIComponent(String(catalogId)));
        __url = __url.replace("{attachmentId}", encodeURIComponent(String(attachmentId)));

        return this._invoke<Response>("GET", __url, "application/octet-stream", undefined, undefined, signal);
    }

    /**
     * Gets the catalog metadata.
     * @param catalogId The catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    public async getMetadata(catalogId: string, signal?: AbortSignal): Promise<CatalogMetadata> {
        let __url = "/api/v1/catalogs/{catalogId}/metadata";
        __url = __url.replace("{catalogId}", encodeURIComponent(String(catalogId)));

        return this._invoke<CatalogMetadata>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Puts the catalog metadata.
     * @param catalogId The catalog identifier.
     * @param metadata The catalog metadata to set.
     * @param signal The signal to cancel the current operation.
     */
    public async setMetadata(catalogId: string, metadata: CatalogMetadata, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/catalogs/{catalogId}/metadata";
        __url = __url.replace("{catalogId}", encodeURIComponent(String(catalogId)));

        return this._invoke<Response>("PUT", __url, "application/octet-stream", "application/json", JSON.stringify(metadata), signal);
    }

}

/**
 * Provides methods to interact with data.
 */
export interface IDataClient {
    /**
     * Gets the requested data.
     * @param resourcePath The path to the resource data to stream.
     * @param begin Start date/time.
     * @param end End date/time.
     * @param signal The signal to cancel the current operation.
     */
    getStream(resourcePath: string, begin: string, end: string, signal?: AbortSignal): Promise<Response>;

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
     * Gets the requested data.
     * @param resourcePath The path to the resource data to stream.
     * @param begin Start date/time.
     * @param end End date/time.
     * @param signal The signal to cancel the current operation.
     */
    public async getStream(resourcePath: string, begin: string, end: string, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/data";

        const __searchParams = new URLSearchParams();
        __searchParams.set("resourcePath", String(resourcePath));
        __searchParams.set("begin", String(begin));
        __searchParams.set("end", String(end));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<Response>("GET", __url, "application/octet-stream", undefined, undefined, signal);
    }

}

/**
 * Provides methods to interact with jobs.
 */
export interface IJobsClient {
    /**
     * Gets a list of jobs.
     * @param signal The signal to cancel the current operation.
     */
    getJobs(signal?: AbortSignal): Promise<Job[]>;

    /**
     * Cancels the specified job.
     * @param jobId
     * @param signal The signal to cancel the current operation.
     */
    cancelJob(jobId: string, signal?: AbortSignal): Promise<Response>;

    /**
     * Gets the status of the specified job.
     * @param jobId
     * @param signal The signal to cancel the current operation.
     */
    getJobStatus(jobId: string, signal?: AbortSignal): Promise<JobStatus>;

    /**
     * Creates a new export job.
     * @param parameters Export parameters.
     * @param signal The signal to cancel the current operation.
     */
    export(parameters: ExportParameters, signal?: AbortSignal): Promise<Job>;

    /**
     * Creates a new job which reloads all extensions and resets the resource catalog.
     * @param signal The signal to cancel the current operation.
     */
    refreshDatabase(signal?: AbortSignal): Promise<Job>;

    /**
     * Clears the aggregation data cache for the specified period of time.
     * @param catalogId The catalog identifier.
     * @param begin Start date/time.
     * @param end End date/time.
     * @param signal The signal to cancel the current operation.
     */
    clearCache(catalogId: string, begin: string, end: string, signal?: AbortSignal): Promise<Job>;

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
     * Gets a list of jobs.
     * @param signal The signal to cancel the current operation.
     */
    public async getJobs(signal?: AbortSignal): Promise<Job[]> {
        let __url = "/api/v1/jobs";

        return this._invoke<Job[]>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Cancels the specified job.
     * @param jobId
     * @param signal The signal to cancel the current operation.
     */
    public async cancelJob(jobId: string, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/jobs/{jobId}";
        __url = __url.replace("{jobId}", encodeURIComponent(String(jobId)));

        return this._invoke<Response>("DELETE", __url, "application/octet-stream", undefined, undefined, signal);
    }

    /**
     * Gets the status of the specified job.
     * @param jobId
     * @param signal The signal to cancel the current operation.
     */
    public async getJobStatus(jobId: string, signal?: AbortSignal): Promise<JobStatus> {
        let __url = "/api/v1/jobs/{jobId}/status";
        __url = __url.replace("{jobId}", encodeURIComponent(String(jobId)));

        return this._invoke<JobStatus>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Creates a new export job.
     * @param parameters Export parameters.
     * @param signal The signal to cancel the current operation.
     */
    public async export(parameters: ExportParameters, signal?: AbortSignal): Promise<Job> {
        let __url = "/api/v1/jobs/export";

        return this._invoke<Job>("POST", __url, "application/json", "application/json", JSON.stringify(parameters), signal);
    }

    /**
     * Creates a new job which reloads all extensions and resets the resource catalog.
     * @param signal The signal to cancel the current operation.
     */
    public async refreshDatabase(signal?: AbortSignal): Promise<Job> {
        let __url = "/api/v1/jobs/refresh-database";

        return this._invoke<Job>("POST", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Clears the aggregation data cache for the specified period of time.
     * @param catalogId The catalog identifier.
     * @param begin Start date/time.
     * @param end End date/time.
     * @param signal The signal to cancel the current operation.
     */
    public async clearCache(catalogId: string, begin: string, end: string, signal?: AbortSignal): Promise<Job> {
        let __url = "/api/v1/jobs/clear-cache";

        const __searchParams = new URLSearchParams();
        __searchParams.set("catalogId", String(catalogId));
        __searchParams.set("begin", String(begin));
        __searchParams.set("end", String(end));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<Job>("POST", __url, "application/json", undefined, undefined, signal);
    }

}

/**
 * Provides methods to interact with package references.
 */
export interface IPackageReferencesClient {
    /**
     * Gets the list of package references.
     * @param signal The signal to cancel the current operation.
     */
    get(signal?: AbortSignal): Promise<Record<string, PackageReference>>;

    /**
     * Creates a package reference.
     * @param packageReference The package reference to create.
     * @param signal The signal to cancel the current operation.
     */
    create(packageReference: PackageReference, signal?: AbortSignal): Promise<string>;

    /**
     * Updates a package reference.
     * @param id The identifier of the package reference to update.
     * @param packageReference The new package reference.
     * @param signal The signal to cancel the current operation.
     */
    update(packageReference: PackageReference, id?: string | undefined, signal?: AbortSignal): Promise<Response>;

    /**
     * Deletes a package reference.
     * @param id The ID of the package reference.
     * @param signal The signal to cancel the current operation.
     */
    delete(id: string, signal?: AbortSignal): Promise<void>;

    /**
     * Gets package versions.
     * @param id The ID of the package reference.
     * @param signal The signal to cancel the current operation.
     */
    getVersions(id: string, signal?: AbortSignal): Promise<string[]>;

}

/**
 * Provides methods to interact with package references.
 */
export class PackageReferencesClient implements IPackageReferencesClient {
    private _invoke: HttpRequestHandler;

    constructor(invoke: HttpRequestHandler) {
        this._invoke = invoke;
    }

    /**
     * Gets the list of package references.
     * @param signal The signal to cancel the current operation.
     */
    public async get(signal?: AbortSignal): Promise<Record<string, PackageReference>> {
        let __url = "/api/v1/packagereferences";

        return this._invoke<Record<string, PackageReference>>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Creates a package reference.
     * @param packageReference The package reference to create.
     * @param signal The signal to cancel the current operation.
     */
    public async create(packageReference: PackageReference, signal?: AbortSignal): Promise<string> {
        let __url = "/api/v1/packagereferences";

        return this._invoke<string>("POST", __url, "application/json", "application/json", JSON.stringify(packageReference), signal);
    }

    /**
     * Updates a package reference.
     * @param id The identifier of the package reference to update.
     * @param packageReference The new package reference.
     * @param signal The signal to cancel the current operation.
     */
    public async update(packageReference: PackageReference, id?: string | undefined, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/packagereferences";

        const __searchParams = new URLSearchParams();
        if (id !== undefined && id !== null)
            __searchParams.set("id", String(id));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<Response>("PUT", __url, "application/octet-stream", "application/json", JSON.stringify(packageReference), signal);
    }

    /**
     * Deletes a package reference.
     * @param id The ID of the package reference.
     * @param signal The signal to cancel the current operation.
     */
    public async delete(id: string, signal?: AbortSignal): Promise<void> {
        let __url = "/api/v1/packagereferences/{id}";
        __url = __url.replace("{id}", encodeURIComponent(String(id)));

        return this._invoke<void>("DELETE", __url, undefined, undefined, undefined, signal);
    }

    /**
     * Gets package versions.
     * @param id The ID of the package reference.
     * @param signal The signal to cancel the current operation.
     */
    public async getVersions(id: string, signal?: AbortSignal): Promise<string[]> {
        let __url = "/api/v1/packagereferences/{id}/versions";
        __url = __url.replace("{id}", encodeURIComponent(String(id)));

        return this._invoke<string[]>("GET", __url, "application/json", undefined, undefined, signal);
    }

}

/**
 * Provides methods to interact with sources.
 */
export interface ISourcesClient {
    /**
     * Gets the list of source descriptions.
     * @param signal The signal to cancel the current operation.
     */
    getDescriptions(signal?: AbortSignal): Promise<ExtensionDescription[]>;

    /**
     * Gets the list of data source pipelines.
     * @param userId The optional user identifier. If not specified, the current user will be used.
     * @param signal The signal to cancel the current operation.
     */
    getPipelines(userId?: string | null, signal?: AbortSignal): Promise<Record<string, DataSourcePipeline>>;

    /**
     * Creates a data source pipeline.
     * @param userId The optional user identifier. If not specified, the current user will be used.
     * @param pipeline The pipeline to create.
     * @param signal The signal to cancel the current operation.
     */
    createPipeline(pipeline: DataSourcePipeline, userId?: string | null, signal?: AbortSignal): Promise<string>;

    /**
     * Updates a data source pipeline.
     * @param pipelineId The identifier of the pipeline to update.
     * @param userId The optional user identifier. If not specified, the current user will be used.
     * @param pipeline The new pipeline.
     * @param signal The signal to cancel the current operation.
     */
    updatePipeline(pipelineId: string, pipeline: DataSourcePipeline, userId?: string | null, signal?: AbortSignal): Promise<Response>;

    /**
     * Deletes a data source pipeline.
     * @param pipelineId The identifier of the pipeline to delete.
     * @param userId The optional user identifier. If not specified, the current user will be used.
     * @param signal The signal to cancel the current operation.
     */
    deletePipeline(pipelineId: string, userId?: string | null, signal?: AbortSignal): Promise<Response>;

}

/**
 * Provides methods to interact with sources.
 */
export class SourcesClient implements ISourcesClient {
    private _invoke: HttpRequestHandler;

    constructor(invoke: HttpRequestHandler) {
        this._invoke = invoke;
    }

    /**
     * Gets the list of source descriptions.
     * @param signal The signal to cancel the current operation.
     */
    public async getDescriptions(signal?: AbortSignal): Promise<ExtensionDescription[]> {
        let __url = "/api/v1/sources/descriptions";

        return this._invoke<ExtensionDescription[]>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Gets the list of data source pipelines.
     * @param userId The optional user identifier. If not specified, the current user will be used.
     * @param signal The signal to cancel the current operation.
     */
    public async getPipelines(userId?: string | null, signal?: AbortSignal): Promise<Record<string, DataSourcePipeline>> {
        let __url = "/api/v1/sources/pipelines";

        const __searchParams = new URLSearchParams();
        if (userId !== undefined && userId !== null)
            __searchParams.set("userId", String(userId));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<Record<string, DataSourcePipeline>>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Creates a data source pipeline.
     * @param userId The optional user identifier. If not specified, the current user will be used.
     * @param pipeline The pipeline to create.
     * @param signal The signal to cancel the current operation.
     */
    public async createPipeline(pipeline: DataSourcePipeline, userId?: string | null, signal?: AbortSignal): Promise<string> {
        let __url = "/api/v1/sources/pipelines";

        const __searchParams = new URLSearchParams();
        if (userId !== undefined && userId !== null)
            __searchParams.set("userId", String(userId));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<string>("POST", __url, "application/json", "application/json", JSON.stringify(pipeline), signal);
    }

    /**
     * Updates a data source pipeline.
     * @param pipelineId The identifier of the pipeline to update.
     * @param userId The optional user identifier. If not specified, the current user will be used.
     * @param pipeline The new pipeline.
     * @param signal The signal to cancel the current operation.
     */
    public async updatePipeline(pipelineId: string, pipeline: DataSourcePipeline, userId?: string | null, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/sources/pipelines/{pipelineId}";
        __url = __url.replace("{pipelineId}", encodeURIComponent(String(pipelineId)));

        const __searchParams = new URLSearchParams();
        if (userId !== undefined && userId !== null)
            __searchParams.set("userId", String(userId));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<Response>("PUT", __url, "application/octet-stream", "application/json", JSON.stringify(pipeline), signal);
    }

    /**
     * Deletes a data source pipeline.
     * @param pipelineId The identifier of the pipeline to delete.
     * @param userId The optional user identifier. If not specified, the current user will be used.
     * @param signal The signal to cancel the current operation.
     */
    public async deletePipeline(pipelineId: string, userId?: string | null, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/sources/pipelines/{pipelineId}";
        __url = __url.replace("{pipelineId}", encodeURIComponent(String(pipelineId)));

        const __searchParams = new URLSearchParams();
        if (userId !== undefined && userId !== null)
            __searchParams.set("userId", String(userId));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<Response>("DELETE", __url, "application/octet-stream", undefined, undefined, signal);
    }

}

/**
 * Provides methods to interact with system.
 */
export interface ISystemClient {
    /**
     * Gets the default file type.
     * @param signal The signal to cancel the current operation.
     */
    getDefaultFileType(signal?: AbortSignal): Promise<string>;

    /**
     * Gets the configured help link.
     * @param signal The signal to cancel the current operation.
     */
    getHelpLink(signal?: AbortSignal): Promise<string>;

}

/**
 * Provides methods to interact with system.
 */
export class SystemClient implements ISystemClient {
    private _invoke: HttpRequestHandler;

    constructor(invoke: HttpRequestHandler) {
        this._invoke = invoke;
    }

    /**
     * Gets the default file type.
     * @param signal The signal to cancel the current operation.
     */
    public async getDefaultFileType(signal?: AbortSignal): Promise<string> {
        let __url = "/api/v1/system/file-type";

        return this._invoke<string>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Gets the configured help link.
     * @param signal The signal to cancel the current operation.
     */
    public async getHelpLink(signal?: AbortSignal): Promise<string> {
        let __url = "/api/v1/system/help-link";

        return this._invoke<string>("GET", __url, "application/json", undefined, undefined, signal);
    }

}

/**
 * Provides methods to interact with users.
 */
export interface IUsersClient {
    /**
     * Authenticates the user.
     * @param scheme The authentication scheme to challenge.
     * @param returnUrl The URL to return after successful authentication.
     * @param signal The signal to cancel the current operation.
     */
    authenticate(scheme: string, returnUrl: string, signal?: AbortSignal): Promise<Response>;

    /**
     * Logs out the user.
     * @param returnUrl The URL to return after logout.
     * @param signal The signal to cancel the current operation.
     */
    signOut(returnUrl: string, signal?: AbortSignal): Promise<void>;

    /**
     * Deletes a personal access token.
     * @param value The personal access token to delete.
     * @param signal The signal to cancel the current operation.
     */
    deleteTokenByValue(value: string, signal?: AbortSignal): Promise<Response>;

    /**
     * Gets the current user.
     * @param signal The signal to cancel the current operation.
     */
    getMe(signal?: AbortSignal): Promise<MeResponse>;

    /**
     * Allows the user to reauthenticate in case of modified claims.
     * @param signal The signal to cancel the current operation.
     */
    reAuthenticate(signal?: AbortSignal): Promise<Response>;

    /**
     * Gets all personal access tokens.
     * @param userId The optional user identifier. If not specified, the current user will be used.
     * @param signal The signal to cancel the current operation.
     */
    getTokens(userId?: string | null, signal?: AbortSignal): Promise<Record<string, PersonalAccessToken>>;

    /**
     * Creates a personal access token.
     * @param userId The optional user identifier. If not specified, the current user will be used.
     * @param token The personal access token to create.
     * @param signal The signal to cancel the current operation.
     */
    createToken(token: PersonalAccessToken, userId?: string | null, signal?: AbortSignal): Promise<string>;

    /**
     * Deletes a personal access token.
     * @param tokenId The identifier of the personal access token.
     * @param signal The signal to cancel the current operation.
     */
    deleteToken(tokenId: string, signal?: AbortSignal): Promise<Response>;

    /**
     * Accepts the license of the specified catalog.
     * @param catalogId The catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    acceptLicense(catalogId: string, signal?: AbortSignal): Promise<Response>;

    /**
     * Gets a list of users.
     * @param signal The signal to cancel the current operation.
     */
    getUsers(signal?: AbortSignal): Promise<Record<string, NexusUser>>;

    /**
     * Creates a user.
     * @param user The user to create.
     * @param signal The signal to cancel the current operation.
     */
    createUser(user: NexusUser, signal?: AbortSignal): Promise<string>;

    /**
     * Deletes a user.
     * @param userId The identifier of the user.
     * @param signal The signal to cancel the current operation.
     */
    deleteUser(userId: string, signal?: AbortSignal): Promise<Response>;

    /**
     * Gets all claims.
     * @param userId The identifier of the user.
     * @param signal The signal to cancel the current operation.
     */
    getClaims(userId: string, signal?: AbortSignal): Promise<Record<string, NexusClaim>>;

    /**
     * Creates a claim.
     * @param userId The identifier of the user.
     * @param claim The claim to create.
     * @param signal The signal to cancel the current operation.
     */
    createClaim(userId: string, claim: NexusClaim, signal?: AbortSignal): Promise<string>;

    /**
     * Deletes a claim.
     * @param claimId The identifier of the claim.
     * @param signal The signal to cancel the current operation.
     */
    deleteClaim(claimId: string, signal?: AbortSignal): Promise<Response>;

}

/**
 * Provides methods to interact with users.
 */
export class UsersClient implements IUsersClient {
    private _invoke: HttpRequestHandler;

    constructor(invoke: HttpRequestHandler) {
        this._invoke = invoke;
    }

    /**
     * Authenticates the user.
     * @param scheme The authentication scheme to challenge.
     * @param returnUrl The URL to return after successful authentication.
     * @param signal The signal to cancel the current operation.
     */
    public async authenticate(scheme: string, returnUrl: string, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/users/authenticate";

        const __searchParams = new URLSearchParams();
        __searchParams.set("scheme", String(scheme));
        __searchParams.set("returnUrl", String(returnUrl));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<Response>("POST", __url, "application/octet-stream", undefined, undefined, signal);
    }

    /**
     * Logs out the user.
     * @param returnUrl The URL to return after logout.
     * @param signal The signal to cancel the current operation.
     */
    public async signOut(returnUrl: string, signal?: AbortSignal): Promise<void> {
        let __url = "/api/v1/users/signout";

        const __searchParams = new URLSearchParams();
        __searchParams.set("returnUrl", String(returnUrl));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<void>("POST", __url, undefined, undefined, undefined, signal);
    }

    /**
     * Deletes a personal access token.
     * @param value The personal access token to delete.
     * @param signal The signal to cancel the current operation.
     */
    public async deleteTokenByValue(value: string, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/users/tokens/delete";

        const __searchParams = new URLSearchParams();
        __searchParams.set("value", String(value));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<Response>("DELETE", __url, "application/octet-stream", undefined, undefined, signal);
    }

    /**
     * Gets the current user.
     * @param signal The signal to cancel the current operation.
     */
    public async getMe(signal?: AbortSignal): Promise<MeResponse> {
        let __url = "/api/v1/users/me";

        return this._invoke<MeResponse>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Allows the user to reauthenticate in case of modified claims.
     * @param signal The signal to cancel the current operation.
     */
    public async reAuthenticate(signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/users/reauthenticate";

        return this._invoke<Response>("GET", __url, "application/octet-stream", undefined, undefined, signal);
    }

    /**
     * Gets all personal access tokens.
     * @param userId The optional user identifier. If not specified, the current user will be used.
     * @param signal The signal to cancel the current operation.
     */
    public async getTokens(userId?: string | null, signal?: AbortSignal): Promise<Record<string, PersonalAccessToken>> {
        let __url = "/api/v1/users/tokens";

        const __searchParams = new URLSearchParams();
        if (userId !== undefined && userId !== null)
            __searchParams.set("userId", String(userId));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<Record<string, PersonalAccessToken>>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Creates a personal access token.
     * @param userId The optional user identifier. If not specified, the current user will be used.
     * @param token The personal access token to create.
     * @param signal The signal to cancel the current operation.
     */
    public async createToken(token: PersonalAccessToken, userId?: string | null, signal?: AbortSignal): Promise<string> {
        let __url = "/api/v1/users/tokens/create";

        const __searchParams = new URLSearchParams();
        if (userId !== undefined && userId !== null)
            __searchParams.set("userId", String(userId));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<string>("POST", __url, "application/json", "application/json", JSON.stringify(token), signal);
    }

    /**
     * Deletes a personal access token.
     * @param tokenId The identifier of the personal access token.
     * @param signal The signal to cancel the current operation.
     */
    public async deleteToken(tokenId: string, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/users/tokens/{tokenId}";
        __url = __url.replace("{tokenId}", encodeURIComponent(String(tokenId)));

        return this._invoke<Response>("DELETE", __url, "application/octet-stream", undefined, undefined, signal);
    }

    /**
     * Accepts the license of the specified catalog.
     * @param catalogId The catalog identifier.
     * @param signal The signal to cancel the current operation.
     */
    public async acceptLicense(catalogId: string, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/users/accept-license";

        const __searchParams = new URLSearchParams();
        __searchParams.set("catalogId", String(catalogId));
        const __query = __searchParams.toString();
        if (__query)
            __url += "?" + __query;

        return this._invoke<Response>("GET", __url, "application/octet-stream", undefined, undefined, signal);
    }

    /**
     * Gets a list of users.
     * @param signal The signal to cancel the current operation.
     */
    public async getUsers(signal?: AbortSignal): Promise<Record<string, NexusUser>> {
        let __url = "/api/v1/users";

        return this._invoke<Record<string, NexusUser>>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Creates a user.
     * @param user The user to create.
     * @param signal The signal to cancel the current operation.
     */
    public async createUser(user: NexusUser, signal?: AbortSignal): Promise<string> {
        let __url = "/api/v1/users";

        return this._invoke<string>("POST", __url, "application/json", "application/json", JSON.stringify(user), signal);
    }

    /**
     * Deletes a user.
     * @param userId The identifier of the user.
     * @param signal The signal to cancel the current operation.
     */
    public async deleteUser(userId: string, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/users/{userId}";
        __url = __url.replace("{userId}", encodeURIComponent(String(userId)));

        return this._invoke<Response>("DELETE", __url, "application/octet-stream", undefined, undefined, signal);
    }

    /**
     * Gets all claims.
     * @param userId The identifier of the user.
     * @param signal The signal to cancel the current operation.
     */
    public async getClaims(userId: string, signal?: AbortSignal): Promise<Record<string, NexusClaim>> {
        let __url = "/api/v1/users/{userId}/claims";
        __url = __url.replace("{userId}", encodeURIComponent(String(userId)));

        return this._invoke<Record<string, NexusClaim>>("GET", __url, "application/json", undefined, undefined, signal);
    }

    /**
     * Creates a claim.
     * @param userId The identifier of the user.
     * @param claim The claim to create.
     * @param signal The signal to cancel the current operation.
     */
    public async createClaim(userId: string, claim: NexusClaim, signal?: AbortSignal): Promise<string> {
        let __url = "/api/v1/users/{userId}/claims";
        __url = __url.replace("{userId}", encodeURIComponent(String(userId)));

        return this._invoke<string>("POST", __url, "application/json", "application/json", JSON.stringify(claim), signal);
    }

    /**
     * Deletes a claim.
     * @param claimId The identifier of the claim.
     * @param signal The signal to cancel the current operation.
     */
    public async deleteClaim(claimId: string, signal?: AbortSignal): Promise<Response> {
        let __url = "/api/v1/users/claims/{claimId}";
        __url = __url.replace("{claimId}", encodeURIComponent(String(claimId)));

        return this._invoke<Response>("DELETE", __url, "application/octet-stream", undefined, undefined, signal);
    }

}

/**
 * Provides methods to interact with writers.
 */
export interface IWritersClient {
    /**
     * Gets the list of writer descriptions.
     * @param signal The signal to cancel the current operation.
     */
    getDescriptions(signal?: AbortSignal): Promise<ExtensionDescription[]>;

}

/**
 * Provides methods to interact with writers.
 */
export class WritersClient implements IWritersClient {
    private _invoke: HttpRequestHandler;

    constructor(invoke: HttpRequestHandler) {
        this._invoke = invoke;
    }

    /**
     * Gets the list of writer descriptions.
     * @param signal The signal to cancel the current operation.
     */
    public async getDescriptions(signal?: AbortSignal): Promise<ExtensionDescription[]> {
        let __url = "/api/v1/writers/descriptions";

        return this._invoke<ExtensionDescription[]>("GET", __url, "application/json", undefined, undefined, signal);
    }

}


/**
 * A catalog item consists of a catalog, a resource and a representation.
 */
export interface CatalogItem {
    /** The catalog. */
    catalog?: ResourceCatalog | undefined;
    /** The resource. */
    resource?: Resource | undefined;
    /** The representation. */
    representation?: Representation | undefined;
    /** The optional dictionary of representation parameters and its arguments. */
    parameters?: Record<string, string> | null;
}


/**
 * A catalog is a top level element and holds a list of resources.
 */
export interface ResourceCatalog {
    /** Gets the identifier. */
    id?: string | undefined;
    /** Gets the properties. */
    properties?: Record<string, unknown> | null;
    /** Gets the list of representations. */
    resources?: Resource[] | null;
}


/**
 * A resource is part of a resource catalog and holds a list of representations.
 */
export interface Resource {
    /** Gets the identifier. */
    id?: string | undefined;
    /** Gets the properties. */
    properties?: Record<string, unknown> | null;
    /** Gets the list of representations. */
    representations?: Representation[] | null;
}


/**
 * A representation is part of a resource.
 */
export interface Representation {
    /** The data type. */
    dataType?: NexusDataType | undefined;
    /** The sample period. */
    samplePeriod?: string | undefined;
    /** The optional list of parameters. */
    parameters?: Record<string, unknown> | null;
}


/**
 * Specifies the Nexus data type.
 */
export enum NexusDataType {
    UInt8 = "UInt8",
    UInt16 = "UInt16",
    UInt32 = "UInt32",
    UInt64 = "UInt64",
    Int8 = "Int8",
    Int16 = "Int16",
    Int32 = "Int32",
    Int64 = "Int64",
    Float32 = "Float32",
    Float64 = "Float64"
}


/**
 * A structure for catalog information.
 */
export interface CatalogInfo {
    /** The identifier. */
    id?: string | undefined;
    /** A nullable title. */
    title?: string | null;
    /** A nullable contact. */
    contact?: string | null;
    /** A nullable readme. */
    readme?: string | null;
    /** A nullable license. */
    license?: string | null;
    /** A boolean which indicates if the catalog is accessible. */
    isReadable?: boolean | undefined;
    /** A boolean which indicates if the catalog is editable. */
    isWritable?: boolean | undefined;
    /** A boolean which indicates if the catalog is released. */
    isReleased?: boolean | undefined;
    /** A boolean which indicates if the catalog is visible. */
    isVisible?: boolean | undefined;
    /** A boolean which indicates if the catalog is owned by the current user. */
    isOwner?: boolean | undefined;
    /** The package reference identifiers. */
    packageReferenceIds?: string[] | undefined;
    /** A structure for pipeline info. */
    pipelineInfo?: PipelineInfo | undefined;
}


/**
 * A structure for pipeline information.
 */
export interface PipelineInfo {
    /** The pipeline identifier. */
    id?: string | undefined;
    /** An array of data source types. */
    types?: string[] | undefined;
    /** An array of data source info URLs. */
    infoUrls?: (string | null)[] | undefined;
}


/**
 * A catalog time range.
 */
export interface CatalogTimeRange {
    /** The date/time of the first data in the catalog. */
    begin?: string | undefined;
    /** The date/time of the last data in the catalog. */
    end?: string | undefined;
}


/**
 * The catalog availability.
 */
export interface CatalogAvailability {
    /** The actual availability data. */
    data?: number[] | undefined;
}


/**
 * A structure for catalog metadata.
 */
export interface CatalogMetadata {
    /** The contact. */
    contact?: string | null;
    /** A list of groups the catalog is part of. */
    groupMemberships?: string[] | null;
    /** Overrides for the catalog. */
    overrides?: ResourceCatalog | null;
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
 * Describes the status of the job.
 */
export interface JobStatus {
    /** The start date/time. */
    start?: string | undefined;
    /** The status. */
    status?: TaskStatus | undefined;
    /** The progress from 0 to 1. */
    progress?: number | undefined;
    /** The nullable exception message. */
    exceptionMessage?: string | null;
    /** The nullable result. */
    result?: unknown | null;
}


/**
 *
 */
export enum TaskStatus {
    Created = "Created",
    WaitingForActivation = "WaitingForActivation",
    WaitingToRun = "WaitingToRun",
    Running = "Running",
    WaitingForChildrenToComplete = "WaitingForChildrenToComplete",
    RanToCompletion = "RanToCompletion",
    Canceled = "Canceled",
    Faulted = "Faulted"
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
}


/**
 * A package reference.
 */
export interface PackageReference {
    /** The provider which loads the package. */
    provider?: string | undefined;
    /** The configuration of the package reference. */
    configuration?: Record<string, string> | undefined;
}


/**
 * An extension description.
 */
export interface ExtensionDescription {
    /** The extension type. */
    type?: string | undefined;
    /** The extension version. */
    version?: string | undefined;
    /** A nullable description. */
    description?: string | null;
    /** A nullable project website URL. */
    projectUrl?: string | null;
    /** A nullable source repository URL. */
    repositoryUrl?: string | null;
    /** Additional information about the extension. */
    additionalInformation?: Record<string, unknown> | undefined;
}


/**
 * A data source pipeline.
 */
export interface DataSourcePipeline {
    /** The list of pipeline elements (data source registrations). */
    registrations?: DataSourceRegistration[] | undefined;
    /** An optional regular expressions pattern to select the catalogs to be released. By default, all catalogs will be released. */
    releasePattern?: string | null;
    /** An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible. */
    visibilityPattern?: string | null;
}


/**
 * A data source registration.
 */
export interface DataSourceRegistration {
    /** The type of the data source. */
    type?: string | undefined;
    /** An optional URL which points to the data. */
    resourceLocator?: string | null;
    /** Configuration parameters for the instantiated source. */
    configuration?: unknown | undefined;
    /** An optional info URL. */
    infoUrl?: string | null;
}


/**
 * A me response.
 */
export interface MeResponse {
    /** The user id. */
    userId?: string | undefined;
    /** The user. */
    user?: NexusUser | undefined;
}


/**
 * Represents a user.
 */
export interface NexusUser {
    /** The user name. */
    name?: string | undefined;
    /** The list of claims. */
    claims?: NexusClaim[] | undefined;
}


/**
 * Represents a claim.
 */
export interface NexusClaim {
    /** The claim type. */
    type?: string | undefined;
    /** The claim value. */
    value?: string | undefined;
}


/**
 * A personal access token.
 */
export interface PersonalAccessToken {
    /** The token description. */
    description?: string | undefined;
    /** The date/time when the token expires. */
    expires?: string | undefined;
    /** The claims that will be part of the token. */
    claims?: TokenClaim[] | undefined;
}


/**
 * A revoke token request.
 */
export interface TokenClaim {
    /** The claim type. */
    type?: string | undefined;
    /** The claim value. */
    value?: string | undefined;
}

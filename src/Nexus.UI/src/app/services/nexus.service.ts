import { Injectable, isDevMode, signal } from "@angular/core";
import { NexusClient, type BufferProvider, type ResourceInfo } from "@nexus-api/_client";
import * as V1 from "@nexus-api/V1";
import * as V2 from "@nexus-api/V2";
import { requestError } from "../request-error";
import {
  decodeVisualization,
  type VisualizationRequest,
  type VisualizationFrame,
} from "../charts/remote-visualization";

export type CatalogNode = V1.CatalogInfo & {
  nodeKey: string;
  depth: number;
  parentId: string;
  isFake: boolean;
  groupedChildren?: V1.CatalogInfo[];
};

export type PreparedCatalogNode = Omit<CatalogNode, "depth" | "parentId">;

export type ResourceRow = {
  catalogId: string;
  id: string;
  path: string;
  description: string;
  warning?: string;
  unit: string;
  groups: string[];
  properties: Record<string, unknown> | null;
  representations: V1.Representation[];
};

export type WriterOption = {
  type?: string;
  label?: string;
  default?: unknown;
  items?: Record<string, string>;
  minimum?: number;
  maximum?: number;
};

export type WriterDescription = V1.ExtensionDescription & {
  additionalInformation?: {
    label?: string;
    options?: Record<string, WriterOption>;
  };
};

export type CatalogBundle = {
  catalog: V1.ResourceCatalog;
  timeRange?: V1.CatalogTimeRange;
  metadata?: V1.CatalogMetadata;
  attachments: string[];
};

export type SessionOverview = {
  system: V1.SystemResponse;
  me: V1.MeResponse;
  writers: WriterDescription[];
  jobs: V1.Job[];
  roots: V1.CatalogInfo[];
};

export type DevAuthMode = "admin" | "user";

const devAuthModeStorageKey = "nexus.devAuthMode";
const devAuthRoleHeader = "X-Nexus-Dev-Role";
const demoCatalogId = "/SAMPLE/LOCAL";
const demoSamplePeriod = "00:01:00";

@Injectable({ providedIn: "root" })
export class NexusService {
  readonly endpoint = globalThis.location?.origin ?? "http://localhost:4200";
  readonly apiAvailable = signal(false);
  readonly system = signal<V1.SystemResponse | null>(null);
  readonly currentUser = signal<V1.MeResponse | null>(null);
  readonly devAuthMode = signal<DevAuthMode>(getInitialDevAuthMode());
  private readonly demoMode = isGitHubPagesDemo();
  private readonly client = new NexusClient(this.endpoint);
  readonly v1 = new V1.V1(this.invoke.bind(this));
  readonly v2 = new V2.V2(this.invoke.bind(this));

  async getCatalogChildren(catalogId = "/") {
    const children = await this.v1.catalogs.getChildCatalogInfos(catalogId);

    this.apiAvailable.set(true);
    const devChildren = isDevMode() && catalogId === "/" ? [createRestrictedDevCatalog()] : [];

    return [...children, ...devChildren]
      .filter((info) => info.isVisible)
      .sort((a, b) => (a.id ?? "").localeCompare(b.id ?? ""));
  }

  async getCatalogBundle(catalogId: string): Promise<CatalogBundle> {
    const [catalog, timeRange, metadata, attachments] = await Promise.all([
      this.v1.catalogs.get(catalogId),
      this.v1.catalogs.getTimeRange(catalogId).catch(() => undefined),
      this.v1.catalogs.getMetadata(catalogId).catch(() => undefined),
      this.v1.catalogs.getAttachments(catalogId).catch(() => [] as string[]),
    ]);

    this.apiAvailable.set(true);

    return { catalog, timeRange, metadata, attachments };
  }

  async getCatalogLicense(catalogId: string) {
    const license = await this.v1.catalogs.getLicense(catalogId);

    this.apiAvailable.set(true);

    return license ?? "";
  }

  async acceptCatalogLicense(catalogId: string) {
    await this.v1.catalogs.acceptLicense(catalogId);
    this.apiAvailable.set(true);
  }

  async uploadCatalogAttachment(catalogId: string, attachmentId: string, content: BodyInit) {
    await this.v1.catalogs.uploadAttachment(catalogId, attachmentId, content);
    this.apiAvailable.set(true);
  }

  async deleteCatalogAttachment(catalogId: string, attachmentId: string) {
    await this.v1.catalogs.deleteAttachment(catalogId, attachmentId);
    this.apiAvailable.set(true);
  }

  async getSessionOverview(): Promise<SessionOverview> {
    const [system, me, writers, jobs, roots] = await Promise.all([
      this.v1.system.get().then((system) => {
        this.system.set(system);

        return system;
      }),
      this.v1.users.getMe().then((me) => {
        this.currentUser.set(me);

        return me;
      }),
      this.v1.writers.getDescriptions() as Promise<WriterDescription[]>,
      this.v1.jobs.getJobs(),
      this.getCatalogChildren("/"),
    ]);

    this.apiAvailable.set(true);

    return { system, me, writers, jobs, roots };
  }

  async getPersonalAccessTokens() {
    const tokens = await this.v1.users.getTokens();

    this.apiAvailable.set(true);

    return tokens;
  }

  async createPersonalAccessToken(token: V1.CreateTokenRequest) {
    const value = await this.v1.users.createToken(token);

    this.apiAvailable.set(true);

    return value;
  }

  async deletePersonalAccessToken(tokenId: string) {
    await this.v1.users.deleteToken(tokenId);
    this.apiAvailable.set(true);
  }

  setDevAuthMode(mode: DevAuthMode) {
    const nextMode = mode === "user" ? "user" : "admin";

    this.devAuthMode.set(nextMode);

    if (isDevMode()) {
      localStorage.setItem(devAuthModeStorageKey, nextMode);
    }
  }

  async exportResources(parameters: V2.ExportParameters) {
    return this.v2.jobs.export(parameters);
  }

  async loadResourcesIntoBuffers(
    begin: string,
    end: string,
    resourcePaths: string[],
    precision: V2.Precision,
    bufferProvider: BufferProvider,
    onProgress?: ((progress: number) => void) | undefined,
    signal?: AbortSignal,
  ) {
    if (this.demoMode) {
      const result = loadDemoResourcesIntoBuffers(
        begin,
        end,
        resourcePaths,
        precision,
        bufferProvider,
        onProgress,
        signal,
      );

      this.apiAvailable.set(true);

      return result;
    }

    const result = await this.client.load(
      begin,
      end,
      resourcePaths,
      precision,
      bufferProvider,
      onProgress,
      signal,
    );

    this.apiAvailable.set(true);

    return result;
  }

  async loadVisualization(
    request: VisualizationRequest,
    sampleCount: bigint,
    signal: AbortSignal,
    onFrame: (frame: VisualizationFrame) => void,
  ): Promise<void> {
    const response = await this.invoke<Response>(
      "POST",
      "/api/v2/data/visualization",
      "application/vnd.apache.arrow.stream",
      "application/json",
      JSON.stringify(request),
      signal,
    );

    await decodeVisualization(response, request, sampleCount, signal, onFrame);
    this.apiAvailable.set(true);
  }

  private async invoke<T>(
    method: string,
    url: string,
    accept?: string,
    contentType?: string,
    body?: BodyInit | null,
    signal?: AbortSignal,
  ): Promise<T> {
    if (this.demoMode) {
      signal?.throwIfAborted();
      const result = await handleDemoRequest(method, url, body, signal);

      this.apiAvailable.set(true);

      return result as T;
    }

    const headers = new Headers();

    if (accept) {
      headers.set("Accept", accept);
    }

    if (contentType) {
      headers.set("Content-Type", contentType);
    }

    if (isDevMode()) {
      headers.set(devAuthRoleHeader, this.devAuthMode());
    }

    const response = await fetch(`${this.endpoint}${url}`, { method, headers, body, signal });

    if (!response.ok) {
      throw await requestError(response);
    }

    if (accept === "application/octet-stream" || accept === "application/vnd.apache.arrow.stream") {
      return response as T;
    }

    const text = await response.text();

    if (!text) {
      return undefined as T;
    }

    // Reject unrepresentable configuration numbers before JSON.parse can round a saved value.
    if (method === "GET" && url.split("?")[0] === "/api/v1/sources/pipelines") {
      const { parseJsonSafely } = await import("../json-schema");
      const parsed = parseJsonSafely(text);

      if (!parsed.valid) {
        throw new Error(`Cannot safely edit these pipelines: ${parsed.errors.join(" ")}`);
      }

      return parsed.value as T;
    }

    return accept?.includes("json") ? (JSON.parse(text) as T) : (text as T);
  }
}

export function prepareChildCatalogs(
  parentId: string,
  childInfos: V1.CatalogInfo[],
): PreparedCatalogNode[] {
  const normalizedParentId = parentId === "/" ? "" : parentId;
  const groups = new Map<string, V1.CatalogInfo[]>();

  for (const info of childInfos) {
    if (!info.isVisible) {
      continue;
    }

    const remainder = (info.id ?? "").slice(normalizedParentId.length);
    const nextSegment = remainder.split("/").filter(Boolean)[0] ?? "";

    groups.set(nextSegment, [...(groups.get(nextSegment) ?? []), info]);
  }

  const result: PreparedCatalogNode[] = [];

  for (const [segment, group] of groups) {
    const fakeId = `${normalizedParentId}/${segment}`;

    if (group.length > 1 || group[0].id !== fakeId) {
      result.push({
        nodeKey: `fake:${normalizedParentId || "/"}:${fakeId}`,
        id: fakeId,
        title: null,
        contact: null,
        readme: null,
        license: null,
        isReadable: true,
        isWritable: false,
        isVisible: true,
        packageReferenceIds: [],
        pipelineInfo: { id: "", types: [], infoUrls: [] },
        isFake: true,
        groupedChildren: group,
      });
    } else {
      result.push({ ...group[0], nodeKey: `real:${group[0].id ?? ""}`, isFake: false });
    }
  }

  return result.sort((a, b) => (a.id ?? "").localeCompare(b.id ?? ""));
}

export function mapResources(catalog: V1.ResourceCatalog | undefined): ResourceRow[] {
  const catalogId = catalog?.id ?? "/";

  return (catalog?.resources ?? []).map((resource) => {
    const properties = resource.properties;

    return {
      catalogId,
      id: resource.id ?? "unnamed-resource",
      path: `${catalogId}/${resource.id ?? ""}`.replace(/\/+/g, "/"),
      description: getString(properties, "description") ?? "",
      warning: getString(properties, "warning") ?? "",
      unit: getString(properties, "unit") ?? "",
      groups: getStringArray(properties, "groups"),
      properties: properties ?? null,
      representations: resource.representations ?? [],
    };
  });
}

export function buildExportParameters(
  begin: string,
  end: string,
  filePeriod: string,
  writer: WriterDescription | undefined,
  resourcePaths: string[],
  configuration: Record<string, unknown>,
  precision: V2.Precision,
): V2.ExportParameters {
  return {
    begin,
    end,
    filePeriod,
    type: writer?.type ?? null,
    resourcePaths,
    configuration,
    precision,
  };
}

function getString(record: Record<string, unknown> | null | undefined, key: string) {
  const value = record?.[key];

  return typeof value === "string" ? value : undefined;
}

function getStringArray(record: Record<string, unknown> | null | undefined, key: string) {
  const value = record?.[key];

  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === "string")
    : [];
}

function createRestrictedDevCatalog(): V1.CatalogInfo {
  return {
    id: "/DEV/RESTRICTED",
    title: "Restricted development catalog",
    contact: "nexus-dev@example.invalid",
    readme:
      "This synthetic catalog is visible in Angular dev mode only, but it is intentionally not readable.",
    license: null,
    isReadable: false,
    isWritable: false,
    isVisible: true,
    packageReferenceIds: [],
    pipelineInfo: { id: "", types: ["dev"], infoUrls: [] },
  };
}

function isGitHubPagesDemo() {
  const location = globalThis.location;

  return (
    location?.hostname.endsWith("github.io") === true && location.pathname.startsWith("/nexus")
  );
}

async function handleDemoRequest(
  method: string,
  url: string,
  body?: BodyInit | null,
  signal?: AbortSignal,
) {
  const [path, query = ""] = url.split("?");
  const parameters = new URLSearchParams(query);

  await Promise.resolve();
  signal?.throwIfAborted();

  if (method === "GET" && path === "/api/v1/system") {
    return getDemoSystem();
  }

  if (method === "GET" && path === "/api/v1/users/me") {
    return getDemoUser();
  }

  if (method === "GET" && path === "/api/v1/users/tokens") {
    return {};
  }

  if (method === "POST" && path === "/api/v1/users/tokens/create") {
    throw new Error("Personal access tokens are not available in the static GitHub Pages demo.");
  }

  if (method === "DELETE" && path.startsWith("/api/v1/users/tokens/")) {
    return undefined;
  }

  if (method === "GET" && path === "/api/v1/writers/descriptions") {
    return [getDemoWriter()];
  }

  if (method === "GET" && path === "/api/v1/jobs") {
    return [];
  }

  if (method === "POST" && (path === "/api/v1/jobs/export" || path === "/api/v2/jobs/export")) {
    throw new Error("Exports are not available in the static GitHub Pages demo.");
  }

  if (method === "POST" && path === "/api/v1/jobs/refresh-database") {
    throw new Error("Database refresh is not available in the static GitHub Pages demo.");
  }

  if (method === "GET" && path === "/api/v1/sources/descriptions") {
    return [];
  }

  if (method === "GET" && path === "/api/v1/sources/pipelines") {
    return {};
  }

  if (method === "GET" && path === "/api/v1/packagereferences") {
    return {};
  }

  if (method === "POST" && path === "/api/v1/catalogs/search-items") {
    const resourcePaths = JSON.parse(String(body ?? "[]")) as string[];

    return Object.fromEntries(
      resourcePaths.map((resourcePath) => [resourcePath, getDemoCatalogItem(resourcePath)]),
    );
  }

  const catalogChildMatch = path.match(/^\/api\/v1\/catalogs\/(.*)\/child-catalog-infos$/);

  if (method === "GET" && catalogChildMatch) {
    const catalogId = decodeURIComponent(catalogChildMatch[1]);

    return catalogId === "/" ? [getDemoCatalogInfo()] : [];
  }

  const catalogTimeRangeMatch = path.match(/^\/api\/v1\/catalogs\/(.*)\/timerange$/);

  if (method === "GET" && catalogTimeRangeMatch) {
    return getDemoTimeRange();
  }

  const catalogAvailabilityMatch = path.match(/^\/api\/v1\/catalogs\/(.*)\/availability$/);

  if (method === "GET" && catalogAvailabilityMatch) {
    const begin = parameters.get("begin") ?? getDemoTimeRange().begin!;
    const end = parameters.get("end") ?? getDemoTimeRange().end!;
    const step = parameters.get("step") ?? demoSamplePeriod;

    return getDemoAvailability(begin, end, step);
  }

  const catalogLicenseMatch = path.match(/^\/api\/v1\/catalogs\/(.*)\/license$/);

  if (method === "GET" && catalogLicenseMatch) {
    return "";
  }

  const catalogAcceptLicenseMatch = path.match(/^\/api\/v1\/catalogs\/(.*)\/accept-license$/);

  if (method === "POST" && catalogAcceptLicenseMatch) {
    return undefined;
  }

  const catalogAttachmentsMatch = path.match(/^\/api\/v1\/catalogs\/(.*)\/attachments$/);

  if (method === "GET" && catalogAttachmentsMatch) {
    return [];
  }

  const catalogMetadataMatch = path.match(/^\/api\/v1\/catalogs\/(.*)\/metadata$/);

  if (method === "GET" && catalogMetadataMatch) {
    return { contact: null, groupMemberships: [], overrides: null } satisfies V1.CatalogMetadata;
  }

  if (method === "PUT" && catalogMetadataMatch) {
    throw new Error("Catalog metadata editing is not available in the static GitHub Pages demo.");
  }

  const catalogMatch = path.match(/^\/api\/v1\/catalogs\/(.*)$/);

  if (method === "GET" && catalogMatch) {
    const catalogId = decodeURIComponent(catalogMatch[1]);

    if (catalogId === demoCatalogId) {
      return getDemoCatalog();
    }
  }

  throw new Error(
    "This feature requires a running Nexus API backend and is not available in the static GitHub Pages demo.",
  );
}

function getDemoSystem(): V1.SystemResponse {
  return {
    version: "GitHub Pages demo",
    applicationName: "Nexus demo",
    helpLink: "https://github.com/nexus-main/nexus",
    logoutUrl: null,
    decimationAvailable: false,
  };
}

function getDemoUser(): V1.MeResponse {
  return {
    userId: "demo@nexus",
    name: "Star Lord",
    claims: [],
  };
}

function getDemoCatalogInfo(): V1.CatalogInfo {
  return {
    id: demoCatalogId,
    title: "Click me to open the sample catalog!",
    contact: null,
    readme: `# Welcome to Nexus!

This is a demo instance running fully within your browser — no server is involved. That means the available features are limited.

You can:

- open this sample catalog,
- select one or more resources,
- plot random in-browser sample data.

A full Nexus server additionally supports exports, metadata editing, attachments, package management, API clients, and custom data-source plugins.`,
    license: null,
    isReadable: true,
    isWritable: false,
    isVisible: true,
    packageReferenceIds: [],
    pipelineInfo: {
      id: "github-pages-demo",
      types: ["Nexus.FakeSource"],
      infoUrls: [null],
    },
  };
}

function getDemoCatalog(): V1.ResourceCatalog {
  return {
    id: demoCatalogId,
    properties: null,
    resources: [
      {
        id: "temperature",
        properties: {
          unit: "°C",
          groups: ["Environment"],
          description: "A description for the temperature resource.",
        },
        representations: [{ dataType: V1.NexusDataType.Float64, samplePeriod: demoSamplePeriod }],
      },
      {
        id: "wind_speed",
        properties: {
          unit: "m/s",
          groups: ["Environment"],
          description: "A description for the wind speed resource.",
        },
        representations: [{ dataType: V1.NexusDataType.Float64, samplePeriod: demoSamplePeriod }],
      },
    ],
  };
}

function getDemoTimeRange(): V1.CatalogTimeRange {
  const end = new Date();

  end.setUTCHours(0, 0, 0, 0);

  const begin = new Date(end);

  begin.setUTCFullYear(begin.getUTCFullYear() - 1);

  return { begin: begin.toISOString(), end: end.toISOString() };
}

function getDemoAvailability(begin: string, end: string, step: string): V1.CatalogAvailability {
  const length = Math.max(
    0,
    Math.min(10000, Math.floor((dateTicks(end) - dateTicks(begin)) / periodTicks(step))),
  );
  const data = Array.from({ length }, (_, index) => 0.5 + deterministicNoise(index, 0) / 2);

  return { data };
}

function getDemoWriter(): WriterDescription {
  return {
    type: "Nexus.Writers.Csv",
    version: "1.0.0",
    description: "Exports comma-separated values following the frictionless data standard",
    projectUrl: "https://github.com/nexus-main/nexus",
    repositoryUrl:
      "https://github.com/nexus-main/nexus/blob/master/src/Nexus/Extensions/Writers/Csv.cs",
    additionalInformation: {
      label: "CSV + Schema (*.csv)",
      options: {
        "row-index-format": {
          type: "select",
          label: "Row index format",
          default: "excel",
          items: {
            excel: "Excel time",
            index: "Index-based",
            unix: "Unix time",
            "iso-8601": "ISO 8601",
          },
        },
        "significant-figures": {
          type: "input-integer",
          label: "Significant figures",
          default: 4,
          minimum: 0,
          maximum: 30,
        },
      },
    },
  };
}

function getDemoCatalogItem(resourcePath: string): V1.CatalogItem {
  const catalog = getDemoCatalog();
  const resourceId = resourcePath.split("/").filter(Boolean).at(-2);
  const resource = catalog.resources?.find((resource) => resource.id === resourceId);
  const representation = resource?.representations?.[0];

  if (!resource || !representation) {
    throw new Error(`The demo catalog does not contain resource path '${resourcePath}'.`);
  }

  return { catalog, resource, representation, parameters: null };
}

function loadDemoResourcesIntoBuffers(
  begin: string,
  end: string,
  resourcePaths: string[],
  precision: V2.Precision,
  bufferProvider: BufferProvider,
  onProgress?: ((progress: number) => void) | undefined,
  signal?: AbortSignal,
) {
  const length = Math.max(
    0,
    Math.floor((dateTicks(end) - dateTicks(begin)) / periodTicks(demoSamplePeriod)),
  );
  const result: Record<string, ResourceInfo> = {};
  const isFloat64 = precision === V2.Precision.Float64;

  resourcePaths.forEach((resourcePath, resourceIndex) => {
    signal?.throwIfAborted();
    const target = bufferProvider(resourcePath, length, length);

    if (target.length < length) {
      throw new Error(`The buffer provided for resource path '${resourcePath}' is too small.`);
    }

    if (
      (isFloat64 && !(target instanceof Float64Array)) ||
      (!isFloat64 && !(target instanceof Float32Array))
    ) {
      throw new Error(
        `The buffer provided for resource path '${resourcePath}' has the wrong precision.`,
      );
    }

    for (let index = 0; index < length; index++) {
      signal?.throwIfAborted();
      target[index] = demoValue(resourcePath, index);
    }

    result[resourcePath] = createDemoResourceInfo(resourcePath);
    onProgress?.((resourceIndex + 1) / Math.max(1, resourcePaths.length));
  });

  onProgress?.(1);

  return result;
}

function createDemoResourceInfo(resourcePath: string): ResourceInfo {
  const catalogItem = getDemoCatalogItem(resourcePath);
  const resource = catalogItem.resource!;

  return {
    catalogItem,
    name: resource.id ?? "",
    unit:
      typeof resource.properties?.["unit"] === "string" ? resource.properties["unit"] : undefined,
    description:
      typeof resource.properties?.["description"] === "string"
        ? resource.properties["description"]
        : undefined,
    samplePeriod: catalogItem.representation?.samplePeriod ?? demoSamplePeriod,
  };
}

function demoValue(resourcePath: string, index: number) {
  const noise = deterministicNoise(index, resourcePath.includes("temperature") ? 17 : 41);

  return resourcePath.includes("temperature") ? 7 + noise * 0.3 : 12 + noise * 3;
}

function deterministicNoise(index: number, seed: number) {
  const value = Math.sin((index + 1) * 12.9898 + seed * 78.233) * 43758.5453;

  return value - Math.floor(value);
}

function dateTicks(value: string) {
  return Date.parse(value) * 10000;
}

function periodTicks(value: string) {
  const timeSpanMatch = value.match(/^(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?$/);

  if (timeSpanMatch) {
    const [, hours, minutes, seconds, fraction = ""] = timeSpanMatch;

    return (
      Number(hours) * 60 * 60 * 10_000_000 +
      Number(minutes) * 60 * 10_000_000 +
      Number(seconds) * 10_000_000 +
      Number(fraction.padEnd(7, "0"))
    );
  }

  const isoMatch = value.match(/^PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+(?:\.\d+)?)S)?$/);

  if (isoMatch) {
    const [, hours = "0", minutes = "0", seconds = "0"] = isoMatch;

    return Math.round(
      (Number(hours) * 60 * 60 + Number(minutes) * 60 + Number(seconds)) * 10_000_000,
    );
  }

  throw new Error(`Unsupported demo period '${value}'.`);
}

function getInitialDevAuthMode(): DevAuthMode {
  if (!isDevMode()) {
    return "admin";
  }

  return localStorage.getItem(devAuthModeStorageKey) === "user" ? "user" : "admin";
}

export { V1, V2 };

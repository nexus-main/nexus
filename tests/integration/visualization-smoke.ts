import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { once } from "node:events";
import { access, appendFile, mkdir, mkdtemp, readdir, realpath, stat } from "node:fs/promises";
import { createRequire } from "node:module";
import { join, resolve } from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";
import {
  decodeVisualization,
  type VisualizationFrame,
  type VisualizationRequest,
} from "../../src/Nexus.UI/src/app/charts/remote-visualization.ts";
import { decodeCursor } from "../../src/Nexus.UI/src/app/charts/exact-cursor.ts";

// Opt-in integration test: never accepts a URL pointing at an existing server.
async function main() {
  const repo = fileURLToPath(new URL("../../", import.meta.url));
  const parent = await realpath(process.env["NEXUS_SMOKE_PARENT"] ?? resolve(repo, "artifacts"));
  const artifacts = await realpath(resolve(repo, "artifacts"));
  assert.ok(
    parent === artifacts ||
      parent.startsWith(`${artifacts}/`) ||
      parent === "/tmp/opencode" ||
      parent.startsWith("/tmp/opencode/"),
  );
  const dll = resolve(repo, "artifacts/bin/Nexus/debug/Nexus.dll");
  await access(dll);
  const root = await mkdtemp(join(parent, "nexus-visualization-"));
  console.log(`Artifacts: ${root}`);
  const home = join(root, "home");
  await mkdir(home);
  const env: NodeJS.ProcessEnv = {
    PATH: process.env["PATH"],
    DOTNET_ROOT: process.env["DOTNET_ROOT"],
    HOME: home,
    TMPDIR: root,
    ASPNETCORE_ENVIRONMENT: "Development",
    ASPNETCORE_URLS: "http://127.0.0.1:0",
    NEXUS_PATHS__SETTINGS: join(root, "settings.json"),
    NEXUS_Data__Visualization__TargetReadBytes: "65536",
    NEXUS_Serilog__MinimumLevel__Override__Microsoft: "Information",
  };
  for (const name of ["Config", "Cache", "Catalogs", "Artifacts", "Packages"]) {
    env[`NEXUS_Paths__${name}`] = join(root, name.toLowerCase());
  }
  const host = spawn("dotnet", [dll], {
    cwd: resolve(repo, "artifacts/bin/Nexus/debug"),
    env,
    stdio: ["ignore", "pipe", "pipe"],
  });
  let log = "";
  let spawnError: Error | undefined;
  host.on("error", (error) => {
    spawnError = error;
  });
  for (const stream of [host.stdout, host.stderr]) {
    stream.on("data", (chunk) => {
      log += chunk.toString();
    });
  }
  const stop = () => {
    host.kill("SIGTERM");
  };
  process.once("SIGINT", stop);
  process.once("SIGTERM", stop);
  const watchdog = setTimeout(stop, 120_000);
  try {
    let url: string | undefined;
    for (let i = 0; i < 300; i++) {
      if (spawnError) {
        throw spawnError;
      }
      assert.ok(
        host.exitCode === null && host.signalCode === null,
        `Host exited before startup:\n${log}`,
      );
      url = log.match(/Now listening on: "?(http:\/\/127\.0\.0\.1:\d+)/)?.[1];
      if (url) {
        break;
      }
      await delay(100);
    }
    assert.ok(url, `Host did not become ready:\n${log}`);
    console.log(`Owned host: PID ${host.pid}, ${url}`);
    const begin = "2020-01-01T00:00:00.000Z";
    const end = "2020-01-08T00:00:00.000Z";
    const sampleCount = 604800n;
    const resourcePaths = ["/SAMPLE/LOCAL/T1/1_s", "/SAMPLE/LOCAL/unix_time2/1_s"];
    const request: VisualizationRequest = {
      begin,
      end,
      resourcePaths,
      views: [{ id: "overview", begin, end, maxPoints: 512 }],
    };
    const post = (
      path: string,
      body: unknown,
      headers: Record<string, string> = {},
      signal?: AbortSignal,
    ) =>
      fetch(`${url}${path}`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/vnd.apache.arrow.stream",
          ...headers,
        },
        body: JSON.stringify(body),
        signal: signal ?? AbortSignal.timeout(30_000),
      });
    async function visualization(label: string, body: VisualizationRequest) {
      const started = performance.now();
      const response = await post("/api/v2/data/visualization", body);
      assert.equal(
        response.status,
        200,
        await (response.ok ? Promise.resolve("") : response.text()),
      );
      assert.match(
        response.headers.get("content-type") ?? "",
        /application\/vnd.apache.arrow.stream/,
      );
      assert.equal(response.headers.get("cache-control"), "no-store");
      const frames: VisualizationFrame[] = [];
      await decodeVisualization(response, body, sampleCount, AbortSignal.timeout(30_000), (frame) =>
        frames.push(frame),
      );
      const progress = frames.filter((frame) => frame.kind === "progress");
      const data = frames.filter(
        (frame): frame is Extract<VisualizationFrame, { kind: "data" }> =>
          frame.kind === "data" && frame.complete,
      );
      assert.ok(progress.some((frame) => frame.progress === 0));
      assert.equal(data.length, body.resourcePaths.length * body.views.length);
      for (let resource = 0; resource < body.resourcePaths.length; resource++) {
        assert.ok(
          progress.some(
            (frame) =>
              frame.resourceIndex === resource && frame.progress === 1 && frame.range?.hasValue,
          ),
        );
      }
      for (const frame of data) {
        assert.ok(frame.values.length > 0);
        assert.ok(frame.values.every(Number.isFinite));
        if (frame.resourceIndex === 1) {
          frame.indices.forEach((index, i) =>
            assert.equal(frame.values[i], Math.fround(Date.parse(begin) / 1000 + Number(index))),
          );
        }
      }
      console.log(
        `${label}: HTTP 200, ${Math.round(performance.now() - started)} ms, ${progress.length} progress frames, completed points [${data.map((frame) => frame.values.length)}]`,
      );
      return data;
    }
    const cacheDirectory = join(root, "cache", "visualization-v1");
    await visualization("cold full domain (604800 samples x 2)", request);
    const cacheFiles = await readdir(cacheDirectory);
    assert.equal(cacheFiles.length, 1, "Cold request must publish one summary dataset");
    const coldCache = await stat(join(cacheDirectory, cacheFiles[0]));
    const at = (index: number) => new Date(Date.parse(begin) + index * 1000).toISOString();
    const zoom = {
      ...request,
      views: [
        ...request.views,
        { id: "zoom", begin: at(120000), end: at(140000), maxPoints: 512 },
        { id: "cursor-neighborhood", begin: at(123456), end: at(123488), maxPoints: 128 },
      ],
    };
    const zoomData = await visualization("warm overview + zoom + 32-point detail", zoom);
    assert.deepEqual(await readdir(cacheDirectory), cacheFiles);
    const warmCache = await stat(join(cacheDirectory, cacheFiles[0]));
    assert.equal(
      warmCache.mtimeMs,
      coldCache.mtimeMs,
      "Warm zoom must not republish the domain dataset",
    );
    console.log(
      `Cache: same summary file, unchanged mtime, ${warmCache.size} bytes (not a read-count assertion)`,
    );
    const raw = await post("/api/v2/data", {
      begin: at(123456),
      end: at(123488),
      resourcePaths,
      precision: "Float32",
    });
    assert.equal(raw.status, 200);
    const cursor = await decodeCursor(raw, 2, 32, AbortSignal.timeout(30_000));
    for (let resource = 0; resource < 2; resource++) {
      const detail = zoomData.find(
        (frame) => frame.resourceIndex === resource && frame.viewIndex === 2,
      )!;
      assert.equal(detail.indices.length, 32);
      assert.deepEqual(detail.values, cursor[resource]);
      assert.deepEqual(
        [...detail.indices],
        Array.from({ length: 32 }, (_, i) => BigInt(123456 + i)),
      );
    }
    console.log(
      "Exact cursor: HTTP 200, real frontend decoder, 32 raw samples x 2 exactly equal detail points",
    );
    const cases: [string, unknown, Record<string, string>, number][] = [
      [
        "invalid point budget",
        { ...request, views: [{ ...request.views[0], maxPoints: 4 }] },
        {},
        422,
      ],
      ["missing resource", { ...request, resourcePaths: ["/SAMPLE/LOCAL/missing/1_s"] }, {}, 404],
      ["invalid request configuration", request, { "Nexus-Configuration": "not-base64!" }, 422],
      [
        "non-admin denied synthetic catalog",
        { ...request, resourcePaths: ["/DEV/LICENSED/T1/1_s"] },
        { "X-Nexus-Dev-Role": "user" },
        403,
      ],
    ];
    for (const [label, body, headers, status] of cases) {
      const response = await post("/api/v2/data/visualization", body, headers);
      const text = await response.text();
      assert.equal(response.status, status, `${label}: ${text}`);
      console.log(`${label}: HTTP ${status}`);
    }
    console.log(
      "Unauthenticated/invalid bearer 401: not asserted because Development automatically supplies a valid header identity",
    );
    const abort = new AbortController();
    const cancelRequest = { ...request, begin: "2019-01-01T00:00:00.000Z" };
    const cancelResponse = await post(
      "/api/v2/data/visualization",
      cancelRequest,
      {},
      abort.signal,
    );
    assert.equal(cancelResponse.status, 200);
    let sawInitialProgress = false;
    await assert.rejects(
      decodeVisualization(cancelResponse, cancelRequest, 32140800n, abort.signal, (frame) => {
        if (frame.kind === "progress") {
          sawInitialProgress = true;
          abort.abort();
        }
      }),
      (error: Error) => error.name === "AbortError",
    );
    assert.ok(sawInitialProgress);
    await visualization("post-cancellation recovery", request);
    console.log(
      "Cancellation: aborted actual response after first progress, parser rejected AbortError, host recovered",
    );
    const require = createRequire(join(repo, "src/Nexus.UI/package.json"));
    for (const dependency of ["tsx", "playwright", "@playwright/test"]) {
      try {
        console.log(`Tool ${dependency}: ${require.resolve(dependency)}`);
      } catch {
        console.log(`Tool ${dependency}: unavailable from UI dependencies`);
      }
    }
    console.log("PASS: live transport smoke (browser rendering not exercised)");
  } finally {
    clearTimeout(watchdog);
    process.removeListener("SIGINT", stop);
    process.removeListener("SIGTERM", stop);
    if (host.exitCode === null && host.signalCode === null && !spawnError) {
      const exited = once(host, "exit");
      host.kill("SIGTERM");
      const kill = setTimeout(() => {
        host.kill("SIGKILL");
      }, 5000);
      try {
        await exited;
      } finally {
        clearTimeout(kill);
      }
    }
    await appendFile(join(root, "host.log"), log);
    console.log(
      `Owned host stopped: exit=${host.exitCode}, signal=${host.signalCode}; log: ${join(root, "host.log")}`,
    );
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});

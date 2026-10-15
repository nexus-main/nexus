import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { access, appendFile, cp, mkdir, mkdtemp, realpath, writeFile } from "node:fs/promises";
import { basename, join, resolve } from "node:path";
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
  const build = resolve(repo, "artifacts/bin/Nexus/debug");
  await access(join(build, "Nexus.dll"));
  const root = await mkdtemp(join(parent, "nexus-visualization-"));
  console.log(`Artifacts: ${root}`);
  const home = join(root, "home");
  await mkdir(home);
  const app = join(root, "app");
  await cp(build, app, {
    recursive: true,
    filter: (source) => !basename(source).startsWith("appsettings."),
  });
  await writeFile(
    join(app, "appsettings.json"),
    JSON.stringify({
      AllowedHosts: "*",
      General: { ApplicationName: "Visualization smoke" },
      Security: { AdministratorGroup: "admins" },
      Serilog: {
        MinimumLevel: { Default: "Information" },
        WriteTo: [{ Name: "Console" }],
      },
    }),
  );
  const env: NodeJS.ProcessEnv = {
    PATH: process.env["PATH"],
    DOTNET_ROOT: process.env["DOTNET_ROOT"],
    HOME: home,
    TMPDIR: root,
    GIT_CONFIG_NOSYSTEM: "1",
    GIT_CONFIG_GLOBAL: "/dev/null",
    ASPNETCORE_ENVIRONMENT: "Development",
    ASPNETCORE_URLS: "http://127.0.0.1:0",
    NEXUS_PATHS__SETTINGS: join(root, "settings.json"),
    NEXUS_Serilog__MinimumLevel__Override__Microsoft: "Information",
    NEXUS_Data__TotalBufferMemoryConsumption: String(8 * 1024 * 1024),
    NEXUS_Data__Visualization__MaxConcurrentReads: "1",
  };
  for (const name of ["Config", "Cache", "Catalogs", "Artifacts", "Packages"]) {
    env[`NEXUS_Paths__${name}`] = join(root, name.toLowerCase());
  }
  const host = spawn("dotnet", [join(app, "Nexus.dll")], {
    cwd: app,
    env,
    stdio: ["ignore", "pipe", "pipe"],
  });
  let log = "";
  let spawnError: Error | undefined;
  const run = new AbortController();
  let kill: ReturnType<typeof setTimeout> | undefined;
  const exited = new Promise<void>((resolve) => host.once("close", () => resolve()));
  host.on("error", (error) => {
    spawnError = error;
    run.abort(error);
  });
  for (const stream of [host.stdout, host.stderr]) {
    stream.on("data", (chunk) => {
      log += chunk.toString();
    });
  }
  const stop = () => {
    run.abort(new Error("Smoke stopped or exceeded its two-minute deadline"));
    if (host.exitCode === null && host.signalCode === null && !spawnError && !kill) {
      host.kill("SIGTERM");
      kill = setTimeout(() => host.kill("SIGKILL"), 5000);
    }
  };
  process.once("SIGINT", stop);
  process.once("SIGTERM", stop);
  const watchdog = setTimeout(stop, 120_000);
  try {
    let url: string | undefined;
    for (let i = 0; i < 300; i++) {
      run.signal.throwIfAborted();
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
      await delay(100, undefined, { signal: run.signal });
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
        signal: AbortSignal.any([
          run.signal,
          AbortSignal.timeout(30_000),
          ...(signal ? [signal] : []),
        ]),
      });
    async function visualization(
      label: string,
      body: VisualizationRequest,
      cacheOutcome: "miss" | "hit",
    ) {
      const started = performance.now();
      const trace = randomUUID();
      const response = await post("/api/v2/data/visualization", body, {
        "X-Nexus-Visualization-Trace": trace,
      });
      assert.equal(response.headers.get("X-Nexus-Visualization-Trace"), trace);
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
      await decodeVisualization(
        response,
        body,
        sampleCount,
        AbortSignal.any([run.signal, AbortSignal.timeout(30_000)]),
        (frame) => frames.push(frame),
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
        const range = progress.find(
          (item) => item.resourceIndex === frame.resourceIndex && item.progress === 1,
        )!.range!;
        assert.ok(frame.values.every((value) => value >= range.minimum && value <= range.maximum));
        if (frame.resourceIndex === 1) {
          assert.equal(range.minimum, Math.fround(Date.parse(begin) / 1000));
          assert.equal(range.maximum, Math.fround(Date.parse(end) / 1000 - 1));
        }
        if (frame.viewIndex === 0) {
          assert.equal(Math.min(...frame.values), range.minimum);
          assert.equal(Math.max(...frame.values), range.maximum);
        }
      }
      // Console delivery can lag HTTP completion; wait for this request's final timing.
      let entries: string[] = [];
      for (let i = 0; i < 100; i++) {
        entries = log
          .replaceAll('"', "")
          .split("\n")
          .filter((line) => line.includes(`request=${trace} `));
        if (entries.some((line) => line.includes("phase=request milestone=end"))) {
          break;
        }
        await delay(50, undefined, { signal: run.signal });
      }
      assert.ok(
        entries.some(
          (line) =>
            line.includes("phase=request milestone=end") && line.includes("outcome=success"),
        ),
      );
      assert.ok(
        entries.some(
          (line) =>
            line.includes("phase=cache-get milestone=end") &&
            line.includes(`outcome=${cacheOutcome}`),
        ),
      );
      if (cacheOutcome === "miss") {
        const reads = entries
          .filter((line) => line.includes("phase=controller-read-inclusive milestone=aggregate"))
          .reduce((sum, line) => sum + Number(line.match(/count=(\d+)/)?.[1] ?? 0), 0);
        assert.ok(reads > 1, "Cold scan must use multiple source-read slices");
      }
      console.log(
        `${label}: HTTP 200, ${Math.round(performance.now() - started)} ms, ${progress.length} progress frames, completed points [${data.map((frame) => frame.values.length)}]`,
      );
      return data;
    }
    const coldData = await visualization("cold full domain (604800 samples x 2)", request, "miss");
    const at = (index: number) => new Date(Date.parse(begin) + index * 1000).toISOString();
    const zoom = {
      ...request,
      views: [
        ...request.views,
        { id: "zoom", begin: at(120000), end: at(140000), maxPoints: 512 },
        { id: "cursor-neighborhood", begin: at(123456), end: at(123488), maxPoints: 128 },
      ],
    };
    const zoomData = await visualization("warm overview + zoom + 32-point detail", zoom, "hit");
    assert.deepEqual(
      zoomData.filter((frame) => frame.viewIndex === 0),
      coldData,
    );
    const raw = await post("/api/v2/data", {
      begin: at(123456),
      end: at(123488),
      resourcePaths,
      precision: "Float32",
    });
    assert.equal(raw.status, 200);
    const cursor = await decodeCursor(
      raw,
      2,
      32,
      AbortSignal.any([run.signal, AbortSignal.timeout(30_000)]),
    );
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
      decodeVisualization(
        cancelResponse,
        cancelRequest,
        32140800n,
        AbortSignal.any([abort.signal, run.signal, AbortSignal.timeout(30_000)]),
        (frame) => {
          if (frame.kind === "progress") {
            sawInitialProgress = true;
            abort.abort();
          }
        },
      ),
      (error: Error) => error.name === "AbortError",
    );
    assert.ok(sawInitialProgress);
    await visualization("post-cancellation recovery", request, "hit");
    console.log(
      "Cancellation: aborted actual response after first progress, parser rejected AbortError, host recovered",
    );
    run.signal.throwIfAborted();
    console.log("PASS: live transport smoke (browser rendering not exercised)");
  } finally {
    clearTimeout(watchdog);
    process.removeListener("SIGINT", stop);
    process.removeListener("SIGTERM", stop);
    stop();
    try {
      await Promise.race([
        exited,
        delay(10_000, undefined, { ref: false }).then(() => {
          throw new Error("Owned host did not close after SIGKILL");
        }),
      ]);
    } finally {
      clearTimeout(kill);
      await appendFile(join(root, "host.log"), log);
    }
    console.log(
      `Owned host stopped: exit=${host.exitCode}, signal=${host.signalCode}; log: ${join(root, "host.log")}`,
    );
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});

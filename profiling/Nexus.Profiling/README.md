# Nexus Profiling

In-process CPU profiling harness for the V2 data endpoint (`DataService.ReadBatchAsStreamAsync`).

## Prerequisites

Install `dotnet-trace`:

```sh
dotnet tool install --global dotnet-trace
```

## Usage

### Quick timing (no profiler overhead)

```sh
dotnet run --project profiling/Nexus.Profiling/Nexus.Profiling.csproj -c Release
```

Prints wall-clock timings for three data sizes (1 day, 1 week, 1 month) across 2 Sample resources at Float32 precision.

### CPU profiling (method-level flame graph)

The `--profile` flag runs 200 iterations of the Large (1 month) workload with a `Stream.Null` sink, giving the sampler enough data-processing samples to produce a meaningful flame graph.

Build first, then trace just the app (not the build):

```sh
dotnet build profiling/Nexus.Profiling/Nexus.Profiling.csproj -c Release
dotnet-trace collect --format speedscope -o artifacts/nexus -- dotnet artifacts/bin/Nexus.Profiling/release/Nexus.Profiling.dll --profile
```

Open the generated `artifacts/nexus.speedscope.json` at https://speedscope.app for an interactive flame graph showing where time is spent in the server pipeline. The trace stops automatically when the app finishes.

For higher sampling resolution, add `--rate 1000`:

```sh
dotnet-trace collect --format speedscope --rate 1000 -o artifacts/nexus -- dotnet artifacts/bin/Nexus.Profiling/release/Nexus.Profiling.dll --profile
```

## What it measures

The harness exercises the real server data pipeline in-process — no HTTP round-trip, no auth, no external setup:

- Real `DataService`, `DataControllerService`, `CatalogManager`, `ProcessingService`, `CacheService`, `DatabaseService`
- Real `Sample` data source (auto-registered by `CatalogManager`)
- Mocked: `IExtensionHive<IDataSource>` (returns `typeof(Sample)`), `IHttpContextAccessor` (null context), `IMemoryTracker` (always grants), `IDBService` (for user pipeline enumeration)

Resources read: `/SAMPLE/LOCAL/T1/1_s` and `/SAMPLE/LOCAL/V1/1_s` (both FLOAT32, 1 s sample period).

## DO NOT use the VS Code launcher

The launch config in `.vscode/launch.json` does **not** profile — it only runs the timing harness. You must use `dotnet-trace collect` from the command line to capture a CPU trace.

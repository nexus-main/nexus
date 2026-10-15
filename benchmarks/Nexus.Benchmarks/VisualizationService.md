# Synthetic Visualization Service

Run from the repository root:

```sh
dotnet run -c Release --project benchmarks/Nexus.Benchmarks -- --filter '*VisualizationServiceBenchmarks*' --artifacts artifacts/visualization-service-benchmarks
```

This replaces the environment-gated xUnit throughput method. It is an opt-in
BenchmarkDotNet workload, not a correctness test automatically run by `dotnet test`.
The existing assembly-wide runner discovers it, with one warmup and three measured
iterations. Iteration setup forces one invocation per iteration and creates a fresh
cache, so measured invocations scan cold datasets rather than accidental cache hits.

## Workload And Validation

Each case scans 4,194,304 timestamps for two Float32 resources (8,388,608 scalar
samples), projecting a 1,000-point-budget view per resource. The 24 cases vary source
delay (0 or 2 ms per batched read), read-worker limit (1/2/4/8), and compute permits
(1/2/4). A separate 65,536-timestamp warmup also validates and disposes its output.

Measured latency includes request preparation, source initialization, strict
`DataSourceController` reads, normalization, summary construction, projection, Arrow
serialization, Arrow decoding and workload validation. Fixture construction and
cache disposal are outside the measurement. Source plugins, catalog/controller
lookup, license/cache services and the memory tracker are mocked. The tracker grants
up to 1 MiB per allocation, not a shared 1 MiB global limit; this does not benchmark
production memory contention. No HTTP, deployed service, source files or storage I/O
are used. `MemoryDiagnoser` reports managed allocations, not peak RSS or all native
Arrow allocations.

Every invocation rejects error records, missing terminal completion, incomplete or
duplicate resource/view results, empty/over-budget points and incorrect domain
extrema. It requires exactly the expected source-sample count, at least one created
source, every source disposed and no active reads. The plugin detects overlapping
reads on the same instance. A 30-second cancellation deadline bounds normal work.
These guards prevent a skipped or failed scan from masquerading as faster throughput;
the service unit tests remain responsible for exhaustive numerical/lifetime behavior.

Compute permits are not independent compute threads: base reduction runs on the read
tasks, with parallelism at most `min(read workers, compute permits)`. Parent building
and projection remain serial within one request. The delay case tests scheduling
under synthetic latency, not an optimal production worker setting. Three iterations
are exploratory; collect longer runs before making performance claims. Divide
8,388,608 by elapsed seconds for scalar samples/s; the return value is output bytes,
not an independently measured throughput counter.

## Execution Check

On October 15, 2026, the revised workload ran successfully from the worktree based
on `50a33b6`, on .NET 9.0.17/Linux x64, with all success/sample/disposal guards enabled.
The command below produced 48 successful cases: the existing runner's 24 normal
three-iteration cases **plus** 24 one-iteration in-process cases. The CLI adds a job
instead of replacing the runner's configured job, so this is not a minimal shortcut.

```sh
dotnet artifacts/bin/Nexus.Benchmarks/release/Nexus.Benchmarks.dll --filter '*VisualizationServiceBenchmarks*' --inProcess --warmupCount 1 --iterationCount 1 --artifacts artifacts/visualization-service-review
```

Reports are in `artifacts/visualization-service-review/results/`. This run verifies
execution, not stable performance: concurrent local activity, short iterations,
different toolchains and limited warmup make its timings unsuitable for speedup
claims. Use the standard command above for comparable out-of-process runs.

## Historical Measurements

The following October 7, 2026 results were originally recorded beside the xUnit
benchmark. They used Release .NET 9.0.17, Linux/KVM, AMD EPYC 7763, 96 exposed CPUs,
and AVX2. The measured commit was not recorded. The old 1 MiB read-budget policy
yielded 607 batched reads; current accounting and validation differ. These are
medians of three cold runs after a small warmup, not results of the current BDN
workload and not directly comparable to its means. Disk persistence was excluded.

| Read workers | Compute permits | No-delay ms | No-delay Msamples/s | 2 ms delay ms | Delay Msamples/s |
| ------------ | --------------- | ----------- | ------------------- | ------------- | ---------------- |
| 1            | 1               | 122.7       | 68.36               | 2429.9        | 3.45             |
| 1            | 2               | 113.4       | 73.99               | 2429.8        | 3.45             |
| 1            | 4               | 117.6       | 71.36               | 2430.1        | 3.45             |
| 2            | 1               | 69.3        | 121.12              | 1218.2        | 6.89             |
| 2            | 2               | 68.5        | 122.38              | 1218.5        | 6.88             |
| 2            | 4               | 67.1        | 125.02              | 1219.6        | 6.88             |
| 4            | 1               | 45.3        | 185.23              | 614.2         | 13.66            |
| 4            | 2               | 44.4        | 188.94              | 613.9         | 13.66            |
| 4            | 4               | 45.4        | 184.61              | 610.3         | 13.75            |
| 8            | 1               | 36.7        | 228.88              | 310.0         | 27.06            |
| 8            | 2               | 37.3        | 225.13              | 315.1         | 26.62            |
| 8            | 4               | 34.8        | 240.98              | 309.1         | 27.14            |

Earlier instrumented delayed-source runs observed overlapping independent source
instances, but no same-instance overlap. These observations establish neither NVMe
throughput nor physical-GPU performance. Current cache, cancellation, dependency and
memory limitations live in the [visualization overview](../../notes/visualization.md).

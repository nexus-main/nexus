# Visualization Reduction Kernel

Run from the repository root:

```sh
dotnet run -c Release --project benchmarks/Nexus.Benchmarks -- --filter '*VisualizationReductionBenchmarks*' --artifacts artifacts/visualization-benchmarks
```

Uses the existing BenchmarkDotNet dependency and runner. Setup requires AVX2 so
the SIMD comparison cannot silently benchmark the scalar fallback. The kernel
itself supports machines without AVX2.

## Algorithm

`Summarize` uses AVX2 integer magnitude comparisons to classify eight floats at
once, including NaN and both infinities. Gap-run starts are counted from mask
transitions, including transitions between vectors. Vector min/max accumulators
exclude nonfinite lanes. A second vector equality pass locates the earliest
extrema, stopping once both are found. Reading those source samples recovers
original signed-zero bits. This trades a bounded second pass over cache-resident
input for avoiding per-lane index tracking in the main loop. Only the final zero
to seven samples are reduced scalarly; disabling SIMD uses the scalar reference.

`BaseBuckets` reduces each 256-sample bucket and folds its summary into a parent.
It includes summary merge overhead but not projection allocations, pyramid page
storage, parallel scheduling, normalization, or source I/O.

## Measurements

2026-10-07, Linux Debian 13, AMD EPYC 7763 (virtualized environment reporting
96 logical CPUs), .NET 9.0.17, x64 AVX2, BenchmarkDotNet 0.15.8. Existing runner
settings: one warmup, three measured iterations. All 24 cases completed; no
managed allocations measured. Process priority elevation was unavailable.

Selected means (microseconds per operation):

| Operation | Samples | Pattern | Scalar | SIMD | Speedup |
| --- | ---: | --- | ---: | ---: | ---: |
| Summarize | 256 | Finite | 0.383 | 0.088 | 4.34x |
| Summarize | 131072 | Finite | 172.08 | 33.10 | 5.20x |
| Summarize | 131072 | Gaps | 183.15 | 25.36 | 7.22x |
| Summarize | 131072 | Invalid | 150.52 | 44.54 | 3.38x |
| BaseBuckets | 131072 | Finite | 221.76 | 71.34 | 3.11x |
| BaseBuckets | 131072 | Gaps | 230.79 | 75.21 | 3.07x |
| BaseBuckets | 131072 | Invalid | 149.77 | 55.38 | 2.70x |

Raw reports are generated under `artifacts/visualization-benchmarks/results/`.
These are warm, single-threaded, synthetic kernel measurements with only three
iterations, not cold-memory or source-read throughput claims. Data-dependent
extrema positions affect the length of the second pass. Real source throughput,
parallel scaling, projection, and architecture-specific results outside AVX2
remain unmeasured.

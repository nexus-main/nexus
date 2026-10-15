# Buffer Utilities Status Masking

Run from the repository root:

```sh
dotnet run -c Release --project benchmarks/Nexus.Benchmarks -- --filter '*BufferUtilitiesBenchmarks*' --artifacts artifacts/buffer-utilities-benchmarks
```

This benchmark compares the scalar and vectorized paths for applying representation
status to numeric data while producing Float32 output. It uses 131,072 source
values and an alternating valid/invalid status mask across all 10 supported numeric
storage types.

## Measurement Notes

Rerun the command above on the target machine before making performance claims. The
table below records one machine-specific run for reference; it is not a guarantee
of current throughput on other hardware or runtime versions.

- Machine: 11th Gen Intel Core i7-1165G7 2.80GHz, 1 CPU, 8 logical / 4 physical cores
- OS: Linux EndeavourOS
- Runtime: .NET 9.0.18, X64 RyuJIT x86-64-v4
- Tool: BenchmarkDotNet v0.15.8
- Iteration setup: 1 warmup / 3 iterations

| Type | Scalar | Vectorized | Speedup |
| --- | ---: | ---: | ---: |
| Byte | 304.19 us | 16.18 us | 18.8x |
| SByte | 290.62 us | 18.61 us | 15.6x |
| UInt16 | 280.78 us | 34.04 us | 8.2x |
| Int16 | 266.89 us | 29.39 us | 9.1x |
| UInt32 | 270.00 us | 24.30 us | 11.1x |
| Int32 | 224.35 us | 16.33 us | 13.7x |
| UInt64 | 269.99 us | 31.99 us | 8.4x |
| Int64 | 254.77 us | 34.88 us | 7.3x |
| Single | 246.24 us | 15.63 us | 15.8x |
| Double | 211.30 us | 30.64 us | 6.9x |

All 10 NexusDataType types vectorized. The AVX-512DQ path was active for UInt64 and Int64.

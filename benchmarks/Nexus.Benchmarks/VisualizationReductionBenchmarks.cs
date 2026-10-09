// MIT License
// Copyright (c) [2024] [nexus-main]

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Nexus.Utilities;
using System.Runtime.Intrinsics.X86;

namespace Nexus.Benchmarks;

// Run: dotnet run -c Release --project benchmarks/Nexus.Benchmarks -- --filter '*VisualizationReductionBenchmarks*'
// Summarize measures raw throughput; BaseBuckets also includes the fixed 256-sample
// boundary and merging overhead used by pyramid construction. No source I/O is timed.
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class VisualizationReductionBenchmarks
{
    private float[] _values = null!;

    [Params(256, 131_072)]
    public int Count { get; set; }

    [Params("Finite", "Gaps", "Invalid")]
    public string Pattern { get; set; } = "Finite";

    [GlobalSetup]
    public void Setup()
    {
        if (!Avx2.IsSupported)
            throw new PlatformNotSupportedException("This comparison requires AVX2; the kernel itself has a scalar fallback.");

        var random = new Random(721);
        _values = new float[Count];

        for (int i = 0; i < Count; i++)
        {
            _values[i] = Pattern == "Invalid" || (Pattern == "Gaps" && i % 257 < 13)
                ? float.NaN
                : random.NextSingle() * 200 - 100;
        }

        if (SummarizeScalar() != SummarizeSimd() || BaseBucketsScalar() != BaseBucketsSimd())
            throw new InvalidOperationException("Scalar/SIMD summaries differ.");
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Summarize")]
    public VisualizationSummary SummarizeScalar() => VisualizationReduction.Summarize(_values, 0, false);

    [Benchmark, BenchmarkCategory("Summarize")]
    public VisualizationSummary SummarizeSimd() => VisualizationReduction.Summarize(_values, 0, true);

    [Benchmark(Baseline = true), BenchmarkCategory("BaseBuckets")]
    public VisualizationSummary BaseBucketsScalar() => BaseBuckets(false);

    [Benchmark, BenchmarkCategory("BaseBuckets")]
    public VisualizationSummary BaseBucketsSimd() => BaseBuckets(true);

    private VisualizationSummary BaseBuckets(bool useSimd)
    {
        var summary = default(VisualizationSummary);

        for (int i = 0; i < _values.Length; i += VisualizationReduction.BaseStride)
        {
            var chunk = _values.AsSpan(i, Math.Min(VisualizationReduction.BaseStride, _values.Length - i));
            summary = VisualizationReduction.Merge(summary, VisualizationReduction.Summarize(chunk, i, useSimd));
        }

        return summary;
    }
}

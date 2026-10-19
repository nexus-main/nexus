// MIT License
// Copyright (c) [2024] [nexus-main]

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using Nexus.Utilities;
using System.Runtime.Intrinsics.X86;

namespace Nexus.Benchmarks;

// Run: dotnet run -c Release --project benchmarks/Nexus.Benchmarks -- --filter '*VisualizationReductionBenchmarks*'
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class VisualizationReductionBenchmarks
{
    private readonly Consumer _consumer = new();
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

        if (SummarizeScalarCore() != SummarizeSimdCore() || BaseBucketsScalarCore() != BaseBucketsSimdCore())
            throw new InvalidOperationException("Scalar/SIMD summaries differ.");
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Summarize")]
    public void SummarizeScalar() => _consumer.Consume(SummarizeScalarCore());

    [Benchmark, BenchmarkCategory("Summarize")]
    public void SummarizeSimd() => _consumer.Consume(SummarizeSimdCore());

    [Benchmark(Baseline = true), BenchmarkCategory("BaseBuckets")]
    public void BaseBucketsScalar() => _consumer.Consume(BaseBucketsScalarCore());

    [Benchmark, BenchmarkCategory("BaseBuckets")]
    public void BaseBucketsSimd() => _consumer.Consume(BaseBucketsSimdCore());

    private VisualizationSummary SummarizeScalarCore() => VisualizationReduction.Summarize(_values, 0, false);

    private VisualizationSummary SummarizeSimdCore() => VisualizationReduction.Summarize(_values, 0, true);

    private VisualizationSummary BaseBucketsScalarCore() => BaseBuckets(false);

    private VisualizationSummary BaseBucketsSimdCore() => BaseBuckets(true);

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

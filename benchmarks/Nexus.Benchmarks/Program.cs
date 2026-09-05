// MIT License
// Copyright (c) [2024] [nexus-main]
//
// Benchmark results — Float32, 131,072 elements, 1 warmup / 3 iterations:
//
//   Machine: 11th Gen Intel Core i7-1165G7 2.80GHz, 1 CPU, 8 logical / 4 physical cores
//   OS:      Linux EndeavourOS
//   Runtime: .NET 9.0.18, X64 RyuJIT x86-64-v4
//   Tool:    BenchmarkDotNet v0.15.8
//
//   Type     | Scalar       | Vectorized   | Speedup
//   ---------|--------------|-------------|--------
//   Byte     | 304.19 us    | 16.18 us    | 18.8x
//   SByte    | 290.62 us    | 18.61 us    | 15.6x
//   UInt16   | 280.78 us    | 34.04 us    | 8.2x
//   Int16    | 266.89 us    | 29.39 us    | 9.1x
//   UInt32   | 270.00 us    | 24.30 us    | 11.1x
//   Int32    | 224.35 us    | 16.33 us    | 13.7x
//   UInt64   | 269.99 us    | 31.99 us    | 8.4x
//   Int64    | 254.77 us    | 34.88 us    | 7.3x
//   Single   | 246.24 us    | 15.63 us    | 15.8x
//   Double   | 211.30 us    | 30.64 us    | 6.9x
//
//   All 10 NexusDataType types vectorized. AVX-512DQ path active for UInt64/Int64.
//   Zero allocations across all benchmarks (UInt64 Vectorized reported 1 B — likely
//   one-time JIT artifact).

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Nexus.Utilities;
using System.Runtime.InteropServices;

namespace Nexus.Benchmarks;

internal class Program
{
    private static void Main(string[] args)
    {
        var config = DefaultConfig.Instance
            .AddJob(Job.Default
                .WithWarmupCount(1)
                .WithIterationCount(3));

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}

[GenericTypeArguments(typeof(byte))]
[GenericTypeArguments(typeof(sbyte))]
[GenericTypeArguments(typeof(ushort))]
[GenericTypeArguments(typeof(short))]
[GenericTypeArguments(typeof(uint))]
[GenericTypeArguments(typeof(int))]
[GenericTypeArguments(typeof(ulong))]
[GenericTypeArguments(typeof(long))]
[GenericTypeArguments(typeof(float))]
[GenericTypeArguments(typeof(double))]
[MemoryDiagnoser]
public class BufferUtilitiesBenchmarks<T>
    where T : unmanaged
{
    private const int Count = 131_072;

    private T[] _data = null!;
    private byte[] _status = null!;
    private float[] _target = null!;

    [GlobalSetup]
    public void Setup()
    {
        _data = new T[Count];
        _status = new byte[Count];
        _target = new float[Count];

        Random.Shared.NextBytes(MemoryMarshal.AsBytes(_data.AsSpan()));

        for (int i = 0; i < Count; i++)
            _status[i] = (byte)((i & 1) == 0 ? 1 : 0);
    }

    [Benchmark(Baseline = true)]
    public void Scalar() =>
        BufferUtilities.ScalarApplyRepresentationStatusFloat32<T>(_data, _status, _target);

    [Benchmark]
    public void Vectorized() =>
        BufferUtilities.ApplyRepresentationStatusFloat32<T>(_data, _status, _target);
}

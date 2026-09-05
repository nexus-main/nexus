// MIT License
// Copyright (c) [2024] [nexus-main]

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

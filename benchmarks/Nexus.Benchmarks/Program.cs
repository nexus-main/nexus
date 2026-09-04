// MIT License
// Copyright (c) [2024] [nexus-main]

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;
using Nexus.DataModel;
using Nexus.Utilities;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nexus.Benchmarks;

internal class Program
{
    private static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
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
[DisassemblyDiagnoser(maxDepth: 3)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class BufferUtilitiesBenchmarks<T>
    where T : unmanaged
{
    private T[] _data = null!;
    private byte[] _status = null!;
    private float[] _target32 = null!;
    private double[] _target64 = null!;

    [Params(1024, 1_048_576)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new T[Count];
        _status = new byte[Count];
        _target32 = new float[Count];
        _target64 = new double[Count];

        Random.Shared.NextBytes(MemoryMarshal.AsBytes(_data.AsSpan()));

        for (int i = 0; i < Count; i++)
            _status[i] = (byte)((i & 1) == 0 ? 1 : 0);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Float32")]
    public void ScalarFloat32() =>
        BufferUtilities.ScalarApplyRepresentationStatusFloat32<T>(_data, _status, _target32);

    [Benchmark, BenchmarkCategory("Float32")]
    public void VectorizedFloat32() =>
        BufferUtilities.ApplyRepresentationStatusFloat32<T>(_data, _status, _target32);

    [Benchmark(Baseline = true), BenchmarkCategory("Float64")]
    public void ScalarFloat64() =>
        BufferUtilities.ScalarApplyRepresentationStatusFloat64<T>(_data, _status, _target64);

    [Benchmark, BenchmarkCategory("Float64")]
    public void VectorizedFloat64() =>
        BufferUtilities.ApplyRepresentationStatusFloat64<T>(_data, _status, _target64);
}

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ByDataTypeBenchmarks
{
    private const int Count = 1_048_576;

    private byte[] _data = null!;
    private byte[] _status = null!;
    private float[] _target32 = null!;
    private double[] _target64 = null!;

    [Params(
        NexusDataType.UINT8, NexusDataType.INT8,
        NexusDataType.UINT16, NexusDataType.INT16,
        NexusDataType.UINT32, NexusDataType.INT32,
        NexusDataType.UINT64, NexusDataType.INT64,
        NexusDataType.FLOAT32, NexusDataType.FLOAT64)]
    public NexusDataType DataType { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var elementSize = NexusUtilities.SizeOf(DataType);
        _data = new byte[Count * elementSize];
        _status = new byte[Count];
        _target32 = new float[Count];
        _target64 = new double[Count];

        Random.Shared.NextBytes(_data);

        for (int i = 0; i < Count; i++)
            _status[i] = (byte)((i & 1) == 0 ? 1 : 0);
    }

    [Benchmark, BenchmarkCategory("Float32")]
    public void Float32ByDataType() =>
        BufferUtilities.ApplyRepresentationStatusFloat32ByDataType(DataType, _data, _status, _target32);

    [Benchmark, BenchmarkCategory("Float64")]
    public void Float64ByDataType() =>
        BufferUtilities.ApplyRepresentationStatusFloat64ByDataType(DataType, _data, _status, _target64);
}

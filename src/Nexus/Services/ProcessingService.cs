using Microsoft.Extensions.Options;
using MudBlazor;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Utilities;
using System.Buffers;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Nexus.Services;

internal interface IProcessingService
{
    void Resample(
        NexusDataType dataType,
        ReadOnlyMemory<byte> data,
        ReadOnlyMemory<byte> status,
        Memory<double> targetBuffer,
        int blockSize,
        int offset);

    void Aggregate(
        NexusDataType dataType,
        RepresentationKind kind,
        Memory<byte> data,
        ReadOnlyMemory<byte> status,
        Memory<double> targetBuffer,
        int blockSize);
}

internal class ProcessingService(IOptions<DataOptions> dataOptions) 
    : IProcessingService
{
    private readonly double _nanThreshold = dataOptions.Value.AggregationNaNThreshold;

    public void Resample(
        NexusDataType dataType,
        ReadOnlyMemory<byte> data,
        ReadOnlyMemory<byte> status,
        Memory<double> targetBuffer,
        int blockSize,
        int offset)
    {
        using var memoryOwner = MemoryPool<double>.Shared.Rent(status.Length);
        var doubleData = memoryOwner.Memory[..status.Length];

        BufferUtilities.ApplyRepresentationStatusByDataType(
            dataType,
            data,
            status,
            target: doubleData);

        var sourceBufferSpan = doubleData.Span;
        var targetBufferSpan = targetBuffer.Span;
        var length = targetBuffer.Length;

        for (int i = 0; i < length; i++)
        {
            targetBufferSpan[i] = sourceBufferSpan[(i + offset) / blockSize];
        }
    }

    public void Aggregate(
        NexusDataType dataType,
        RepresentationKind kind,
        Memory<byte> data,
        ReadOnlyMemory<byte> status,
        Memory<double> targetBuffer,
        int blockSize)
    {
        var targetType = NexusUtilities.GetTypeFromNexusDataType(dataType);

        // TODO: cache
        var method = typeof(ProcessingService)
            .GetMethod(nameof(GenericProcess), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(targetType);

        method.Invoke(this, new object[] { kind, data, status, targetBuffer, blockSize });
    }

    private void GenericProcess<T>(
        RepresentationKind kind,
        Memory<byte> data,
        ReadOnlyMemory<byte> status,
        Memory<double> targetBuffer,
        int blockSize) where T : unmanaged
    {
        var Tdata = new CastMemoryManager<byte, T>(data).Memory;

        switch (kind)
        {
            case RepresentationKind.Mean:
            case RepresentationKind.MeanPolarDeg:
            case RepresentationKind.Min:
            case RepresentationKind.Max:
            case RepresentationKind.Std:
            case RepresentationKind.Rms:
            case RepresentationKind.Sum:

                using (var memoryOwner = MemoryPool<double>.Shared.Rent(Tdata.Length))
                {
                    var doubleData2 = memoryOwner.Memory[..Tdata.Length];

                    BufferUtilities.ApplyRepresentationStatus<T>(Tdata, status, target: doubleData2);
                    ApplyAggregationFunction(kind, blockSize, doubleData2, targetBuffer);
                }

                break;

            case RepresentationKind.MinBitwise:
            case RepresentationKind.MaxBitwise:

                ApplyAggregationFunction(kind, blockSize, Tdata, status, targetBuffer);

                break;

            default:
                throw new Exception($"The representation kind {kind} is not supported.");
        }
    }

    private void ApplyAggregationFunction(
        RepresentationKind kind,
        int blockSize,
        Memory<double> data,
        Memory<double> targetBuffer)
    {
        switch (kind)
        {
            case RepresentationKind.Mean:

                Parallel.For(0, targetBuffer.Length, x =>
                {
                    var chunkData = GetNaNFreeData(data.Slice(x * blockSize, blockSize)).Span;
                    var isHighQuality = (chunkData.Length / (double)blockSize) >= _nanThreshold;

                    if (isHighQuality)
                        targetBuffer.Span[x] = Mean(chunkData);

                    else
                        targetBuffer.Span[x] = double.NaN;
                });

                break;

            case RepresentationKind.MeanPolarDeg:

                using (var memoryOwner_sin = MemoryPool<double>.Shared.Rent(targetBuffer.Length))
                using (var memoryOwner_cos = MemoryPool<double>.Shared.Rent(targetBuffer.Length))
                {
                    var sinBuffer = memoryOwner_sin.Memory[..targetBuffer.Length];
                    var cosBuffer = memoryOwner_cos.Memory[..targetBuffer.Length];

                    var limit = 360;
                    var factor = 2 * Math.PI / limit;

                    Parallel.For(0, targetBuffer.Length, x =>
                    {
                        var sin = sinBuffer.Span;
                        var cos = cosBuffer.Span;
                        var chunkData = GetNaNFreeData(data.Slice(x * blockSize, blockSize)).Span;
                        var length = chunkData.Length;
                        var isHighQuality = (length / (double)blockSize) >= _nanThreshold;

                        if (isHighQuality)
                        {
                            sin[x] = 0.0;
                            cos[x] = 0.0;

                            for (int i = 0; i < chunkData.Length; i++)
                            {
                                sin[x] += Math.Sin(chunkData[i] * factor);
                                cos[x] += Math.Cos(chunkData[i] * factor);
                            }

                            targetBuffer.Span[x] = Math.Atan2(sin[x], cos[x]) / factor;

                            if (targetBuffer.Span[x] < 0)
                                targetBuffer.Span[x] += limit;
                        }
                        else
                        {
                            targetBuffer.Span[x] = double.NaN;
                        }
                    });
                }

                break;

            case RepresentationKind.Min:

                Parallel.For(0, targetBuffer.Length, x =>
                {
                    var chunkData = GetNaNFreeData(data.Slice(x * blockSize, blockSize)).Span;
                    var isHighQuality = (chunkData.Length / (double)blockSize) >= _nanThreshold;

                    if (isHighQuality)
                        targetBuffer.Span[x] = Minimum(chunkData);

                    else
                        targetBuffer.Span[x] = double.NaN;
                });

                break;

            case RepresentationKind.Max:

                Parallel.For(0, targetBuffer.Length, x =>
                {
                    var chunkData = GetNaNFreeData(data.Slice(x * blockSize, blockSize)).Span;
                    var isHighQuality = (chunkData.Length / (double)blockSize) >= _nanThreshold;

                    if (isHighQuality)
                        targetBuffer.Span[x] = Maximum(chunkData);

                    else
                        targetBuffer.Span[x] = double.NaN;
                });

                break;

            case RepresentationKind.Std:

                Parallel.For(0, targetBuffer.Length, x =>
                {
                    var chunkData = GetNaNFreeData(data.Slice(x * blockSize, blockSize)).Span;
                    var isHighQuality = (chunkData.Length / (double)blockSize) >= _nanThreshold;

                    if (isHighQuality)
                        targetBuffer.Span[x] = StandardDeviation(chunkData);

                    else
                        targetBuffer.Span[x] = double.NaN;
                });

                break;

            case RepresentationKind.Rms:

                Parallel.For(0, targetBuffer.Length, x =>
                {
                    var chunkData = GetNaNFreeData(data.Slice(x * blockSize, blockSize)).Span;
                    var isHighQuality = (chunkData.Length / (double)blockSize) >= _nanThreshold;

                    if (isHighQuality)
                        targetBuffer.Span[x] = RootMeanSquare(chunkData);

                    else
                        targetBuffer.Span[x] = double.NaN;
                });

                break;

            case RepresentationKind.Sum:

                Parallel.For(0, targetBuffer.Length, x =>
                {
                    var chunkData = GetNaNFreeData(data.Slice(x * blockSize, blockSize)).Span;
                    var isHighQuality = (chunkData.Length / (double)blockSize) >= _nanThreshold;

                    if (isHighQuality)
                        targetBuffer.Span[x] = Sum(chunkData);

                    else
                        targetBuffer.Span[x] = double.NaN;
                });

                break;

            default:
                throw new Exception($"The representation kind {kind} is not supported.");

        }
    }

    private void ApplyAggregationFunction<T>(
        RepresentationKind kind,
        int blockSize,
        Memory<T> data,
        ReadOnlyMemory<byte> status,
        Memory<double> targetBuffer) where T : unmanaged
    {
        switch (kind)
        {
            case RepresentationKind.MinBitwise:

                var bitField_and = new T[targetBuffer.Length];

                Parallel.For(0, targetBuffer.Length, x =>
                {
                    var chunkData = GetNaNFreeData(
                        data.Slice(x * blockSize, blockSize),
                        status.Slice(x * blockSize, blockSize)).Span;

                    var targetBufferSpan = targetBuffer.Span;
                    var length = chunkData.Length;
                    var isHighQuality = (length / (double)blockSize) >= _nanThreshold;

                    if (isHighQuality)
                    {
                        for (int i = 0; i < length; i++)
                        {
                            if (i == 0)
                                bitField_and[x] = GenericBitOr<T>.BitOr(bitField_and[x], chunkData[i]);

                            else
                                bitField_and[x] = GenericBitAnd<T>.BitAnd(bitField_and[x], chunkData[i]);
                        }

                        targetBuffer.Span[x] = Convert.ToDouble(bitField_and[x]);
                    }

                    else
                    {
                        targetBuffer.Span[x] = double.NaN;
                    }
                });

                break;

            case RepresentationKind.MaxBitwise:

                var bitField_or = new T[targetBuffer.Length];

                Parallel.For(0, targetBuffer.Length, x =>
                {
                    var chunkData = GetNaNFreeData(data
                        .Slice(x * blockSize, blockSize), status
                        .Slice(x * blockSize, blockSize)).Span;

                    var length = chunkData.Length;
                    var isHighQuality = (length / (double)blockSize) >= _nanThreshold;

                    if (isHighQuality)
                    {
                        for (int i = 0; i < length; i++)
                        {
                            bitField_or[x] = GenericBitOr<T>.BitOr(bitField_or[x], chunkData[i]);
                        }

                        targetBuffer.Span[x] = Convert.ToDouble(bitField_or[x]);
                    }

                    else
                    {
                        targetBuffer.Span[x] = double.NaN;
                    }
                });

                break;

            default:
                throw new Exception($"The representation kind {kind} is not supported.");

        }
    }

    private static Memory<T> GetNaNFreeData<T>(Memory<T> data, ReadOnlyMemory<byte> status) where T : unmanaged
    {
        var targetLength = 0;
        var sourceLength = data.Length;
        var spanData = data.Span;
        var spanStatus = status.Span;

        for (int i = 0; i < sourceLength; i++)
        {
            if (spanStatus[i] == 1)
            {
                spanData[targetLength] = spanData[i];
                targetLength++;
            }
        }

        return data[..targetLength];
    }

    private static Memory<double> GetNaNFreeData(Memory<double> data)
    {
        var targetLength = 0;
        var sourceLength = data.Length;
        var spanData = data.Span;

        for (int i = 0; i < sourceLength; i++)
        {
            var value = spanData[i];

            if (!double.IsNaN(value))
            {
                spanData[targetLength] = value;
                targetLength++;
            }
        }

        return data[..targetLength];
    }

    // TODO: vectorize
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Sum(Span<double> data)
    {
        if (data.Length == 0)
            return double.NaN;

        var sum = 0.0;

        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i];
        }

        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Mean(Span<double> data)
    {
        if (data.Length == 0)
            return double.NaN;

        var mean = 0.0;
        var m = 0UL;

        for (int i = 0; i < data.Length; i++)
        {
            mean += (data[i] - mean) / ++m;
        }

        return mean;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Minimum(Span<double> data)
    {
        if (data.Length == 0)
            return double.NaN;

        var min = double.PositiveInfinity;

        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] < min || double.IsNaN(data[i]))
            {
                min = data[i];
            }
        }

        return min;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Maximum(Span<double> data)
    {
        if (data.Length == 0)
            return double.NaN;

        var max = double.NegativeInfinity;

        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] > max || double.IsNaN(data[i]))
            {
                max = data[i];
            }
        }

        return max;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double StandardDeviation(Span<double> samples)
    {
        return Math.Sqrt(Variance(samples));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Variance(Span<double> samples)
    {
        if (samples.Length <= 1)
            return double.NaN;

        var variance = 0.0;
        var t = samples[0];

        for (int i = 1; i < samples.Length; i++)
        {
            t += samples[i];
            var diff = ((i + 1) * samples[i]) - t;
            variance += diff * diff / ((i + 1.0) * i);
        }

        return variance / (samples.Length - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double RootMeanSquare(Span<double> data)
    {
        if (data.Length == 0)
            return double.NaN;

        var mean = 0.0;
        var m = 0UL;

        for (int i = 0; i < data.Length; i++)
        {
            mean += (data[i] * data[i] - mean) / ++m;
        }

        return Math.Sqrt(mean);
    }
}
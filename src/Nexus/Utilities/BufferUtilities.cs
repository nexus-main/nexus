// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core;
using Nexus.DataModel;
using System.Reflection;

namespace Nexus.Utilities;

internal static class BufferUtilities
{
    public static void ApplyRepresentationStatusFloat32ByDataType(NexusDataType dataType, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> status, Memory<float> target)
    {
        var targetType = NexusUtilities.GetTypeFromNexusDataType(dataType);

        var method = typeof(BufferUtilities)
            .GetMethod(nameof(InternalApplyRepresentationStatusFloat32ByDataType), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(targetType);

        method.Invoke(null, [data, status, target]);
    }

    private static void InternalApplyRepresentationStatusFloat32ByDataType<T>(ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> status, Memory<float> target)
        where T : unmanaged
    {
        ApplyRepresentationStatusFloat32(data.Cast<byte, T>(), status, target);
    }

    public static void ApplyRepresentationStatusFloat64ByDataType(NexusDataType dataType, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> status, Memory<double> target)
    {
        var targetType = NexusUtilities.GetTypeFromNexusDataType(dataType);

        var method = typeof(BufferUtilities)
            .GetMethod(nameof(InternalApplyRepresentationStatusFloat64ByDataType), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(targetType);

        method.Invoke(null, [data, status, target]);
    }

    private static void InternalApplyRepresentationStatusFloat64ByDataType<T>(ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> status, Memory<double> target)
        where T : unmanaged
    {
        ApplyRepresentationStatusFloat64(data.Cast<byte, T>(), status, target);
    }

    public static unsafe void ApplyRepresentationStatusFloat32<T>(ReadOnlyMemory<T> data, ReadOnlyMemory<byte> status, Memory<float> target) where T : unmanaged
    {
        fixed (T* dataPtr = data.Span)
        {
            fixed (byte* statusPtr = status.Span)
            {
                fixed (float* targetPtr = target.Span)
                {
                    InternalApplyRepresentationStatusFloat32(target.Length, dataPtr, statusPtr, targetPtr);
                }
            }
        }
    }

    public static unsafe void ApplyRepresentationStatusFloat64<T>(ReadOnlyMemory<T> data, ReadOnlyMemory<byte> status, Memory<double> target) where T : unmanaged
    {
        fixed (T* dataPtr = data.Span)
        {
            fixed (byte* statusPtr = status.Span)
            {
                fixed (double* targetPtr = target.Span)
                {
                    InternalApplyRepresentationStatusFloat64(target.Length, dataPtr, statusPtr, targetPtr);
                }
            }
        }
    }

    private unsafe static void InternalApplyRepresentationStatusFloat32<T>(int length, T* dataPtr, byte* statusPtr, float* targetPtr) where T : unmanaged
    {
        Parallel.For(0, length, i =>
        {
            if (statusPtr[i] != 1)
                targetPtr[i] = float.NaN;

            else
                targetPtr[i] = GenericToFloat32<T>.ToFloat32(dataPtr[i]);
        });
    }

    private unsafe static void InternalApplyRepresentationStatusFloat64<T>(int length, T* dataPtr, byte* statusPtr, double* targetPtr) where T : unmanaged
    {
        Parallel.For(0, length, i =>
        {
            if (statusPtr[i] != 1)
                targetPtr[i] = double.NaN;

            else
                targetPtr[i] = GenericToFloat64<T>.ToFloat64(dataPtr[i]);
        });
    }
}

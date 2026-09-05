// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core;
using Nexus.DataModel;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Nexus.Utilities;

internal static class BufferUtilities
{
    private static readonly ConcurrentDictionary<NexusDataType, Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Memory<float>>> _float32ByDataTypeCache = new();
    private static readonly ConcurrentDictionary<NexusDataType, Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Memory<double>>> _float64ByDataTypeCache = new();

    public static void ApplyRepresentationStatusFloat32ByDataType(NexusDataType dataType, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> status, Memory<float> target)
    {
        var action = _float32ByDataTypeCache.GetOrAdd(dataType, dt =>
        {
            var targetType = NexusUtilities.GetTypeFromNexusDataType(dt);

            return (Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Memory<float>>)
                typeof(BufferUtilities)
                    .GetMethod(nameof(InternalApplyRepresentationStatusFloat32ByDataType), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(targetType)
                    .CreateDelegate(typeof(Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Memory<float>>));
        });

        action(data, status, target);
    }

    private static void InternalApplyRepresentationStatusFloat32ByDataType<T>(ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> status, Memory<float> target)
        where T : unmanaged
    {
        ApplyRepresentationStatusFloat32(data.Cast<byte, T>(), status, target);
    }

    public static void ApplyRepresentationStatusFloat64ByDataType(NexusDataType dataType, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> status, Memory<double> target)
    {
        var action = _float64ByDataTypeCache.GetOrAdd(dataType, dt =>
        {
            var targetType = NexusUtilities.GetTypeFromNexusDataType(dt);

            return (Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Memory<double>>)
                typeof(BufferUtilities)
                    .GetMethod(nameof(InternalApplyRepresentationStatusFloat64ByDataType), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(targetType)
                    .CreateDelegate(typeof(Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Memory<double>>));
        });

        action(data, status, target);
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

    internal static unsafe void ScalarApplyRepresentationStatusFloat32<T>(ReadOnlyMemory<T> data, ReadOnlyMemory<byte> status, Memory<float> target) where T : unmanaged
    {
        fixed (T* dataPtr = data.Span)
        {
            fixed (byte* statusPtr = status.Span)
            {
                fixed (float* targetPtr = target.Span)
                {
                    ScalarApplyFloat32(target.Length, dataPtr, statusPtr, targetPtr);
                }
            }
        }
    }

    internal static unsafe void ScalarApplyRepresentationStatusFloat64<T>(ReadOnlyMemory<T> data, ReadOnlyMemory<byte> status, Memory<double> target) where T : unmanaged
    {
        fixed (T* dataPtr = data.Span)
        {
            fixed (byte* statusPtr = status.Span)
            {
                fixed (double* targetPtr = target.Span)
                {
                    ScalarApplyFloat64(target.Length, dataPtr, statusPtr, targetPtr);
                }
            }
        }
    }

    private unsafe static void InternalApplyRepresentationStatusFloat32<T>(int length, T* dataPtr, byte* statusPtr, float* targetPtr) where T : unmanaged
    {
        if (Avx2.IsSupported)
        {
            if (typeof(T) == typeof(float))
                ApplyFloat32FromFloat(length, (float*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(double))
                ApplyFloat32FromDouble(length, (double*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(int))
                ApplyFloat32FromInt32(length, (int*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(uint))
                ApplyFloat32FromUInt32(length, (uint*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(short))
                ApplyFloat32FromInt16(length, (short*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(ushort))
                ApplyFloat32FromUInt16(length, (ushort*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(byte))
                ApplyFloat32FromByte(length, (byte*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(sbyte))
                ApplyFloat32FromSByte(length, (sbyte*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(long) && Avx512DQ.IsSupported)
                ApplyFloat32FromInt64(length, (long*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(ulong) && Avx512DQ.IsSupported)
                ApplyFloat32FromUInt64(length, (ulong*)(void*)dataPtr, statusPtr, targetPtr);
            else
                ScalarApplyFloat32(length, dataPtr, statusPtr, targetPtr);
        }
        else
        {
            ScalarApplyFloat32(length, dataPtr, statusPtr, targetPtr);
        }
    }

    private unsafe static void InternalApplyRepresentationStatusFloat64<T>(int length, T* dataPtr, byte* statusPtr, double* targetPtr) where T : unmanaged
    {
        if (Avx2.IsSupported)
        {
            if (typeof(T) == typeof(double))
                ApplyFloat64FromDouble(length, (double*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(float))
                ApplyFloat64FromFloat(length, (float*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(int))
                ApplyFloat64FromInt32(length, (int*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(uint))
                ApplyFloat64FromUInt32(length, (uint*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(short))
                ApplyFloat64FromInt16(length, (short*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(ushort))
                ApplyFloat64FromUInt16(length, (ushort*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(byte))
                ApplyFloat64FromByte(length, (byte*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(sbyte))
                ApplyFloat64FromSByte(length, (sbyte*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(long) && Avx512DQ.IsSupported)
                ApplyFloat64FromInt64(length, (long*)(void*)dataPtr, statusPtr, targetPtr);
            else if (typeof(T) == typeof(ulong) && Avx512DQ.IsSupported)
                ApplyFloat64FromUInt64(length, (ulong*)(void*)dataPtr, statusPtr, targetPtr);
            else
                ScalarApplyFloat64(length, dataPtr, statusPtr, targetPtr);
        }
        else
        {
            ScalarApplyFloat64(length, dataPtr, statusPtr, targetPtr);
        }
    }

    private unsafe static void ScalarApplyFloat32<T>(int length, T* dataPtr, byte* statusPtr, float* targetPtr) where T : unmanaged
    {
        for (int i = 0; i < length; i++)
        {
            if (statusPtr[i] != 1)
                targetPtr[i] = float.NaN;
            else
                targetPtr[i] = GenericToFloat32<T>.ToFloat32(dataPtr[i]);
        }
    }

    private unsafe static void ScalarApplyFloat64<T>(int length, T* dataPtr, byte* statusPtr, double* targetPtr) where T : unmanaged
    {
        for (int i = 0; i < length; i++)
        {
            if (statusPtr[i] != 1)
                targetPtr[i] = double.NaN;
            else
                targetPtr[i] = GenericToFloat64<T>.ToFloat64(dataPtr[i]);
        }
    }

    #region Status mask helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector256<float> LoadStatusMaskFloat8(byte* statusPtr, int i)
    {
        ulong status = Unsafe.ReadUnaligned<ulong>(statusPtr + i);
        var statusVec = Vector128.CreateScalar(status).AsSByte();
        var cmp = Sse2.CompareEqual(statusVec, Vector128.Create((sbyte)1));
        return Avx2.ConvertToVector256Int32(cmp).AsSingle();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector128<float> LoadStatusMaskFloat4(byte* statusPtr, int i)
    {
        uint status = Unsafe.ReadUnaligned<uint>(statusPtr + i);
        var statusVec = Vector128.CreateScalar(status).AsSByte();
        var cmp = Sse2.CompareEqual(statusVec, Vector128.Create((sbyte)1));
        return Avx2.ConvertToVector256Int32(cmp).GetLower().AsSingle();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector256<double> LoadStatusMaskDouble4(byte* statusPtr, int i)
    {
        uint status = Unsafe.ReadUnaligned<uint>(statusPtr + i);
        var statusVec = Vector128.CreateScalar(status).AsSByte();
        var cmp = Sse2.CompareEqual(statusVec, Vector128.Create((sbyte)1));
        var maskInt32 = Avx2.ConvertToVector256Int32(cmp).GetLower();
        return Avx2.ConvertToVector256Int64(maskInt32).AsDouble();
    }

    #endregion

    #region Conversion helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> ConvertInt32ToFloat64x4(Vector128<int> data)
    {
        var lo = Sse2.ConvertToVector128Double(data);
        var hiData = Sse2.Shuffle(data, 0x0E);
        var hi = Sse2.ConvertToVector128Double(hiData);
        return Vector256.Create(lo, hi);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> ConvertFloat32ToFloat64x4(Vector128<float> data)
    {
        var lo = Sse2.ConvertToVector128Double(data);
        var hiData = Sse2.Shuffle(data.AsInt32(), 0x0E).AsSingle();
        var hi = Sse2.ConvertToVector128Double(hiData);
        return Vector256.Create(lo, hi);
    }

    #endregion

    #region Float32 vectorized paths

    private static unsafe void ApplyFloat32FromFloat(int length, float* dataPtr, byte* statusPtr, float* targetPtr)
    {
        var nan = Vector256.Create(float.NaN);
        int i = 0;
        int vectorEnd = length - (length % 8);

        for (; i < vectorEnd; i += 8)
        {
            var mask = LoadStatusMaskFloat8(statusPtr, i);
            var data = Unsafe.ReadUnaligned<Vector256<float>>(dataPtr + i);
            var result = Avx.BlendVariable(nan, data, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : float.NaN;
    }

    private static unsafe void ApplyFloat32FromDouble(int length, double* dataPtr, byte* statusPtr, float* targetPtr)
    {
        var nan = Vector128.Create(float.NaN);
        int i = 0;
        int vectorEnd = length - (length % 4);

        for (; i < vectorEnd; i += 4)
        {
            var mask = LoadStatusMaskFloat4(statusPtr, i);
            var doubles = Unsafe.ReadUnaligned<Vector256<double>>(dataPtr + i);
            var floats = Avx.ConvertToVector128Single(doubles);
            var result = Sse41.BlendVariable(nan, floats, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? (float)dataPtr[i] : float.NaN;
    }

    private static unsafe void ApplyFloat32FromInt32(int length, int* dataPtr, byte* statusPtr, float* targetPtr)
    {
        var nan = Vector256.Create(float.NaN);
        int i = 0;
        int vectorEnd = length - (length % 8);

        for (; i < vectorEnd; i += 8)
        {
            var mask = LoadStatusMaskFloat8(statusPtr, i);
            var data = Unsafe.ReadUnaligned<Vector256<int>>(dataPtr + i);
            var floats = Avx2.ConvertToVector256Single(data);
            var result = Avx.BlendVariable(nan, floats, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : float.NaN;
    }

    private static unsafe void ApplyFloat32FromUInt32(int length, uint* dataPtr, byte* statusPtr, float* targetPtr)
    {
        var nan = Vector256.Create(float.NaN);
        var signBit = Vector256.Create(unchecked((int)0x80000000));
        var zero = Vector256.Create(0);
        var two31 = Vector256.Create(2147483648.0f);
        int i = 0;
        int vectorEnd = length - (length % 8);

        for (; i < vectorEnd; i += 8)
        {
            var mask = LoadStatusMaskFloat8(statusPtr, i);
            var data = Unsafe.ReadUnaligned<Vector256<uint>>(dataPtr + i).AsInt32();
            var highBitSet = Avx2.CompareGreaterThan(zero, data);
            var cleared = Avx2.AndNot(signBit, data);
            var floats = Avx2.ConvertToVector256Single(cleared);
            var adjustment = Avx2.And(highBitSet.AsSingle(), two31);
            var converted = Avx2.Add(floats, adjustment);
            var result = Avx.BlendVariable(nan, converted, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : float.NaN;
    }

    private static unsafe void ApplyFloat32FromInt16(int length, short* dataPtr, byte* statusPtr, float* targetPtr)
    {
        var nan = Vector128.Create(float.NaN);
        int i = 0;
        int vectorEnd = length - (length % 4);

        for (; i < vectorEnd; i += 4)
        {
            var mask = LoadStatusMaskFloat4(statusPtr, i);
            ulong data8 = Unsafe.ReadUnaligned<ulong>(dataPtr + i);
            var dataShorts = Vector128.CreateScalar(data8).AsInt16();
            var dataInt32 = Sse41.ConvertToVector128Int32(dataShorts);
            var floats = Sse2.ConvertToVector128Single(dataInt32);
            var result = Sse41.BlendVariable(nan, floats, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : float.NaN;
    }

    private static unsafe void ApplyFloat32FromUInt16(int length, ushort* dataPtr, byte* statusPtr, float* targetPtr)
    {
        var nan = Vector128.Create(float.NaN);
        int i = 0;
        int vectorEnd = length - (length % 4);

        for (; i < vectorEnd; i += 4)
        {
            var mask = LoadStatusMaskFloat4(statusPtr, i);
            ulong data8 = Unsafe.ReadUnaligned<ulong>(dataPtr + i);
            var dataUShorts = Vector128.CreateScalar(data8).AsUInt16();
            var dataInt32 = Sse41.ConvertToVector128Int32(dataUShorts);
            var floats = Sse2.ConvertToVector128Single(dataInt32);
            var result = Sse41.BlendVariable(nan, floats, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : float.NaN;
    }

    private static unsafe void ApplyFloat32FromByte(int length, byte* dataPtr, byte* statusPtr, float* targetPtr)
    {
        var nan = Vector256.Create(float.NaN);
        int i = 0;
        int vectorEnd = length - (length % 8);

        for (; i < vectorEnd; i += 8)
        {
            var mask = LoadStatusMaskFloat8(statusPtr, i);
            ulong data8 = Unsafe.ReadUnaligned<ulong>(dataPtr + i);
            var dataBytes = Vector128.CreateScalar(data8).AsByte();
            var dataInt32 = Avx2.ConvertToVector256Int32(dataBytes);
            var floats = Avx2.ConvertToVector256Single(dataInt32);
            var result = Avx.BlendVariable(nan, floats, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : float.NaN;
    }

    private static unsafe void ApplyFloat32FromSByte(int length, sbyte* dataPtr, byte* statusPtr, float* targetPtr)
    {
        var nan = Vector256.Create(float.NaN);
        int i = 0;
        int vectorEnd = length - (length % 8);

        for (; i < vectorEnd; i += 8)
        {
            var mask = LoadStatusMaskFloat8(statusPtr, i);
            ulong data8 = Unsafe.ReadUnaligned<ulong>(dataPtr + i);
            var dataSBytes = Vector128.CreateScalar(data8).AsSByte();
            var dataInt32 = Avx2.ConvertToVector256Int32(dataSBytes);
            var floats = Avx2.ConvertToVector256Single(dataInt32);
            var result = Avx.BlendVariable(nan, floats, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : float.NaN;
    }

    private static unsafe void ApplyFloat32FromInt64(int length, long* dataPtr, byte* statusPtr, float* targetPtr)
    {
        var nan = Vector256.Create(float.NaN);
        int i = 0;
        int vectorEnd = length - (length % 8);

        for (; i < vectorEnd; i += 8)
        {
            var mask = LoadStatusMaskFloat8(statusPtr, i);
            var data = Unsafe.ReadUnaligned<Vector512<long>>(dataPtr + i);
            var floats = Avx512DQ.ConvertToVector256Single(data);
            var result = Avx.BlendVariable(nan, floats, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : float.NaN;
    }

    private static unsafe void ApplyFloat32FromUInt64(int length, ulong* dataPtr, byte* statusPtr, float* targetPtr)
    {
        var nan = Vector256.Create(float.NaN);
        int i = 0;
        int vectorEnd = length - (length % 8);

        for (; i < vectorEnd; i += 8)
        {
            var mask = LoadStatusMaskFloat8(statusPtr, i);
            var data = Unsafe.ReadUnaligned<Vector512<ulong>>(dataPtr + i);
            var floats = Avx512DQ.ConvertToVector256Single(data);
            var result = Avx.BlendVariable(nan, floats, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : float.NaN;
    }

    #endregion

    #region Float64 vectorized paths

    private static unsafe void ApplyFloat64FromDouble(int length, double* dataPtr, byte* statusPtr, double* targetPtr)
    {
        var nan = Vector256.Create(double.NaN);
        int i = 0;
        int vectorEnd = length - (length % 4);

        for (; i < vectorEnd; i += 4)
        {
            var mask = LoadStatusMaskDouble4(statusPtr, i);
            var data = Unsafe.ReadUnaligned<Vector256<double>>(dataPtr + i);
            var result = Avx.BlendVariable(nan, data, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : double.NaN;
    }

    private static unsafe void ApplyFloat64FromFloat(int length, float* dataPtr, byte* statusPtr, double* targetPtr)
    {
        var nan = Vector256.Create(double.NaN);
        int i = 0;
        int vectorEnd = length - (length % 4);

        for (; i < vectorEnd; i += 4)
        {
            var mask = LoadStatusMaskDouble4(statusPtr, i);
            var data = Unsafe.ReadUnaligned<Vector128<float>>(dataPtr + i);
            var doubles = ConvertFloat32ToFloat64x4(data);
            var result = Avx.BlendVariable(nan, doubles, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : double.NaN;
    }

    private static unsafe void ApplyFloat64FromInt32(int length, int* dataPtr, byte* statusPtr, double* targetPtr)
    {
        var nan = Vector256.Create(double.NaN);
        int i = 0;
        int vectorEnd = length - (length % 4);

        for (; i < vectorEnd; i += 4)
        {
            var mask = LoadStatusMaskDouble4(statusPtr, i);
            var data = Unsafe.ReadUnaligned<Vector128<int>>(dataPtr + i);
            var doubles = ConvertInt32ToFloat64x4(data);
            var result = Avx.BlendVariable(nan, doubles, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : double.NaN;
    }

    private static unsafe void ApplyFloat64FromUInt32(int length, uint* dataPtr, byte* statusPtr, double* targetPtr)
    {
        var nan = Vector256.Create(double.NaN);
        var signBit = Vector128.Create(unchecked((int)0x80000000));
        var zero = Vector128.Create(0);
        var two31 = Vector256.Create(2147483648.0);
        int i = 0;
        int vectorEnd = length - (length % 4);

        for (; i < vectorEnd; i += 4)
        {
            var mask = LoadStatusMaskDouble4(statusPtr, i);
            var data = Unsafe.ReadUnaligned<Vector128<uint>>(dataPtr + i).AsInt32();
            var highBitSet = Sse2.CompareGreaterThan(zero, data);
            var cleared = Sse2.AndNot(signBit, data);
            var doubles = ConvertInt32ToFloat64x4(cleared);
            var maskInt64 = Avx2.ConvertToVector256Int64(highBitSet);
            var adjustment = Avx.And(maskInt64.AsDouble(), two31);
            var converted = Avx.Add(doubles, adjustment);
            var result = Avx.BlendVariable(nan, converted, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : double.NaN;
    }

    private static unsafe void ApplyFloat64FromInt16(int length, short* dataPtr, byte* statusPtr, double* targetPtr)
    {
        var nan = Vector256.Create(double.NaN);
        int i = 0;
        int vectorEnd = length - (length % 4);

        for (; i < vectorEnd; i += 4)
        {
            var mask = LoadStatusMaskDouble4(statusPtr, i);
            ulong data8 = Unsafe.ReadUnaligned<ulong>(dataPtr + i);
            var dataShorts = Vector128.CreateScalar(data8).AsInt16();
            var dataInt32 = Sse41.ConvertToVector128Int32(dataShorts);
            var doubles = ConvertInt32ToFloat64x4(dataInt32);
            var result = Avx.BlendVariable(nan, doubles, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : double.NaN;
    }

    private static unsafe void ApplyFloat64FromUInt16(int length, ushort* dataPtr, byte* statusPtr, double* targetPtr)
    {
        var nan = Vector256.Create(double.NaN);
        int i = 0;
        int vectorEnd = length - (length % 4);

        for (; i < vectorEnd; i += 4)
        {
            var mask = LoadStatusMaskDouble4(statusPtr, i);
            ulong data8 = Unsafe.ReadUnaligned<ulong>(dataPtr + i);
            var dataUShorts = Vector128.CreateScalar(data8).AsUInt16();
            var dataInt32 = Sse41.ConvertToVector128Int32(dataUShorts);
            var doubles = ConvertInt32ToFloat64x4(dataInt32);
            var result = Avx.BlendVariable(nan, doubles, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : double.NaN;
    }

    private static unsafe void ApplyFloat64FromByte(int length, byte* dataPtr, byte* statusPtr, double* targetPtr)
    {
        var nan = Vector256.Create(double.NaN);
        int i = 0;
        int vectorEnd = length - (length % 4);

        for (; i < vectorEnd; i += 4)
        {
            var mask = LoadStatusMaskDouble4(statusPtr, i);
            uint data4 = Unsafe.ReadUnaligned<uint>(dataPtr + i);
            var dataBytes = Vector128.CreateScalar(data4).AsByte();
            var dataInt32 = Sse41.ConvertToVector128Int32(dataBytes);
            var doubles = ConvertInt32ToFloat64x4(dataInt32);
            var result = Avx.BlendVariable(nan, doubles, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : double.NaN;
    }

    private static unsafe void ApplyFloat64FromSByte(int length, sbyte* dataPtr, byte* statusPtr, double* targetPtr)
    {
        var nan = Vector256.Create(double.NaN);
        int i = 0;
        int vectorEnd = length - (length % 4);

        for (; i < vectorEnd; i += 4)
        {
            var mask = LoadStatusMaskDouble4(statusPtr, i);
            uint data4 = Unsafe.ReadUnaligned<uint>(dataPtr + i);
            var dataSBytes = Vector128.CreateScalar(data4).AsSByte();
            var dataInt32 = Sse41.ConvertToVector128Int32(dataSBytes);
            var doubles = ConvertInt32ToFloat64x4(dataInt32);
            var result = Avx.BlendVariable(nan, doubles, mask);
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : double.NaN;
    }

    private static unsafe void ApplyFloat64FromInt64(int length, long* dataPtr, byte* statusPtr, double* targetPtr)
    {
        var nan = Vector512.Create(double.NaN);
        int i = 0;
        int vectorEnd = length - (length % 8);

        for (; i < vectorEnd; i += 8)
        {
            var mask = LoadStatusMaskDouble8(statusPtr, i);
            var data = Unsafe.ReadUnaligned<Vector512<long>>(dataPtr + i);
            var doubles = Avx512DQ.ConvertToVector512Double(data);
            var result = Vector512.BitwiseOr(
                Vector512.BitwiseAnd(doubles.AsInt64(), mask),
                Vector512.BitwiseAnd(nan.AsInt64(), Vector512.OnesComplement(mask))
            ).AsDouble();
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : double.NaN;
    }

    private static unsafe void ApplyFloat64FromUInt64(int length, ulong* dataPtr, byte* statusPtr, double* targetPtr)
    {
        var nan = Vector512.Create(double.NaN);
        int i = 0;
        int vectorEnd = length - (length % 8);

        for (; i < vectorEnd; i += 8)
        {
            var mask = LoadStatusMaskDouble8(statusPtr, i);
            var data = Unsafe.ReadUnaligned<Vector512<ulong>>(dataPtr + i);
            var doubles = Avx512DQ.ConvertToVector512Double(data);
            var result = Vector512.BitwiseOr(
                Vector512.BitwiseAnd(doubles.AsInt64(), mask),
                Vector512.BitwiseAnd(nan.AsInt64(), Vector512.OnesComplement(mask))
            ).AsDouble();
            Unsafe.WriteUnaligned(targetPtr + i, result);
        }

        for (; i < length; i++)
            targetPtr[i] = statusPtr[i] == 1 ? dataPtr[i] : double.NaN;
    }

    #endregion

    #region AVX-512 status mask helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector512<long> LoadStatusMaskDouble8(byte* statusPtr, int i)
    {
        ulong status = Unsafe.ReadUnaligned<ulong>(statusPtr + i);
        var statusVec = Vector128.CreateScalar(status).AsSByte();
        var cmp = Sse2.CompareEqual(statusVec, Vector128.Create((sbyte)1));
        var maskInt32 = Avx2.ConvertToVector256Int32(cmp);
        var lower = Avx2.ConvertToVector256Int64(maskInt32.GetLower());
        var upper = Avx2.ConvertToVector256Int64(maskInt32.GetUpper());
        return Vector512.Create(lower, upper);
    }

    #endregion
}

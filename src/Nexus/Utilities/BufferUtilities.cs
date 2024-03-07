using Nexus.Core;
using Nexus.DataModel;
using System.Reflection;

namespace Nexus.Utilities
{
    internal static class BufferUtilities
    {
        public static void ApplyRepresentationStatusByDataType(NexusDataType dataType, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> status, Memory<double> target)
        {
            var targetType = NexusUtilities.GetTypeFromNexusDataType(dataType);

            var method = typeof(BufferUtilities)
                .GetMethod(nameof(InternalApplyRepresentationStatusByDataType), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(targetType);

            method.Invoke(null, new object[] { data, status, target });
        }

        private static void InternalApplyRepresentationStatusByDataType<T>(ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> status, Memory<double> target)
            where T : unmanaged
        {
            ApplyRepresentationStatus<T>(data.Cast<byte, T>(), status, target);
        }

        public static unsafe void ApplyRepresentationStatus<T>(ReadOnlyMemory<T> data, ReadOnlyMemory<byte> status, Memory<double> target) where T : unmanaged
        {
            fixed (T* dataPtr = data.Span)
            {
                fixed (byte* statusPtr = status.Span)
                {
                    fixed (double* targetPtr = target.Span)
                    {
                        InternalApplyRepresentationStatus(target.Length, dataPtr, statusPtr, targetPtr);
                    }
                }
            }
        }

        private unsafe static void InternalApplyRepresentationStatus<T>(int length, T* dataPtr, byte* statusPtr, double* targetPtr) where T : unmanaged
        {
            Parallel.For(0, length, i =>
            {
                if (statusPtr[i] != 1)
                    targetPtr[i] = double.NaN;

                else
                    targetPtr[i] = GenericToDouble<T>.ToDouble(dataPtr[i]);
            });
        }
    }
}

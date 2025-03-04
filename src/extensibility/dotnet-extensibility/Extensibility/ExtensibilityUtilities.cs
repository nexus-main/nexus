﻿// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.DataModel;
using System.Buffers;

namespace Nexus.Extensibility;

/// <summary>
/// A static class with useful helper methods.
/// </summary>
public static class ExtensibilityUtilities
{
    /// <summary>
    /// Creates buffers of the correct size for a given representation and time period.
    /// </summary>
    /// <param name="representation">The representation.</param>
    /// <param name="begin">The beginning of the time period.</param>
    /// <param name="end">The end of the time period.</param>
    /// <returns>The data and status buffers.</returns>
    public static (Memory<byte>, Memory<byte>) CreateBuffers(Representation representation, DateTime begin, DateTime end)
    {
        var elementCount = CalculateElementCountInt32(begin, end, representation.SamplePeriod);

        var dataOwner = MemoryPool<byte>.Shared.Rent(elementCount * representation.ElementSize);
        var data = dataOwner.Memory[..(elementCount * representation.ElementSize)];
        data.Span.Clear();

        var statusOwner = MemoryPool<byte>.Shared.Rent(elementCount);
        var status = statusOwner.Memory[..elementCount];
        status.Span.Clear();

        return (data, status);
    }

    internal static int CalculateElementCountInt32(DateTime begin, DateTime end, TimeSpan samplePeriod)
    {
        var elementCount = (end.Ticks - begin.Ticks) / samplePeriod.Ticks;

        if (elementCount > int.MaxValue)
            throw new Exception("The number of elements is > int.MaxValue");

        return (int)elementCount;
    }

    internal static long CalculateElementCountLong(DateTime begin, DateTime end, TimeSpan samplePeriod)
    {
        return (end.Ticks - begin.Ticks) / samplePeriod.Ticks;
    }
}
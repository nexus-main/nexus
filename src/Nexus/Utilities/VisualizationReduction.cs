// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Nexus.Utilities;

/// <summary>A drawing point at an exact source sample coordinate.</summary>
/// <param name="Index">Absolute sample index, not a uniformly spaced output index.</param>
/// <param name="Value">Source Float32 value, or NaN to break continuity.</param>
public readonly record struct VisualizationPoint(long Index, float Value);

/// <summary>
/// Summary of a contiguous sample interval. GapRuns saturates at two; First/Last
/// retain source bits, including nonfinite values. With no finite samples, extrema
/// are NaN and their indices are -1. FirstGapIndex is -1 when GapRuns is zero.
/// Count == 0 (including default) is the empty identity; its other fields are ignored.
/// </summary>
public readonly record struct VisualizationSummary(
    long Start, long Count, float First, float Last,
    float Min, long MinIndex, float Max, long MaxIndex,
    long FirstGapIndex, int GapRuns, bool HasFinite);

/// <summary>
/// Canonical Float32 chart reduction. Merge summaries, never projected points,
/// to build coarser levels. Callers own bucket alignment and cancellation.
/// </summary>
public static class VisualizationReduction
{
    /// <summary>Number of source samples in a complete base pyramid bucket.</summary>
    public const int BaseStride = 256;

    /// <summary>
    /// Reduces any contiguous fragment. Extrema ties select the earliest sample,
    /// preserving its original bits (also for signed zero). NaN and infinities are gaps.
    /// The inclusive last coordinate must fit in Int64; empty input returns default.
    /// </summary>
    public static VisualizationSummary Summarize(ReadOnlySpan<float> values, long startIndex, bool useSimd = true)
    {
        if (values.IsEmpty)
            return default;

        _ = checked(startIndex + (values.Length - 1L));

        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        int minIndex = -1;
        int maxIndex = -1;
        int firstGap = -1;
        int gapRuns = 0;
        bool previousGap = false;
        int offset = 0;

        if (useSimd && Avx2.IsSupported && values.Length >= Vector256<float>.Count)
        {
            ref float source = ref MemoryMarshal.GetReference(values);
            var vectorMin = Vector256.Create(float.PositiveInfinity);
            var vectorMax = Vector256.Create(float.NegativeInfinity);
            var magnitudeMask = Vector256.Create(int.MaxValue);
            var infinityBits = Vector256.Create(0x7f800000);
            int vectorLength = values.Length - values.Length % Vector256<float>.Count;

            for (; offset < vectorLength; offset += Vector256<float>.Count)
            {
                var vector = Vector256.LoadUnsafe(ref source, (nuint)offset);
                var finite = Avx2.CompareGreaterThan(infinityBits,
                    Avx2.And(vector.AsInt32(), magnitudeMask)).AsSingle();
                uint gaps = (uint)(~Avx.MoveMask(finite) & 0xff);

                if (gaps != 0)
                {
                    if (firstGap < 0)
                        firstGap = offset + BitOperations.TrailingZeroCount(gaps);

                    // A set bit begins a run only when its predecessor was finite.
                    uint starts = gaps & ~((gaps << 1) | (previousGap ? 1u : 0u));
                    gapRuns = Math.Min(2, gapRuns + BitOperations.PopCount(starts));
                }

                previousGap = (gaps & 0x80) != 0;
                vectorMin = Avx.Min(vectorMin, Avx.BlendVariable(
                    Vector256.Create(float.PositiveInfinity), vector, finite));
                vectorMax = Avx.Max(vectorMax, Avx.BlendVariable(
                    Vector256.Create(float.NegativeInfinity), vector, finite));
            }

            for (int lane = 0; lane < Vector256<float>.Count; lane++)
            {
                min = Math.Min(min, vectorMin.GetElement(lane));
                max = Math.Max(max, vectorMax.GetElement(lane));
            }

            if (float.IsFinite(min))
            {
                var minTarget = Vector256.Create(min);
                var maxTarget = Vector256.Create(max);

                // A second vector pass avoids per-lane index bookkeeping in the hot
                // reduction loop. Stop once both earliest matches have been found.
                for (int i = 0; i < vectorLength && (minIndex < 0 || maxIndex < 0); i += Vector256<float>.Count)
                {
                    var vector = Vector256.LoadUnsafe(ref source, (nuint)i);
                    uint minMatches = (uint)Avx.MoveMask(Avx.CompareEqual(vector, minTarget));
                    uint maxMatches = (uint)Avx.MoveMask(Avx.CompareEqual(vector, maxTarget));

                    if (minIndex < 0 && minMatches != 0)
                        minIndex = i + BitOperations.TrailingZeroCount(minMatches);

                    if (maxIndex < 0 && maxMatches != 0)
                        maxIndex = i + BitOperations.TrailingZeroCount(maxMatches);
                }

                // SIMD min/max may choose a later signed zero. Recover source bits.
                min = values[minIndex];
                max = values[maxIndex];
            }
        }

        // Scalar reference when SIMD is disabled/unavailable, otherwise only the tail.
        for (; offset < values.Length; offset++)
        {
            float value = values[offset];
            bool gap = !float.IsFinite(value);

            if (gap)
            {
                if (firstGap < 0)
                    firstGap = offset;

                if (!previousGap)
                    gapRuns = Math.Min(2, gapRuns + 1);
            }
            else
            {
                if (value < min)
                {
                    min = value;
                    minIndex = offset;
                }

                if (value > max)
                {
                    max = value;
                    maxIndex = offset;
                }
            }

            previousGap = gap;
        }

        bool hasFinite = minIndex >= 0;

        return new VisualizationSummary(startIndex, values.Length, values[0], values[^1],
            hasFinite ? min : float.NaN, hasFinite ? startIndex + minIndex : -1,
            hasFinite ? max : float.NaN, hasFinite ? startIndex + maxIndex : -1,
            firstGap >= 0 ? startIndex + firstGap : -1, gapRuns, hasFinite);
    }

    /// <summary>
    /// Merges adjacent, ordered summaries produced by this kernel. Empty operands
    /// are identities irrespective of Start. Nonadjacent intervals are rejected.
    /// </summary>
    public static VisualizationSummary Merge(VisualizationSummary left, VisualizationSummary right)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(left.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(right.Count);

        if (left.Count == 0)
            return right;

        if (right.Count == 0)
            return left;

        if (checked(left.Start + left.Count) != right.Start)
            throw new ArgumentException("Summary intervals must be contiguous and ordered.", nameof(right));

        long count = checked(left.Count + right.Count);
        _ = checked(left.Start + (count - 1));
        bool useLeftMin = left.HasFinite && (!right.HasFinite || left.Min <= right.Min);
        bool useLeftMax = left.HasFinite && (!right.HasFinite || left.Max >= right.Max);
        int joinedGap = !float.IsFinite(left.Last) && !float.IsFinite(right.First) ? 1 : 0;
        int gapRuns = Math.Min(2, left.GapRuns + right.GapRuns - joinedGap);

        return new VisualizationSummary(left.Start, count, left.First, right.Last,
            useLeftMin ? left.Min : right.Min, useLeftMin ? left.MinIndex : right.MinIndex,
            useLeftMax ? left.Max : right.Max, useLeftMax ? left.MaxIndex : right.MaxIndex,
            left.GapRuns != 0 ? left.FirstGapIndex : right.FirstGapIndex,
            gapRuns, left.HasFinite || right.HasFinite);
    }

    /// <summary>
    /// Emits at most five sorted, index-deduplicated points: endpoints, finite
    /// extrema, and the first gap. Nonfinite values become NaN. All-invalid or
    /// multigap intervals emit only a NaN at FirstGapIndex, never invented continuity.
    /// </summary>
    public static VisualizationPoint[] Project(VisualizationSummary summary)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(summary.Count);

        if (summary.Count == 0)
            return [];

        if (!summary.HasFinite || summary.GapRuns >= 2)
            return [new VisualizationPoint(summary.FirstGapIndex, float.NaN)];

        Span<VisualizationPoint> points = stackalloc VisualizationPoint[5];
        points[0] = new(summary.Start, float.IsFinite(summary.First) ? summary.First : float.NaN);
        points[1] = new(checked(summary.Start + (summary.Count - 1)), float.IsFinite(summary.Last) ? summary.Last : float.NaN);
        points[2] = new(summary.MinIndex, summary.Min);
        points[3] = new(summary.MaxIndex, summary.Max);
        int count = 4;

        if (summary.GapRuns != 0)
            points[count++] = new(summary.FirstGapIndex, float.NaN);

        for (int i = 1; i < count; i++)
        {
            var point = points[i];
            int j = i;

            while (j > 0 && points[j - 1].Index > point.Index)
            {
                points[j] = points[j - 1];
                j--;
            }

            points[j] = point;
        }

        int uniqueCount = 1;

        for (int i = 1; i < count; i++)
        {
            if (points[i].Index != points[uniqueCount - 1].Index)
                points[uniqueCount++] = points[i];
        }

        return points[..uniqueCount].ToArray();
    }
}

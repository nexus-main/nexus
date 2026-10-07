// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Utilities;
using Xunit;

namespace Other;

public class VisualizationReductionTests
{
    [Fact]
    public void EmptyIsAnIdentity()
    {
        var empty = VisualizationReduction.Summarize([], long.MaxValue);
        var summary = VisualizationReduction.Summarize([3, float.NaN, 2], 42);

        Assert.Equal(default, empty);
        Assert.Empty(VisualizationReduction.Project(empty));
        AssertSummary(summary, VisualizationReduction.Merge(empty, summary));
        AssertSummary(summary, VisualizationReduction.Merge(summary, empty));
        AssertSummary(empty, VisualizationReduction.Merge(empty, empty));
        AssertSummary(summary, VisualizationReduction.Merge(empty with { Start = 999 }, summary));
        Assert.Equal(256, VisualizationReduction.BaseStride);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndpointsExtremaAndGapAreSortedAndDeduplicated(bool useSimd)
    {
        float[] values = [3, 10, 2, float.PositiveInfinity, 1, -10, 4, 5, 6];
        var summary = VisualizationReduction.Summarize(values, 100, useSimd);

        AssertSummary(Reference(values, 100), summary);
        Assert.Equal(new VisualizationPoint[]
        {
            new(100, 3), new(101, 10), new(103, float.NaN), new(105, -10), new(108, 6)
        }, VisualizationReduction.Project(summary));

        Assert.Equal(new VisualizationPoint[] { new(5, 2), new(7, 4) },
            VisualizationReduction.Project(VisualizationReduction.Summarize([2, 3, 4], 5, useSimd)));
        Assert.Equal(new VisualizationPoint[] { new(5, 2) },
            VisualizationReduction.Project(VisualizationReduction.Summarize([2], 5, useSimd)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtremaTiesKeepEarliestOriginalBits(bool useSimd)
    {
        foreach (float first in new[] { 0f, -0f })
        {
            var values = Enumerable.Repeat(-first, 41).ToArray();
            values[0] = float.NaN;
            values[1] = first;
            var summary = VisualizationReduction.Summarize(values, 1L << 54, useSimd);

            Assert.Equal((1L << 54) + 1, summary.MinIndex);
            Assert.Equal(summary.MinIndex, summary.MaxIndex);
            AssertBits(first, summary.Min);
            AssertBits(first, summary.Max);
            AssertSummary(summary, VisualizationReduction.Merge(
                VisualizationReduction.Summarize(values.AsSpan(0, 7), 1L << 54, useSimd),
                VisualizationReduction.Summarize(values.AsSpan(7), (1L << 54) + 7, useSimd)));
        }

        float[] ties = [1, 8, -8, 8, -8, 1, 8, -8, 8, -8, 1];
        var tied = VisualizationReduction.Summarize(ties, 0, useSimd);
        Assert.Equal(1, tied.MaxIndex);
        Assert.Equal(2, tied.MinIndex);
    }

    [Fact]
    public void AllInvalidAndMultipleGapsNeverEmitFinitePoints()
    {
        float nan = BitConverter.Int32BitsToSingle(unchecked((int)0xffc01234));
        float[][] cases =
        [
            [nan],
            [float.PositiveInfinity, nan, float.NegativeInfinity],
            [1, nan, 2, float.PositiveInfinity, 3],
            [nan, 1, nan],
            Enumerable.Repeat(nan, 257).ToArray()
        ];

        foreach (var values in cases)
        {
            var summary = VisualizationReduction.Summarize(values, 800);
            AssertSummary(Reference(values, 800), summary);
            var point = Assert.Single(VisualizationReduction.Project(summary));
            Assert.Equal(summary.FirstGapIndex, point.Index);
            Assert.True(float.IsNaN(point.Value));
            AssertBits(values[0], summary.First);
            AssertBits(values[^1], summary.Last);
        }
    }

    [Fact]
    public void SingleGapAtEitherBoundaryDoesNotInventContinuity()
    {
        Assert.Equal(new VisualizationPoint[] { new(0, float.NaN), new(2, 2), new(3, 3) },
            VisualizationReduction.Project(VisualizationReduction.Summarize([float.NegativeInfinity, float.NaN, 2, 3], 0)));
        Assert.Equal(new VisualizationPoint[] { new(0, 3), new(1, 2), new(2, float.NaN), new(3, float.NaN) },
            VisualizationReduction.Project(VisualizationReduction.Summarize([3, 2, float.NaN, float.PositiveInfinity], 0)));
    }

    [Fact]
    public void EveryGapPatternAndSplitMatchesDirectReduction()
    {
        const int length = 10;

        for (int mask = 0; mask < 1 << length; mask++)
        {
            var values = Enumerable.Range(0, length)
                .Select(i => (mask & (1 << i)) != 0 ? float.NaN : (float)i).ToArray();
            var expected = Reference(values, -5);
            AssertSummary(expected, VisualizationReduction.Summarize(values, -5));

            for (int split = 0; split <= length; split++)
            {
                var left = VisualizationReduction.Summarize(values.AsSpan(0, split), -5);
                var right = VisualizationReduction.Summarize(values.AsSpan(split), -5 + split);
                AssertSummary(expected, VisualizationReduction.Merge(left, right));
            }
        }
    }

    [Fact]
    public void VectorBoundariesAndEveryTailMatchReference()
    {
        for (int length = 1; length <= 73; length++)
        {
            for (int special = 0; special < length; special++)
            {
                foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -99f, 99f, -0f })
                {
                    var storage = new float[length + 3];
                    var values = storage.AsSpan(3); // Deliberately unaligned vector loads.
                    values.Fill(0);
                    values[special] = value;
                    AssertSummary(Reference(values, 123), VisualizationReduction.Summarize(values, 123));
                }
            }
        }
    }

    [Fact]
    public void InvalidVectorsWithFiniteTailsAndExtremeFloatsMatchReference()
    {
        float[][] cases =
        [
            [float.MaxValue, float.MinValue, float.Epsilon, -float.Epsilon, 0, -0f, 1, -1, float.MinValue, float.MaxValue],
            [float.Epsilon, -float.Epsilon, float.Epsilon, -float.Epsilon, float.Epsilon, -float.Epsilon, float.Epsilon, -float.Epsilon]
        ];

        for (int tail = 1; tail <= 7; tail++)
        {
            var values = Enumerable.Repeat(float.NegativeInfinity, 16 + tail).ToArray();
            values[^1] = tail % 2 == 0 ? -0f : float.Epsilon;
            AssertSummary(Reference(values, 0), VisualizationReduction.Summarize(values, 0));
        }

        foreach (var values in cases)
        {
            AssertSummary(Reference(values, 0), VisualizationReduction.Summarize(values, 0, false));
            AssertSummary(Reference(values, 0), VisualizationReduction.Summarize(values, 0, true));
        }
    }

    [Fact]
    public void RandomizedPartitionsAndMergeTreesAreCanonical()
    {
        var random = new Random(34721);

        for (int iteration = 0; iteration < 500; iteration++)
        {
            int length = random.Next(0, 2049);
            var values = new float[length];

            for (int i = 0; i < length; i++)
            {
                values[i] = (iteration % 4) switch
                {
                    0 => random.Next(-10, 11),
                    1 => BitConverter.Int32BitsToSingle((int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1)),
                    2 => random.Next(4) == 0 ? float.NaN : random.Next(-10, 11),
                    _ => i >= length / 3 && i < length * 2 / 3 ? float.PositiveInfinity : random.Next(-10, 11)
                };
            }

            long start = (1L << 54) + random.Next(1000);
            var expected = Reference(values, start);
            AssertSummary(expected, VisualizationReduction.Summarize(values, start, false));
            AssertSummary(expected, VisualizationReduction.Summarize(values, start, true));
            var chunks = new List<VisualizationSummary>();
            var folded = default(VisualizationSummary);

            for (int offset = 0; offset < length;)
            {
                int count = Math.Min(random.Next(1, 270), length - offset);
                var chunk = VisualizationReduction.Summarize(values.AsSpan(offset, count), start + offset, random.Next(2) == 0);
                chunks.Add(chunk);
                folded = VisualizationReduction.Merge(folded, chunk);
                offset += count;
            }

            AssertSummary(expected, folded);

            while (chunks.Count > 1)
            {
                int index = random.Next(chunks.Count - 1);
                chunks[index] = VisualizationReduction.Merge(chunks[index], chunks[index + 1]);
                chunks.RemoveAt(index + 1);
            }

            AssertSummary(expected, chunks.Count == 0 ? default : chunks[0]);
            var points = VisualizationReduction.Project(folded);
            Assert.InRange(points.Length, 0, 5);

            for (int i = 0; i < points.Length; i++)
            {
                Assert.InRange(points[i].Index, start, start + length - 1);

                if (i > 0)
                    Assert.True(points[i - 1].Index < points[i].Index);

                if (float.IsFinite(points[i].Value))
                    AssertBits(values[(int)(points[i].Index - start)], points[i].Value);
            }
        }
    }

    [Fact]
    public void FourWayPyramidIncludingPartialBucketsMatchesDirect()
    {
        var values = Enumerable.Range(0, 256 * 70 + 19).Select(i => (float)Math.Sin(i)).ToArray();
        Array.Fill(values, float.NaN, 250, 300);
        var level = new List<VisualizationSummary>();

        for (int i = 0; i < values.Length; i += VisualizationReduction.BaseStride)
            level.Add(VisualizationReduction.Summarize(values.AsSpan(i, Math.Min(256, values.Length - i)), i));

        while (level.Count > 1)
        {
            var parents = new List<VisualizationSummary>();

            for (int i = 0; i < level.Count; i += 4)
            {
                var parent = default(VisualizationSummary);

                for (int child = i; child < Math.Min(i + 4, level.Count); child++)
                    parent = VisualizationReduction.Merge(parent, level[child]);

                AssertSummary(Reference(values.AsSpan((int)parent.Start, (int)parent.Count), parent.Start), parent);
                parents.Add(parent);
            }

            level = parents;
        }

        AssertSummary(Reference(values, 0), Assert.Single(level));
    }

    [Fact]
    public void CoordinatesAndCountsAreCheckedWithoutNarrowing()
    {
        var end = VisualizationReduction.Summarize([1, 2], long.MaxValue - 1);
        Assert.Equal(long.MaxValue, VisualizationReduction.Project(end)[^1].Index);
        AssertSummary(end, VisualizationReduction.Merge(
            VisualizationReduction.Summarize([1], long.MaxValue - 1),
            VisualizationReduction.Summarize([2], long.MaxValue)));
        Assert.Throws<OverflowException>(() => VisualizationReduction.Summarize(new float[3], long.MaxValue - 1));

        var left = VisualizationReduction.Summarize([1, 2], 0);
        Assert.Throws<ArgumentException>(() => VisualizationReduction.Merge(left, VisualizationReduction.Summarize([3], 1)));
        Assert.Throws<ArgumentException>(() => VisualizationReduction.Merge(left, VisualizationReduction.Summarize([3], 3)));
        Assert.Throws<ArgumentException>(() => VisualizationReduction.Merge(left, VisualizationReduction.Summarize([3], -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualizationReduction.Merge(left with { Count = -1 }, default));

        var huge = left with { Start = long.MinValue, Count = long.MaxValue };
        var next = VisualizationReduction.Summarize([3], -1);
        Assert.Throws<OverflowException>(() => VisualizationReduction.Merge(huge, next));
    }

    private static VisualizationSummary Reference(ReadOnlySpan<float> values, long start)
    {
        if (values.IsEmpty)
            return default;

        // Independent, intentionally allocation-heavy oracle; OrderBy is stable for ties.
        var samples = values.ToArray().Select((value, index) => new VisualizationPoint(start + index, value)).ToArray();
        var finite = samples.Where(point => float.IsFinite(point.Value)).ToArray();
        var minimum = finite.OrderBy(point => point.Value).FirstOrDefault(new VisualizationPoint(-1, float.NaN));
        var maximum = finite.OrderByDescending(point => point.Value).FirstOrDefault(new VisualizationPoint(-1, float.NaN));
        var gaps = samples.Where(point => !float.IsFinite(point.Value)).ToArray();
        int runs = gaps.Where((point, index) => index == 0 || point.Index != gaps[index - 1].Index + 1).Count();

        return new(start, values.Length, values[0], values[^1],
            minimum.Value, minimum.Index, maximum.Value, maximum.Index,
            gaps.Length == 0 ? -1 : gaps[0].Index, Math.Min(2, runs), finite.Length != 0);
    }

    private static void AssertSummary(VisualizationSummary expected, VisualizationSummary actual)
    {
        Assert.Equal(expected, actual);
        AssertBits(expected.First, actual.First);
        AssertBits(expected.Last, actual.Last);
        AssertBits(expected.Min, actual.Min);
        AssertBits(expected.Max, actual.Max);
    }

    private static void AssertBits(float expected, float actual) =>
        Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual));
}

using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Services;
using System.Runtime.InteropServices;
using Xunit;

namespace Services;

public class ProcessingServiceTests
{
    [InlineData("Min", 0.90, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 }, new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, -4)]
    [InlineData("Min", 0.99, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 }, new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

    [InlineData("Max", 0.90, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 }, new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 97)]
    [InlineData("Max", 0.99, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 }, new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

    [InlineData("Mean", 0.90, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 }, new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 12)]
    [InlineData("Mean", 0.99, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 }, new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

    [InlineData("MeanPolarDeg", 0.90, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 }, new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 9.25)]
    [InlineData("MeanPolarDeg", 0.99, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 }, new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

    [InlineData("Sum", 0.90, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 }, new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 132)]
    [InlineData("Sum", 0.99, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 }, new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

    [InlineData("MinBitwise", 0.90, new int[] { 2, 2, 2, 3, 2, 3, 65, 2, 98, 14 }, new byte[] { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 2)]
    [InlineData("MinBitwise", 0.99, new int[] { 2, 2, 2, 3, 2, 3, 65, 2, 98, 14 }, new byte[] { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

    [InlineData("MaxBitwise", 0.90, new int[] { 2, 2, 2, 3, 2, 3, 65, 2, 98, 14 }, new byte[] { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 111)]
    [InlineData("MaxBitwise", 0.99, new int[] { 2, 2, 2, 3, 2, 3, 65, 2, 98, 14 }, new byte[] { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

    [Theory]
    public void CanAggregateSingle(string kindString, double nanThreshold, int[] data, byte[] status, double expected)
    {
        // Arrange
        var kind = Enum.Parse<RepresentationKind>(kindString);
        var options = Options.Create(new DataOptions() { AggregationNaNThreshold = nanThreshold });
        var processingService = new ProcessingService(options);
        var blockSize = data.Length;
        var actual = new double[1];
        var byteData = MemoryMarshal.AsBytes<int>(data).ToArray();

        // Act
        processingService.Aggregate(NexusDataType.INT32, kind, byteData, status, targetBuffer: actual, blockSize);

        // Assert
        Assert.Equal(expected, actual[0], precision: 2);
    }

    [Fact]
    public void CanAggregateMultiple()
    {
        // Arrange
        var data = new int[]
        {
            0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13,
            0, 1, 2, 3, -4, 5, 6, 7, 3, 2, 87, 12
        };

        var status = new byte[]
        {
            1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1,
            1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
        };

        var expected = new double[] { 132, 123 };
        var options = Options.Create(new DataOptions() { AggregationNaNThreshold = 0.9 });
        var processingService = new ProcessingService(options);
        var blockSize = data.Length / 2;
        var actual = new double[expected.Length];
        var byteData = MemoryMarshal.AsBytes<int>(data).ToArray();

        // Act
        processingService.Aggregate(NexusDataType.INT32, RepresentationKind.Sum, byteData, status, targetBuffer: actual, blockSize);

        // Assert
        Assert.True(expected.SequenceEqual(actual));
    }

    [Fact]
    public void CanResample()
    {
        // Arrange
        var data = new float[]
        {
            0, 1, 2, 3
        };

        var status = new byte[]
        {
            1, 1, 0, 1
        };

        var expected = new double[] { 0, 0, 1, 1, 1, 1, double.NaN, double.NaN, double.NaN, double.NaN, 3, 3 };
        var options = Options.Create(new DataOptions());
        var processingService = new ProcessingService(options);
        var blockSize = 4;
        var actual = new double[expected.Length];
        var byteData = MemoryMarshal.AsBytes<float>(data).ToArray();

        // Act
        processingService.Resample(NexusDataType.FLOAT32, byteData, status, targetBuffer: actual, blockSize, offset: 2);

        // Assert
        Assert.True(expected.SequenceEqual(actual));
    }
}
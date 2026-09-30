// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core;
using Nexus.DataModel;
using Xunit;

namespace Other;

public class CatalogContainerRangeTests
{
    [Fact]
    public void TryGetSampleAlignedContainedRangeReturnsRequestRangeWhenUnrestricted()
    {
        // Arrange
        var container = CreateContainer();
        var begin = new DateTime(2024, 01, 01, 00, 00, 00, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 05, 00, 00, 00, DateTimeKind.Utc);

        // Act
        var actual = container.TryGetSampleAlignedContainedRange(begin, end, TimeSpan.FromDays(1), out var range);

        // Assert
        Assert.True(actual);
        Assert.Equal(begin, range.Begin);
        Assert.Equal(end, range.End);
    }

    [Fact]
    public void TryGetSampleAlignedContainedRangeRoundsMinBeginUpAndMaxEndDown()
    {
        // Arrange
        var container = CreateContainer(
            minBegin: new DateTime(2024, 01, 02, 12, 00, 00, DateTimeKind.Utc),
            maxEnd: new DateTime(2024, 01, 04, 18, 00, 00, DateTimeKind.Utc));
        var begin = new DateTime(2024, 01, 01, 00, 00, 00, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 05, 00, 00, 00, DateTimeKind.Utc);

        // Act
        var actual = container.TryGetSampleAlignedContainedRange(begin, end, TimeSpan.FromDays(1), out var range);

        // Assert
        Assert.True(actual);
        Assert.Equal(new DateTime(2024, 01, 03, 00, 00, 00, DateTimeKind.Utc), range.Begin);
        Assert.Equal(new DateTime(2024, 01, 04, 00, 00, 00, DateTimeKind.Utc), range.End);
    }

    [Fact]
    public void TryGetSampleAlignedContainedRangeReturnsFalseWhenRangeIsEmptyAfterRounding()
    {
        // Arrange
        var container = CreateContainer(
            minBegin: new DateTime(2024, 01, 02, 12, 00, 00, DateTimeKind.Utc),
            maxEnd: new DateTime(2024, 01, 03, 00, 00, 00, DateTimeKind.Utc));
        var begin = new DateTime(2024, 01, 01, 00, 00, 00, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 05, 00, 00, 00, DateTimeKind.Utc);

        // Act
        var actual = container.TryGetSampleAlignedContainedRange(begin, end, TimeSpan.FromDays(1), out _);

        // Assert
        Assert.False(actual);
    }

    [Fact]
    public void TryGetContainedBucketRangeReturnsFalseWhenNoBucketIsFullyContained()
    {
        // Arrange
        var container = CreateContainer(
            minBegin: new DateTime(2024, 01, 02, 12, 00, 00, DateTimeKind.Utc),
            maxEnd: new DateTime(2024, 01, 03, 12, 00, 00, DateTimeKind.Utc));
        var begin = new DateTime(2024, 01, 01, 00, 00, 00, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 04, 00, 00, 00, DateTimeKind.Utc);

        // Act
        var actual = container.TryGetContainedBucketRange(begin, end, TimeSpan.FromDays(1), out _);

        // Assert
        Assert.False(actual);
    }

    [Fact]
    public void TryGetContainedBucketRangeReturnsFirstIndexAndCount()
    {
        // Arrange
        var container = CreateContainer(
            minBegin: new DateTime(2024, 01, 02, 00, 00, 00, DateTimeKind.Utc),
            maxEnd: new DateTime(2024, 01, 04, 00, 00, 00, DateTimeKind.Utc));
        var begin = new DateTime(2024, 01, 01, 00, 00, 00, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 05, 12, 00, 00, 00, DateTimeKind.Utc);

        // Act
        var actual = container.TryGetContainedBucketRange(begin, end, TimeSpan.FromDays(1), out var range);

        // Assert
        Assert.True(actual);
        Assert.Equal(1, range.FirstIndex);
        Assert.Equal(2, range.Count);
        Assert.Equal(new DateTime(2024, 01, 02, 00, 00, 00, DateTimeKind.Utc), range.Begin);
        Assert.Equal(new DateTime(2024, 01, 04, 00, 00, 00, DateTimeKind.Utc), range.End);
    }

    private static CatalogContainer CreateContainer(DateTime? minBegin = default, DateTime? maxEnd = default)
    {
        return new CatalogContainer(
            new CatalogRegistration("/ALIAS", default, MinBegin: minBegin, MaxEnd: maxEnd),
            default,
            default!,
            default!,
            default!,
            default!,
            default!,
            default!,
            backingSourceId: "/SOURCE");
    }
}

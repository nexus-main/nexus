// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.DataModel;
using Xunit;

namespace Nexus.Extensibility.Tests;

public class DataModelExtensionsTests
{
    [Theory]
    [InlineData("00:00:00.0000001", "100_ns")]
    [InlineData("00:00:00.0000002", "200_ns")]
    [InlineData("00:00:00.0000015", "1500_ns")]

    [InlineData("00:00:00.0000010", "1_us")]
    [InlineData("00:00:00.0000100", "10_us")]
    [InlineData("00:00:00.0001000", "100_us")]
    [InlineData("00:00:00.0015000", "1500_us")]

    [InlineData("00:00:00.0010000", "1_ms")]
    [InlineData("00:00:00.0100000", "10_ms")]
    [InlineData("00:00:00.1000000", "100_ms")]
    [InlineData("00:00:01.5000000", "1500_ms")]

    [InlineData("00:00:01.0000000", "1_s")]
    [InlineData("00:00:15.0000000", "15_s")]

    [InlineData("00:01:00.0000000", "1_min")]
    [InlineData("00:15:00.0000000", "15_min")]
    public void CanCreateUnitStrings(string periodString, string expected)
    {
        var actual = TimeSpan
            .Parse(periodString)
            .ToUnitString();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("100_ns", "00:00:00.0000001")]
    [InlineData("200_ns", "00:00:00.0000002")]
    [InlineData("1500_ns", "00:00:00.0000015")]

    [InlineData("1_us", "00:00:00.0000010")]
    [InlineData("10_us", "00:00:00.0000100")]
    [InlineData("100_us", "00:00:00.0001000")]
    [InlineData("1500_us", "00:00:00.0015000")]

    [InlineData("1_ms", "00:00:00.0010000")]
    [InlineData("10_ms", "00:00:00.0100000")]
    [InlineData("100_ms", "00:00:00.1000000")]
    [InlineData("1500_ms", "00:00:01.5000000")]

    [InlineData("1_s", "00:00:01.0000000")]
    [InlineData("15_s", "00:00:15.0000000")]

    [InlineData("1_min", "00:01:00.0000000")]
    [InlineData("15_min", "00:15:00.0000000")]
    public void CanParseUnitStrings(string unitString, string expectedPeriodString)
    {
        var expected = TimeSpan
            .Parse(expectedPeriodString);

        var actual = DataModelExtensions.ToSamplePeriod(unitString);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("A and B/C/D", UriKind.Relative, "A and B/C/D")]
    [InlineData("A and B/C/D.ext", UriKind.Relative, "A and B/C/D.ext")]
    [InlineData(@"file:///C:/A and B", UriKind.Absolute, @"C:/A and B")]
    [InlineData(@"file:///C:/A and B/C.ext", UriKind.Absolute, @"C:/A and B/C.ext")]
    [InlineData(@"file:///root/A and B", UriKind.Absolute, @"/root/A and B")]
    [InlineData(@"file:///root/A and B/C.ext", UriKind.Absolute, @"/root/A and B/C.ext")]
    public void CanConvertUriToPath(string uriString, UriKind uriKind, string expected)
    {
        var uri = new Uri(uriString, uriKind);
        var actual = uri.ToPath();

        Assert.Equal(actual, expected);
    }
}
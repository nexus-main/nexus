// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.UI.Core;
using Xunit;

namespace Nexus.UI.Tests;

public class NexusDemoClientTests
{
    [Fact]
    public async Task LoadAsyncUsesAdvertisedSamplePeriod()
    {
        var client = new NexusDemoClient();
        var begin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var resourcePath = "/SAMPLE/LOCAL/temperature/1_min";

        var result = await client.LoadAsync(begin, begin.AddMinutes(2), [resourcePath]);

        var response = Assert.Single(result).Value;
        Assert.Equal(TimeSpan.FromMinutes(1), response.SamplePeriod);
        Assert.Equal(2, response.Values.Length);
    }
}

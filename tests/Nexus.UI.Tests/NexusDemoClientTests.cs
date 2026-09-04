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

        var result = await client.LoadAsync<float>(begin, begin.AddMinutes(2), [resourcePath]);

        var response = Assert.Single(result).Value;
        Assert.Equal(TimeSpan.FromMinutes(1), response.SamplePeriod);
        Assert.Equal(2, response.Values.Length);
        Assert.Equal("temperature", response.Name);
        Assert.Equal("°C", response.Unit);
        Assert.Equal("A description for the temperature resource.", response.Description);
        Assert.Equal("temperature", response.CatalogItem.Resource.Id);
    }

    [Fact]
    public async Task CanLoadMultipleFramedResources()
    {
        var client = new NexusDemoClient();
        var begin = DateTime.UnixEpoch;
        var result = await client.LoadAsync<float>(begin, begin.AddMinutes(2),
        [
            "/SAMPLE/LOCAL/temperature/1_min",
            "/SAMPLE/LOCAL/wind_speed/1_min"
        ]);

        Assert.Equal(2, result.Count);
        Assert.All(result.Values, response => Assert.Equal(2, response.Values.Length));
    }
}

// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.UI.Core;
using Xunit;

namespace Nexus.UI.Tests;

public class NexusDemoClientTests
{
    [Fact]
    public async Task LoadAsyncWithBufferProviderUsesAdvertisedSamplePeriod()
    {
        var client = new NexusDemoClient();
        var begin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var resourcePath = "/SAMPLE/LOCAL/temperature/1_min";
        var values = new float[2];

        var result = await client.LoadAsync<float>(
            begin,
            begin.AddMinutes(2),
            [resourcePath],
            (_, chunkLength, _) => values.AsMemory(0, chunkLength));

        var response = Assert.Single(result).Value;
        Assert.Equal(TimeSpan.FromMinutes(1), response.SamplePeriod);
        Assert.Equal(2, values.Length);
        Assert.Equal("temperature", response.Name);
        Assert.Equal("°C", response.Unit);
        Assert.Equal("A description for the temperature resource.", response.Description);
        Assert.Equal("temperature", response.CatalogItem.Resource.Id);
    }
}

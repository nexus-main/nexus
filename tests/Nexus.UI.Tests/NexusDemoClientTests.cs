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
        Assert.Equal("temperature", response.Name);
        Assert.Equal("°C", response.Unit);
        Assert.Equal("A description for the temperature resource.", response.Description);
        Assert.Equal("temperature", response.CatalogItem.Resource.Id);
    }

    [Fact]
    public async Task BatchChannelRequiresMatchingSession()
    {
        var client = new DataV2DemoClient();
        var request = new Api.V2.BatchStreamRequest(
            DateTime.UnixEpoch,
            DateTime.UnixEpoch.AddMinutes(1),
            ["/SAMPLE/LOCAL/temperature/1_min"]);
        var session = await client.RegisterBatchStreamAsync(request);

        using var wrongSessionResponse = await client.GetBatchStreamChannelAsync(
            Guid.NewGuid(),
            session.Channels[0].ChannelId);
        using var correctSessionResponse = await client.GetBatchStreamChannelAsync(
            session.SessionId,
            session.Channels[0].ChannelId);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, wrongSessionResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, correctSessionResponse.StatusCode);
    }
}

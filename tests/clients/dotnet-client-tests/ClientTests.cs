// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Net;
using System.Text;
using System.Text.Json;
using Moq;
using Moq.Protected;
using Xunit;

namespace Nexus.Api.Tests;

public class ClientTests
{
    public const string NexusConfigurationHeaderKey = "Nexus-Configuration";

    [Fact]
    public async Task CanAddConfigurationAsync()
    {
        // Arrange
        var messageHandlerMock = new Mock<HttpMessageHandler>();
        var catalogId = "my-catalog-id";
        var expectedCatalog = new ResourceCatalog(Id: catalogId, default, default);

        var actualHeaders = new List<IEnumerable<string>?>();

        messageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((requestMessage, cancellationToken) =>
            {
                requestMessage.Headers.TryGetValues(NexusConfigurationHeaderKey, out var headers);
                actualHeaders.Add(headers);
            })
            .ReturnsAsync(() =>
            {
                return new HttpResponseMessage()
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(expectedCatalog), Encoding.UTF8, "application/json"),
                };
            });

        // -> http client
        var httpClient = new HttpClient(messageHandlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost")
        };

        // -> API client
        var client = new NexusClient(httpClient);

        // -> configuration
        var configuration = new
        {
            foo1 = "bar1",
            foo2 = "bar2"
        };

        // Act
        _ = await client.Catalogs.GetAsync(catalogId);

        using (var disposable = client.AttachConfiguration(configuration))
        {
            _ = await client.Catalogs.GetAsync(catalogId);
        }

        _ = await client.Catalogs.GetAsync(catalogId);

        // Assert
        var encodedJson = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(configuration));

        Assert.Collection(actualHeaders,
            Assert.Null,
            headers =>
            {
                Assert.NotNull(headers);
                var header = Assert.Single(headers);
                Assert.Equal(encodedJson, header);
            },
            Assert.Null);
    }
}
